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
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Scenes/RemotePreview.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/RemoteGame.unity", true)
        };
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        deadline = EditorApplication.timeSinceStartup + 30;
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
            if (data == null || !data.isLocalTest || data.promptWord != "十" || data.strokes.Count != 2)
                throw new Exception("Completed template was not transferred.");
            if (data.strokes[0].points.Count != 33 || data.strokes[1].points.Count != 33)
                throw new Exception("Template coordinates were lost.");
            if (UnityEngine.Object.FindObjectOfType<RemotePreviewController>() != null)
                throw new Exception("Preview scene was not unloaded.");
            Debug.Log("PREVIEW_FLOW_PASS: progressive drawing, completion, automatic scene transition and 66 coordinates retained");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
