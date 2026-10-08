// Assets/scripts/GameManager.cs
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Scene Names")]
    [SerializeField] private string titleSceneName = "Title";
    [SerializeField] private string inputNameSceneName = "InputName";
    [SerializeField] private string pregameSceneName = "PreGame"; // ← 修正（大文字G）
    [SerializeField] private string resultSceneName = "Result";

    [Header("Only react in Title scene")]
    [SerializeField] private bool onlyInTitle = true;

    void Update()
    {
        // --- グローバルキー（どのシーンでも有効） ---
        if (Input.GetKeyDown(KeyCode.F1)) { SafeLoadScene(titleSceneName); return; }   // タイトルへ
        if (Input.GetKeyDown(KeyCode.F2)) { SafeLoadScene(inputNameSceneName); return; } // InputNameへ

        // --- リザルト専用：Aでタイトルへ戻る ---
        if (SceneManager.GetActiveScene().name == resultSceneName)
        {
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.JoystickButton0))
            {
                SafeLoadScene(titleSceneName);
                return;
            }
        }

        // --- ここから下はタイトルでのみ反応（オプション） ---
        if (onlyInTitle && SceneManager.GetActiveScene().name != titleSceneName) return;

        // タイトルで Q：PreGame へ
        if (Input.GetKeyDown(KeyCode.Q))
        {
            SafeLoadScene(pregameSceneName);
            return;
        }

        // タイトルで A or パッドA：InputName へ
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.JoystickButton0))
        {
            SafeLoadScene(inputNameSceneName);
            return;
        }

        // （任意）タイトルで S：Result へ
        if (Input.GetKeyDown(KeyCode.S))
        {
            SafeLoadScene(resultSceneName);
            return;
        }
    }

    private void SafeLoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[GameManager] Scene name is empty.");
            return;
        }
        if (!IsInBuildSettings(sceneName))
        {
            Debug.LogError($"[GameManager] Scene '{sceneName}' is not in Build Settings.");
            return;
        }
        SceneManager.LoadScene(sceneName);
    }

    private bool IsInBuildSettings(string sceneName)
    {
        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            var path = SceneUtility.GetScenePathByBuildIndex(i);
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (name == sceneName) return true;
        }
        return false;
    }
}
