using UnityEngine;

public static class SavedInkOnPaper
{
    static Material inkMaterial;
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
            // Resources keeps the shader in player builds. Standard's back-face culling
            // drops folded triangles in dense handwriting, even on a flat paper plane.
            if (inkMaterial == null)
                inkMaterial = new Material(Resources.Load<Shader>("SavedInk"));
            line.sharedMaterial = inkMaterial;
            var points = new Vector3[line.positionCount];
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 world = line.useWorldSpace ? line.GetPosition(i) : line.transform.TransformPoint(line.GetPosition(i));
                Vector3 onPaper = paper.InverseTransformPoint(world);
                onPaper.z = -0.015f;
                points[i] = paper.TransformPoint(onPaper);
            }
            // A camera-facing ribbon can straddle the paper even when its centerline
            // is in front. Keep the full stroke width parallel to the paper instead.
            line.transform.rotation = paper.rotation;
            // Saving already scales widthMultiplier for the thumbnail. TransformZ also
            // scales the ribbon with its transform; avoid shrinking the width twice.
            if (line.alignment != LineAlignment.TransformZ)
            {
                var parentScale = line.transform.parent != null ? line.transform.parent.lossyScale : Vector3.one;
                line.transform.localScale = new Vector3(1f / parentScale.x, 1f / parentScale.y, 1f / parentScale.z);
            }
            line.alignment = LineAlignment.TransformZ;
            for (int i = 0; i < points.Length; i++)
                points[i] = line.transform.InverseTransformPoint(points[i]);
            line.useWorldSpace = false;
            line.SetPositions(points);
        }
    }
}
