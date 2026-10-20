using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CooperativeScoreSmoke
{
    static double deadline;
    public static void Run()
    {
        var sample = RemoteTestHandwriting.Create();
        int count = RemoteCooperativeResult.CountStrokes(sample);
        if (count != 7 || RemoteCooperativeResult.Points(count, 100) != 700 ||
            RemoteCooperativeResult.Points(count, 80) != 560 ||
            RemoteCooperativeResult.Points(count, 60) != 420)
            throw new Exception("Award calculation failed");
        sample.strokes.Add(sample.strokes[0]);
        sample.strokes.Add(new RemoteTemplateData.Stroke { character = 2, stroke = 99 });
        if (RemoteCooperativeResult.CountStrokes(sample) != 7 ||
            RemoteCooperativeResult.CountStrokes(null) != 0)
            throw new Exception("Empty/duplicate strokes counted");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("ResultTransferRoot");
        root.SetActive(false);
        var score = root.AddComponent<RemoteCooperativeResult>();
        score.strokeCount = count;
        score.multiplier = 80;
        score.award = "銀賞";
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        deadline = EditorApplication.timeSinceStartup + 30;
        EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (EditorApplication.timeSinceStartup > deadline) { EditorApplication.Exit(1); return; }
        if (!EditorApplication.isPlaying) return;
        foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == "ResultTransferRoot") root.SetActive(true);
        var text = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();
        if (text == null || string.IsNullOrEmpty(text.text)) return;
        if (!text.text.Contains("560") || !text.text.Contains("7画"))
            throw new Exception("Score UI not populated");
        Debug.Log("COOPERATIVE_SCORE_PASS: 7 template strokes; gold 700, silver 560, bronze 420; duplicate/empty excluded; inactive transfer activation renders score");
        EditorApplication.Exit(0);
    }
}
