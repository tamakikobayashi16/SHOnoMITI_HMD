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
            receiver.ShowCharacter(1);
            var active = go.GetComponentInChildren<LineRenderer>();
            Check(active != null && active.positionCount == 1, "large tracing guide created");
            Check(active.GetPosition(0).z < 3f, "guide in front of paper");
            var paperBounds = paper.GetComponent<Renderer>().bounds;
            float expectedScale = Mathf.Min(paperBounds.size.x / 3.84f, paperBounds.size.y / 2.16f) * 0.9f;
            Debug.Log("Mapping check: bounds=" + paperBounds + " point=" + active.GetPosition(0));
            Check(Math.Abs(active.GetPosition(0).x - (paperBounds.center.x + 0.2f * expectedScale)) < 0.001f, "tablet coordinates mapped with aspect ratio preserved");
            receiver.displayScale = 1.65f;
            Tick();
            Check(Math.Abs(active.GetPosition(0).x - (paperBounds.center.x + 0.2f * expectedScale * 1.65f)) < 0.001f,
                "live scale adjustment updates existing handwriting");
            receiver.displayScale = 1f;
            Tick();
            float originalWidth = active.startWidth;
            receiver.lineWidthMultiplier = 2.5f;
            Tick();
            Check(Math.Abs(active.startWidth - originalWidth * 2.5f) < 0.001f,
                "template width multiplier thickens existing lines");
            receiver.lineWidthMultiplier = 1f;
            Tick();
            Queue(6, "experimentEnd", 1, 0f);
            Tick();
            Check(!receiver.HasFinished, "completion waits for earlier stroke packet");
            Queue(5, "strokeEnd", 1, 0f);
            Tick();
            Check(receiver.HasFinished, "completion received after ordered packets");
            Check(Math.Abs(active.GetPosition(0).x - paperBounds.center.x) < 0.001f, "last character centered at completion");
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
            go.SetActive(true);
            Queue(0, "nextCharacter", 3, 0f);
            Tick();
            Check(receiver.Error == null, "advance after third character is allowed");
            Queue(1, "point", 3, 0f);
            Tick();
            Check(receiver.Error != null && receiver.CharacterCount == 0, "fourth character is rejected");
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
            receiver = imported;
            imported.displayScale = 1.65f;
            imported.ShowCharacter(0);
            Queue(0, "point", 0, 10f);
            Queue(1, "point", 0, 30f);
            Tick();
            var fittedLine = importedObject.GetComponentInChildren<LineRenderer>();
            float liveX = fittedLine.GetPosition(0).x;
            Queue(2, "strokeEnd", 0, 0f);
            Tick();
            Check(Math.Abs(fittedLine.GetPosition(0).x - liveX) < 0.001f, "stroke end does not fit an unfinished character");
            Queue(3, "nextCharacter", 1, 0f);
            Tick();
            Check(fittedLine.GetPosition(0).x > paperBounds.min.x && fittedLine.GetPosition(1).x < paperBounds.max.x,
                "large offset handwriting fits paper at character completion");
            Check(Math.Abs((fittedLine.GetPosition(0).x + fittedLine.GetPosition(1).x) / 2f - paperBounds.center.x) < 0.001f,
                "completed handwriting is centered");
            imported.displayScale = 2f;
            imported.lineWidthMultiplier = 4f;
            Tick();
            Check(InsideFrame(CurrentLiveLines()[0], paperBounds), "completed oversized guide fits printed frame at maximum scale with thicker ink");
            Queue(4, "clearCharacter", 0, 0f);
            Queue(5, "point", 0, 10f);
            Tick();
            Check(!imported.HasFinished && imported.HasEveryCharacter(), "redo after fitting accepts fresh handwriting");
            importedObject.SetActive(false);
            importedObject.SetActive(true);
            imported.fitWhileReceiving = true;
            imported.lineWidthMultiplier = 4f;
            imported.ShowCharacter(0);
            Queue(0, "point", 0, 10f);
            Queue(1, "point", 0, 30f);
            Tick();
            var liveLine = CurrentLiveLines()[0];
            Check(!imported.HasFinished && InsideFrame(liveLine, paperBounds), "unfinished offset handwriting fits inside printed frame including thick caps");
            float oldPosition = liveLine.GetPosition(0).x;
            var newStroke = new RemoteHandwritingReceiver.Packet {
                version = 1, session = "test", method = "PenTablet", sequence = 2,
                eventType = "point", character = 0, stroke = 1, x = 100f, y = 200f
            };
            typeof(RemoteHandwritingReceiver).GetMethod("QueuePacket", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(imported, new object[] { JsonUtility.ToJson(newStroke) });
            Tick();
            Check(Math.Abs(oldPosition - liveLine.GetPosition(0).x) > 0.001f, "new stroke rescales previous strokes together");
            foreach (var line in CurrentLiveLines())
                Check(InsideFrame(line, paperBounds), "all live strokes remain inside frame after horizontal and vertical expansion");
            Queue(3, "experimentEnd", 0, 0f);
            Tick();
            var raw = imported.ExportTemplate("あ", false);
            Check(raw.strokes.Exists(s => s.points.Exists(p => p.x == 100f && p.y == 200f)), "live fitting preserves original coordinates for tracing");
            importedObject.SetActive(false);
            importedObject.SetActive(true);
            imported.ShowCharacter(0);
            Queue(0, "point", 0, 10f);
            Tick();
            var resetLine = CurrentLiveLines()[0];
            Check(Math.Abs(resetLine.GetPosition(0).x - paperBounds.center.x) < 0.001f, "new reception clears previous live bounds");
            Queue(1, "clearCharacter", 0, 0f);
            Queue(2, "point", 0, -100f);
            Tick();
            var liveBounds = (System.Collections.IDictionary)typeof(RemoteHandwritingReceiver)
                .GetField("liveBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(imported);
            Check(((Bounds)liveBounds[0]).center.x == -100f, "redo resets live fit bounds");
            importedObject.SetActive(false);
            importedObject.SetActive(true);
            imported.fitWhileReceiving = false;
            var replay = imported.ReplayTestWord();
            Check(replay.MoveNext() && imported.CharacterCount == 1 && !imported.HasFinished, "test handwriting starts progressively");
            while (replay.MoveNext()) { }
            var sample = imported.ExportTemplate(RemoteTestHandwriting.Word, true);
            Check(imported.CharacterCount == 3 && imported.HasEveryCharacter() && sample.strokes.Count == 7, "three-character sample completes with all seven strokes preserved");
            importedObject.SetActive(false);
            Debug.Log("REMOTE_SMOKE_PASS: " + checks + " checks");
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static List<LineRenderer> CurrentLiveLines()
    {
        // Destroy is deferred until the frame ends; ignore retired renderers from earlier cases.
        var strokes = (System.Collections.IDictionary)typeof(RemoteHandwritingReceiver)
            .GetField("strokes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(receiver);
        var result = new List<LineRenderer>();
        foreach (var stroke in strokes.Values)
        {
            var line = (LineRenderer)stroke.GetType().GetField("active").GetValue(stroke);
            if (line != null) result.Add(line);
        }
        return result;
    }

    static bool InsideFrame(LineRenderer line, Bounds paperBounds)
    {
        float radius = line.startWidth * 0.5f;
        for (int i = 0; i < line.positionCount; i++)
        {
            Vector3 offset = line.GetPosition(i) - paperBounds.center;
            if (Math.Abs(offset.x) + radius > paperBounds.size.x * 0.4f + 0.001f ||
                Math.Abs(offset.y) + radius > paperBounds.size.y * 0.4f + 0.001f) return false;
        }
        return true;
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
