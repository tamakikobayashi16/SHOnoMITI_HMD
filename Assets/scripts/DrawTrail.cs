using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ストロークを管理。ポイントごとの幅を保持し、Glyph（文字）ごとに
/// ストロークをまとめる機能を提供します。
/// </summary>
public class DrawTrail : MonoBehaviour
{
    [Header("Line のプレハブ（LineRenderer を持った Prefab）")]
    public GameObject linePrefab;

    [Header("線の描画設定")]
    public float minDistance = 0.01f;

    [Header("線幅（圧力マッピング）")]
    public float minLineWidth = 0.06f;
    public float maxLineWidth = 0.36f;
    public float minPressureKg = 0f;
    public float maxPressureKg = 30f;

    [Header("LineRenderer の見た目")]
    public int capVertices = 8;
    public int cornerVertices = 8;
    public Material lineMaterial;

    [Header("Glyph 管理")]
    public Transform glyphContainer; // null なら this.transform
    private GameObject currentGlyph;
    private int glyphCounter = 0;
    public List<GameObject> glyphObjects = new List<GameObject>();

    private LineRenderer currentLineRenderer;
    private List<Vector3> currentStrokePoints;
    private List<float> currentStrokeWidths;

    private List<GameObject> strokeObjects = new List<GameObject>();

    // 保存用データ（必要なら参照可能）
    public List<List<List<Vector3>>> allGlyphStrokes = new List<List<List<Vector3>>>();
    public List<List<List<float>>> allGlyphWidths = new List<List<List<float>>>();

    private bool isDrawing = false;
    public bool IsDrawing => isDrawing;

    // --- Glyph 管理 ---
    public void NextGlyph()
    {
        Transform parent = (glyphContainer != null) ? glyphContainer : this.transform;
        GameObject g = new GameObject("Glyph_" + glyphCounter++);
        g.transform.parent = parent;
        g.transform.localPosition = Vector3.zero;
        g.transform.localRotation = Quaternion.identity;
        g.transform.localScale = Vector3.one;
        glyphObjects.Add(g);
        currentGlyph = g;

        allGlyphStrokes.Add(new List<List<Vector3>>());
        allGlyphWidths.Add(new List<List<float>>());
    }

    public void ClearAllGlyphs()
    {
        foreach (var g in glyphObjects)
        {
            if (g != null) Destroy(g);
        }
        glyphObjects.Clear();
        allGlyphStrokes.Clear();
        allGlyphWidths.Clear();
        currentGlyph = null;
        glyphCounter = 0;
    }

    // 現在の (最後の) Glyph を参照だけ返す（削除しない）
    public GameObject PeekLastGlyph()
    {
        if (glyphObjects == null || glyphObjects.Count == 0) return null;
        return glyphObjects[glyphObjects.Count - 1];
    }

    // 最後の Glyph を管理リストから取り出して返す（呼び出し側で破棄／保存する想定）
    public GameObject PopLastGlyph()
    {
        if (glyphObjects == null || glyphObjects.Count == 0) return null;
        int last = glyphObjects.Count - 1;
        GameObject g = glyphObjects[last];
        glyphObjects.RemoveAt(last);

        if (last < allGlyphStrokes.Count) allGlyphStrokes.RemoveAt(last);
        if (last < allGlyphWidths.Count) allGlyphWidths.RemoveAt(last);

        if (currentGlyph == g) currentGlyph = null;

        return g;
    }

    // --- Stroke 操作 ---
    public void StartStroke()
    {
        if (currentGlyph == null) NextGlyph();

        GameObject lineObj;
        if (linePrefab != null) lineObj = Instantiate(linePrefab, currentGlyph.transform);
        else
        {
            lineObj = new GameObject("Stroke");
            lineObj.transform.parent = currentGlyph.transform;
            lineObj.AddComponent<LineRenderer>();
        }

        currentLineRenderer = lineObj.GetComponent<LineRenderer>();
        if (currentLineRenderer == null) currentLineRenderer = lineObj.AddComponent<LineRenderer>();

        if (lineMaterial != null) currentLineRenderer.material = new Material(lineMaterial);
        else if (currentLineRenderer.material == null) currentLineRenderer.material = new Material(Shader.Find("Sprites/Default"));

        currentLineRenderer.startColor = Color.black;
        currentLineRenderer.endColor = Color.black;

        currentLineRenderer.startWidth = minLineWidth;
        currentLineRenderer.endWidth = minLineWidth;
        currentLineRenderer.positionCount = 0;
        currentLineRenderer.useWorldSpace = true;
        currentLineRenderer.numCapVertices = capVertices;
        currentLineRenderer.numCornerVertices = cornerVertices;
        currentLineRenderer.widthCurve = new AnimationCurve(new Keyframe(0f, minLineWidth), new Keyframe(1f, minLineWidth));

        currentStrokePoints = new List<Vector3>();
        currentStrokeWidths = new List<float>();
        strokeObjects.Add(lineObj);

        int gidx = glyphObjects.IndexOf(currentGlyph);
        if (gidx >= 0)
        {
            allGlyphStrokes[gidx].Add(new List<Vector3>());
            allGlyphWidths[gidx].Add(new List<float>());
        }

        isDrawing = true;
    }

    public void UpdateStroke(Vector3 worldPos)
    {
        UpdateStroke(worldPos, minPressureKg);
    }

    public void UpdateStroke(Vector3 worldPos, float pressureKg)
    {
        if (!isDrawing || currentLineRenderer == null) return;

        if (currentStrokePoints.Count == 0 ||
            Vector3.Distance(currentStrokePoints[currentStrokePoints.Count - 1], worldPos) >= minDistance)
        {
            currentStrokePoints.Add(worldPos);

            float t = 0f;
            if (maxPressureKg > minPressureKg)
                t = (pressureKg - minPressureKg) / (maxPressureKg - minPressureKg);
            t = Mathf.Clamp01(t);
            float w = Mathf.Lerp(minLineWidth, maxLineWidth, t);
            currentStrokeWidths.Add(w);

            currentLineRenderer.positionCount = currentStrokePoints.Count;
            currentLineRenderer.SetPosition(currentStrokePoints.Count - 1, worldPos);

            ApplyWidthCurveToCurrentLine();

            int gidx = glyphObjects.IndexOf(currentGlyph);
            if (gidx >= 0)
            {
                int sidx = allGlyphStrokes[gidx].Count - 1;
                if (sidx >= 0)
                {
                    allGlyphStrokes[gidx][sidx].Add(worldPos);
                    allGlyphWidths[gidx][sidx].Add(w);
                }
            }
        }
    }

    public void EndStroke()
    {
        if (!isDrawing) return;
        isDrawing = false;

        if (currentStrokePoints == null || currentStrokePoints.Count == 0)
        {
            if (strokeObjects.Count > 0)
            {
                var last = strokeObjects[strokeObjects.Count - 1];
                strokeObjects.RemoveAt(strokeObjects.Count - 1);
                if (last != null) Destroy(last);
            }
            int gidx = glyphObjects.IndexOf(currentGlyph);
            if (gidx >= 0 && allGlyphStrokes[gidx].Count > 0)
            {
                allGlyphStrokes[gidx].RemoveAt(allGlyphStrokes[gidx].Count - 1);
                allGlyphWidths[gidx].RemoveAt(allGlyphWidths[gidx].Count - 1);
            }
        }

        currentLineRenderer = null;
        currentStrokePoints = null;
        currentStrokeWidths = null;
    }

    public void ClearStrokes()
    {
        foreach (var obj in strokeObjects)
        {
            if (obj != null) Destroy(obj);
        }
        strokeObjects.Clear();
        for (int i = 0; i < allGlyphStrokes.Count; i++)
        {
            allGlyphStrokes[i].Clear();
            allGlyphWidths[i].Clear();
        }
    }

    public void ClearTrail()
    {
        ClearAllGlyphs();
    }

    public void EvaluateStroke()
    {
        Debug.Log($"Glyph count: {glyphObjects.Count}");
        for (int g = 0; g < allGlyphStrokes.Count; g++)
        {
            Debug.Log($"Glyph {g}: stroke count = {allGlyphStrokes[g].Count}");
            for (int s = 0; s < allGlyphStrokes[g].Count; s++)
            {
                Debug.Log($"  Stroke {s}: points = {allGlyphStrokes[g][s].Count}");
            }
        }
    }

    private void ApplyWidthCurveToCurrentLine()
    {
        if (currentLineRenderer == null || currentStrokeWidths == null || currentStrokeWidths.Count == 0) return;

        int n = currentStrokeWidths.Count;
        Keyframe[] kfs = new Keyframe[n];
        if (n == 1)
        {
            kfs[0] = new Keyframe(0f, currentStrokeWidths[0]);
            currentLineRenderer.widthCurve = new AnimationCurve(kfs);
            currentLineRenderer.startWidth = currentStrokeWidths[0];
            currentLineRenderer.endWidth = currentStrokeWidths[0];
            return;
        }

        for (int i = 0; i < n; i++)
        {
            float time = (float)i / (n - 1);
            kfs[i] = new Keyframe(time, currentStrokeWidths[i]);
        }

        currentLineRenderer.widthCurve = new AnimationCurve(kfs);
        currentLineRenderer.startWidth = currentStrokeWidths[0];
        currentLineRenderer.endWidth = currentStrokeWidths[n - 1];
    }
}
