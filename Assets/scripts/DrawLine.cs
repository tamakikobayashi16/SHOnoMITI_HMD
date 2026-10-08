using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class DrawLine : MonoBehaviour
{
    public bool isDrawing = false;
    private LineRenderer lineRenderer;
    private List<Vector3> points = new List<Vector3>();

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0;
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        lineRenderer.startColor = Color.white;
        lineRenderer.endColor = Color.white;
    }

    public void ClearTrail()
    {
        points.Clear();
        lineRenderer.positionCount = 0;
    }

    public void AppendPosition(Vector3 pos)
    {
        if (!isDrawing) return;
        if (points.Count == 0 || Vector3.Distance(points[points.Count - 1], pos) > 0.01f)
        {
            points.Add(pos);
            lineRenderer.positionCount = points.Count;
            lineRenderer.SetPositions(points.ToArray());
        }
    }

    public List<Vector3> GetDrawnPoints()
    {
        return points;
    }
}
