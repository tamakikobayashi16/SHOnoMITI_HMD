using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SaveProgressSmoke
{
    static double started;
    static float animationAt;
    static int stage; static int saved;
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
    static void ValidateSaved()
    {
        for(int index=0; index<=saved; index++)
        {
            var group=GameObject.Find("SavedGroup_"+index);
            if(group==null)throw new Exception("Missing saved character "+index);
            var paper=GameObject.Find("Saved_MoveArea_"+index).transform;
            foreach(var line in group.GetComponentsInChildren<LineRenderer>())
            {
                if(line.alignment!=LineAlignment.TransformZ)throw new Exception("Ink still camera-facing");
                var mesh=new Mesh();line.BakeMesh(mesh,Camera.main,false);
                if(mesh.vertexCount==0)throw new Exception("Empty saved ink");
                foreach(var vertex in mesh.vertices)
                    if(paper.InverseTransformPoint(line.transform.TransformPoint(vertex)).z>=-0.001f)
                        throw new Exception("Saved ink crosses paper at character "+index);
                UnityEngine.Object.Destroy(mesh);
            }
        }
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
                field.SetValue(control, Enum.Parse(field.FieldType, "READY_TO_WRITE"));
                var draw = UnityEngine.Object.FindObjectOfType<DrawTrail>();
                draw.StartStroke();
                for(int i=0;i<30;i++) draw.UpdateStroke(new Vector3(-2+i*.13f, Mathf.Sin(i*.3f)*2,0), 10+i*.35f);
                draw.EndStroke();
                typeof(PreGameControl).GetField("charTimerSec", Private).SetValue(control,0f);
                animationAt = Time.time; stage = 1; return;
            }
            if (stage == 1)
            {
                if(Time.time-animationAt<2)return;
                ValidateSaved();
                saved++;
                if(saved<3){
                    var d=UnityEngine.Object.FindObjectOfType<DrawTrail>();
                    d.StartStroke();for(int i=0;i<30;i++)d.UpdateStroke(new Vector3(-2+i*.13f, Mathf.Cos(i*.3f)*2,0),10+i*.35f);d.EndStroke();
                    typeof(PreGameControl).GetField("charTimerSec",Private).SetValue(control,0f);
                    animationAt=Time.time;return;
                }

                ScreenCapture.CaptureScreenshot(Path.GetFullPath("save-progress.png")); stage = 2; return;
            }
            if (stage == 2 && File.Exists("save-progress.png"))
            { Debug.Log("SAVE_PROGRESS_PASS: three timeout advances preserve all thick stroke mesh vertices in front of paper"); EditorApplication.Exit(0); }
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
