using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Append-only records shared by the learner and teacher, joined by trialId.</summary>
public sealed class RemoteTrialRecorder : IDisposable
{
    [Serializable]
    public class Entry
    {
        public string trialId, role, utc, eventType, detail;
        public float elapsed, x, y, z, pressure, width;
        public int character, stroke, sequence;
    }
    [Serializable]
    class Metadata
    {
        public int schemaVersion = 1;
        public string trialId, role, participantId, prompt, startedUtc;
        public bool localTest;
    }
    readonly string trialId, role;
    readonly float started = Time.realtimeSinceStartup;
    StreamWriter writer;
    float nextFlush;
    public string DirectoryPath { get; private set; }
    public string Error { get; private set; }

    public RemoteTrialRecorder(string id, string actor, string prompt, string participant, bool test)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid trial ID.");
        trialId = id;
        role = actor;
        try
        {
            DirectoryPath = Path.Combine(Application.persistentDataPath, "RemoteTrials", id);
            Directory.CreateDirectory(DirectoryPath);
            // Never overwrite a previous recording, including when a scene is restarted.
            writer = new StreamWriter(new FileStream(Path.Combine(DirectoryPath, actor + "_events.jsonl"),
                FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            var meta = new Metadata { trialId = id, role = actor, prompt = prompt, participantId = participant,
                localTest = test, startedUtc = DateTime.UtcNow.ToString("O") };
            File.WriteAllText(Path.Combine(DirectoryPath, actor + "_metadata.json"), JsonUtility.ToJson(meta, true));
            Record("trialStarted", detail: "Coordinates: template=sender XY; learner=Unity world XYZ. Pressure: teacher=pen; learner=adjusted kg.");
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
        { Fail(e); }
    }

    public void Record(string eventType, int character = -1, int stroke = -1, Vector3 point = default(Vector3),
        float pressure = 0f, int sequence = -1, string detail = null, float width = 0f)
    {
        if (writer == null || Error != null) return;
        try
        {
            writer.WriteLine(JsonUtility.ToJson(new Entry { trialId = trialId, role = role,
                utc = DateTime.UtcNow.ToString("O"), elapsed = Time.realtimeSinceStartup - started,
                eventType = eventType, character = character, stroke = stroke, sequence = sequence,
                x = point.x, y = point.y, z = point.z, pressure = pressure, width = width, detail = detail }));
            if (eventType != "point" && eventType != "tracePoint" || Time.realtimeSinceStartup >= nextFlush)
            { writer.Flush(); nextFlush = Time.realtimeSinceStartup + 1f; }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Fail(e); }
    }

    public void SaveTemplate(string json)
    {
        if (Error != null) return;
        try { File.WriteAllText(Path.Combine(DirectoryPath, "template.json"), json, new UTF8Encoding(false)); }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { Fail(e); }
    }

    void Fail(Exception exception)
    {
        Error = "記録を保存できません: " + exception.Message;
        Debug.LogError(Error);
    }

    public void Dispose()
    {
        try { writer?.Dispose(); }
        catch (IOException e) { Fail(e); }
        writer = null;
    }
}
