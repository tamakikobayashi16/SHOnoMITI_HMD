using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RemotePreviewController : MonoBehaviour
{
    public bool useLocalTestTemplate = true;
    public int listenPort = 5005;
    public string nextSceneName = "RemoteGame";
    public float completedDisplaySeconds = 1f;
    RemoteHandwritingReceiver receiver;
    readonly List<GameObject> previews = new List<GameObject>();
    string promptWord, message;
    bool transitioning;
    Material paperMaterial;
    Font uiFont;

    void Start()
    {
        GlobalData.RemoteTemplate = null;
        promptWord = useLocalTestTemplate ? "十" : (GlobalData.PromptWord ?? "");
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
        receiver.paper = paper;
        receiver.guideColor = Color.black;
        receiver.CharacterAdded += CharacterAdded;
        receiveObject.SetActive(true);
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
        yield return receiver.ReplayTestCross();
    }

    void Update()
    {
        if (receiver == null || transitioning) return;
        if (!string.IsNullOrEmpty(receiver.Error)) { message = receiver.Error; return; }
        if (!receiver.HasFinished) return;
        if (!receiver.HasEveryCharacter())
        {
            message = "筆跡のない文字があります。両側を再起動して書き直してください。";
            return;
        }
        if (promptWord.Length > 0 && promptWord.Length != receiver.CharacterCount)
        {
            message = $"お題は{promptWord.Length}文字、お手本は{receiver.CharacterCount}文字です。文字数を合わせて書き直してください。";
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            message = "RemoteGameがBuild Settingsに登録されていません。";
            return;
        }
        transitioning = true;
        GlobalData.RemoteTemplate = receiver.ExportTemplate(promptWord, useLocalTestTemplate);
        message = "お手本が完成しました。なぞりの画面へ進みます。";
        StartCoroutine(ContinueToGame());
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
    }

    void OnDestroy()
    {
        if (paperMaterial != null) Destroy(paperMaterial);
        if (uiFont != null) Destroy(uiFont);
    }
}
