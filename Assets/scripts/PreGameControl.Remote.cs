using UnityEngine;

public partial class PreGameControl
{
    [Header("Remote handwriting (shodoVR PenTablet)")]
    public bool useRemoteTemplate;
    [Tooltip("UDP受信の代わりに座標で作ったテスト文字「あいうえお」を表示します。")]
    public bool useLocalTestTemplate;
    [Tooltip("ローカルのお手本テスト中だけ、バランスボードの接続とキャリブレーションを省略します。")]
    public bool skipBalanceBoardInLocalTest = true;
    bool SkipBalanceBoard => useRemoteTemplate && useLocalTestTemplate && skipBalanceBoardInLocalTest;
    public int remoteListenPort = 5005;
    public Vector2 remoteSourceHalfSize = new Vector2(1.92f, 1.08f);
    [Tooltip("なぞるお手本の表示倍率。1が従来の大きさです。")]
    [Range(0.5f, 2f)] public float remoteTemplateDisplayScale = 1.65f;
    [Range(0.5f, 5f)] public float remoteTemplateLineWidthMultiplier = 2.5f;
    RemoteHandwritingReceiver remoteTemplate;
    RemoteTrialRuntime remoteTrial;
    int remoteTraceStroke = -1;
    bool remoteTraceStarted;
    bool remoteRecordFailed;

    void LateUpdate()
    {
        if (!useRemoteTemplate || timerText == null || moveArea == null) return;
        var paperRenderer = moveArea.GetComponent<Renderer>();
        var camera = Camera.main;
        var parent = timerText.rectTransform.parent as RectTransform;
        if (paperRenderer == null || camera == null || parent == null) return;
        var canvas = timerText.canvas;
        Vector3 screen = camera.WorldToScreenPoint(new Vector3(paperRenderer.bounds.center.x,
            paperRenderer.bounds.max.y, paperRenderer.bounds.center.z));
        if (screen.z <= 0f) return;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        var rt = timerText.rectTransform;
        float halfHeight = rt.rect.height * scale * 0.5f;
        screen.y = Mathf.Clamp(screen.y + halfHeight + 8f, halfHeight, Screen.height - halfHeight);
        var uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out var local))
            rt.localPosition = new Vector3(local.x, local.y, 0f);
    }

    void ConfigureRemoteTimer()
    {
        if (timerText == null) return;
        timerText.gameObject.SetActive(true);
        timerText.enabled = true;
        timerText.rectTransform.anchorMin = timerText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        timerText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        timerText.rectTransform.sizeDelta = new Vector2(200f, 100f);
        timerText.fontSize = 80f;
        timerText.enableWordWrapping = false;
        timerText.color = Color.white;
        timerText.outlineColor = Color.black;
        timerText.outlineWidth = 0.2f;
        timerText.raycastTarget = false;
        timerText.rectTransform.SetAsLastSibling();
    }

    [System.Serializable]
    class TraceLayout
    {
        public float displayScale, lineWidthMultiplier;
        public Vector3 paperCenter, paperSize;
    }

    void RecordRemoteTrace(string kind, Vector3 point, float pressure, float width)
    {
        if (remoteTrial == null) return;
        if (kind == "strokeStarted") remoteTraceStroke++;
        remoteTrial.Recorder.Record(kind, currentPromptIndex, remoteTraceStroke, point, pressure, width: width);
    }

    bool UpdateRemoteTrial()
    {
        if (!useRemoteTemplate || remoteTrial == null) return true;
        if (remoteTrial.Error != null)
        {
            if (!remoteRecordFailed) { remoteRecordFailed = true; remoteTrial.Phase("error", remoteTrial.Error); }
            ShowMessage(remoteTrial.Error + " 記録先を確認して、やり直してください。");
            return false;
        }
        if (status == Status.READY_TO_WRITE && !remoteTraceStarted)
        {
            remoteTraceStarted = true;
            remoteTrial.Phase("tracing");
            var renderer = moveArea != null ? moveArea.GetComponent<Renderer>() : null;
            if (renderer != null) remoteTrial.Recorder.Record("traceLayout", detail: JsonUtility.ToJson(new TraceLayout {
                displayScale = remoteTemplateDisplayScale, lineWidthMultiplier = remoteTemplateLineWidthMultiplier,
                paperCenter = renderer.bounds.center, paperSize = renderer.bounds.size }));
        }
        return true;
    }

    void InitializeRemoteTemplate()
    {
        // Scene references previously pointed at the prefab asset instead of its instance.
        var scenePaper = GameObject.Find("MoveArea");
        if (scenePaper != null) moveArea = paperObject = scenePaper;
        var completedTemplate = GlobalData.RemoteTemplate;
        GlobalData.RemoteTemplate = null;
        if (completedTemplate == null && !useLocalTestTemplate)
        {
            RemoteTrialRuntime.RequestLivePreview = true;
            UnityEngine.SceneManagement.SceneManager.LoadScene("RemotePreview");
            return;
        }
        if (completedTemplate != null) useLocalTestTemplate = completedTemplate.isLocalTest;
        currentPromptWord = completedTemplate != null ? (completedTemplate.promptWord ?? "")
            : (useLocalTestTemplate ? RemoteTestHandwriting.Word : (GlobalData.PromptWord ?? ""));
        currentPromptIndex = 0;
        remoteTrial = RemoteTrialRuntime.Active;
        if (remoteTrial == null || completedTemplate == null || remoteTrial.TrialId != completedTemplate.trialId)
            remoteTrial = RemoteTrialRuntime.Begin(useLocalTestTemplate, 5006, currentPromptWord, "");
        if (trailDrawer != null) trailDrawer.TraceRecorded += RecordRemoteTrace;
        var receiverObject = new GameObject("RemoteHandwriting");
        receiverObject.transform.SetParent(transform, false);
        receiverObject.SetActive(false);
        remoteTemplate = receiverObject.AddComponent<RemoteHandwritingReceiver>();
        remoteTemplate.listenPort = remoteListenPort;
        remoteTemplate.receiveUdp = completedTemplate == null && !useLocalTestTemplate;
        remoteTemplate.sourceHalfSize = remoteSourceHalfSize;
        remoteTemplate.paper = moveArea;
        remoteTemplate.displayScale = remoteTemplateDisplayScale;
        remoteTemplate.lineWidthMultiplier = remoteTemplateLineWidthMultiplier;
        remoteTemplate.CharacterAdded += RefreshRemoteSlots;
        remoteTemplate.SetPreviewTargets(rightSlots);
        receiverObject.SetActive(true);
        if (completedTemplate != null) remoteTemplate.LoadTemplate(completedTemplate);
        else if (useLocalTestTemplate) remoteTemplate.LoadTestWord();
        if (remoteTemplate.HasFinished)
            remoteTrial.SaveTemplate(remoteTemplate.ExportTemplate(currentPromptWord, useLocalTestTemplate));
    }

    void RefreshRemoteSlots()
    {
        int count = remoteTemplate.CharacterCount;
        string labels = currentPromptWord;
        if (labels.Length < count) labels = labels.PadRight(count, '　');
        CreateRightSlotsForWord(labels.Substring(0, count));
        remoteTemplate.SetPreviewTargets(rightSlots);
        // Loading the completed word adds characters in sequence; keep the tracing selection
        // on the first character instead of following the last character added.
        int selected = Mathf.Clamp(currentPromptIndex, 0, count - 1);
        SetActiveSlot(selected);
        remoteTemplate.ShowCharacter(selected);
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
