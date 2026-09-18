using System;
using UnityEngine;

[DefaultExecutionOrder(15000)]
public sealed class PirateTestModalAcknowledger : MonoBehaviour
{
    public static bool SuspendAutomaticAcknowledgement { get; set; }
    public static int AcknowledgedCards { get; private set; }
    public static int InvariantFailures { get; private set; }
    public static bool IsInstalled => instance != null;

    private static PirateTestModalAcknowledger instance;
    private PirateHUD observedHud;
    private PirateUpgrade? observedUpgrade;
    private int observedFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDomain()
    {
        instance = null;
        SuspendAutomaticAcknowledgement = false;
        AcknowledgedCards = 0;
        InvariantFailures = 0;
    }

    public static void Install()
    {
        if (!PirateFrontEnd.IsAutomationRun && !PirateFrontEnd.IsMenuRegression && !PirateSaveRestartProbe.IsRequested)
            throw new InvalidOperationException("Modal acknowledgement driver requires an explicit Pirate Quest test process.");
        if (instance != null) return;
        var driver = new GameObject("Explicit test driver - acknowledge equipment cards");
        DontDestroyOnLoad(driver);
        instance = driver.AddComponent<PirateTestModalAcknowledger>();
        Debug.Log("PIRATE_TEST_MODAL_DRIVER_INSTALLED realModal=True commonDismissHandler=True visualRenderProof=False");
    }

    private void LateUpdate()
    {
        PirateHUD hud = PirateHUD.Instance;
        if (SuspendAutomaticAcknowledgement || hud == null || !hud.IsUpgradeOpen)
        {
            observedHud = null;
            observedUpgrade = null;
            return;
        }
        if (observedHud != hud || observedUpgrade != hud.DisplayedUpgrade)
        {
            observedHud = hud;
            observedUpgrade = hud.DisplayedUpgrade;
            observedFrame = Time.frameCount;
            return;
        }
        if (Time.frameCount <= observedFrame) return;

        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        bool timeStopped = Mathf.Abs(Time.timeScale) < .0001f;
        bool controlsBlocked = player != null && !player.ControlsEnabled;
        bool valid = timeStopped && controlsBlocked;
        string detail = $"upgrade={hud.DisplayedUpgrade}, heldFrames={Time.frameCount - observedFrame}, " +
            $"timeStopped={timeStopped}, controlsBlocked={controlsBlocked}, commonHandler=True, visualRenderProof=False";
        if (!valid)
        {
            InvariantFailures++;
            Debug.LogError("PIRATE_TEST_MODAL_ACK_FAILED " + detail);
        }
        else Debug.Log("PIRATE_TEST_MODAL_ACK " + detail);
        hud.DismissUpgrade();
        AcknowledgedCards++;
        observedHud = null;
        observedUpgrade = null;
    }

    private void OnDestroy() { if (instance == this) instance = null; }
}
