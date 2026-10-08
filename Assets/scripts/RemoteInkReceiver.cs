using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

public class RemoteInkReceiver : MonoBehaviour
{
    [Range(1, 65535)] public int listenPort = 5005;
    public Material lineMaterial;
    public float lineWidth = 0.3f;
    public Vector3 positionOffset;
    public float positionScale = 1f;
    public int maxPacketsPerFrame = 1000;

    public UnityEvent experimentEnded = new UnityEvent();
    public bool HasExperimentEnded { get; private set; }
    public bool HasPacketLoss { get; private set; }
    public string ReceiveError { get; private set; }
    public bool IsListening => running;
    public int[] Characters {
        get {
            var ids = new SortedSet<int>();
            foreach (var key in lines.Keys) ids.Add(int.Parse(key.Split(':')[0]));
            var result = new int[ids.Count]; ids.CopyTo(result); return result;
        }
    }
    private readonly HashSet<string> retiredSessions = new HashSet<string>();
    public void ShowCharacter(int character) {
        foreach (var pair in lines) pair.Value.gameObject.SetActive(pair.Key.StartsWith(character + ":", StringComparison.Ordinal));
    }
    public void TranslateExample(Vector3 delta) {
        foreach (var line in lines.Values)
            for (int i = 0; i < line.positionCount; i++) line.SetPosition(i, line.GetPosition(i) + delta);
    }
    public void HideAll() { foreach (var line in lines.Values) line.gameObject.SetActive(false); }
    public void ResetReception() {
        if (session != null) retiredSessions.Add(session);
        while (incoming.TryDequeue(out _)) { }
        ClearAll();
        HasExperimentEnded = false;
        HasPacketLoss = false;
        session = null;
        lastSequence = -1;
        queueOverflow = false;
    }


    [Serializable]
    private class Packet
    {
        public int version;
        public string session, method, eventType;
        public int sequence, character, stroke;
        public float x, y, z, pressure;
    }

    private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    private readonly Dictionary<string, LineRenderer> lines = new Dictionary<string, LineRenderer>();
    private UdpClient socket;
    private Thread worker;
    private volatile bool running;
    private string session;
    private int lastSequence = -1;
    private string receiveError;
    private volatile bool queueOverflow;

    private void OnEnable()
    {
        try
        {
            socket = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
            running = true;
            worker = new Thread(ReceiveLoop) { IsBackground = true };
            worker.Start();
        }
        catch (Exception e) { ReceiveError = e.Message; running = false; socket?.Close(); Debug.LogError($"Drawing UDP receiver: {e.Message}", this); }
    }

    private void ReceiveLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, 0);
        while (running)
        {
            try
            {
                var bytes = socket.Receive(ref endpoint);
                // Bound pending work when the application cannot keep up.
                if (incoming.Count < 10000) incoming.Enqueue(Encoding.UTF8.GetString(bytes));
                else queueOverflow = true;
            }
            catch (SocketException e) { if (running) receiveError = e.Message; break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private void Update()
    {
        if (queueOverflow) HasPacketLoss = true;
        if (receiveError != null)
        {
            Debug.LogError($"Drawing UDP receiver: {receiveError}", this);
            ReceiveError = receiveError;
            running = false;
            receiveError = null;
        }
        for (int i = 0; i < Mathf.Max(1, maxPacketsPerFrame) && incoming.TryDequeue(out var json); i++)
        {
            ProcessPacket(json);
        }
    }

    private void ProcessPacket(string json)
    {
        Packet packet;
        try { packet = JsonUtility.FromJson<Packet>(json); }
        catch (ArgumentException) { return; }
        if (packet == null || packet.version != 1 || packet.method != "PenTablet" ||
            string.IsNullOrEmpty(packet.session)) return;

        if (HasExperimentEnded || retiredSessions.Contains(packet.session)) return;
        if (packet.sequence < 0 || packet.character < 0 || packet.stroke < 0 || packet.character > 255 || packet.stroke > 4095) return;
        if (packet.eventType != "point" && packet.eventType != "strokeEnd" && packet.eventType != "nextCharacter" && packet.eventType != "clearCharacter" && packet.eventType != "experimentEnd") return;
        if (packet.eventType == "point" && (!Finite(packet.x) || !Finite(packet.y) || !Finite(packet.z) || !Finite(packet.pressure))) return;
        if (session != packet.session)
        {
            if (session != null) retiredSessions.Add(session);
            ClearAll();
            HasPacketLoss = queueOverflow;
            HasExperimentEnded = false;
            session = packet.session;
            lastSequence = -1;
        }
        if (packet.sequence <= lastSequence) return;
        if (packet.sequence != lastSequence + 1) HasPacketLoss = true;
        lastSequence = packet.sequence;

        if (HasExperimentEnded) return;

        switch (packet.eventType)
        {
            case "experimentEnd":
                HasExperimentEnded = true;
                Debug.Log("Received experimentEnd: drawing experiment finished.", this);
                experimentEnded.Invoke();
                break;
            case "clearCharacter":
                ClearCharacter(packet.character);
                break;
            case "nextCharacter":
                ShowCharacter(packet.character);
                // Keep previous characters for tracing after the full example arrives.
                break;
            case "point":
                AddPoint(packet);
                ShowCharacter(packet.character);
                break;
            // Each stroke has its own line, so strokeEnd requires no update.
        }
    }

    private void ClearCharacter(int character)
    {
        string prefix = character + ":";
        foreach (var key in new List<string>(lines.Keys))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            DestroyLine(lines[key].gameObject);
            lines.Remove(key);
        }
    }

    private void AddPoint(Packet packet)
    {
        if (!Finite(packet.x) || !Finite(packet.y) || !Finite(packet.z)) return;
        string key = packet.character + ":" + packet.stroke;
        if (!lines.TryGetValue(key, out var line))
        {
            line = CreateLine(key);
            lines.Add(key, line);
        }

        int index = line.positionCount;
        line.positionCount = index + 1;
        line.SetPosition(index,
            new Vector3(packet.x, packet.y, packet.z) * positionScale + positionOffset);
    }

    private LineRenderer CreateLine(string key)
    {
        var obj = new GameObject("ReceivedStroke_" + key);
        obj.transform.SetParent(transform, false);
        var line = obj.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.sharedMaterial = lineMaterial;
        line.startWidth = line.endWidth = lineWidth;
        line.positionCount = 0;
        line.numCapVertices = 8;
        return line;
    }

    private static void DestroyLine(GameObject obj) {
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private void ClearAll()
    {
        foreach (var line in lines.Values) if (line != null) DestroyLine(line.gameObject);
        lines.Clear();
    }
    private void OnDisable()
    {
        running = false;
        socket?.Close();
        worker?.Join(1000);
        socket = null;
        worker = null;
        while (incoming.TryDequeue(out _)) { }
        ResetReception();
        retiredSessions.Clear();
        ReceiveError = null;
        HasExperimentEnded = false;
        session = null;
        lastSequence = -1;
    }
}
