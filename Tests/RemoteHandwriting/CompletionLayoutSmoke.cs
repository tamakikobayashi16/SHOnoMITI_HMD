using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CompletionLayoutSmoke
{
    static double started;
    static float animationAt;
    static int stage, frames;
    static PreGameControl control;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/TempRemoteVerification/Assets"))
            throw new Exception("Run only in isolated verification project");
        EditorSceneManager.OpenScene("Assets/Scenes/RemoteGame.unity");
        control = UnityEngine.Object.FindObjectOfType<PreGameControl>();
        control.useLocalTestTemplate = true; control.skipBalanceBoardInLocalTest = true;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick; EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 60) throw new Exception("timeout");
            if (!EditorApplication.isPlaying || Time.time < 6) return;
            control = UnityEngine.Object.FindObjectOfType<PreGameControl>();
            if (stage == 0)
            {
                var field = typeof(PreGameControl).GetField("status", Private);
                field.SetValue(control, Enum.Parse(field.FieldType, "FINISHED"));
                var draw = UnityEngine.Object.FindObjectOfType<DrawTrail>();
                for (int letter = 0; letter < 3; letter++)
                {
                    draw.StartStroke();
                    for (int i = 0; i < 20; i++) draw.UpdateStroke(new Vector3(-2 + i * .2f, -2 + i * .2f, 0), 15);
                    draw.EndStroke();
                    typeof(PreGameControl).GetMethod("SaveGlyphObjectToGallery", Private).Invoke(control, new object[] { draw.PopLastGlyph() });
                }
                typeof(PreGameControl).GetMethod("ClearGhost", Private).Invoke(control, null);
                typeof(PreGameControl).GetMethod("TriggerCompletionAnimations", Private).Invoke(control, null);
                animationAt = Time.time; stage = 1; return;
            }
            if (stage == 1)
            {
                // Observe the previous rendered frame, including the controller's LateUpdate.
                for (int i = 0; i < 3; i++)
                {
                    var left = GameObject.Find("Saved_MoveArea_" + i).GetComponent<Renderer>().bounds;
                    var right = control.rightPanelParent.GetChild(i).GetComponent<Renderer>().bounds;
                    if (Mathf.Abs(left.center.y - right.center.y) > .01f || Mathf.Abs(left.center.z - right.center.z) > .01f ||
                        Mathf.Abs(left.size.y - right.size.y) > .01f)
                        throw new Exception("Columns differ at frame " + frames + ": " + left + " / " + right);
                }
                frames++;
                if (Time.time - animationAt < 3) return;
                ScreenCapture.CaptureScreenshot(Path.GetFullPath("completion-layout.png")); stage = 2; return;
            }
            if (stage == 2 && File.Exists("completion-layout.png"))
            { Debug.Log("COMPLETION_LAYOUT_PASS: aligned 3 rows during " + frames + " frames"); EditorApplication.Exit(0); }
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
