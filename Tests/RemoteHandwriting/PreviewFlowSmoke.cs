using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PreviewFlowSmoke
{
    static double deadline;
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/TempRemoteVerification/Assets"))
            throw new Exception("Run this test only in the isolated TempRemoteVerification project.");
        var game = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(game, "Assets/Scenes/RemoteGame.unity");
        EditorSceneManager.OpenScene("Assets/Scenes/RemotePreview.unity");
        UnityEngine.Object.FindObjectOfType<RemotePreviewController>().useLocalTestTemplate = true;
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Scenes/RemotePreview.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/RemoteGame.unity", true)
        };
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        deadline = EditorApplication.timeSinceStartup + 45;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > deadline)
        {
            Debug.LogError("PREVIEW_FLOW_FAILED: transition timeout");
            EditorApplication.Exit(1);
            return;
        }
        if (!EditorApplication.isPlaying || SceneManager.GetActiveScene().name != "RemoteGame") return;
        try
        {
            var data = GlobalData.RemoteTemplate;
            if (data == null || !data.isLocalTest || data.promptWord != "あいう" || data.strokes.Count != 7)
                throw new Exception("Completed template was not transferred.");
            int[] strokeCounts = new int[3];
            foreach (var stroke in data.strokes)
            {
                if (stroke.character < 0 || stroke.character > 2 || stroke.points.Count < 2)
                    throw new Exception("Template coordinates were lost.");
                strokeCounts[stroke.character]++;
            }
            if (strokeCounts[0] != 3 || strokeCounts[1] != 2 || strokeCounts[2] != 2)
                throw new Exception("Character stroke groups were lost.");
            if (UnityEngine.Object.FindObjectOfType<RemotePreviewController>() != null)
                throw new Exception("Preview scene was not unloaded.");
            Debug.Log("PREVIEW_FLOW_PASS: progressive drawing, completion, automatic scene transition and all three characters retained");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
