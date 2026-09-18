using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class OneWayContactProbeEditor
{
    private const string ActiveKey = "PirateQuest.ContactProbe.Active";
    private const string DeadlineKey = "PirateQuest.ContactProbe.Deadline";
    static OneWayContactProbeEditor()
    {
        if (SessionState.GetBool(ActiveKey, false)) Attach();
    }
    public static void Run()
    {
        string scene = EditorBuildSettings.scenes.Length > 0 ? EditorBuildSettings.scenes[0].path : "Assets/Scenes/SampleScene.unity";
        EditorSceneManager.OpenScene(scene);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + 90f);
        Attach();
        EditorApplication.EnterPlaymode();
    }
    private static void Attach()
    {
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= Watch;
        EditorApplication.update += Watch;
    }
    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (!condition.StartsWith("PIRATE_ONEWAY_CONTACT_RESULT")) return;
        SessionState.SetBool(ActiveKey, false);
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }
    private static void Watch()
    {
        if (SessionState.GetBool(ActiveKey, false) &&
            EditorApplication.timeSinceStartup > SessionState.GetFloat(DeadlineKey, 0f))
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.Exit(9);
        }
    }
}
