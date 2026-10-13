using UnityEngine;

public partial class PreGameControl
{
    [Header("Remote handwriting (shodoVR PenTablet)")]
    public bool useRemoteTemplate;
    [Tooltip("UDP受信の代わりに座標で作ったテスト文字「十」を表示します。")]
    public bool useLocalTestTemplate;
    [Tooltip("ローカルのお手本テスト中だけ、バランスボードの接続とキャリブレーションを省略します。")]
    public bool skipBalanceBoardInLocalTest = true;
    bool SkipBalanceBoard => useRemoteTemplate && useLocalTestTemplate && skipBalanceBoardInLocalTest;
    public int remoteListenPort = 5005;
    public Vector2 remoteSourceHalfSize = new Vector2(1.92f, 1.08f);
    RemoteHandwritingReceiver remoteTemplate;

    void InitializeRemoteTemplate()
    {
        // Scene references previously pointed at the prefab asset instead of its instance.
        var scenePaper = GameObject.Find("MoveArea");
        if (scenePaper != null) moveArea = paperObject = scenePaper;
        var completedTemplate = GlobalData.RemoteTemplate;
        GlobalData.RemoteTemplate = null;
        if (completedTemplate != null) useLocalTestTemplate = completedTemplate.isLocalTest;
        currentPromptWord = completedTemplate != null ? (completedTemplate.promptWord ?? "")
            : (useLocalTestTemplate ? "十" : (GlobalData.PromptWord ?? ""));
        currentPromptIndex = 0;
        var receiverObject = new GameObject("RemoteHandwriting");
        receiverObject.transform.SetParent(transform, false);
        receiverObject.SetActive(false);
        remoteTemplate = receiverObject.AddComponent<RemoteHandwritingReceiver>();
        remoteTemplate.listenPort = remoteListenPort;
        remoteTemplate.receiveUdp = completedTemplate == null && !useLocalTestTemplate;
        remoteTemplate.sourceHalfSize = remoteSourceHalfSize;
        remoteTemplate.paper = moveArea;
        remoteTemplate.CharacterAdded += RefreshRemoteSlots;
        remoteTemplate.SetPreviewTargets(rightSlots);
        receiverObject.SetActive(true);
        if (completedTemplate != null) remoteTemplate.LoadTemplate(completedTemplate);
        else if (useLocalTestTemplate) remoteTemplate.LoadTestCross();
    }

    void RefreshRemoteSlots()
    {
        int count = remoteTemplate.CharacterCount;
        string labels = currentPromptWord;
        if (labels.Length < count) labels = labels.PadRight(count, '　');
        CreateRightSlotsForWord(labels.Substring(0, count));
        remoteTemplate.SetPreviewTargets(rightSlots);
        SetActiveSlot(count - 1);
        remoteTemplate.ShowCharacter(count - 1);
    }

    void TryStartRemoteTracing()
    {
        if (remoteTemplate == null) return;
        if (!string.IsNullOrEmpty(remoteTemplate.Error))
        {
            ShowMessage(remoteTemplate.Error);
            return;
        }
        if (!remoteTemplate.HasFinished) return;
        int count = remoteTemplate.CharacterCount;
        if (!remoteTemplate.HasEveryCharacter())
        {
            ShowMessage("お手本に筆跡のない文字があります。遠隔側とこのシーンを再起動して書き直してください。");
            return;
        }
        if (!string.IsNullOrEmpty(currentPromptWord) && currentPromptWord.Length != count)
        {
            ShowMessage($"入力したお題は{currentPromptWord.Length}文字、お手本は{count}文字です。文字数を合わせて再起動してください。");
            return;
        }
        if (string.IsNullOrEmpty(currentPromptWord)) currentPromptWord = new string('　', count);
        GlobalData.ResultPromptWord = currentPromptWord;
        currentPromptIndex = 0;
        SetActiveSlot(0);
        ShowGhostForCurrentChar();
        if (preCountdownBgmSource != null) preCountdownBgmSource.Stop();
        if (enableStartCountdown)
        {
            status = Status.COUNTDOWN;
            StartCoroutine(CoCountdownThenStart());
        }
        else
        {
            status = Status.READY_TO_WRITE;
            _nextBonusSeconds = 0f;
            ResetCharTimer();
            ShowMessage("");
        }
    }
}
