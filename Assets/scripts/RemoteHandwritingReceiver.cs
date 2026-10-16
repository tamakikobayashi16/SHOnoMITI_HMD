using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>Receives shodoVR DrawingUdpSender version 1 PenTablet packets.</summary>
public class RemoteHandwritingReceiver : MonoBehaviour
{
    public const int MaxCharacters = 5;
    [Range(0.5f, 2f)] public float displayScale = 1f;
    [Range(0.5f, 5f)] public float lineWidthMultiplier = 1f;
    float previousLineWidthMultiplier = float.NaN;
    float previousDisplayScale = float.NaN;
    public int listenPort = 5005;
    public bool receiveUdp = true;
    public string expectedSession;
    public bool acceptRemotePackets = true;
    public float LastPacketTime { get; private set; }
    public event Action<Packet> PacketApplied;
    public GameObject paper;
    public Vector2 sourceHalfSize = new Vector2(1.92f, 1.08f);
    public float sourceLineWidth = 0.03f;
    public Color guideColor = new Color(0.65f, 0.65f, 0.65f, 1f);
    public bool HasFinished { get; private set; }
    public int CharacterCount { get; private set; }
    public string Error { get; private set; }
    public event Action CharacterAdded;

    [Serializable]
    public class Packet
    {
        public int version;
        public string session, method, eventType;
        public int sequence, character, stroke;
        public float x, y, z, pressure;
    }

    class Stroke
    {
        public int character, stroke;
        public readonly List<Vector3> points = new List<Vector3>();
        public LineRenderer preview, active;
    }

    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly SortedDictionary<int, Packet> pending = new SortedDictionary<int, Packet>();
    readonly Dictionary<string, Stroke> strokes = new Dictionary<string, Stroke>();
    readonly Dictionary<int, Bounds> completedBounds = new Dictionary<int, Bounds>();
    List<GameObject> previewTargets = new List<GameObject>();
    UdpClient socket;
    Thread worker;
    volatile bool running;
    string session;
    volatile string receiveError;
    int nextSequence, activeCharacter = -1;
    float gapSince = -1f;
    Material material;

    void OnEnable()
    {
        try
        {
            material = new Material(Shader.Find("Sprites/Default"));
            if (!receiveUdp) return;
            socket = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
            running = true;
            worker = new Thread(ReceiveLoop) { IsBackground = true };
            worker.Start();
        }
        catch (Exception e)
        {
            Error = "お手本のUDP受信を開始できません: " + e.Message;
            Debug.LogError(Error, this);
        }
    }

    void ReceiveLoop()
    {
        var endpoint = new IPEndPoint(IPAddress.Any, 0);
        var client = socket;
        while (running)
        {
            try
            {
                byte[] bytes = client.Receive(ref endpoint);
                if (incoming.Count < 10000) incoming.Enqueue(Encoding.UTF8.GetString(bytes));
                else receiveError = "お手本の受信が追いつきません。シーンを再起動して書き直してください。";
            }
            catch (ObjectDisposedException) { break; }
            catch (SocketException e)
            {
                if (running) receiveError = "お手本のUDP受信が停止しました: " + e.Message;
                break;
            }
        }
    }

    void Update()
    {
        if (previousDisplayScale != displayScale || previousLineWidthMultiplier != lineWidthMultiplier)
        {
            previousDisplayScale = displayScale;
            previousLineWidthMultiplier = lineWidthMultiplier;
            foreach (var stroke in strokes.Values)
            {
                if (stroke.preview != null) stroke.preview.positionCount = 0;
                if (stroke.active != null) stroke.active.positionCount = 0;
                DrawStroke(stroke);
            }
        }
        if (receiveError != null) Error = receiveError;
        if (receiveUdp && !acceptRemotePackets) return;
        for (int i = 0; i < 1000 && incoming.TryDequeue(out var json); i++) QueuePacket(json);
        // Briefly wait for reordered packets before treating a sequence gap as loss.
        while (pending.Count > 0 && !HasFinished)
        {
            int first = FirstSequence();
            if (first > nextSequence)
            {
                if (gapSince < 0f) gapSince = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - gapSince < 0.15f) break;
                Error = "お手本の通信データが一部欠けています。両側を再起動して書き直してください。";
                nextSequence = first;
            }
            Packet packet = pending[first];
            pending.Remove(first);
            nextSequence = first + 1;
            gapSince = -1f;
            ApplyPacket(packet);
        }
    }

    int FirstSequence()
    {
        foreach (var pair in pending) return pair.Key;
        return nextSequence;
    }

    void QueuePacket(string json)
    {
        if (HasFinished) return;
        Packet packet;
        try { packet = JsonUtility.FromJson<Packet>(json); }
        catch (ArgumentException) { return; }
        if (packet == null || packet.version != 1 || packet.method != "PenTablet" ||
            string.IsNullOrEmpty(packet.session) || packet.sequence < 0 ||
            packet.character < 0 || packet.character >= 64 || packet.stroke < 0) return;
        if (packet.eventType != "point" && packet.eventType != "strokeEnd" &&
            packet.eventType != "clearCharacter" && packet.eventType != "nextCharacter" &&
            packet.eventType != "experimentEnd") return;
        if (!Finite(packet.x) || !Finite(packet.y) || !Finite(packet.z)) return;
        if (!string.IsNullOrEmpty(expectedSession) && packet.session != expectedSession) return;
        if (session == null) session = packet.session;
        if (packet.session != session || packet.sequence < nextSequence) return;
        if (pending.Count < 10000) pending[packet.sequence] = packet;
    }

    void ApplyPacket(Packet packet)
    {
        LastPacketTime = Time.realtimeSinceStartup;
        PacketApplied?.Invoke(packet);
        if (packet.eventType == "experimentEnd")
        {
            for (int i = 0; i < CharacterCount; i++) CompleteCharacter(i);
            HasFinished = true;
            return;
        }
        if (packet.eventType == "nextCharacter")
        {
            CompleteCharacter(packet.character - 1);
            return;
        }
        if (packet.eventType == "clearCharacter")
        {
            completedBounds.Remove(packet.character);
            foreach (var key in new List<string>(strokes.Keys))
            {
                Stroke stroke = strokes[key];
                if (stroke.character != packet.character) continue;
                DestroyLines(stroke);
                strokes.Remove(key);
            }
            return;
        }
        // nextCharacter advances the sender's index; retain all previous handwriting.
        if (packet.eventType != "point") return;
        if (packet.character >= MaxCharacters)
        {
            Error = "お手本は最大5文字です。5文字以内で書き直してください。";
            return;
        }
        if (packet.character >= CharacterCount)
        {
            CharacterCount = packet.character + 1;
            CharacterAdded?.Invoke();
        }
        string id = packet.character + ":" + packet.stroke;
        if (!strokes.TryGetValue(id, out var current))
        {
            current = new Stroke { character = packet.character, stroke = packet.stroke };
            strokes.Add(id, current);
        }
        if (current.points.Count >= 100000)
        {
            Error = "お手本の点数が上限を超えました。書き直してください。";
            return;
        }
        current.points.Add(new Vector3(packet.x, packet.y, 0f));
        DrawStroke(current);
    }

    // Only fit at a character boundary, so incoming points never move existing ink.
    void CompleteCharacter(int character)
    {
        if (character < 0 || completedBounds.ContainsKey(character)) return;
        Bounds bounds = new Bounds();
        bool found = false;
        foreach (var stroke in strokes.Values)
            if (stroke.character == character)
                foreach (var point in stroke.points)
                {
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
        if (!found) return;
        completedBounds.Add(character, bounds);
        foreach (var stroke in strokes.Values)
        {
            if (stroke.character != character) continue;
            if (stroke.preview != null) stroke.preview.positionCount = 0;
            if (stroke.active != null) stroke.active.positionCount = 0;
            DrawStroke(stroke);
        }
    }

    /// <summary>Local coordinate sample using the same stroke rendering as received packets.</summary>
    public void LoadTestWord()
    {
        LoadTemplate(RemoteTestHandwriting.Create());
    }

    public RemoteTemplateData ExportTemplate(string promptWord, bool isLocalTest)
    {
        if (!HasFinished || !HasEveryCharacter() || Error != null)
            throw new InvalidOperationException("The template is incomplete or has a receive error.");
        var data = new RemoteTemplateData {
            promptWord = promptWord, isLocalTest = isLocalTest,
            sourceHalfSize = sourceHalfSize, lineWidth = sourceLineWidth
        };
        foreach (var stroke in strokes.Values)
            data.strokes.Add(new RemoteTemplateData.Stroke {
                character = stroke.character, stroke = stroke.stroke,
                points = new List<Vector3>(stroke.points)
            });
        return data;
    }

    public void LoadTemplate(RemoteTemplateData data)
    {
        if (receiveUdp || HasFinished || CharacterCount != 0 || data == null)
            throw new InvalidOperationException("Load the template into a new receiver with UDP disabled.");
        sourceHalfSize = data.sourceHalfSize;
        sourceLineWidth = data.lineWidth;
        foreach (var stroke in data.strokes)
            foreach (var point in stroke.points)
                ApplyPacket(new Packet { eventType = "point", character = stroke.character,
                    stroke = stroke.stroke, x = point.x, y = point.y });
        ApplyPacket(new Packet { eventType = "experimentEnd" });
    }

    public System.Collections.IEnumerator ReplayTestWord(float secondsPerPoint = 0.02f)
    {
        if (receiveUdp || HasFinished || CharacterCount != 0)
            throw new InvalidOperationException("Replay the local sample in a new receiver with UDP disabled.");
        int previousCharacter = -1;
        foreach (var stroke in RemoteTestHandwriting.Create().strokes)
        {
            if (previousCharacter >= 0 && previousCharacter != stroke.character)
            {
                ApplyPacket(new Packet { eventType = "nextCharacter", character = stroke.character });
                yield return new WaitForSeconds(0.5f);
            }
            previousCharacter = stroke.character;
            foreach (var point in stroke.points)
            {
                ApplyPacket(new Packet { eventType = "point", character = stroke.character,
                    stroke = stroke.stroke, x = point.x, y = point.y });
                yield return new WaitForSeconds(Mathf.Max(0.001f, secondsPerPoint));
            }
            yield return new WaitForSeconds(0.25f);
        }
        ApplyPacket(new Packet { eventType = "experimentEnd" });
    }

    public bool HasEveryCharacter()
    {
        if (CharacterCount == 0) return false;
        for (int i = 0; i < CharacterCount; i++)
        {
            bool found = false;
            foreach (var stroke in strokes.Values)
                if (stroke.character == i && stroke.points.Count > 0) { found = true; break; }
            if (!found) return false;
        }
        return true;
    }

    public void SetPreviewTargets(List<GameObject> targets)
    {
        previewTargets = new List<GameObject>(targets);
        foreach (var stroke in strokes.Values)
        {
            if (stroke.preview != null) Destroy(stroke.preview.gameObject);
            stroke.preview = null;
            DrawStroke(stroke);
        }
    }

    public void ShowCharacter(int index)
    {
        HideActiveCharacter();
        activeCharacter = index;
        foreach (var stroke in strokes.Values) DrawStroke(stroke);
    }

    public void HideActiveCharacter()
    {
        activeCharacter = -1;
        foreach (var stroke in strokes.Values)
        {
            if (stroke.active != null) Destroy(stroke.active.gameObject);
            stroke.active = null;
        }
    }

    void DrawStroke(Stroke stroke)
    {
        if (stroke.character < previewTargets.Count && previewTargets[stroke.character] != null)
        {
            if (stroke.preview == null) stroke.preview = CreateLine(previewTargets[stroke.character].transform, Color.black);
            UpdateLine(stroke.preview, stroke, previewTargets[stroke.character]);
        }
        if (stroke.character == activeCharacter && paper != null)
        {
            if (stroke.active == null) stroke.active = CreateLine(transform, guideColor);
            UpdateLine(stroke.active, stroke, paper);
        }
    }

    LineRenderer CreateLine(Transform parent, Color color)
    {
        var go = new GameObject("RemoteTemplateStroke");
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.positionCount = 0;
        line.startColor = line.endColor = color;
        line.numCapVertices = line.numCornerVertices = 8;
        line.sortingOrder = 0;
        return line;
    }

    void UpdateLine(LineRenderer line, Stroke stroke, GameObject target)
    {
        var points = stroke.points;
        var renderer = target.GetComponent<Renderer>();
        Bounds bounds = renderer != null ? renderer.bounds : new Bounds(target.transform.position, Vector3.one);
        float scale = Mathf.Min(bounds.size.x / (2f * Mathf.Max(0.001f, sourceHalfSize.x)),
            bounds.size.y / (2f * Mathf.Max(0.001f, sourceHalfSize.y))) * 0.9f * Mathf.Clamp(displayScale, 0.5f, 2f);
        Vector3 sourceCenter = Vector3.zero;
        float width = Mathf.Max(0.001f, sourceLineWidth) * Mathf.Clamp(lineWidthMultiplier, 0.5f, 5f);
        if (completedBounds.TryGetValue(stroke.character, out var inkBounds))
        {
            sourceCenter = inkBounds.center;
            // Include the round stroke caps and leave a 5% margin on every edge.
            float fit = Mathf.Min(bounds.size.x / (inkBounds.size.x + width),
                bounds.size.y / (inkBounds.size.y + width));
            scale = fit * 0.9f * Mathf.Clamp(displayScale / 1.65f, 0.1f, 1f);
        }
        var center = bounds.center;
        // The scene camera looks along +Z. Keep guides behind the learner's ink.
        center.z = bounds.min.z - 0.015f;
        line.startWidth = line.endWidth = Mathf.Max(0.001f, width * scale);
        int first = line.positionCount;
        // Existing points do not move while the remote person writes.
        line.positionCount = points.Count;
        for (int i = first; i < points.Count; i++) line.SetPosition(i, center + (points[i] - sourceCenter) * scale);
    }

    static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    void DestroyLines(Stroke stroke)
    {
        if (stroke.preview != null) Destroy(stroke.preview.gameObject);
        if (stroke.active != null) Destroy(stroke.active.gameObject);
    }

    void OnDisable()
    {
        running = false;
        socket?.Close();
        worker?.Join(1000);
        socket = null;
        worker = null;
        while (incoming.TryDequeue(out _)) { }
        pending.Clear();
        foreach (var stroke in strokes.Values) DestroyLines(stroke);
        strokes.Clear();
        completedBounds.Clear();
        if (material != null) Destroy(material);
        session = null;
        nextSequence = CharacterCount = 0;
        activeCharacter = -1;
        gapSince = -1f;
        HasFinished = false;
        Error = receiveError = null;
        LastPacketTime = 0f;
    }
}
