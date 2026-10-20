using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
// Copy PlayerInkProbe.cs into the isolated project's Assets and this file into Assets/Editor.
public static class BuildInkProbe
{
    public static void Run()
    {
        if (!Application.dataPath.Replace('\\', '/').EndsWith("/TempRemoteVerification/Assets"))
            throw new Exception("Run only in isolated verification project");
        var report = BuildPipeline.BuildPlayer(
            new[] { "Assets/Scenes/RemoteGame.unity", "Assets/Scenes/GoldResult.unity" },
            "PlayerInkTest/InkProbe.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
        EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
    }
}
