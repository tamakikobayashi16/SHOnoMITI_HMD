using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The isolated project also copies shodoVR/Assets/DrawingUdpSender.cs to compile this test.
public static class SessionFlowSmoke
{
    static double deadline;
    static int stage, drawPort;
    static DrawingUdpSender sender;
    static string firstTrial, secondTrial, firstFolder;
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/TempRemoteVerification/Assets"))
            throw new Exception("Use the isolated test project.");
        using (var probe = new UdpClient(0)) drawPort = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        var game = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(game, "Assets/Scenes/RemoteGame.unity");
        var previewScene = EditorSceneManager.OpenScene("Assets/Scenes/RemotePreview.unity");
        var preview = UnityEngine.Object.FindObjectOfType<RemotePreviewController>();
        preview.useLocalTestTemplate = false;
        preview.listenPort = drawPort;
        preview.controlPort = 0;
        preview.drawingIdleTimeoutSeconds = 5f;
        EditorSceneManager.SaveScene(previewScene);
        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene("Assets/Scenes/RemotePreview.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/RemoteGame.unity", true) };
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        GlobalData.PromptWord = "あい";
        deadline = EditorApplication.timeSinceStartup + 45;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("flow timeout at stage " + stage);
            if (!EditorApplication.isPlaying || RemoteTrialRuntime.Active == null) return;
            var runtime = RemoteTrialRuntime.Active;
            var preview = UnityEngine.Object.FindObjectOfType<RemotePreviewController>();
            if (stage == 0)
            {
                firstTrial = runtime.TrialId;
                firstFolder = runtime.Recorder.DirectoryPath;
                var go = new GameObject("ActualShodoSender");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                sender = go.AddComponent<DrawingUdpSender>();
                sender.receiverIp = "127.0.0.1";
                sender.receiverPort = drawPort;
                sender.controlPort = runtime.Link.LocalPort;
                go.SetActive(true);
                sender.DeviceReady = true;
                stage = 1;
            }
            else if (stage == 1 || stage == 4)
            {
                var teacher = Get<RemoteTrialLink>(sender, "link");
                if (!teacher.Connected || teacher.State.trialId != runtime.TrialId) return;
                runtime.Link.LocalReady = true;
                teacher.LocalReady = true;
                if (!sender.CanDraw) return;
                sender.Send("PenTablet", "point", new Vector3(-0.5f, 0.2f), 0, 0, 0.3f);
                sender.Send("PenTablet", "point", new Vector3(0.5f, 0.3f), 0, 0, 0.4f);
                sender.Send("PenTablet", "strokeEnd", Vector3.zero, 0, 0);
                stage = stage == 1 ? 2 : 5;
            }
            else if (stage == 2)
            {
                if (!Get<bool>(preview, "failed")) return;
                if (runtime.Link.State.phase != "error") throw new Exception("Idle timeout did not stop trial.");
                Debug.Log("PASS: idle writing timeout blocks the live preview");
                preview.Retry();
                stage = 3;
            }
            else if (stage == 3)
            {
                if (runtime.TrialId == firstTrial) return;
                secondTrial = runtime.TrialId;
                if (!File.ReadAllText(Path.Combine(firstFolder, "learner_events.jsonl")).Contains("trialAborted"))
                    throw new Exception("Interrupted trial was not preserved.");
                Debug.Log("PASS: retry reloads preview and preserves interrupted trial");
                stage = 4;
            }
            else if (stage == 5)
            {
                sender.Send("PenTablet", "nextCharacter", Vector3.zero, 1, 0);
                sender.Send("PenTablet", "point", new Vector3(-0.2f, -0.5f), 1, 0, 0.4f);
                sender.Send("PenTablet", "point", new Vector3(0.2f, 0.5f), 1, 0, 0.5f);
                sender.Send("PenTablet", "strokeEnd", Vector3.zero, 1, 0);
                // Drop the first completion datagram; its retry must finish the preview.
                var destination = Get<IPEndPoint>(sender, "destination");
                using (var sink = new UdpClient(0))
                {
                    Set(sender, "destination", new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)sink.Client.LocalEndPoint).Port));
                    sender.Send("PenTablet", "experimentEnd", Vector3.zero, 1, 1);
                }
                Set(sender, "destination", destination);
                stage = 6;
            }
            else if (stage == 6 && SceneManager.GetActiveScene().name == "RemoteGame")
            {
                var snapshot = GlobalData.RemoteTemplate;
                if (snapshot == null || snapshot.trialId != secondTrial || snapshot.strokes.Count != 2)
                    throw new Exception("Trial snapshot was not transferred.");
                if (sender.TrialId != secondTrial || !File.Exists(Path.Combine(runtime.Recorder.DirectoryPath, "teacher_events.jsonl")))
                    throw new Exception("Teacher recording was not joined to the learner trial.");
                if (!File.Exists(Path.Combine(runtime.Recorder.DirectoryPath, "template.json")))
                    throw new Exception("Template was not persisted.");
                Debug.Log("PASS: lost completion packet is retried and scene transfer succeeds");
                Debug.Log("SESSION_FLOW_PASS: actual sender + receiver, readiness, idle timeout, retry, paired recordings and template transfer");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
}
