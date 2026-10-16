using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    Material paperMaterial;
    Font uiFont;
    RemoteTrialRuntime trial;
    bool failed, drawingStarted;
    float drawingStartedAt;
    Camera previewCamera;
    readonly List<Material> decorationMaterials = new List<Material>();
    readonly List<GameObject> slotMarks = new List<GameObject>();
    static readonly Color Ink = new Color(0.19f, 0.16f, 0.12f);
    static readonly Color Vermilion = new Color(0.62f, 0.20f, 0.15f);

    void Start()
    {
        if (RemoteTrialRuntime.RequestLivePreview)
        { useLocalTestTemplate = false; RemoteTrialRuntime.RequestLivePreview = false; }
        GlobalData.RemoteTemplate = null;
        promptWord = useLocalTestTemplate ? RemoteTestHandwriting.Word : (GlobalData.PromptWord ?? "");
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        previewCamera = camera;
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.91f, 0.87f, 0.77f);
        camera.transform.position = new Vector3(0f, 0f, -7f);
        paperMaterial = new Material(Shader.Find("Unlit/Color"));
        paperMaterial.color = new Color(0.99f, 0.98f, 0.94f);
        uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Mincho", "MS Mincho", "Yu Gothic", "Arial" }, 24);
        BuildJapaneseBackdrop();
        AddPanel("PaperShadow", new Vector3(-1.34f, -0.07f, 3.10f), new Vector2(7.44f, 6.64f), new Color(0.66f, 0.60f, 0.48f));
        AddPanel("PaperBorder", new Vector3(-1.4f, 0f, 3.05f), new Vector2(7.36f, 6.56f), new Color(0.34f, 0.25f, 0.16f));
        var paper = CreatePaper("LiveTemplatePaper", new Vector3(-1.4f, 0f, 3f), new Vector2(7.2f, 6.4f));
        for (int i = 0; i < RemoteHandwritingReceiver.MaxCharacters; i++)
        {
            float y = 2.65f - i * 1.3f;
            AddPanel("PreviewFrame_" + i, new Vector3(4f, y, 3.05f), new Vector2(1.12f, 1.12f), new Color(0.57f, 0.46f, 0.32f));
            previews.Add(CreatePaper("CharacterPreview_" + i, new Vector3(4f, y, 3f), new Vector2(1.04f, 1.04f)));
            var mark = AddPanel("SelectedCharacter_" + i, new Vector3(3.28f, y, 2.98f), new Vector2(0.045f, 1.12f), Vermilion);
            mark.SetActive(false);
            slotMarks.Add(mark);
        }
        var receiveObject = new GameObject("RemoteHandwriting");
        receiveObject.SetActive(false);
        receiver = receiveObject.AddComponent<RemoteHandwritingReceiver>();
        receiver.listenPort = listenPort;
        receiver.receiveUdp = !useLocalTestTemplate;
        receiver.acceptRemotePackets = useLocalTestTemplate;
        receiver.paper = paper;
        receiver.displayScale = templateDisplayScale;
        receiver.lineWidthMultiplier = templateLineWidthMultiplier;
        receiver.guideColor = Color.black;
        receiver.CharacterAdded += CharacterAdded;
        receiver.SetPreviewTargets(previews);
        receiveObject.SetActive(true);
        if (promptWord.Length > RemoteHandwritingReceiver.MaxCharacters)
        {
            receiver.enabled = false;
            message = "お題は最大5文字です。InputNameに戻って入力し直してください。";
            return;
        }
        trial = RemoteTrialRuntime.Begin(useLocalTestTemplate, controlPort, promptWord, participantId);
        receiver.expectedSession = trial.TrialId;
        receiver.PacketApplied += packet => trial.Recorder.Record(packet.eventType, packet.character, packet.stroke,
            new Vector3(packet.x, packet.y, packet.z), packet.pressure, packet.sequence);
        message = useLocalTestTemplate ? "テストのお手本を書いています…" : "遠隔者のお手本を待っています…";
        if (useLocalTestTemplate) StartCoroutine(PlayTest());
    }

    GameObject CreatePaper(string name, Vector3 position, Vector2 size)
    {
        var paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
        paper.name = name;
        paper.transform.position = position;
        paper.transform.localScale = new Vector3(size.x, size.y, 1f);
        paper.GetComponent<Renderer>().sharedMaterial = paperMaterial;
        return paper;
    }

    GameObject AddPanel(string name, Vector3 position, Vector2 size, Color color)
    {
        var material = new Material(Shader.Find("Unlit/Color")) { color = color };
        decorationMaterials.Add(material);
        var panel = CreatePaper(name, position, size);
        panel.GetComponent<Renderer>().sharedMaterial = material;
        Destroy(panel.GetComponent<Collider>());
        return panel;
    }

    void BuildJapaneseBackdrop()
    {
        var wood = new Color(0.39f, 0.29f, 0.18f);
        // Shoji lattice at the sides leaves the writing surface free of decoration.
        for (int side = -1; side <= 1; side += 2)
        {
            AddPanel("ShojiPaper", new Vector3(side * 9f, 0, 4.5f), new Vector2(6f, 12f), new Color(0.84f, 0.79f, 0.66f));
            for (int i = 0; i < 7; i++)
                AddPanel("ShojiVertical", new Vector3(side * (6f + i), 0, 4.4f), new Vector2(0.045f, 12f), wood);
            for (int i = -5; i <= 5; i++)
                AddPanel("ShojiHorizontal", new Vector3(side * 9f, i, 4.4f), new Vector2(6f, 0.045f), wood);
        }
        AddPanel("HeadingRule", new Vector3(-0.3f, 3.72f, 4f), new Vector2(10.2f, 0.018f), new Color(0.61f, 0.50f, 0.35f));
        AddPanel("FooterRule", new Vector3(-0.3f, -3.66f, 4f), new Vector2(10.2f, 0.018f), new Color(0.61f, 0.50f, 0.35f));
    }

    void CharacterAdded()
    {
        for (int i = 0; i < slotMarks.Count; i++) slotMarks[i].SetActive(i == receiver.CharacterCount - 1);
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
        if (previewCamera != null) previewCamera.orthographicSize = Mathf.Max(5f, 5.8f / previewCamera.aspect);
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

    void OnGUI()
    {
        if (previewCamera == null) return;
        float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 900f);
        var style = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = Mathf.Max(14, Mathf.RoundToInt(23 * scale)),
            alignment = TextAnchor.MiddleLeft, wordWrap = true };
        style.normal.textColor = Ink;
        var title = new GUIStyle(style) { fontSize = Mathf.Max(22, Mathf.RoundToInt(38 * scale)) };
        LabelWorld(new Vector3(-5.08f, 4.33f, 3), "遠隔のお手本", title, 620 * scale, 60 * scale);
        var subtitle = new GUIStyle(style) { fontSize = Mathf.Max(12, Mathf.RoundToInt(17 * scale)) };
        LabelWorld(new Vector3(-5.08f, 3.91f, 3), "筆の運びを見てから、一文字ずつなぞりましょう。", subtitle, 660 * scale, 30 * scale);
        LabelWorld(new Vector3(-5f, 3.44f, 3), "お題　" + (string.IsNullOrEmpty(promptWord) ? "自由課題" : promptWord), subtitle, 500 * scale, 28 * scale);
        LabelWorld(new Vector3(3.36f, 3.44f, 3), "お手本一覧", subtitle, 200 * scale, 28 * scale);
        for (int i = 0; i < previews.Count; i++)
            LabelWorld(new Vector3(4.73f, 2.65f - i * 1.3f, 3), (i + 1) + "", subtitle, 40 * scale, 30 * scale);
        var stateStyle = new GUIStyle(subtitle);
        stateStyle.normal.textColor = Vermilion;
        string state = failed ? "要確認" : transitioning ? "完成" : useLocalTestTemplate ? "テスト"
            : drawingStarted ? "受信中" : trial?.Link?.Connected == true ? "準備待ち" : "接続待ち";
        LabelWorld(new Vector3(3.36f, 4.24f, 3), "［ " + state + " ］", stateStyle, 220 * scale, 40 * scale);
        float footerY = Screen.height * 0.875f;
        float footerLeft = previewCamera.WorldToScreenPoint(new Vector3(-5.3f, 0, 3)).x;
        float footerRight = previewCamera.WorldToScreenPoint(new Vector3(4.9f, 0, 3)).x;
        float buttonWidth = 150 * scale, buttonHeight = 48 * scale;
        Color previousContent = GUI.contentColor;
        GUI.contentColor = Ink;
        GUI.Label(new Rect(footerLeft, footerY, footerRight - footerLeft - 2 * buttonWidth - 30 * scale, 84 * scale),
            message ?? "準備中…", style);
        GUI.contentColor = previousContent;
        var buttonStyle = new GUIStyle(GUI.skin.button) { font = uiFont, fontSize = Mathf.Max(14, Mathf.RoundToInt(21 * scale)) };
        buttonStyle.normal.background = buttonStyle.hover.background = buttonStyle.active.background = Texture2D.whiteTexture;
        buttonStyle.normal.textColor = buttonStyle.hover.textColor = buttonStyle.active.textColor = Color.white;
        Color previousBackground = GUI.backgroundColor;
        if (!transitioning)
        {
            GUI.backgroundColor = Vermilion;
            if (!useLocalTestTemplate && !failed && trial?.Link != null && trial.Link.Connected &&
                !trial.Link.LocalReady && GUI.Button(new Rect(footerRight - 2 * buttonWidth - 12 * scale, footerY,
                    buttonWidth, buttonHeight), "準備完了", buttonStyle))
            {
                trial.Link.LocalReady = true;
                trial.Recorder.Record("learnerReady", detail: "ready to observe; balance board calibration follows template completion");
            }
            GUI.backgroundColor = new Color(0.40f, 0.33f, 0.23f);
            if (GUI.Button(new Rect(footerRight - buttonWidth, footerY, buttonWidth, buttonHeight), "やり直す", buttonStyle)) Retry();
        }
        GUI.backgroundColor = previousBackground;
    }

    void LabelWorld(Vector3 position, string text, GUIStyle style, float width, float height)
    {
        Vector3 screen = previewCamera.WorldToScreenPoint(position);
        GUI.Label(new Rect(screen.x, Screen.height - screen.y - height * 0.5f, width, height), text, style);
    }

    void OnDestroy()
    {
        if (paperMaterial != null) Destroy(paperMaterial);
        if (uiFont != null) Destroy(uiFont);
        foreach (var material in decorationMaterials) if (material != null) Destroy(material);
    }
}
