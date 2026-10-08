#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ResultSceneController))]
public class ResultSceneControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var ctrl = (ResultSceneController)target;
        if (ctrl == null || ctrl.resultGalleryParent == null) return;

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Editor Placement", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Center to Origin"))
            {
                Undo.RegisterFullObjectHierarchyUndo(ctrl.resultGalleryParent.gameObject, "Center to Origin");
                ctrl.RecenterAllAtWorld(Vector3.zero);
                EditorUtility.SetDirty(ctrl);
            }

            if (GUILayout.Button("Center to Custom"))
            {
                Undo.RegisterFullObjectHierarchyUndo(ctrl.resultGalleryParent.gameObject, "Center to Custom");
                ctrl.RecenterAllAtWorld(ctrl.customWorldCenter);
                EditorUtility.SetDirty(ctrl);
            }
        }

        EditorGUILayout.HelpBox(
            "シーンビュー上のシアンのハンドルをドラッグすると、書いた文字の中心を直接移動できます。",
            MessageType.Info
        );
    }

    // シーンビューで中心をドラッグして配置
    void OnSceneGUI()
    {
        var ctrl = (ResultSceneController)target;
        if (ctrl == null || ctrl.resultGalleryParent == null) return;

        if (!ctrl.TryComputeTotalBounds(out Bounds b)) return;

        // 線とハンドル色
        Handles.color = new Color(0f, 1f, 1f, 0.8f);
        Handles.DrawWireCube(b.center, b.size);

        EditorGUI.BeginChangeCheck();
        Vector3 newCenter = Handles.PositionHandle(b.center, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RegisterFullObjectHierarchyUndo(ctrl.resultGalleryParent.gameObject, "Move Result Content");
            Vector3 delta = newCenter - b.center;
            ctrl.MoveAllChildrenWorld(delta);
            EditorUtility.SetDirty(ctrl);
        }
    }
}
#endif
