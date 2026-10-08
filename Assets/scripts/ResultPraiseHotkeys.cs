// ResultPraiseHotkeys.cs 〈縦書き固定・1〜6・中央ぞろえ版＋効果音〉
using System.Collections;
using System.Text;
using UnityEngine;
using TMPro;

[DefaultExecutionOrder(200)]
public class ResultPraiseHotkeys : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("表示先の TextMeshProUGUI")]
    public TextMeshProUGUI targetText;

    [Header("Timing")]
    [Tooltip("表示のキープ時間（秒）")]
    public float holdSeconds = 1.2f;
    [Tooltip("フェードアウト時間（秒）")]
    public float fadeOutSeconds = 0.35f;
    [Tooltip("ポップ表示の拡大率（1.0 = 拡大なし）")]
    public float popScale = 1.15f;
    [Tooltip("ポップ表示のイーズ時間（秒）")]
    public float popEaseSeconds = 0.12f;

    [Header("Vertical Layout")]
    [Tooltip("縦書き時の行間（TextMeshPro の Line Spacing）")]
    public float verticalLineSpacing = 0f;
    [Tooltip("縦書き時、スペースは改行に置き換える（単語間を1行空ける感じ）")]
    public bool spaceAsNewline = true;

    [Header("Phrases (1〜6)")]
    public string key1Phrase = "力強い！";
    public string key2Phrase = "芸術的！";
    public string key3Phrase = "起筆がきれい！";   
    public string key4Phrase = "終筆がきれい！";  
    public string key5Phrase = "線がきれい！";    
    public string key6Phrase = "完璧！";           

    [Header("Audio")]
    [Tooltip("効果音を再生する AudioSource（PlayOnAwake は OFF 推奨）")]
    public AudioSource sfxSource;
    [Tooltip("個別が未設定のときに使うデフォルト効果音")]
    public AudioClip sfxDefault;
    [Tooltip("キー1〜6の個別効果音（未設定なら sfxDefault を使用）")]
    public AudioClip sfx1, sfx2, sfx3, sfx4, sfx5, sfx6;

    Coroutine _running;
    Vector3 _baseScale;
    Color _baseColor;
    float _origLineSpacing;

    void Awake()
    {
        if (targetText == null)
        {
            Debug.LogWarning("[ResultPraiseHotkeys] targetText が未設定です。Canvas 上の TextMeshProUGUI を割り当ててください。");
            return;
        }
        _baseScale = targetText.rectTransform.localScale;
        _baseColor = targetText.color;
        _origLineSpacing = targetText.lineSpacing;

        // 初期は透明
        var c = targetText.color;
        c.a = 0f;
        targetText.color = c;
        if (!targetText.gameObject.activeSelf) targetText.gameObject.SetActive(true);

        // 縦書き＆中央ぞろえ固定
        targetText.enableWordWrapping = false;
        targetText.alignment = TextAlignmentOptions.Center; 
        targetText.lineSpacing = verticalLineSpacing;
    }

    void OnValidate()
    {
        if (targetText != null)
        {
            targetText.lineSpacing = verticalLineSpacing;
            targetText.alignment = TextAlignmentOptions.Center; 
        }
    }

    void Update()
    {
        if (targetText == null) return;

        if (GetDown(KeyCode.Alpha1, KeyCode.Keypad1)) { PlayKeySfx(1); Show(key1Phrase); }
        else if (GetDown(KeyCode.Alpha2, KeyCode.Keypad2)) { PlayKeySfx(2); Show(key2Phrase); }
        else if (GetDown(KeyCode.Alpha3, KeyCode.Keypad3)) { PlayKeySfx(3); Show(key3Phrase); }
        else if (GetDown(KeyCode.Alpha4, KeyCode.Keypad4)) { PlayKeySfx(4); Show(key4Phrase); }
        else if (GetDown(KeyCode.Alpha5, KeyCode.Keypad5)) { PlayKeySfx(5); Show(key5Phrase); }
        else if (GetDown(KeyCode.Alpha6, KeyCode.Keypad6)) { PlayKeySfx(6); Show(key6Phrase); }
    }

    bool GetDown(KeyCode main, KeyCode keypad)
    {
        return Input.GetKeyDown(main) || Input.GetKeyDown(keypad);
    }

    
    void PlayKeySfx(int keyIndex)
    {
        if (sfxSource == null) return;

        AudioClip clip = null;
        switch (keyIndex)
        {
            case 1: clip = sfx1; break;
            case 2: clip = sfx2; break;
            case 3: clip = sfx3; break;
            case 4: clip = sfx4; break;
            case 5: clip = sfx5; break;
            case 6: clip = sfx6; break;
        }
        if (clip == null) clip = sfxDefault;
        if (clip != null) sfxSource.PlayOneShot(clip);
    }

    public void Show(string message)
    {
        if (!gameObject.activeInHierarchy) return;
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(CoShow(message));
    }

    IEnumerator CoShow(string message)
    {
        // 縦書き整形（1文字ずつ改行）
        string display = ToVertical(message, spaceAsNewline);

        // レイアウト（縦書き＆中央ぞろえ固定）
        targetText.enableWordWrapping = false;
        targetText.alignment = TextAlignmentOptions.Center; 
        targetText.lineSpacing = verticalLineSpacing;

        targetText.text = display;

        // ポップ（拡大→元）
        float t = 0f;
        Vector3 startScale = _baseScale * popScale;
        Vector3 endScale = _baseScale;

        var c = targetText.color;
        c.a = 1f;
        targetText.color = c;

        targetText.rectTransform.localScale = startScale;

        float dur = Mathf.Max(0.01f, popEaseSeconds);
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            float s = 1f - Mathf.Pow(1f - u, 3f); // easeOutCubic
            targetText.rectTransform.localScale = Vector3.LerpUnclamped(startScale, endScale, s);
            yield return null;
        }
        targetText.rectTransform.localScale = endScale;

        // 表示維持
        if (holdSeconds > 0f) yield return new WaitForSeconds(holdSeconds);

        // フェードアウト
        float f = 0f;
        float fDur = Mathf.Max(0.01f, fadeOutSeconds);
        Color from = targetText.color;
        Color to = targetText.color; to.a = 0f;

        while (f < fDur)
        {
            f += Time.deltaTime;
            float u = Mathf.Clamp01(f / fDur);
            targetText.color = Color.LerpUnclamped(from, to, u);
            yield return null;
        }
        targetText.color = to;

        _running = null;
    }

    // --- 縦書き：1文字ずつ改行 ---
    string ToVertical(string s, bool spaceToNewline)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var sb = new StringBuilder(s.Length * 2);
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];

            // スペースは改行へ（オプション）
            if (spaceToNewline && char.IsWhiteSpace(ch))
            {
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n') sb.Append('\n');
                continue;
            }

            sb.Append(ch);
            if (i != s.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }
}
