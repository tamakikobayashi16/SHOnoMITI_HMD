// ResultScene.cs
using UnityEngine;
using TMPro;

public class ResultScene : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI promptLabel;

    void Start()
    {
        string p = !string.IsNullOrEmpty(GlobalData.ResultPromptWord)
            ? GlobalData.ResultPromptWord
            : GlobalData.PromptWord;

        if (promptLabel != null)
            promptLabel.text = string.IsNullOrEmpty(p) ? "お題: （不明）" : $"お題: {p}";

        // ※ 位置関係維持のため、ここでは一切の再配置・再親子付けを行いません。
        //   （受け渡しは ResultSceneController がワールド座標維持で処理）
    }
}
