using System;
using System.Collections;
using UnityEngine;

public static class PirateRunClockRegression
{
    public sealed class Result
    {
        public bool ActiveAdvances, MenuFrozen, UpgradeFrozen, TimeScaleFrozen, ResumedAdvances, StateRestored;
        public double ActiveDelta, ResumedDelta;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && ActiveAdvances && MenuFrozen && UpgradeFrozen &&
            TimeScaleFrozen && ResumedAdvances && StateRestored;
        public override string ToString() => $"success={Success}, active={ActiveAdvances}/{ActiveDelta:F4}, " +
            $"menu={MenuFrozen}, upgrade={UpgradeFrozen}, timeScale={TimeScaleFrozen}, " +
            $"resumed={ResumedAdvances}/{ResumedDelta:F4}, restored={StateRestored}, " +
            $"actualGameFlowUpdate=True, frontEndHandlerProof=False, transitionProof=False, victoryProof=False, error={Error}";
    }

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        PirateHUD hud = flow != null ? flow.HUD : null;
        if (!PirateFrontEnd.IsAutomationRun || flow == null || !flow.IsInitialized || flow.IsTransitioning ||
            flow.IsVictory || hud == null || hud.IsUpgradeOpen || life == null || body == null ||
            life.IsRespawning || life.IsSnared || !player.ControlsEnabled || Time.timeScale != 1f ||
            (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking))
        {
            result.Error = "Requires an isolated automation run with the initialized, live, unpaused flow.";
            completed?.Invoke(result);
            yield break;
        }

        var savedStatistics = new PirateSaveData();
        PirateCampaignSession.Statistics.CaptureInto(savedStatistics);
        RigidbodyConstraints2D constraints = body.constraints;
        Vector2 velocity = body.linearVelocity;
        bool protection = life.IsExitProtected;
        bool automaticAcknowledgement = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        void Check(bool value, string label)
        {
            if (!value) result.Error = string.IsNullOrEmpty(result.Error) ? label : result.Error + "; " + label;
        }
        double Seconds() => PirateCampaignSession.Statistics.ActiveSeconds;
        bool Unchanged(double before) => BitConverter.DoubleToInt64Bits(before) == BitConverter.DoubleToInt64Bits(Seconds());

        try
        {
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            life.SetExitProtected(true);
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;
            double before = Seconds();
            for (int frame = 0; frame < 5; frame++) yield return null;
            result.ActiveDelta = Seconds() - before;
            result.ActiveAdvances = result.ActiveDelta > 0d && Time.timeScale == 1f;
            Check(result.ActiveAdvances, "An active gameplay window did not advance the run clock.");

            flow.SetMenuBlocked(true);
            before = Seconds();
            for (int frame = 0; frame < 5; frame++) yield return null;
            result.MenuFrozen = Unchanged(before) && Time.timeScale == 0f && !player.ControlsEnabled;
            Check(result.MenuFrozen, "The real flow menu-block gate advanced active time.");
            flow.SetMenuBlocked(false);

            hud.ShowUpgrade(PirateUpgrade.DoubleJump);
            before = Seconds();
            for (int frame = 0; frame < 5; frame++) yield return null;
            result.UpgradeFrozen = Unchanged(before) && hud.IsUpgradeOpen && Time.timeScale == 0f;
            Check(result.UpgradeFrozen, "The real equipment modal advanced active time.");
            hud.DismissUpgrade();

            Time.timeScale = 0f;
            before = Seconds();
            for (int frame = 0; frame < 5; frame++) yield return null;
            result.TimeScaleFrozen = Unchanged(before);
            Check(result.TimeScaleFrozen, "A time-scale pause advanced the run clock without a menu flag.");
            Time.timeScale = 1f;
            before = Seconds();
            for (int frame = 0; frame < 5; frame++) yield return null;
            result.ResumedDelta = Seconds() - before;
            result.ResumedAdvances = result.ResumedDelta > 0d && !hud.IsUpgradeOpen && player.ControlsEnabled;
            Check(result.ResumedAdvances, "Dismissing pause/modal left the run clock stuck.");
        }
        finally
        {
            hud.ClearUpgrade();
            flow.SetMenuBlocked(false);
            Time.timeScale = 1f;
            body.constraints = constraints;
            body.linearVelocity = velocity;
            life.SetExitProtected(protection);
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = automaticAcknowledgement;
            PirateCampaignSession.Statistics.Restore(savedStatistics);
        }
        var restored = new PirateSaveData();
        PirateCampaignSession.Statistics.CaptureInto(restored);
        result.StateRestored = JsonUtility.ToJson(savedStatistics) == JsonUtility.ToJson(restored) &&
            body.constraints == constraints && body.linearVelocity == velocity && life.IsExitProtected == protection &&
            !hud.IsUpgradeOpen && player.ControlsEnabled && Time.timeScale == 1f;
        Check(result.StateRestored, "The fixture did not restore statistics, motion constraints, protection and pause state.");
        Debug.Log("PIRATE_RUN_CLOCK_REGRESSION " + result);
        completed?.Invoke(result);
    }
}
