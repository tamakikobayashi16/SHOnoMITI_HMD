using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CoGLine : MonoBehaviour
{
    [Tooltip("false のときは軌跡を一切描画しません（メソッドは呼ばれても無視します）。")]
    public bool drawEnabled = false;

    private LineRenderer lineRenderer;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        // 初期状態を反映（デフォルト false → 非表示）
        lineRenderer.enabled = drawEnabled;
        lineRenderer.positionCount = 0;
    }

    // インスペクタで drawEnabled を切り替えた時にも反映
    void OnValidate()
    {
        if (lineRenderer == null) lineRenderer = GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            lineRenderer.enabled = drawEnabled;
            if (!drawEnabled) lineRenderer.positionCount = 0;
        }
    }

    public void setPositions(List<RecvBB.BBDatum> list, float[] offset)
    {
        if (!drawEnabled || lineRenderer == null) return;
        if (offset == null || offset.Length != 4 || list == null || list.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        int n = list.Count;
        var vecs = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float[] pos = list[i].getCoG(offset);
            vecs[i] = new Vector3(pos[0], pos[1], 0f);
        }
        lineRenderer.positionCount = n;
        lineRenderer.SetPositions(vecs);
    }

    public void appendPosition(RecvBB.BBDatum d, float[] offset)
    {
        if (!drawEnabled || lineRenderer == null) return;
        if (offset == null || offset.Length != 4 || d == null) return;

        float[] pos = d.getCoG(offset);
        int count = lineRenderer.positionCount;
        lineRenderer.positionCount = count + 1;
        lineRenderer.SetPosition(count, new Vector3(pos[0], pos[1], 0f));
    }

    public void Clear()
    {
        if (lineRenderer == null) return;
        lineRenderer.positionCount = 0;
    }
}
