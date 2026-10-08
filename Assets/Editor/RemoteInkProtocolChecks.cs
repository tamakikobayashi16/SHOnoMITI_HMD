using System;
using System.Reflection;
using UnityEngine;
using UnityEditor;

public static class RemoteInkProtocolChecks
{
    [MenuItem("Tools/Remote Ink/Run protocol checks")]
    public static void Run()
    {
        var root = new GameObject("ProtocolCheck");
        root.SetActive(false); // No network socket is needed for packet replay.
        var receiver = root.AddComponent<RemoteInkReceiver>();
        var process = typeof(RemoteInkReceiver).GetMethod("ProcessPacket", BindingFlags.NonPublic | BindingFlags.Instance);
        int sessionNumber = 0;
        Action<string> feed = json => process.Invoke(receiver, new object[] { json.Replace("test", "test" + sessionNumber) });
        try {
            feed(Packet(0, "point", 0));
            feed(Packet(1, "strokeEnd", 0));
            feed(Packet(2, "nextCharacter", 1));
            feed(Packet(3, "point", 1));
            Require(receiver.Characters.Length == 2, "Previous character must survive nextCharacter");
            feed(Packet(4, "clearCharacter", 1));
            Require(receiver.Characters.Length == 1, "Clear must affect only the selected character");
            feed(Packet(5, "point", 1));
            feed(Packet(6, "experimentEnd", 1));
            Require(receiver.HasExperimentEnded && !receiver.HasPacketLoss, "Complete contiguous example");
            feed(Packet(7, "clearCharacter", 0));
            Require(receiver.Characters.Length == 2, "Completed example must be immutable");
            receiver.ResetReception();
            sessionNumber++;
            feed(Packet(0, "point", 0));
            feed(Packet(2, "experimentEnd", 0));
            Require(receiver.HasPacketLoss, "Missing sequence must prevent tracing");
            receiver.ResetReception();
            sessionNumber++;
            feed("invalid JSON");
            Require(receiver.Characters.Length == 0, "Invalid JSON must not create strokes");
            Debug.Log("Remote Ink protocol checks passed (6 checks).");
        } finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static string Packet(int sequence, string type, int character) {
        return "{\"version\":1,\"session\":\"test\",\"method\":\"PenTablet\",\"eventType\":\"" + type +
            "\",\"sequence\":" + sequence + ",\"character\":" + character + ",\"stroke\":0,\"x\":1.25,\"y\":-0.5,\"z\":9.5,\"pressure\":0.7}";
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
