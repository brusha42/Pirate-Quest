using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class PirateHudRegression
{
    public sealed class Result
    {
        public bool FixtureAttached, OpensAndBlocks, PositionPreserved, VelocityPreserved, HookPreserved;
        public bool QueueOrder, QueueKeptPause, LastDismissResumes, InputConsumed;
        public bool NestedPausePreserved, ClearResumes, RestartClears, AllDescriptions, StateRestored;
        public bool EquipmentReopens, EquipmentUnownedRejected, EquipmentReadOnly;
        public bool RespawnReviewRejected, RespawnCompletes;
        public bool CurrentShortcutLabels, PlainAcknowledgement, DoubleJumpLabels;
        public int Checks, DescriptionCount;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && FixtureAttached && OpensAndBlocks &&
            PositionPreserved && VelocityPreserved && HookPreserved && QueueOrder && QueueKeptPause &&
            LastDismissResumes && InputConsumed && NestedPausePreserved && ClearResumes && RestartClears &&
            AllDescriptions && StateRestored && EquipmentReopens && EquipmentUnownedRejected && EquipmentReadOnly &&
            RespawnReviewRejected && RespawnCompletes && CurrentShortcutLabels && PlainAcknowledgement && DoubleJumpLabels;
        public override string ToString() => $"success={Success}, checks={Checks}, " +
            $"open={OpensAndBlocks}, rope={FixtureAttached}/{HookPreserved}, position={PositionPreserved}, " +
            $"velocity={VelocityPreserved}, queue={QueueOrder}/{QueueKeptPause}/{LastDismissResumes}, " +
            $"inputConsumed={InputConsumed}, nestedPause={NestedPausePreserved}, clear={ClearResumes}, " +
            $"restart={RestartClears}, descriptions={AllDescriptions}/{DescriptionCount}, restored={StateRestored}, " +
            $"equipmentReview={EquipmentReopens}, unownedRejected={EquipmentUnownedRejected}, reviewReadOnly={EquipmentReadOnly}, " +
            $"respawnReviewRejected={RespawnReviewRejected}, respawnCompletes={RespawnCompletes}, " +
            $"currentShortcutLabels={CurrentShortcutLabels}, plainAcknowledgement={PlainAcknowledgement}, doubleJumpLabels={DoubleJumpLabels}, " +
            $"fixtureRelocation=True, visualRenderProof=False, fullTraversalProof=False, error={Error}";
    }

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        PirateHUD hud = flow != null ? flow.GetComponent<PirateHUD>() : null;
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        bool optedIn = PirateFrontEnd.IsAutomationRun || PirateFrontEnd.IsMenuRegression || PirateSaveRestartProbe.IsRequested;
        if (!optedIn || flow == null || !flow.IsInitialized || flow.IsTransitioning || flow.IsVictory ||
            hud == null || body == null || abilities == null || grapple == null || life == null ||
            hud.IsUpgradeOpen || !player.ControlsEnabled || Mathf.Abs(Time.timeScale - 1f) > .0001f ||
            life.IsRespawning || life.IsSnared || life.IsExitProtected || grapple.IsAttached || !body.simulated)
        {
            result.Error = "HUD fixture requires an opted-in, initialized, unpaused live scene with no pending card, snare or rope.";
            completed?.Invoke(result);
            yield break;
        }

        Vector2 originalPosition = body.position;
        Vector2 originalVelocity = body.linearVelocity;
        PirateUpgrade[] originalUpgrades = abilities.CaptureProgression();
        int originalDeaths = life.DeathCount;
        int originalSessionDeaths = PirateCampaignSession.Deaths;
        bool previousSuspension = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        GameObject anchorObject = null;
        int reviewNotifications = 0;
        void ReviewNotification(PirateUpgrade upgrade) => reviewNotifications++;
        PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;

        void Check(bool condition, string description)
        {
            result.Checks++;
            if (!condition) result.Error = string.IsNullOrEmpty(result.Error) ? description : result.Error + "; " + description;
        }

        bool Frozen() => Mathf.Abs(Time.timeScale) < .0001f && !player.ControlsEnabled;
        bool Running() => Mathf.Abs(Time.timeScale - 1f) < .0001f && player.ControlsEnabled;

        try
        {
            Debug.Log("PIRATE_HUD_FIXTURE_SCOPE realModal=True realJoint=True realDeath=True fixtureRelocation=True visualRenderProof=False fullTraversalProof=False");
            player.ResetMotion();
            player.SetAutomationInputOverride(Vector2.zero);
            grapple.SetAutomationInputOverride(true);
            abilities.ResetProgression();
            result.EquipmentUnownedRejected = true;
            foreach (PirateUpgrade upgrade in Enum.GetValues(typeof(PirateUpgrade)))
                result.EquipmentUnownedRejected &= !hud.OpenEquipmentDescription(upgrade);
            result.EquipmentUnownedRejected &= !hud.OpenEquipmentDescription((PirateUpgrade)999) &&
                !hud.IsUpgradeOpen && Running() && abilities.CaptureProgression().Length == 0;
            Check(result.EquipmentUnownedRejected, "Equipment review opened or granted unowned gear.");

            abilities.Apply(PirateUpgrade.Hook1, false);
            abilities.Apply(PirateUpgrade.Saber1, false);
            PirateUpgrade[] reviewUpgrades = abilities.CaptureProgression();
            abilities.Upgraded += ReviewNotification;
            result.EquipmentReopens = true;
            foreach (PirateUpgrade upgrade in new[] { PirateUpgrade.Hook1, PirateUpgrade.Saber1 })
            {
                bool opened = hud.OpenEquipmentDescription(upgrade);
                result.EquipmentReopens &= opened && hud.DisplayedUpgrade == upgrade &&
                    hud.PendingUpgradeCount == 1 && Frozen() && PirateHUD.ConsumedInputThisFrame;
                yield return null;
                result.EquipmentReopens &= hud.IsUpgradeOpen && Frozen();
                hud.DismissUpgrade();
                result.EquipmentReopens &= !hud.IsUpgradeOpen && hud.PendingUpgradeCount == 0 &&
                    Running() && PirateHUD.ConsumedInputThisFrame;
            }
            Check(result.EquipmentReopens, "Owned Hook I or Saber I review did not pause, persist and resume through the common handler.");
            result.EquipmentReadOnly = reviewNotifications == 0 &&
                new HashSet<PirateUpgrade>(abilities.CaptureProgression()).SetEquals(reviewUpgrades);
            Check(result.EquipmentReadOnly, "Reviewing owned equipment changed progression or emitted an acquisition notification.");
            abilities.Upgraded -= ReviewNotification;

            Vector2 fixturePosition = new Vector2(7500f, 7500f);
            body.position = fixturePosition;
            player.transform.position = fixturePosition;
            anchorObject = new GameObject("HUD lifecycle fixture - local chain pivot");
            anchorObject.transform.position = fixturePosition + Vector2.up * 2.2f;
            HookAnchor anchor = anchorObject.AddComponent<HookAnchor>();
            anchor.Configure();
            Physics2D.SyncTransforms();
            result.FixtureAttached = grapple.TryAttachToAnchor(anchor);
            Check(result.FixtureAttached, "Could not attach the real rope fixture.");
            body.linearVelocity = new Vector2(2.25f, -.65f);
            Vector2 pausedVelocity = body.linearVelocity;
            float ropeLength = grapple.RopeLength;

            hud.ShowUpgrade(PirateUpgrade.Hook2);
            result.OpensAndBlocks = hud.IsUpgradeOpen && hud.DisplayedUpgrade == PirateUpgrade.Hook2 &&
                hud.PendingUpgradeCount == 1 && Frozen() && !hud.CanAcceptGameplayInput;
            Check(result.OpensAndBlocks, "A new equipment card did not synchronously block gameplay and time.");
            yield return null;
            yield return null;
            result.PositionPreserved = Vector2.Distance(body.position, fixturePosition) < .0001f;
            result.VelocityPreserved = Vector2.Distance(body.linearVelocity, pausedVelocity) < .0001f;
            result.HookPreserved = grapple.IsAttached && grapple.CurrentAnchor == anchor &&
                Mathf.Abs(grapple.RopeLength - ropeLength) < .0001f;
            Check(result.PositionPreserved, "Player position changed during the equipment card.");
            Check(result.VelocityPreserved, "Opening the card discarded the player's in-flight velocity.");
            Check(result.HookPreserved, "The equipment card detached or changed the live rope after Update.");

            hud.ShowUpgrade(PirateUpgrade.SpringLeg);
            hud.ShowUpgrade(PirateUpgrade.SpringLeg);
            result.QueueOrder = hud.PendingUpgradeCount == 2 && hud.DisplayedUpgrade == PirateUpgrade.Hook2;
            Check(result.QueueOrder, "Card queue lost acquisition order or duplicated an already queued upgrade.");
            hud.DismissUpgrade();
            result.QueueKeptPause = hud.IsUpgradeOpen && hud.PendingUpgradeCount == 1 &&
                hud.DisplayedUpgrade == PirateUpgrade.SpringLeg && Frozen();
            Check(result.QueueKeptPause, "Dismissing the first queued card briefly resumed gameplay.");
            yield return null;
            Check(Frozen() && hud.DisplayedUpgrade == PirateUpgrade.SpringLeg, "Queued card did not stay blocked for a full frame.");
            hud.DismissUpgrade();
            result.InputConsumed = PirateHUD.ConsumedInputThisFrame && !hud.CanAcceptGameplayInput;
            Check(result.InputConsumed, "The acknowledgement frame was not marked as consumed input.");
            yield return null;
            result.LastDismissResumes = !hud.IsUpgradeOpen && hud.PendingUpgradeCount == 0 &&
                Running() && hud.CanAcceptGameplayInput;
            Check(result.LastDismissResumes, "Dismissing the final card did not release only the modal pause.");

            flow.SetMenuBlocked(true);
            hud.ShowUpgrade(PirateUpgrade.Saber1);
            hud.DismissUpgrade();
            yield return null;
            result.NestedPausePreserved = !hud.IsUpgradeOpen && Frozen();
            Check(result.NestedPausePreserved, "Acknowledging equipment removed an independent menu pause.");
            flow.SetMenuBlocked(false);

            hud.ShowUpgrade(PirateUpgrade.Saber2);
            hud.ShowUpgrade(PirateUpgrade.Parrot);
            Check(hud.PendingUpgradeCount == 2 && Frozen(), "Clear fixture failed to establish two pending cards.");
            hud.ClearUpgrade();
            yield return null;
            result.ClearResumes = !hud.IsUpgradeOpen && hud.PendingUpgradeCount == 0 && Running();
            Check(result.ClearResumes, "ClearUpgrade left a pending card or stale modal pause.");

            hud.ShowUpgrade(PirateUpgrade.DoubleJump);
            flow.RestartFromCheckpoint();
            yield return null;
            result.RestartClears = !hud.IsUpgradeOpen && hud.PendingUpgradeCount == 0 && Running();
            Check(result.RestartClears, "Checkpoint restart left the old equipment card or a stuck pause.");

            life.Die();
            result.RespawnReviewRejected = life.IsRespawning && abilities.Has(PirateUpgrade.Hook1) &&
                !hud.OpenEquipmentDescription(PirateUpgrade.Hook1) && !hud.IsUpgradeOpen &&
                hud.PendingUpgradeCount == 0 && !player.IsModalInputBlocked &&
                Mathf.Abs(Time.timeScale - 1f) < .0001f && !body.simulated;
            Check(result.RespawnReviewRejected, "Owned equipment review opened a modal during real respawn.");
            float respawnDeadline = Time.realtimeSinceStartup + 3f;
            while (life.IsRespawning && Time.realtimeSinceStartup < respawnDeadline) yield return null;
            result.RespawnCompletes = !life.IsRespawning && body.simulated && !hud.IsUpgradeOpen &&
                !player.IsModalInputBlocked && Running();
            Check(result.RespawnCompletes, "Rejected review interrupted the normal unscaled respawn or left a modal pause.");

            result.AllDescriptions = true;
            foreach (PirateUpgrade upgrade in Enum.GetValues(typeof(PirateUpgrade)))
            {
                bool described = PirateHUD.HasCompleteUpgradeDescription(upgrade);
                result.AllDescriptions &= described;
                if (described) result.DescriptionCount++;
                Check(described, "Missing title, description or key instructions for " + upgrade);
            }
            Check(result.DescriptionCount == 7, "Expected descriptions for all seven current upgrades.");
            Check(!PirateHUD.HasCompleteUpgradeDescription((PirateUpgrade)999), "Invalid upgrade was presented as a valid equipment description.");
            result.CurrentShortcutLabels = PirateHUD.SaberShortcut == "LMB" && PirateHUD.GrappleShortcut == "RMB" &&
                PirateHUD.GetUpgradeKeyLabel(PirateUpgrade.Saber1, 0) == "LMB" &&
                PirateHUD.GetUpgradeKeyLabel(PirateUpgrade.Hook1, 0) == "RMB" &&
                PirateHUD.GetUpgradeKeyLabel(PirateUpgrade.Hook2, 0) == "RMB";
            Check(result.CurrentShortcutLabels, "HUD or upgrade descriptions still advertise retired saber/grapple shortcuts.");
            result.PlainAcknowledgement = PirateHUD.UpgradeAcknowledgement == "Got it";
            Check(result.PlainAcknowledgement, "The confirmation button includes an extra shortcut instead of its single caption.");
            result.DoubleJumpLabels = PirateHUD.GetUpgradeKeyLabel(PirateUpgrade.DoubleJump, 0) == "SPACE" &&
                PirateHUD.GetUpgradeKeyLabel(PirateUpgrade.DoubleJump, 1) == "SPACE" &&
                PirateHUD.GetUpgradeActionLabel(PirateUpgrade.DoubleJump, 0).Contains("ground") &&
                PirateHUD.GetUpgradeActionLabel(PirateUpgrade.DoubleJump, 1).Contains("air");
            Check(result.DoubleJumpLabels, "DoubleJump no longer has distinct readable ground/air instructions with matching Space keycaps.");
        }
        finally
        {
            abilities.Upgraded -= ReviewNotification;
            hud.ClearUpgrade();
            flow.SetMenuBlocked(false);
            if (life.IsRespawning) life.RespawnImmediately();
            life.SetDeathCount(originalDeaths);
            PirateCampaignSession.Deaths = originalSessionDeaths;
            grapple.Detach();
            if (anchorObject != null) UnityEngine.Object.Destroy(anchorObject);
            abilities.RestoreProgression(originalUpgrades);
            player.ResetMotion();
            player.ClearAutomationInputOverride();
            grapple.ClearAutomationInputOverride();
            body.position = originalPosition;
            player.transform.position = originalPosition;
            body.linearVelocity = originalVelocity;
            Physics2D.SyncTransforms();
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = previousSuspension;
        }
        result.StateRestored = Vector2.Distance(body.position, originalPosition) < .0001f &&
            Vector2.Distance(body.linearVelocity, originalVelocity) < .0001f &&
            new HashSet<PirateUpgrade>(abilities.CaptureProgression()).SetEquals(originalUpgrades) &&
            life.DeathCount == originalDeaths && PirateCampaignSession.Deaths == originalSessionDeaths &&
            !life.IsRespawning && body.simulated && Running();
        Check(result.StateRestored, "HUD fixture did not restore the original player position, velocity, progression, death counters or pause state.");
        Debug.Log("PIRATE_HUD_REGRESSION_RESULT " + result);
        completed?.Invoke(result);
    }
}
