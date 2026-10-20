using System;
using UnityEditor;
using UnityEngine;

public static class SavedInkSmoke
{
    public static void Run()
    {
        var group = new GameObject("SavedGroup");
        group.transform.position = new Vector3(-6, 3, 0);
        group.transform.localScale = Vector3.one * 0.28f;
        var paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
        paper.name = "Saved_MoveArea_0";
        paper.transform.SetParent(group.transform, false);
        paper.transform.localScale = new Vector3(2.268f, 2.072f, 0.28f);
        var animator = paper.AddComponent<Animator>();
        foreach (bool worldSpace in new[] { true, false })
        {
            var ink = new GameObject("Ink");
            ink.transform.SetParent(group.transform, false);
            var line = ink.AddComponent<LineRenderer>();
            line.startWidth = line.endWidth = 0.3f;
            line.numCapVertices = line.numCornerVertices = 8;
            line.useWorldSpace = worldSpace;
            line.positionCount = 2;
            for (int i = 0; i < 2; i++)
            {
                var point = paper.transform.TransformPoint(new Vector3(i * 0.4f, i * -0.3f, -10));
                line.SetPosition(i, worldSpace ? point : ink.transform.InverseTransformPoint(point));
            }
        }
        SavedInkOnPaper.Align(group);
        foreach (var line in group.GetComponentsInChildren<LineRenderer>())
            if (Vector3.Distance(line.transform.lossyScale, Vector3.one) > 0.001f)
                throw new Exception("Thumbnail scale applied to stroke width twice");
        SavedInkOnPaper.Align(group); // Repeated alignment must not change position or width.
        if (animator.enabled) throw new Exception("Saved paper animation still enabled");
        Check(group);
        var transferred = UnityEngine.Object.Instantiate(group);
        transferred.transform.SetPositionAndRotation(new Vector3(2, 1, 8), Quaternion.Euler(0, 20, 10));
        transferred.transform.localScale = Vector3.one * 1.7f;
        Check(transferred);
        UnityEngine.Object.DestroyImmediate(group);
        UnityEngine.Object.DestroyImmediate(transferred);
        Debug.Log("SAVED_INK_PASS: world/local lines projected onto paper; XY preserved; saved animator stopped; cloned/moved/scaled result preserves paper-relative ink");
        EditorApplication.Exit(0);
    }

    static void Check(GameObject group)
    {
        var paper = group.GetComponentInChildren<MeshRenderer>().transform;
        var cameraObject = new GameObject("Ink test camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.transform.position = new Vector3(0, 0, -10);
        camera.transform.rotation = Quaternion.Euler(15, 25, 0);
        foreach (var line in group.GetComponentsInChildren<LineRenderer>())
        {
            if (line.useWorldSpace) throw new Exception("Ink must follow gallery transform");
            for (int i = 0; i < 2; i++)
            {
                var point = paper.InverseTransformPoint(line.transform.TransformPoint(line.GetPosition(i)));
                if ((point - new Vector3(i * 0.4f, i * -0.3f, -0.015f)).sqrMagnitude > 0.00001f)
                    throw new Exception("Ink detached from paper");
            }
            // Check the entire thick ribbon, not only its centerline points.
            var mesh = new Mesh();
            line.BakeMesh(mesh, camera, false);
            foreach (var vertex in mesh.vertices)
                if (paper.InverseTransformPoint(line.transform.TransformPoint(vertex)).z >= -0.001f)
                    throw new Exception("Thick ink mesh crosses behind the paper");
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        UnityEngine.Object.DestroyImmediate(cameraObject);
    }
}
