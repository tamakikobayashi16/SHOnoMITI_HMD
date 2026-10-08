using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class InputNameScene : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField nameInput;
    public Button startButton;
    public TextMeshProUGUI messageText;

    [Header("Next Scene")]
    public string nextSceneName = "PreGame";

    // ▼ 追加：二文字モード用ボタンと遷移先
    [Header("Two-Chars Mode")]
    [Tooltip("「二文字」というラベルのボタン（押すと Game2 へ遷移）")]
    public Button twoCharsButton;
    [Tooltip("二文字ボタンを押したときに遷移するシーン名")]
    public string twoCharsSceneName = "Game2";
    [Tooltip("二文字ボタンのラベル（子にある TextMeshProUGUI があれば自動で設定）")]
    public string twoCharsButtonLabel = "二文字";

    // 最初に一度だけクリーン実行するためのフラグ
    bool _cleaned = false;

    void Start()
    {
        // 最初に一度だけクリーン（お題＆書いた文字のみリセット。BB接続ほかは維持）
        CleanOnce();

        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);

        // ▼ 追加：二文字ボタンのセットアップ
        if (twoCharsButton != null)
        {
            twoCharsButton.onClick.AddListener(OnTwoCharsClicked);

            // 子にある TMP_Text があればラベルを「二文字」に更新（エディタで既に設定していればそのままでOK）
            var lbl = twoCharsButton.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null && !string.IsNullOrEmpty(twoCharsButtonLabel))
            {
                lbl.text = twoCharsButtonLabel;
            }
        }
    }

    void Update()
    {
        // Enter キーでも開始（通常モード：PreGame へ）
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            OnStartClicked();
        }
    }

    void OnStartClicked()
    {
        // 既存挙動：入力に応じて GlobalData を設定し、nextSceneName へ遷移（= PreGame）
        StartGame(nextSceneName);
    }

    // ▼ 追加：二文字ボタン押下時の処理（Game2 へ）
    void OnTwoCharsClicked()
    {
        StartGame(twoCharsSceneName);
    }

    // 入力値をもとに GlobalData を整えて指定シーンへ遷移する共通関数
    void StartGame(string sceneName)
    {
        string s = (nameInput != null) ? nameInput.text : null;
        s = string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // 入力が「果物/くだもの/クダモノ/kudamono」または空ならデフォルトお題を出題
        if (IsDefaultRequest(s))
        {
            // デフォルトお題を使うため、明示的にクリアして PreGame/各シーン側の既定処理に委ねる
            GlobalData.PromptWord = null;
            GlobalData.ResultPromptWord = null;

            if (messageText != null) messageText.text = "デフォルトのお題を出題します。";
        }
        else
        {
            // 入力文字をそのままお題に採用（各シーン側で二文字化や加工を行う想定）
            GlobalData.PromptWord = s;
            GlobalData.ResultPromptWord = s;

            if (messageText != null) messageText.text = "";
        }

        SceneManager.LoadScene(sceneName);
    }

    // 「果物」「くだもの」「クダモノ」「kudamono」または未入力なら true
    bool IsDefaultRequest(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return true;

        // 空白（半角/全角/その他の空白）をすべて除去して判定を安定化
        string normalized = RemoveAllWhitespaces(input);

        if (normalized == "果物") return true;
        if (normalized == "くだもの") return true;
        if (normalized == "クダモノ") return true;
        if (string.Equals(normalized, "kudamono", System.StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    string RemoveAllWhitespaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length);
        foreach (char ch in s.Trim())
        {
            if (!char.IsWhiteSpace(ch)) sb.Append(ch);
        }
        return sb.ToString();
    }

    // クリーン処理本体（このシーンに入った時に一度だけ）
    // ① お題（Prompt）だけリセット
    // ② 書いた文字（ギャラリー受け渡し用の SceneTransferRoot）を破棄
    // ※ バランスボード接続や他の状態、UIの入力内容などは変更しません
    void CleanOnce()
    {
        if (_cleaned) return;
        _cleaned = true;

        // ① お題リセット
        GlobalData.PromptWord = null;
        GlobalData.ResultPromptWord = null;

        // ② 書いた文字（前回リザルト用に保持していたギャラリー）を破棄
        if (GlobalData.SceneTransferRoot != null)
        {
            Destroy(GlobalData.SceneTransferRoot);
            GlobalData.SceneTransferRoot = null;
        }
    }
}
