using UnityEngine;

public static class SavedInkOnPaper
{
    // Keep ink in the paper's local plane so gallery/result movement carries both together.
    public static void Align(GameObject group)
    {
        Transform paper = null;
        foreach (var renderer in group.GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.name.StartsWith("Saved_MoveArea_")) { paper = renderer.transform; break; }
        if (paper == null) return;
        foreach (var animator in paper.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        foreach (var line in group.GetComponentsInChildren<LineRenderer>(true))
        {
            var points = new Vector3[line.positionCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 world = line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i));
                Vector3 onPaper = paper.InverseTransformPoint(world);
                onPaper.z = -0.015f;
                points[i] = line.transform.InverseTransformPoint(paper.TransformPoint(onPaper));
            }
            line.useWorldSpace = false;
            line.SetPositions(points);
        }
    }
}
