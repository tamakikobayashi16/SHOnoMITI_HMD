// TitleScreenController.cs
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class TitleScreenController : MonoBehaviour
{
    [Header("Title")]
    [SerializeField] private TMP_Text titleText;
    [SerializeField, TextArea] private string gameTitle = "書の道"; // 任意に変更OK
    [SerializeField] private bool verticalTitle = true;
    [SerializeField, Tooltip("1文字あたりの出現間隔(秒)")] private float titleWriteInterval = 0.12f;

    [Header("UI")]
    [SerializeField] private CanvasGroup fader;            // 黒フェード
    [SerializeField] private CanvasGroup menuGroup;         // メニューRoot(CanvasGroup)
    [SerializeField] private TMP_Text pressAnyKeyText;      // 点滅表示
    [SerializeField] private RectTransform rakkanStamp;     // 朱印Image（★エフェクトなし・静止表示）
    [SerializeField, Tooltip("任意キーですぐゲームへ(メニュー無し)")]
    private bool goDirectlyToGameOnAnyKey = false;

    [Header("Scenes")]
    [SerializeField] private string nextSceneName = "PreGame";
    [SerializeField] private string tutorialSceneName = "Tutorial";

    [Header("SFX (任意)")]
    [SerializeField] private AudioSource brushSfx; // 筆サウンド(ループ)
    [SerializeField] private AudioSource clickSfx; // ボタン

    private bool introFinished = false;
    private bool isTransitioning = false;

    private void Awake()
    {
        Application.targetFrameRate = 60;
        if (menuGroup != null)
        {
            menuGroup.alpha = 0f;
            menuGroup.interactable = false;
            menuGroup.blocksRaycasts = false;
        }
        if (pressAnyKeyText != null) pressAnyKeyText.gameObject.SetActive(false);
    }

    private IEnumerator Start()
    {
        SetupTitle();

        // フェードイン
        if (fader != null) fader.alpha = 1f;
        yield return FadeCanvasGroup(fader, 1f, 0f, 0.8f);

        // タイトル描画アニメ
        yield return WriteTitleAnimation();

        // ★落款エフェクトなし：静止表示だけ整える
        if (rakkanStamp != null)
        {
            rakkanStamp.localScale = Vector3.one;
            rakkanStamp.localRotation = Quaternion.identity;
        }

        // Press Any Key
        if (!goDirectlyToGameOnAnyKey && pressAnyKeyText != null)
        {
            pressAnyKeyText.gameObject.SetActive(true);
            StartCoroutine(BlinkText(pressAnyKeyText));
        }

        introFinished = true;

        if (goDirectlyToGameOnAnyKey)
        {
            // 任意キーですぐゲームへ
            yield return WaitAnyKey();
            yield return StartCoroutine(GoNextScene(nextSceneName));
        }
        else
        {
            // 任意キーでメニュー表示
            yield return WaitAnyKey();
            if (pressAnyKeyText != null) pressAnyKeyText.gameObject.SetActive(false);
            yield return FadeCanvasGroup(menuGroup, 0f, 1f, 0.35f, true);
        }
    }

    private void SetupTitle()
    {
        if (!titleText) return;
        string t = gameTitle;
        if (verticalTitle)
        {
            // 縦組みっぽく見せるために1文字ずつ改行
            var sb = new StringBuilder();
            for (int i = 0; i < t.Length; i++)
            {
                sb.Append(t[i]);
                if (i < t.Length - 1) sb.Append('\n');
            }
            t = sb.ToString();
        }
        titleText.text = t;
        titleText.maxVisibleCharacters = 0;
    }

    private IEnumerator WriteTitleAnimation()
    {
        if (!titleText) yield break;

        // textInfo更新
        titleText.ForceMeshUpdate();
        int total = titleText.textInfo.characterCount;

        if (brushSfx != null)
        {
            brushSfx.loop = true;
            brushSfx.Play();
        }

        for (int i = 0; i <= total; i++)
        {
            titleText.maxVisibleCharacters = i;
            yield return new WaitForSeconds(titleWriteInterval);
        }

        if (brushSfx != null) brushSfx.Stop();
    }

    private IEnumerator WaitAnyKey()
    {
        // ユーザーの任意操作待ち（キーボード／マウス左／パッドA）
        while (true)
        {
            if (Input.anyKeyDown) yield break;
            if (Input.GetMouseButtonDown(0)) yield break;
            if (Input.GetKeyDown(KeyCode.JoystickButton0)) yield break;
            yield return null;
        }
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration, bool enableAtEnd = false)
    {
        if (cg == null) yield break;
        cg.alpha = from;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            cg.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        cg.alpha = to;
        cg.interactable = enableAtEnd && to >= 0.99f;
        cg.blocksRaycasts = cg.interactable;
    }

    private IEnumerator GoNextScene(string sceneName)
    {
        if (isTransitioning) yield break;
        isTransitioning = true;
        if (clickSfx != null) clickSfx.Play();

        yield return FadeCanvasGroup(fader, fader ? fader.alpha : 0f, 1f, 0.6f);
        yield return new WaitForSeconds(0.05f);

        if (!string.IsNullOrEmpty(sceneName))
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    private IEnumerator BlinkText(TMP_Text t)
    {
        float timer = 0f;
        while (!isTransitioning)
        {
            timer += Time.deltaTime;
            float a = 0.5f + 0.5f * Mathf.Sin(timer * 5f);
            var c = t.color;
            c.a = Mathf.Lerp(0.35f, 1f, a);
            t.color = c;
            yield return null;
        }
    }

    // ==== Button Events ====
    public void OnClickStart()
    {
        if (!introFinished || isTransitioning) return;
        StartCoroutine(GoNextScene(nextSceneName));
    }

    public void OnClickTutorial()
    {
        if (!introFinished || isTransitioning) return;
        StartCoroutine(GoNextScene(tutorialSceneName));
    }

    public void OnClickQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    [Header("Optional Panels")]
    [SerializeField] private GameObject howToPanel;
    [SerializeField] private GameObject settingsPanel;

    public void OnClickHowTo()
    {
        if (clickSfx != null) clickSfx.Play();
        ShowPanel(howToPanel, true);
    }

    public void OnClickSettings()
    {
        if (clickSfx != null) clickSfx.Play();
        ShowPanel(settingsPanel, true);
    }

    public void OnClickBack()
    {
        if (clickSfx != null) clickSfx.Play();
        ShowPanel(howToPanel, false);
        ShowPanel(settingsPanel, false);
    }

    private void ShowPanel(GameObject panel, bool show)
    {
        if (!panel) return;
        panel.SetActive(show);
    }
}
