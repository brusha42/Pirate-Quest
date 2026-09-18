using System;
using System.Collections;
using UnityEngine;

public sealed class PirateHudLayoutRegression : MonoBehaviour
{
    private PirateHUD hud;
    private bool finished;
    private bool success;
    private string details;

    public static IEnumerator Run(PirateHUD currentHud, Action<bool, string> completed)
    {
        bool optedIn = PirateFrontEnd.IsAutomationRun || PirateFrontEnd.IsMenuRegression || PirateSaveRestartProbe.IsRequested;
        if (!optedIn || currentHud == null)
        {
            completed?.Invoke(false, "HUD layout regression requires an opted-in run and the actual initialized HUD.");
            yield break;
        }
        GameObject host = new GameObject("HUD layout measurement regression");
        PirateHudLayoutRegression probe = host.AddComponent<PirateHudLayoutRegression>();
        probe.hud = currentHud;
        try
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (!probe.finished && Time.realtimeSinceStartup < deadline) yield return null;
            string result = probe.finished ? probe.details : "No IMGUI repaint within 5 seconds; run this fixture in graphics mode.";
            Debug.Log("PIRATE_HUD_LAYOUT_REGRESSION_RESULT success=" + (probe.finished && probe.success) + " " + result);
            completed?.Invoke(probe.finished && probe.success, result);
        }
        finally { Destroy(host); }
    }

    private void OnGUI()
    {
        if (finished || hud == null || Event.current.type != EventType.Repaint) return;
        success = true;
        int count = 0;
        string failures = string.Empty;
        foreach (PirateUpgrade upgrade in Enum.GetValues(typeof(PirateUpgrade)))
        {
            bool valid = hud.VerifyUpgradeLayout(upgrade, out string measured);
            success &= valid;
            count++;
            Debug.Log("PIRATE_HUD_LAYOUT_MEASUREMENT " + measured);
            if (!valid) failures += upgrade + "; ";
        }
        success &= count == 7;
        bool controlsValid = hud.VerifyControlLayout(out string controls);
        success &= controlsValid;
        Debug.Log("PIRATE_HUD_CONTROL_MEASUREMENT " + controls);
        details = $"descriptions={count}, controls={controlsValid}, failures={failures}, actualGuiFont=True, nativeRenderProof=False";
        finished = true;
    }
}
