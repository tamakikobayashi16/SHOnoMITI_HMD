using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
public class RemotePreviewController : MonoBehaviour
{
    public bool useLocalTestTemplate = true;
    public int listenPort = 5005;
    public int controlPort = 5006;
    [Tooltip("記録用の匿名参加者ID。空欄でも実行できます。")]
    public string participantId = "";
    public float drawingIdleTimeoutSeconds = 60f;
    public string nextSceneName = "RemoteGame";
    public float completedDisplaySeconds = 1f;
    [Tooltip("お手本の表示倍率。1が従来の大きさです。")]
    [Range(0.5f, 2f)] public float templateDisplayScale = 1.65f;
    [Range(0.5f, 5f)] public float templateLineWidthMultiplier = 2.5f;
    RemoteHandwritingReceiver receiver;
    readonly List<GameObject> previews = new List<GameObject>();
    string promptWord, message;
    bool transitioning;
    public GameObject backgroundPrefab;
    public Transform rightPanelParent;
    public GameObject galleryArea;
    public TMP_Text headingText, statusText;
    RemoteTrialRuntime trial;
    bool failed, drawingStarted;
    float drawingStartedAt;
    Button readyButton, retryButton;

    void Start()
    {
        if (RemoteTrialRuntime.RequestLivePreview)
        { useLocalTestTemplate = false; RemoteTrialRuntime.RequestLivePreview = false; }
        GlobalData.RemoteTemplate = null;
        promptWord = useLocalTestTemplate ? RemoteTestHandwriting.Word : (GlobalData.PromptWord ?? "");
        // Reuse RemoteGame's scene camera, room, paper and thumbnail prefab.
        var paper = GameObject.Find("MoveArea");
        // The copied paper was parented to the overlay Canvas with a 1920x1080 offset.
        // Keep its original world-space size independently of the preview window size.
        paper.transform.SetParent(null, false);
        paper.transform.position = new Vector3(0f, 0f, 3f);
        // Freeze only the gameplay exit animations, just as RemoteGame does.
        // The room's sliding doors and camera keep their normal opening animation.
        foreach (var target in new[] { paper, rightPanelParent.gameObject, galleryArea })
        {
            var animator = target.GetComponent<Animator>();
            if (animator == null) continue;
            animator.Update(0f);
            animator.enabled = false;
        }
        BuildInterface();
        var paperSize = paper.GetComponent<Renderer>().bounds.size;
        float top = galleryArea.GetComponent<Renderer>().bounds.max.y - 0.75f;
        for (int i = 0; i < RemoteHandwritingReceiver.MaxCharacters; i++)
        {
            var slot = Instantiate(backgroundPrefab, rightPanelParent, false);
            slot.name = "CharacterPreview_" + i;
            slot.transform.position = new Vector3(rightPanelParent.position.x, top - i * 2.4f, rightPanelParent.position.z);
            slot.transform.rotation = rightPanelParent.rotation;
            var size = slot.GetComponent<Renderer>().bounds.size;
            slot.transform.localScale = Vector3.Scale(slot.transform.localScale,
                new Vector3(paperSize.x / size.x, paperSize.y / size.y, 1f) * 0.28f);
            foreach (var text in slot.GetComponentsInChildren<TMP_Text>()) text.text = "";
            previews.Add(slot);
        }
        var receiveObject = new GameObject("RemoteHandwriting");
        receiveObject.SetActive(false);
        receiver = receiveObject.AddComponent<RemoteHandwritingReceiver>();
        receiver.listenPort = listenPort;
        receiver.receiveUdp = !useLocalTestTemplate;
        receiver.acceptRemotePackets = useLocalTestTemplate;
        receiver.paper = paper;
        receiver.fitWhileReceiving = true;
        receiver.displayScale = templateDisplayScale;
        receiver.lineWidthMultiplier = templateLineWidthMultiplier;
        receiver.guideColor = Color.black;
        receiver.CharacterAdded += CharacterAdded;
        receiver.SetPreviewTargets(previews);
        receiveObject.SetActive(true);
        if (promptWord.Length > RemoteHandwritingReceiver.MaxCharacters)
        {
            receiver.enabled = false;
            message = "お題は最大3文字です。InputNameに戻って入力し直してください。";
            return;
        }
        trial = RemoteTrialRuntime.Begin(useLocalTestTemplate, controlPort, promptWord, participantId);
        receiver.expectedSession = trial.TrialId;
        receiver.PacketApplied += packet => trial.Recorder.Record(packet.eventType, packet.character, packet.stroke,
            new Vector3(packet.x, packet.y, packet.z), packet.pressure, packet.sequence);
        message = useLocalTestTemplate ? "テストのお手本を書いています…" : "遠隔者のお手本を待っています…";
        if (useLocalTestTemplate) StartCoroutine(PlayTest());
    }

    void BuildInterface()
    {
        ConfigureText(headingText, new Vector2(0.5f, 1f), new Vector2(0, -55), new Vector2(1100, 80), 48);
        ConfigureText(statusText, new Vector2(0.5f, 0f), new Vector2(0, 115), new Vector2(1500, 90), 30);
        readyButton = CreateButton("準備完了", new Vector2(-140, 40), () => {
            if (trial?.Link == null || !trial.Link.Connected || failed || transitioning) return;
            trial.Link.LocalReady = true;
            trial.Recorder.Record("learnerReady", detail: "ready to observe; balance board calibration follows template completion");
        });
        retryButton = CreateButton("やり直す", new Vector2(140, 40), Retry);
    }

    void ConfigureText(TMP_Text text, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
    {
        text.gameObject.SetActive(true);
        text.enabled = true;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = anchor;
        text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        text.rectTransform.anchoredPosition = position;
        text.rectTransform.sizeDelta = size;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.outlineColor = Color.black;
        text.outlineWidth = 0.2f;
        text.raycastTarget = false;
        text.rectTransform.SetAsLastSibling();
    }

    Button CreateButton(string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(statusText.transform.parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(240, 60);
        go.GetComponent<Image>().color = new Color(0, 0, 0, 0.65f);
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(action);
        var text = Instantiate(headingText, go.transform);
        ConfigureText(text, new Vector2(0.5f, 0.5f), Vector2.zero, rect.sizeDelta, 30);
        text.text = label;
        return button;
    }

    void LateUpdate()
    {
        if (headingText == null) return;
        headingText.text = "遠隔のお手本" + (receiver != null && receiver.CharacterCount > 0 ? "　" + receiver.CharacterCount + "文字目" : "");
        statusText.text = message ?? "準備中…";
        readyButton.gameObject.SetActive(!useLocalTestTemplate && !failed && !transitioning &&
            trial?.Link != null && trial.Link.Connected && !trial.Link.LocalReady);
        retryButton.gameObject.SetActive(!transitioning);
    }

    void CharacterAdded()
    {
        receiver.ShowCharacter(receiver.CharacterCount - 1);
        message = $"お手本の{receiver.CharacterCount}文字目を書いています…";
    }

    IEnumerator PlayTest()
    {
        yield return new WaitForSeconds(0.6f);
        yield return receiver.ReplayTestWord();
    }

    void Update()
    {
        if (receiver == null || transitioning) return;
        receiver.displayScale = templateDisplayScale;
        receiver.lineWidthMultiplier = templateLineWidthMultiplier;
        if (failed || trial == null) return;
        if (!string.IsNullOrEmpty(trial.Error)) { Fail(trial.Error); return; }
        if (!string.IsNullOrEmpty(receiver.Error)) { Fail(receiver.Error); return; }
        if (!useLocalTestTemplate)
        {
            var link = trial.Link;
            if (link == null) { Fail("接続確認を開始できませんでした。"); return; }
            if (!string.IsNullOrEmpty(link.PeerError)) { Fail("遠隔者側のエラー: " + link.PeerError); return; }
            if (link.TimedOut) { Fail("遠隔者との通信が確認できません。接続を確認して、やり直してください。"); return; }
            if (!drawingStarted)
            {
                message = !link.Connected ? "遠隔者の接続を待っています…"
                    : !link.State.deviceReady ? "遠隔者のペンタブの準備を待っています…"
                    : !link.LocalReady ? "遠隔者に接続しました。お手本を見る準備ができたら「準備完了」を押してください。"
                    : "こちらは準備完了です。遠隔者の準備完了を待っています…";
                if (!link.CanDraw) return;
                drawingStarted = true;
                drawingStartedAt = Time.realtimeSinceStartup;
                trial.Recorder.Record("bothReady");
                receiver.acceptRemotePackets = true;
                message = "両側の準備ができました。遠隔者がお手本を書きます。";
            }
            if (!link.State.deviceReady) { Fail("遠隔者のペンタブが利用できなくなりました。接続を確認してやり直してください。"); return; }
            if (!receiver.HasFinished && Time.realtimeSinceStartup - Mathf.Max(drawingStartedAt, receiver.LastPacketTime)
                > Mathf.Max(5f, drawingIdleTimeoutSeconds))
            { Fail("お手本の入力が一定時間ありません。やり直してください。"); return; }
        }
        if (!receiver.HasFinished) return;
        if (!receiver.HasEveryCharacter())
        {
            Fail("筆跡のない文字があります。やり直してください。");
            return;
        }
        if (promptWord.Length > 0 && promptWord.Length != receiver.CharacterCount)
        {
            Fail($"お題は{promptWord.Length}文字、お手本は{receiver.CharacterCount}文字です。文字数を合わせてやり直してください。");
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            message = "RemoteGameがBuild Settingsに登録されていません。";
            return;
        }
        transitioning = true;
        GlobalData.RemoteTemplate = receiver.ExportTemplate(promptWord, useLocalTestTemplate);
        trial.SaveTemplate(GlobalData.RemoteTemplate);
        if (trial.Recorder.Error != null) { transitioning = false; Fail(trial.Recorder.Error); return; }
        trial.Phase("templateCompleted");
        message = "お手本が完成しました。なぞりの画面へ進みます。";
        StartCoroutine(ContinueToGame());
    }

    void Fail(string reason)
    {
        failed = true;
        message = reason;
        receiver.acceptRemotePackets = false;
        StopAllCoroutines();
        trial?.Phase("error", reason);
    }

    public void Retry()
    {
        StopAllCoroutines();
        GlobalData.RemoteTemplate = null;
        // Closing the drawing socket before reload also discards queued packets from the old trial.
        if (receiver != null) receiver.enabled = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    IEnumerator ContinueToGame()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, completedDisplaySeconds));
        SceneManager.LoadScene(nextSceneName);
    }

}
