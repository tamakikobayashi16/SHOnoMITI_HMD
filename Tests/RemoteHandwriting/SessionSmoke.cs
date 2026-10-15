using System;
using System.IO;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

public static class SessionSmoke
{
    static int checks;
    public static void Run()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.isPlaying = true;
    }

    static void Changed(PlayModeStateChange mode)
    {
        if (mode != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            string first = Guid.NewGuid().ToString("N"), second = Guid.NewGuid().ToString("N");
            using (var host = new RemoteTrialLink(true, 0))
            using (var teacher = new RemoteTrialLink(false, host.LocalPort, "127.0.0.1"))
            {
                host.BeginTrial(first, "あいうえお", "anonymous-test");
                teacher.DeviceReady = true;
                Exchange(host, teacher);
                Check(host.Connected && teacher.Connected, "bidirectional UDP heartbeat connects both PCs");
                Check(teacher.State.trialId == first && teacher.State.prompt == "あいうえお", "trial ID and prompt shared");
                Check(!host.CanDraw && !teacher.CanDraw, "drawing blocked until both ready");
                teacher.LocalReady = true;
                Exchange(host, teacher);
                Check(!host.CanDraw, "teacher readiness alone cannot start drawing");
                host.LocalReady = true;
                Exchange(host, teacher);
                Check(host.CanDraw && teacher.CanDraw, "both readiness acknowledgements start drawing");
                host.SetPhase("error", "test gap");
                Exchange(host, teacher);
                Check(!teacher.CanDraw && teacher.State.error == "test gap", "receive failure stops teacher drawing");
                host.BeginTrial(second, "いう", "anonymous-test");
                Exchange(host, teacher);
                Check(teacher.State.trialId == second && !teacher.LocalReady && !host.LocalReady,
                    "retry creates a fresh trial and resets both readiness flags");
                host.DisconnectSeconds = 0.001f;
                teacher.DisconnectSeconds = 0.001f;
                Thread.Sleep(20);
                Check(host.TimedOut && teacher.TimedOut && !teacher.CanDraw, "heartbeat loss stops drawing");
            }
            using (var waiting = new RemoteTrialLink(true, 0))
            {
                waiting.InitialWaitSeconds = 0.001f;
                waiting.BeginTrial(first, "あ");
                Thread.Sleep(20);
                Check(waiting.TimedOut, "initial connection wait times out");
            }

            var obj = new GameObject("SessionReceiver");
            obj.SetActive(false);
            var receiver = obj.AddComponent<RemoteHandwritingReceiver>();
            receiver.receiveUdp = false;
            receiver.expectedSession = second;
            obj.SetActive(true);
            Queue(receiver, first, 0);
            Invoke(receiver, "Update");
            Check(receiver.CharacterCount == 0, "delayed handwriting from previous trial rejected");
            Queue(receiver, second, 0);
            Invoke(receiver, "Update");
            Check(receiver.CharacterCount == 1 && receiver.Error == null, "new trial accepts sequence zero");
            obj.SetActive(false);

            string folder;
            using (var learner = new RemoteTrialRecorder(second, "learner", "いう", "anonymous-test", false))
            using (var teacher = new RemoteTrialRecorder(second, "teacher", "いう", "anonymous-test", false))
            {
                folder = learner.DirectoryPath;
                learner.Record("tracePoint", 0, 0, new Vector3(1, 2, 3), 4f, width: 0.2f);
                learner.Record("characterSaved", 0);
                learner.SaveTemplate(JsonUtility.ToJson(new RemoteTemplateData { trialId = second, promptWord = "いう" }));
                teacher.Record("point", 0, 0, new Vector3(0.1f, 0.2f, 0), 0.3f, 0);
                teacher.Record("experimentEnd");
                Check(learner.Error == null && teacher.Error == null, "both roles save records under matching trial ID");
            }
            var entries = File.ReadAllLines(Path.Combine(folder, "learner_events.jsonl"));
            var point = JsonUtility.FromJson<RemoteTrialRecorder.Entry>(entries[1]);
            Check(point.trialId == second && point.role == "learner" && point.x == 1f && point.width == 0.2f,
                "saved trace retains trial, coordinates and line width");
            Check(File.Exists(Path.Combine(folder, "teacher_metadata.json")) && File.Exists(Path.Combine(folder, "template.json")),
                "metadata and completed template persisted");
            using (var duplicate = new RemoteTrialRecorder(second, "learner", "いう", "", false))
                Check(duplicate.Error != null, "existing trial recording cannot be overwritten");
            Check(File.ReadAllLines(Path.Combine(folder, "learner_events.jsonl")).Length == entries.Length,
                "failed duplicate open preserves previous records");

            var trace = new GameObject("RecordedTrace").AddComponent<DrawTrail>();
            int starts = 0, points = 0, ends = 0, clears = 0;
            trace.TraceRecorded += (kind, p, pressure, width) => {
                if (kind == "strokeStarted") starts++;
                if (kind == "tracePoint") points++;
                if (kind == "strokeEnded") ends++;
                if (kind == "traceCleared") clears++;
            };
            trace.StartStroke();
            trace.UpdateStroke(Vector3.zero, 5f);
            trace.UpdateStroke(Vector3.zero, 5f);
            trace.UpdateStroke(Vector3.one, 6f);
            trace.EndStroke();
            trace.EndStroke();
            trace.ClearTrail();
            Check(starts == 1 && points == 2 && ends == 1 && clears == 1, "trace log records only accepted ink points and actual boundaries");
            Debug.Log("SESSION_SMOKE_PASS: " + checks + " checks");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void Exchange(RemoteTrialLink host, RemoteTrialLink teacher)
    {
        for (int i = 0; i < 70; i++) { host.Tick(); teacher.Tick(); Thread.Sleep(10); }
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAILED: " + label);
        checks++;
        Debug.Log("PASS: " + label);
    }
    static void Queue(RemoteHandwritingReceiver receiver, string id, int sequence)
    {
        string json = JsonUtility.ToJson(new RemoteHandwritingReceiver.Packet { version = 1, session = id,
            method = "PenTablet", eventType = "point", sequence = sequence, character = 0, stroke = 0, x = 1f });
        receiver.GetType().GetMethod("QueuePacket", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(receiver, new object[] { json });
    }
    static void Invoke(object obj, string method)
    { obj.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null); }
}
