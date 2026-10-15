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
    RemoteHandwritingReceiver receiver;
    readonly List<GameObject> previews = new List<GameObject>();
    string promptWord, message;
    bool transitioning;
    Material paperMaterial;
    Font uiFont;
    RemoteTrialRuntime trial;
    bool failed, drawingStarted;
    float drawingStartedAt;

    void Start()
    {
        if (RemoteTrialRuntime.RequestLivePreview)
        { useLocalTestTemplate = false; RemoteTrialRuntime.RequestLivePreview = false; }
        GlobalData.RemoteTemplate = null;
        promptWord = useLocalTestTemplate ? RemoteTestHandwriting.Word : (GlobalData.PromptWord ?? "");
        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.12f, 0.14f, 0.17f);
        camera.transform.position = new Vector3(0f, 0f, -7f);
        paperMaterial = new Material(Shader.Find("Unlit/Color"));
        paperMaterial.color = new Color(0.98f, 0.97f, 0.93f);
        uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Meiryo", "Arial" }, 24);
        var paper = CreatePaper("LiveTemplatePaper", new Vector3(-1.4f, 0f, 3f), new Vector2(7.2f, 6.4f));
        var receiveObject = new GameObject("RemoteHandwriting");
        receiveObject.SetActive(false);
        receiver = receiveObject.AddComponent<RemoteHandwritingReceiver>();
        receiver.listenPort = listenPort;
        receiver.receiveUdp = !useLocalTestTemplate;
        receiver.acceptRemotePackets = useLocalTestTemplate;
        receiver.paper = paper;
        receiver.displayScale = templateDisplayScale;
        receiver.guideColor = Color.black;
        receiver.CharacterAdded += CharacterAdded;
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

    void CharacterAdded()
    {
        while (previews.Count < receiver.CharacterCount)
            previews.Add(CreatePaper("CharacterPreview_" + previews.Count, Vector3.zero, Vector2.one));
        float spacing = Mathf.Min(1.7f, 7f / Mathf.Max(1, previews.Count));
        for (int i = 0; i < previews.Count; i++)
        {
            previews[i].transform.position = new Vector3(4f, 3.1f - i * spacing, 3f);
            previews[i].transform.localScale = new Vector3(spacing * 0.85f, spacing * 0.85f, 1f);
        }
        receiver.SetPreviewTargets(previews);
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
        var style = new GUIStyle(GUI.skin.label) { font = uiFont, fontSize = 24,
            alignment = TextAnchor.MiddleCenter, wordWrap = true };
        GUI.Label(new Rect(20, 15, Screen.width - 40, 45), "遠隔のお手本", style);
        GUI.Label(new Rect(20, Screen.height - 95, Screen.width - 40, 80), message ?? "準備中…", style);
        if (!transitioning)
        {
            if (!useLocalTestTemplate && !failed && trial?.Link != null && trial.Link.Connected &&
                !trial.Link.LocalReady && GUI.Button(new Rect(Screen.width / 2 - 190, Screen.height - 145, 180, 40), "準備完了"))
            {
                trial.Link.LocalReady = true;
                trial.Recorder.Record("learnerReady", detail: "ready to observe; balance board calibration follows template completion");
            }
            if (GUI.Button(new Rect(Screen.width / 2 + 10, Screen.height - 145, 180, 40), "やり直す")) Retry();
        }
    }

    void OnDestroy()
    {
        if (paperMaterial != null) Destroy(paperMaterial);
        if (uiFont != null) Destroy(uiFont);
    }
}
