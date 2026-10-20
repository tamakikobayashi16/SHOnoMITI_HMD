using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Run only in the isolated verification project; exercises real UDP and Preview UI.
public static class TopicSmoke
{
    static double started;
    static int stage, checks;
    static UdpClient sender;
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/TempRemoteVerification/Assets"))
            throw new Exception("Run only in the isolated verification project");
        EditorSceneManager.OpenScene("Assets/Scenes/RemotePreview.unity");
        var preview = UnityEngine.Object.FindObjectOfType<RemotePreviewController>();
        preview.useLocalTestTemplate = false;
        using (var probe = new UdpClient(0)) preview.controlPort = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        using (var probe = new UdpClient(0)) preview.listenPort = ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorWindow.GetWindow(Type.GetType("UnityEditor.GameView,UnityEditor")).Show();
        started = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - started > 60) throw new Exception("timeout");
            if (!EditorApplication.isPlaying || Time.time < 5 || RemoteTrialRuntime.Active?.Link == null) return;
            var link = RemoteTrialRuntime.Active.Link;
            var preview = UnityEngine.Object.FindObjectOfType<RemotePreviewController>();
            if (stage == 0)
            {
                sender = new UdpClient(0);
                sender.Client.ReceiveTimeout = 2000;
                var message = new RemoteTrialLink.Message { kind = "hello", peerId = "topic-test",
                    topicId = "T01", topicText = "夏といえば？", topicRevision = 1, deviceReady = true };
                string originalPrompt = link.State.prompt;
                Send(link, message);
                Check(link.State.topicText == "夏といえば？", "initial empty trial accepted from new waiting peer");
                var endpoint = new IPEndPoint(IPAddress.Any, 0);
                var ack = JsonUtility.FromJson<RemoteTrialLink.Message>(Encoding.UTF8.GetString(sender.Receive(ref endpoint)));
                Check(ack.topicId == "T01" && ack.topicRevision == 1 && ack.topicText == message.topicText, "status acknowledges exact topic");
                message.trialId = link.State.trialId;
                message.topicRevision = 2; message.topicText = "花といえば？";
                Send(link, message);
                Check(link.State.topicText == message.topicText, "new revision updates topic");
                message.topicRevision = 1; message.topicText = "古い質問"; Send(link, message);
                Check(link.State.topicRevision == 2 && link.State.topicText == "花といえば？", "old revision ignored");
                message.topicRevision = 3; message.trialId = "previous-trial"; Send(link, message);
                Check(link.State.topicRevision == 2, "wrong trial ignored");
                message.trialId = link.State.trialId; message.topicText = new string('あ', 257); Send(link, message);
                Check(link.State.topicRevision == 2, "oversized topic ignored");
                message.peerId = "other-peer"; message.topicText = "別PC"; Send(link, message);
                Check(link.State.topicRevision == 2, "another live peer rejected");
                Check(link.State.prompt == originalPrompt && !link.CanDraw, "question does not change prompt or bypass readiness");
                link.BeginTrial("topic-retry", originalPrompt);
                Check(string.IsNullOrEmpty(link.State.topicText), "retry clears topic");
                message.peerId = "topic-test"; message.trialId = "topic-retry";
                message.topicRevision = 2; message.topicText = "夏といえば？"; Send(link, message);
                Check(link.State.topicText == message.topicText, "retry restores retransmitted topic");
                stage = 1;
                return;
            }
            if (stage == 1)
            {
                Check(preview.headingText.text == "お題：夏といえば？", "received topic appears in Preview heading");
                Check(!preview.headingText.isTextOverflowing, "Japanese question fits heading");
                ScreenCapture.CaptureScreenshot(Path.GetFullPath("topic-preview.png"));
                stage = 2; return;
            }
            if (stage == 2 && File.Exists("topic-preview.png"))
            { sender.Dispose(); Debug.Log("TOPIC_SMOKE_PASS: " + checks + " checks"); EditorApplication.Exit(0); }
        }
        catch (Exception e) { sender?.Dispose(); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Send(RemoteTrialLink link, RemoteTrialLink.Message message)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
        sender.Send(bytes, bytes.Length, "127.0.0.1", link.LocalPort);
        link.Tick();
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new Exception(label);
        checks++; Debug.Log("PASS: " + label);
    }
}
