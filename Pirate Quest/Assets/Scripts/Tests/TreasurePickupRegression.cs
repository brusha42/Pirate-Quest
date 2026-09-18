using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class TreasurePickupRegression
{
    public sealed class Result
    {
        public int Checks, Enters, Stays;
        public bool FirstContact, NoFarming, MenuBlocks, MenuResumes, ModalBlocks, ModalResumes;
        public bool RespawnBlocks, DeathKeepsReceipts, RestoredContactCollects, StateRestored;
        public bool CoinSilent, GemSilent, RelicNotification, RelicNoDuplicate;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && FirstContact && NoFarming && MenuBlocks && MenuResumes &&
            ModalBlocks && ModalResumes && RespawnBlocks && DeathKeepsReceipts && RestoredContactCollects && StateRestored &&
            CoinSilent && GemSilent && RelicNotification && RelicNoDuplicate;
        public override string ToString() => $"success={Success}, checks={Checks}, first={FirstContact}, noFarm={NoFarming}, " +
            $"menu={MenuBlocks}/{MenuResumes}, modal={ModalBlocks}/{ModalResumes}, respawn={RespawnBlocks}/{RestoredContactCollects}, " +
            $"deathKeeps={DeathKeepsReceipts}, notifications={CoinSilent}/{GemSilent}/{RelicNotification}/{RelicNoDuplicate}, " +
            $"callbacks={Enters}/{Stays}, restored={StateRestored}, error={Error}";
    }

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        PirateHUD hud = flow != null ? flow.GetComponent<PirateHUD>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        if (!PirateFrontEnd.IsAutomationRun || flow == null || !flow.IsInitialized || flow.IsVictory || flow.IsTransitioning ||
            hud == null || life == null || life.IsRespawning || !player.ControlsEnabled || Time.timeScale != 1f || hud.IsUpgradeOpen)
        {
            result.Error = "Requires an explicit automation process, real live flow and unpaused player.";
            completed?.Invoke(result); yield break;
        }
        PirateTreasureLedger ledger = PirateCampaignSession.TreasureLedger;
        var available = PirateTreasureLayout.Create(flow.Generator.Campaign)
            .Where(item => !ledger.Contains(item.Key)).ToArray();
        var descriptions = available.Where(item => item.Kind == PirateTreasureKind.Doubloon).Take(4).ToArray();
        var gemDescription = available.FirstOrDefault(item => item.Kind == PirateTreasureKind.Gem);
        var relicDescription = available.FirstOrDefault(item => item.Kind == PirateTreasureKind.Relic);
        PirateTreasureArtLibrary art = PirateTreasureArtLibrary.Load();
        FieldInfo statusField = typeof(PirateGameFlow).GetField("statusMessage", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo statusUntilField = typeof(PirateGameFlow).GetField("statusMessageUntil", BindingFlags.Instance | BindingFlags.NonPublic);
        if (descriptions.Length != 4 || gemDescription == null || relicDescription == null || art == null || !art.IsComplete ||
            statusField == null || statusUntilField == null)
        {
            result.Error = "Requires four coins, one gem, one relic, imported treasure sprites and observable flow status fields.";
            completed?.Invoke(result); yield break;
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        Vector2 position = body.position, velocity = body.linearVelocity;
        Vector3 checkpoint = life.CheckpointPosition;
        RigidbodyConstraints2D constraints = body.constraints;
        PirateUpgrade[] upgrades = abilities.CaptureProgression();
        string[] receipts = ledger.Capture();
        int score = ledger.Score, deaths = life.DeathCount, sessionDeaths = PirateCampaignSession.Deaths;
        bool protection = life.IsExitProtected;
        bool acknowledgement = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        SimulationMode2D simulation = Physics2D.simulationMode;
        string originalStatus = (string)statusField.GetValue(flow);
        float originalStatusUntil = (float)statusUntilField.GetValue(flow);
        var origin = new Vector2(12000f,12000f);
        var objects = new List<GameObject>();
        var probes = new List<ModalHazardContactProbe>();
        void Check(bool condition, string detail)
        {
            result.Checks++;
            if (!condition) result.Error = string.IsNullOrEmpty(result.Error) ? detail : result.Error+"; "+detail;
        }
        void Move(Vector2 point)
        {
            body.position = point; player.transform.position = point; body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }
        void Step()
        {
            Physics2D.SyncTransforms(); body.WakeUp();
            Check(Physics2D.Simulate(Time.fixedDeltaTime), "Fixture physics step was refused.");
        }
        TreasurePickup Pickup(PirateTreasureLayout.Item actual, string label)
        {
            var relocated = new PirateTreasureLayout.Item { Key = actual.Key, Kind = actual.Kind,
                Position = origin, RouteNode = actual.RouteNode, Support = actual.Support, Optional = actual.Optional };
            var obj = new GameObject("Treasure trigger fixture - real identity " + label); objects.Add(obj);
            TreasurePickup pickup = obj.AddComponent<TreasurePickup>(); pickup.Initialize(flow,relocated,art.Get(actual.Kind));
            probes.Add(obj.AddComponent<ModalHazardContactProbe>());
            return pickup;
        }
        TreasurePickup Coin(int index) => Pickup(descriptions[index], "coin " + index);
        try
        {
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;
            Physics2D.simulationMode = SimulationMode2D.Script;
            player.ResetMotion(); player.SetAutomationInputOverride(Vector2.zero);
            body.constraints = RigidbodyConstraints2D.FreezeAll; life.SetExitProtected(false);
            life.SetCheckpoint(origin);
            Move(origin + Vector2.left*3f);
            TreasurePickup first = Coin(0); Step(); Move(origin); Step();
            result.FirstContact = first.IsCollected && !first.gameObject.activeSelf && ledger.Score == score+10 && ledger.Contains(first.Key);
            Check(result.FirstContact, "First actual coin trigger did not collect exactly ten points.");
            result.CoinSilent = (string)statusField.GetValue(flow) == originalStatus &&
                (float)statusUntilField.GetValue(flow) == originalStatusUntil;
            Check(result.CoinSilent, "A coin pickup replaced or extended the gameplay hint with a reward popup.");
            first.gameObject.SetActive(true); Step(); Step();
            Move(origin+Vector2.left*3f); Step(); Move(origin); Step();
            result.NoFarming = ledger.Score == score+10 && ledger.Count == receipts.Length+1;
            Check(result.NoFarming, "Coin Stay/re-enter duplicated its receipt/score.");
            first.gameObject.SetActive(false);

            Move(origin+Vector2.left*3f);
            TreasurePickup menu = Coin(1); Step();
            flow.SetMenuBlocked(true); Move(origin); Step(); Step();
            result.MenuBlocks = Time.timeScale == 0f && !menu.IsCollected && ledger.Score == score+10;
            Check(result.MenuBlocks, "Menu-blocked real trigger collected treasure.");
            flow.SetMenuBlocked(false); Step();
            result.MenuResumes = menu.IsCollected && ledger.Score == score+20 && probes[1].Stays > 0;
            Check(result.MenuResumes, "Menu resume lost the overlapping treasure Stay.");

            Move(origin+Vector2.left*3f);
            TreasurePickup modal = Coin(2); Step();
            hud.ShowUpgrade(PirateUpgrade.Hook2); Move(origin); Step(); Step();
            result.ModalBlocks = life.IsModalProtected && !modal.IsCollected && ledger.Score == score+20;
            Check(result.ModalBlocks, "Equipment-card trigger collected treasure while frozen.");
            hud.DismissUpgrade(); Step();
            result.ModalResumes = modal.IsCollected && ledger.Score == score+30 && probes[2].Stays > 0;
            Check(result.ModalResumes, "Card resume lost the retained overlap.");

            Move(origin+Vector2.left*3f);
            TreasurePickup respawn = Coin(3); Step();
            life.Die();
            bool diedNormally = life.IsRespawning && !body.simulated && !capsule.enabled && life.DeathCount == deaths+1;
            result.DeathKeepsReceipts = diedNormally && ledger.Score == score+30 &&
                descriptions.Take(3).All(item=>ledger.Contains(item.Key));
            Check(result.DeathKeepsReceipts, "Normal PlayerLife death lost the collected treasure ledger.");
            body.simulated = true; capsule.enabled = true; Move(origin); Step();
            result.RespawnBlocks = life.IsRespawning && !respawn.IsCollected && ledger.Score == score+30 && probes[3].Enters > 0;
            Check(result.RespawnBlocks, "An actual queued trigger bypassed IsRespawning.");
            body.simulated = false; capsule.enabled = false;
            life.RespawnImmediately(); body.constraints = RigidbodyConstraints2D.FreezeAll;
            Step();
            result.RestoredContactCollects = !life.IsRespawning && respawn.IsCollected && ledger.Score == score+40;
            Check(result.RestoredContactCollects, "A live restored pirate could no longer collect the remaining coin.");

            Move(origin+Vector2.left*3f);
            string statusBeforeGem = (string)statusField.GetValue(flow);
            float untilBeforeGem = (float)statusUntilField.GetValue(flow);
            TreasurePickup gem = Pickup(gemDescription, "gem"); Step(); Move(origin); Step();
            result.GemSilent = gem.IsCollected && ledger.Contains(gem.Key) && ledger.Score == score+80 &&
                (string)statusField.GetValue(flow) == statusBeforeGem && (float)statusUntilField.GetValue(flow) == untilBeforeGem;
            Check(result.GemSilent, "A real gem trigger lost its forty points or replaced/extended the gameplay hint.");

            Move(origin+Vector2.left*3f);
            TreasurePickup relic = Pickup(relicDescription, "relic"); Step(); Move(origin); Step();
            float relicUntil = (float)statusUntilField.GetValue(flow);
            result.RelicNotification = relic.IsCollected && ledger.Contains(relic.Key) && ledger.Score == score+230 &&
                (string)statusField.GetValue(flow) == "Relic +150 pts" && Mathf.Abs(relicUntil-Time.unscaledTime-1.8f) < .001f;
            Check(result.RelicNotification, "A real relic trigger did not award 150 points with the short points notification.");
            relic.gameObject.SetActive(true); Step(); Step();
            Move(origin+Vector2.left*3f); Step(); Move(origin); Step();
            result.RelicNoDuplicate = ledger.Score == score+230 && ledger.Count == receipts.Length+6 &&
                (string)statusField.GetValue(flow) == "Relic +150 pts" && (float)statusUntilField.GetValue(flow) == relicUntil;
            Check(result.RelicNoDuplicate, "A relic Stay/re-enter farmed points or refreshed the notification.");
            relic.gameObject.SetActive(false);
            foreach (ModalHazardContactProbe probe in probes) { result.Enters += probe.Enters; result.Stays += probe.Stays; }
            Check(result.Enters >= 4 && result.Stays >= 2, "Fixture lacked the asserted real trigger callbacks.");
            yield return null;
        }
        finally
        {
            foreach (GameObject obj in objects) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            hud.ClearUpgrade(); flow.SetMenuBlocked(false); Time.timeScale = 1f;
            life.SetCheckpoint(checkpoint); if (life.IsRespawning) life.RespawnImmediately();
            ledger.Restore(receipts); PirateCampaignSession.Deaths = sessionDeaths;
            abilities.RestoreProgression(upgrades); abilities.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride(); player.ResetMotion(); player.SetControlsEnabled(true);
            body.simulated = true; capsule.enabled = true; body.constraints = constraints;
            Move(position); body.linearVelocity = velocity;
            life.SetDeathCount(deaths); life.SetExitProtected(protection);
            Physics2D.simulationMode = simulation;
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = acknowledgement;
            statusField.SetValue(flow, originalStatus);
            statusUntilField.SetValue(flow, originalStatusUntil);
        }
        result.StateRestored = ledger.Score == score && ledger.Capture().SequenceEqual(receipts) &&
            life.DeathCount == deaths && life.CheckpointPosition == checkpoint && body.position == position &&
            body.constraints == constraints && player.ControlsEnabled && !life.IsRespawning && Physics2D.simulationMode == simulation &&
            (string)statusField.GetValue(flow) == originalStatus && (float)statusUntilField.GetValue(flow) == originalStatusUntil;
        Check(result.StateRestored, "Fixture changed persistent session/player/simulation state.");
        Debug.Log("PIRATE_TREASURE_TRIGGER_REGRESSION " + result +
            " realFlow=True realCallbacks=True fixtureRelocation=True respawnShellFaultInjection=True diskAccess=False fullRouteProof=False");
        completed?.Invoke(result);
    }
}
