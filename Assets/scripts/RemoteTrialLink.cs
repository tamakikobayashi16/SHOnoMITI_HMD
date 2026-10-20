using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>Shared by the two projects. Control traffic is separate from drawing packets.</summary>
public sealed class RemoteTrialLink : IDisposable
{
    [Serializable]
    public class Message
    {
        public string protocol = "shodo-control";
        public int version = 1;
        public string kind, peerId, hostId, trialId, prompt, participantId, phase, error;
        public long revision;
        public string topicId, topicText;
        public long topicRevision;
        public bool hostReady, teacherReady, deviceReady;
    }

    readonly UdpClient socket;
    readonly bool host;
    readonly string identity = Guid.NewGuid().ToString("N");
    readonly HashSet<string> retiredHosts = new HashSet<string>();
    IPEndPoint peer;
    string peerIdentity;
    float lastSeen = -1f, openedAt, nextHello;
    long revision, lastRevision = -1;
    long receivedTopicRevision = -1;
    public Message State { get; private set; } = new Message { phase = "waiting" };
    public bool LocalReady { get; set; }
    public bool DeviceReady { get; set; }
    public string LocalError { get; set; }
    public string PeerError { get; private set; }
    public string Error { get; private set; }
    public float DisconnectSeconds = 5f;
    // Zero disables the initial timeout; a peer may start at any later time.
    public float InitialWaitSeconds = 0f;
    public bool Connected => lastSeen >= 0f && Time.realtimeSinceStartup - lastSeen < DisconnectSeconds;
    public bool TimedOut => lastSeen < 0f
        ? InitialWaitSeconds > 0f && Time.realtimeSinceStartup - openedAt > InitialWaitSeconds
        : !Connected;
    public bool CanDraw => Connected && State.phase == "drawing" && State.teacherReady && State.hostReady;
    public int LocalPort => ((IPEndPoint)socket.Client.LocalEndPoint).Port;

    public RemoteTrialLink(bool isHost, int port, string hostAddress = null)
    {
        host = isHost;
        socket = new UdpClient(new IPEndPoint(IPAddress.Any, isHost ? port : 0));
        if (!host) peer = new IPEndPoint(IPAddress.Parse(hostAddress), port);
        openedAt = Time.realtimeSinceStartup;
    }

    public void BeginTrial(string trialId, string prompt, string participantId = "")
    {
        if (!host) throw new InvalidOperationException("Only the learner starts a trial.");
        State = new Message { hostId = identity, trialId = trialId, prompt = prompt,
            participantId = participantId, phase = "waiting" };
        LocalReady = false;
        Error = null;
        PeerError = null;
        openedAt = Time.realtimeSinceStartup;
        lastSeen = -1f;
        receivedTopicRevision = -1;
        SendStatus();
    }

    public void SetPhase(string phase, string error = null)
    {
        if (!host) return;
        State.phase = phase;
        State.error = error;
        SendStatus();
    }

    public void Tick()
    {
        try
        {
            for (int i = 0; i < 128 && socket.Available > 0; i++)
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = socket.Receive(ref from);
                Message message;
                try { message = JsonUtility.FromJson<Message>(Encoding.UTF8.GetString(bytes)); }
                catch (ArgumentException) { continue; }
                if (message == null || message.protocol != "shodo-control" || message.version != 1) continue;
                if (host)
                {
                    if (message.kind != "hello" || string.IsNullOrEmpty(message.peerId)) continue;
                    // Keep one remote PC per live connection; allow a restarted sender after timeout.
                    if (peerIdentity != null && (message.peerId != peerIdentity || !from.Equals(peer)) && Connected) continue;
                    bool newPeer = message.peerId != peerIdentity;
                    if (newPeer) receivedTopicRevision = -1;
                    peer = from;
                    peerIdentity = message.peerId;
                    lastSeen = Time.realtimeSinceStartup;
                    // The question is display metadata, separate from the answer/prompt.
                    bool topicForThisTrial = message.trialId == State.trialId ||
                        (newPeer && string.IsNullOrEmpty(message.trialId) && State.phase == "waiting");
                    if (topicForThisTrial && message.topicRevision > receivedTopicRevision && message.topicRevision > 0 &&
                        !string.IsNullOrEmpty(message.topicId) && message.topicId.Length <= 64 &&
                        !string.IsNullOrEmpty(message.topicText) && message.topicText.Length <= 256)
                    {
                        State.topicId = message.topicId;
                        State.topicText = message.topicText;
                        State.topicRevision = receivedTopicRevision = message.topicRevision;
                    }
                    State.deviceReady = message.deviceReady;
                    PeerError = message.trialId == State.trialId ? message.error : null;
                    State.teacherReady = message.trialId == State.trialId && message.teacherReady && message.deviceReady;
                    State.hostReady = LocalReady;
                    if (State.phase == "waiting" && State.hostReady && State.teacherReady) State.phase = "drawing";
                    SendStatus();
                }
                else
                {
                    if (!from.Equals(peer) || message.kind != "status" || message.peerId != identity ||
                        string.IsNullOrEmpty(message.hostId) || string.IsNullOrEmpty(message.trialId)) continue;
                    if (retiredHosts.Contains(message.hostId)) continue;
                    if (message.hostId != State.hostId)
                    {
                        if (!string.IsNullOrEmpty(State.hostId)) retiredHosts.Add(State.hostId);
                        lastRevision = -1;
                    }
                    if (message.revision <= lastRevision) continue;
                    lastRevision = message.revision;
                    if (message.trialId != State.trialId) LocalReady = false;
                    State = message;
                    lastSeen = Time.realtimeSinceStartup;
                }
            }
            if (!host && Time.realtimeSinceStartup >= nextHello)
            {
                nextHello = Time.realtimeSinceStartup + 0.5f;
                Send(new Message { kind = "hello", peerId = identity, trialId = State.trialId,
                    teacherReady = LocalReady, deviceReady = DeviceReady, error = LocalError });
            }
        }
        catch (SocketException e) { Error = "接続確認の通信エラー: " + e.Message; }
        catch (ObjectDisposedException) { }
    }

    void SendStatus()
    {
        if (peer == null) return;
        State.kind = "status";
        State.peerId = peerIdentity;
        State.revision = ++revision;
        State.hostReady = LocalReady;
        Send(State);
    }

    void Send(Message message)
    {
        try
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(message));
            socket.Send(bytes, bytes.Length, peer);
        }
        catch (SocketException e) { Error = "接続確認を送信できません: " + e.Message; }
    }

    public void Dispose() { socket.Close(); }
}
