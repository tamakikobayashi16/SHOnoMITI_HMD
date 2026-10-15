using System;
using UnityEngine;

/// <summary>Keeps the control connection and recording alive across Preview/Game/Result.</summary>
[DefaultExecutionOrder(-100)]
public class RemoteTrialRuntime : MonoBehaviour
{
    public static RemoteTrialRuntime Active { get; private set; }
    public static bool RequestLivePreview;
    public RemoteTrialLink Link { get; private set; }
    public RemoteTrialRecorder Recorder { get; private set; }
    public string TrialId { get; private set; }
    public string Error { get; private set; }
    public bool IsLocalTest { get; private set; }
    public bool Finished { get; private set; }
    int port;

    public static RemoteTrialRuntime Begin(bool localTest, int controlPort, string prompt, string participant)
    {
        if (Active == null)
        {
            Active = new GameObject("RemoteTrialSession").AddComponent<RemoteTrialRuntime>();
            DontDestroyOnLoad(Active.gameObject);
        }
        Active.StartTrial(localTest, controlPort, prompt, participant);
        return Active;
    }

    void StartTrial(bool localTest, int controlPort, string prompt, string participant)
    {
        if (Recorder != null)
        {
            if (!Finished) Recorder.Record("trialAborted", detail: "retry");
            Recorder.Dispose();
        }
        if (IsLocalTest != localTest || port != controlPort)
        { Link?.Dispose(); Link = null; }
        IsLocalTest = localTest;
        port = controlPort;
        TrialId = Guid.NewGuid().ToString("N");
        Finished = false;
        Error = null;
        Recorder = new RemoteTrialRecorder(TrialId, "learner", prompt, participant, localTest);
        try
        {
            if (!localTest)
            {
                if (Link == null) Link = new RemoteTrialLink(true, controlPort);
                Link.BeginTrial(TrialId, prompt, participant);
            }
        }
        catch (Exception e) { Error = "接続確認を開始できません: " + e.Message; }
        if (Recorder.Error != null) Error = Recorder.Error;
    }

    void Update()
    {
        Link?.Tick();
        if (Recorder?.Error != null) Error = Recorder.Error;
        if (Link?.Error != null) Error = Link.Error;
    }

    public void Phase(string phase, string reason = null)
    {
        Link?.SetPhase(phase, reason);
        Recorder?.Record(phase, detail: reason);
        if (phase == "completed") Finished = true;
    }

    public void SaveTemplate(RemoteTemplateData data)
    {
        data.trialId = TrialId;
        Recorder.SaveTemplate(JsonUtility.ToJson(data, true));
    }

    void OnDestroy()
    {
        if (Active != this) return;
        if (!Finished) Recorder?.Record("trialAborted", detail: "session closed");
        Recorder?.Dispose();
        Link?.Dispose();
        Active = null;
    }

    void OnGUI()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "RemoteGame") return;
        if (GUI.Button(new Rect(Screen.width - 185, 10, 175, 35), "最初からやり直す"))
        {
            RequestLivePreview = !IsLocalTest;
            GlobalData.RemoteTemplate = null;
            UnityEngine.SceneManagement.SceneManager.LoadScene("RemotePreview");
        }
    }
}
