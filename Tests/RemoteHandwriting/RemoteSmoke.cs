using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

public static class RemoteSmoke
{
    static RemoteHandwritingReceiver receiver;
    static int checks;
    public static void Run()
    {
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        EditorApplication.playModeStateChanged += Changed;
        EditorApplication.isPlaying = true;
    }
    static void Changed(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        try
        {
            var go = new GameObject("Receiver");
            go.SetActive(false);
            receiver = go.AddComponent<RemoteHandwritingReceiver>();
            using (var portProbe = new UdpClient(0))
                receiver.listenPort = ((IPEndPoint)portProbe.Client.LocalEndPoint).Port;
            var paper = GameObject.CreatePrimitive(PrimitiveType.Quad);
            paper.transform.localScale = new Vector3(8f, 7f, 1f);
            paper.transform.position = new Vector3(0f, 0f, 3f);
            receiver.paper = paper;
            var previews = new List<GameObject> { GameObject.CreatePrimitive(PrimitiveType.Quad), GameObject.CreatePrimitive(PrimitiveType.Quad) };
            receiver.SetPreviewTargets(previews);
            go.SetActive(true);
            Check(receiver.Error == null, "UDP socket opened");

            using (var sender = new UdpClient())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Json(0, "point", 0, -0.5f));
                sender.Send(bytes, bytes.Length, "127.0.0.1", receiver.listenPort);
                for (int n = 0; n < 50 && receiver.CharacterCount == 0; n++) { Thread.Sleep(10); Tick(); }
            }
            Check(receiver.CharacterCount == 1, "real UDP point received");
            Check(!receiver.HasFinished, "point is not completion");
            Queue(2, "point", 1, 0.5f);
            Tick();
            Check(receiver.CharacterCount == 1, "out-of-order packet waits");
            Queue(1, "nextCharacter", 1, 0f);
            Tick();
            Check(receiver.CharacterCount == 2 && receiver.HasEveryCharacter(), "previous character retained");
            Check(receiver.Error == null, "reordering recovered without loss");
            Check(previews[0].GetComponentInChildren<LineRenderer>().positionCount == 1, "first preview retained");
            Queue(2, "point", 1, 0.5f);
            Tick();
            Check(previews[1].GetComponentInChildren<LineRenderer>().positionCount == 1, "duplicate ignored");
            Queue(3, "clearCharacter", 1, 0f);
            Tick();
            Check(!receiver.HasEveryCharacter(), "redo clears only current character");
            Queue(4, "point", 1, 0.2f);
            Tick();
            Check(receiver.HasEveryCharacter(), "redrawn character restored");
            receiver.ShowCharacter(0);
            var active = go.GetComponentInChildren<LineRenderer>();
            Check(active != null && active.positionCount == 1, "large tracing guide created");
            Check(active.GetPosition(0).z < 3f, "guide in front of paper");
            var paperBounds = paper.GetComponent<Renderer>().bounds;
            float expectedScale = Mathf.Min(paperBounds.size.x / 3.84f, paperBounds.size.y / 2.16f) * 0.9f;
            Debug.Log("Mapping check: bounds=" + paperBounds + " point=" + active.GetPosition(0));
            Check(Math.Abs(active.GetPosition(0).x - (paperBounds.center.x - 0.5f * expectedScale)) < 0.001f, "tablet coordinates mapped with aspect ratio preserved");
            Queue(6, "experimentEnd", 1, 0f);
            Tick();
            Check(!receiver.HasFinished, "completion waits for earlier stroke packet");
            Queue(5, "strokeEnd", 1, 0f);
            Tick();
            Check(receiver.HasFinished, "completion received after ordered packets");
            var completed = receiver.ExportTemplate("十字", false);
            Check(completed.strokes.Count == 2, "completed handwriting exported by character and stroke");
            Queue(7, "point", 1, 1f);
            Tick();
            Check(receiver.CharacterCount == 2, "finished template frozen");
            go.SetActive(false);
            go.SetActive(true);
            Check(receiver.CharacterCount == 0 && !receiver.HasFinished, "receiver lifecycle resets and releases socket");
            Queue(1, "point", 0, 0f);
            Tick();
            Thread.Sleep(180);
            Tick();
            Check(receiver.Error != null, "missing UDP packet reported");
            go.SetActive(false);
            var importedObject = new GameObject("ImportedTemplate");
            importedObject.SetActive(false);
            var imported = importedObject.AddComponent<RemoteHandwritingReceiver>();
            imported.receiveUdp = false;
            imported.paper = paper;
            importedObject.SetActive(true);
            imported.LoadTemplate(completed);
            Check(imported.HasFinished && imported.CharacterCount == 2 && imported.HasEveryCharacter(), "template restored after source receiver shutdown");
            completed.strokes[0].points.Clear();
            Check(imported.ExportTemplate("十字", false).strokes[0].points.Count > 0, "import copies coordinates independently");
            importedObject.SetActive(false);
            importedObject.SetActive(true);
            var replay = imported.ReplayTestCross();
            Check(replay.MoveNext() && imported.CharacterCount == 1 && !imported.HasFinished, "test handwriting starts progressively");
            while (replay.MoveNext()) { }
            var sample = imported.ExportTemplate("十", true);
            Check(sample.strokes.Count == 2 && sample.strokes[0].points.Count == 33 && sample.strokes[1].points.Count == 33, "test completes with both strokes preserved");
            importedObject.SetActive(false);
            Debug.Log("REMOTE_SMOKE_PASS: " + checks + " checks");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new Exception("FAILED: " + label);
        checks++;
        Debug.Log("PASS: " + label);
    }
    static string Json(int sequence, string type, int character, float x)
    {
        return JsonUtility.ToJson(new RemoteHandwritingReceiver.Packet {
            version = 1, session = "test", method = "PenTablet", sequence = sequence,
            eventType = type, character = character, stroke = 0, x = x, y = 0f, z = 10f
        });
    }
    static void Queue(int sequence, string type, int character, float x)
    {
        typeof(RemoteHandwritingReceiver).GetMethod("QueuePacket", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(receiver, new object[] { Json(sequence, type, character, x) });
    }
    static void Tick()
    {
        typeof(RemoteHandwritingReceiver).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(receiver, null);
    }
}
