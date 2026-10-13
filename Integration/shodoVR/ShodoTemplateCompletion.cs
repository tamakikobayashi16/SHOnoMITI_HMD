using UnityEngine;

// Copy this script into shodoVR/Assets and attach it to the object with PenPosi.
[RequireComponent(typeof(PenPosi))]
public class ShodoTemplateCompletion : MonoBehaviour
{
    public PenPosi penDrawing;
    bool buttonPressed;
    Font font;

    void Awake()
    {
        if (penDrawing == null) penDrawing = GetComponent<PenPosi>();
        font = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic", "Meiryo", "Arial" }, 22);
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.button) { font = font, fontSize = 22 };
        GUI.enabled = penDrawing != null && !buttonPressed;
        if (GUI.Button(new Rect(Screen.width - 260, 20, 240, 65),
            buttonPressed ? "完成を通知しました" : "お手本完成（Enter）", style))
        {
            // PenPosi also handles Enter. Both routes use its existing completion packet.
            penDrawing.FinishExperiment();
            buttonPressed = true;
        }
        GUI.enabled = true;
    }

    void OnDestroy() { if (font != null) Destroy(font); }
}
