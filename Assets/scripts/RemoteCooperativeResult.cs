using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Carried with the saved gallery; no static score can leak into a later trial.
public class RemoteCooperativeResult : MonoBehaviour
{
    public int strokeCount, multiplier;
    public string award;
    public TMP_FontAsset font;

    public static int CountStrokes(RemoteTemplateData template)
    {
        var ids = new HashSet<string>();
        if (template?.strokes != null)
            foreach (var stroke in template.strokes)
                if (stroke != null && stroke.points != null && stroke.points.Count > 0)
                    ids.Add(stroke.character + ":" + stroke.stroke);
        return ids.Count;
    }

    public static int Points(int strokes, int rate) => strokes * rate;

    void Start()
    {
        var canvasObject = new GameObject("CooperativeScore", typeof(Canvas), typeof(CanvasScaler));
        // Scene-local UI is automatically removed on leaving the result scene.
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        var panel = new GameObject("ScorePanel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0, 24);
        rect.sizeDelta = new Vector2(1000, 160);
        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
        panel.GetComponent<Image>().raycastTarget = false;
        var label = new GameObject("Score", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.transform.SetParent(panel.transform, false);
        var text = label.GetComponent<TextMeshProUGUI>();
        text.font = font != null ? font : TMP_Settings.defaultFontAsset;
        text.rectTransform.anchorMin = Vector2.zero;
        text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(12, 8);
        text.rectTransform.offsetMax = new Vector2(-12, -8);
        text.fontSize = 42;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = $"協力スコア　{award}　{Points(strokeCount, multiplier)}点\nお手本の合計 {strokeCount}画 × {multiplier}点";
    }
}
