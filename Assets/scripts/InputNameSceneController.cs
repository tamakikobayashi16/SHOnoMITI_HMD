// InputNameSceneController.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class InputNameSceneController : MonoBehaviour
{
    [Header("UI")]
    public TMP_InputField nameInput;
    public Button startButton;

    [Header("Next Scene")]
    [Tooltip("入力後に遷移するゲームシーン名")]
    public string nextSceneName = "Main"; // 実際のゲームシーン名に合わせてInspectorで設定

    void Start()
    {
        if (startButton != null)
            startButton.onClick.AddListener(OnClickStart);
    }

    public void OnClickStart()
    {
        string s = (nameInput != null) ? nameInput.text : "";
        NameStore.SelectedName = string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        SceneManager.LoadScene(nextSceneName, LoadSceneMode.Single);
    }
}
