using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ExpeditionBodyHazardRegression
{
    public sealed class Result
    {
        public int Checks, Enters, Stays;
        public bool CannonOutline, PlantOutline, CannonOutsideSafe, PlantOutsideSafe;
        public bool CannonPaused, CannonResumes, PlantPaused, PlantResumes, SaberKillsIdlePlant, StateRestored;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && CannonOutline && PlantOutline &&
            CannonOutsideSafe && PlantOutsideSafe && CannonPaused && CannonResumes && PlantPaused &&
            PlantResumes && SaberKillsIdlePlant && StateRestored && Enters >= 2 && Stays >= 2;
        public override string ToString() => $"success={Success}, checks={Checks}, outline={CannonOutline}/{PlantOutline}, " +
            $"outside={CannonOutsideSafe}/{PlantOutsideSafe}, cannon={CannonPaused}/{CannonResumes}, " +
            $"plant={PlantPaused}/{PlantResumes}/{SaberKillsIdlePlant}, callbacks={Enters}/{Stays}, restored={StateRestored}, error={Error}";
    }

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        PirateHUD hud = flow != null ? flow.GetComponent<PirateHUD>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        if (!PirateFrontEnd.IsAutomationRun || hud == null || life == null || abilities == null ||
            !player.ControlsEnabled || life.IsRespawning || Time.timeScale != 1f || hud.IsUpgradeOpen)
        {
            result.Error = "Requires isolated automation with live unpaused player and real HUD.";
            completed?.Invoke(result); yield break;
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        Vector2 position = body.position, velocity = body.linearVelocity;
        Vector3 checkpoint = life.CheckpointPosition;
        int deaths = life.DeathCount;
        bool protection = life.IsExitProtected;
        RigidbodyConstraints2D constraints = body.constraints;
        PirateUpgrade[] upgrades = abilities.CaptureProgression();
        SimulationMode2D simulation = Physics2D.simulationMode;
        bool acknowledgement = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        var origin = new Vector2(11600f, 11600f);
        var objects = new List<GameObject>();
        var probes = new List<ModalHazardContactProbe>();
        void Check(bool condition, string detail)
        {
            result.Checks++;
            if (!condition) result.Error = string.IsNullOrEmpty(result.Error) ? detail : result.Error + "; " + detail;
        }
        void Place(Vector2 point)
        {
            hud.ClearUpgrade();
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.ResetTransientState(); player.ResetMotion(); player.SetControlsEnabled(true);
            player.SetAutomationInputOverride(Vector2.zero);
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            body.position = point; player.transform.position = point; body.linearVelocity = Vector2.zero;
            life.SetCheckpoint(origin + Vector2.left * 20f); Physics2D.SyncTransforms();
        }
        void Step()
        {
            Physics2D.SyncTransforms(); body.WakeUp();
            Check(Physics2D.Simulate(Time.fixedDeltaTime), "Script physics step was refused.");
        }
        bool Alive(int before) => life.DeathCount == before && !life.IsRespawning;
        bool Fits(PolygonCollider2D collider, PirateWorldVisual art, bool cannon)
        {
            if (collider == null || !collider.enabled || !collider.isTrigger || collider.pathCount != 1) return false;
            PirateArtKind frame = cannon
                ? (art.Renderer.sprite == PirateWorldArt.GetSprite(PirateArtKind.CannonFire) ? PirateArtKind.CannonFire : PirateArtKind.Cannon)
                : (art.Renderer.sprite == PirateWorldArt.GetSprite(PirateArtKind.PlantAttack) ? PirateArtKind.PlantAttack : PirateArtKind.Plant);
            Rect opaque = cannon ? PirateWorldArt.Library.GetLayoutBounds(frame) : PirateWorldArt.Library.GetOpaqueBounds(frame);
            foreach (Vector2 point in collider.GetPath(0))
            {
                Vector2 local = art.Renderer.transform.InverseTransformPoint(collider.transform.TransformPoint(point));
                if (art.Renderer.flipX) local.x = -local.x;
                if (art.Renderer.flipY) local.y = -local.y;
                if (local.x < opaque.xMin - .003f || local.x > opaque.xMax + .003f ||
                    local.y < opaque.yMin - .003f || local.y > opaque.yMax + .003f) return false;
            }
            return true;
        }
        GameObject Make(PirateArtKind kind, Vector2 size)
        {
            GameObject obj = PirateWorldArt.Create("Body hazard fixture - " + kind, kind, origin, size);
            objects.Add(obj);
            obj.AddComponent<BoxCollider2D>().size = Vector2.one * 12f;
            var probe = obj.AddComponent<ModalHazardContactProbe>(); probes.Add(probe);
            return obj;
        }
        try
        {
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;
            Physics2D.simulationMode = SimulationMode2D.Script;
            life.SetExitProtected(false); abilities.ResetProgression();
            var target = new GameObject("Body hazard fixture - distant inactive target"); objects.Add(target);
            target.transform.position = origin + Vector2.right * 100f;

            GameObject cannonObject = Make(PirateArtKind.Cannon, new Vector2(1.5f, 1.3f));
            PirateWorldVisual cannonArt = cannonObject.GetComponent<PirateWorldVisual>();
            cannonArt.SetGroundSurface(origin.y);
            Cannon cannon = cannonObject.AddComponent<Cannon>();
            cannon.Initialize(target.transform, PirateWorldArt.GetSprite(PirateArtKind.Cannonball));
            PolygonCollider2D cannonBody = cannon.BodyHitbox as PolygonCollider2D;
            bool cannonFits = !cannonObject.GetComponent<BoxCollider2D>().enabled && Fits(cannonBody, cannonArt, true);
            cannonArt.SetFrame(PirateArtKind.CannonFire); cannon.RefreshBodyHitbox();
            cannonFits &= Fits(cannonBody, cannonArt, true);
            cannonArt.Renderer.flipX = !cannonArt.Renderer.flipX; cannon.RefreshBodyHitbox();
            cannonFits &= Fits(cannonBody, cannonArt, true);
            cannonArt.SetFrame(PirateArtKind.Cannon); cannon.RefreshBodyHitbox();
            result.CannonOutline = cannonFits;
            Check(result.CannonOutline, "Cannon body escaped its registered artwork, included flash or retained huge marker.");
            Physics2D.SyncTransforms();
            Bounds cannonBounds = cannonBody.bounds;
            Place(new Vector2(cannonBounds.max.x + capsule.bounds.extents.x + .15f, cannonBounds.center.y));
            int before = life.DeathCount;
            Step();
            result.CannonOutsideSafe = Alive(before);
            Check(result.CannonOutsideSafe, "Cannon killed outside visible body (inside legacy activation/marker area).");
            hud.ShowUpgrade(PirateUpgrade.Hook2);
            body.position = cannonBounds.center; player.transform.position = body.position;
            Step();
            ModalHazardContactProbe cannonProbe = cannonObject.GetComponent<ModalHazardContactProbe>();
            result.CannonPaused = hud.IsUpgradeOpen && life.IsModalProtected && cannonProbe.Enters > 0 && Alive(before);
            Check(result.CannonPaused, "Actual cannon body enter killed under the modal.");
            hud.DismissUpgrade(); Step();
            result.CannonResumes = cannonProbe.Stays > 0 && life.DeathCount == before + 1;
            Check(result.CannonResumes, "Stationary cannon body overlap was lost after modal resume.");
            cannonObject.SetActive(false);

            Place(origin + Vector2.right * 10f);
            GameObject plantObject = Make(PirateArtKind.Plant, new Vector2(1.7f, 2.2f));
            HangingPlant plant = plantObject.AddComponent<HangingPlant>(); plant.Initialize(target.transform);
            PirateWorldVisual plantArt = plantObject.GetComponent<PirateWorldVisual>();
            PolygonCollider2D plantBody = plant.BodyHitbox as PolygonCollider2D;
            bool plantFits = !plantObject.GetComponent<BoxCollider2D>().enabled && Fits(plantBody, plantArt, false);
            plantArt.SetFrame(PirateArtKind.PlantAttack); plant.RefreshBodyHitbox();
            plantFits &= Fits(plantBody, plantArt, false);
            plantArt.Renderer.flipX = true; plant.RefreshBodyHitbox(); plantFits &= Fits(plantBody, plantArt, false);
            plantArt.SetFrame(PirateArtKind.Plant); plant.RefreshBodyHitbox();
            result.PlantOutline = plantFits;
            Check(result.PlantOutline, "Plant body escaped idle/attack artwork or retained a huge marker.");
            Physics2D.SyncTransforms(); Bounds plantBounds = plantBody.bounds;
            Place(new Vector2(plantBounds.max.x + capsule.bounds.extents.x + .15f, plantBounds.center.y));
            before = life.DeathCount; Step();
            result.PlantOutsideSafe = Alive(before);
            Check(result.PlantOutsideSafe, "Plant perception radius/transparent exterior caused body damage.");
            hud.ShowUpgrade(PirateUpgrade.Hook2);
            body.position = plantBounds.center; player.transform.position = body.position;
            Step();
            ModalHazardContactProbe plantProbe = plantObject.GetComponent<ModalHazardContactProbe>();
            result.PlantPaused = !plant.IsAttacking && plantProbe.Enters > 0 && Alive(before) && !plant.IsDead;
            Check(result.PlantPaused, "Idle plant body failed queued-contact modal protection.");
            hud.DismissUpgrade(); Step();
            result.PlantResumes = plantProbe.Stays > 0 && life.DeathCount == before + 1;
            Check(result.PlantResumes, "Idle plant body did not become lethal on resumed stay.");
            plantObject.SetActive(false);

            Place(origin + Vector2.right * 10f);
            plantObject.SetActive(true);
            abilities.Apply(PirateUpgrade.Saber1, false);
            body.position = plantBounds.center + (player.IsFacingRight ? Vector3.left : Vector3.right) * .9f;
            player.transform.position = body.position; Physics2D.SyncTransforms();
            Check(PirateHUD.ConsumedInputThisFrame && !abilities.TryAttack(),
                "Closing a modal must not also swing the saber in that same frame.");
            yield return null;
            bool attacked = abilities.TryAttack();
            result.SaberKillsIdlePlant = attacked && plant.IsDead && !plantBody.enabled;
            Check(result.SaberKillsIdlePlant, "An idle plant body ignored the production saber overlap.");
            foreach (ModalHazardContactProbe probe in probes) { result.Enters += probe.Enters; result.Stays += probe.Stays; }
            yield return null;
        }
        finally
        {
            foreach (GameObject obj in objects) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            hud.ClearUpgrade(); Time.timeScale = 1f;
            life.SetCheckpoint(checkpoint);
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.RestoreProgression(upgrades); abilities.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride(); player.ResetMotion(); player.SetControlsEnabled(true);
            body.constraints = constraints; body.position = position; player.transform.position = position;
            body.linearVelocity = velocity; life.SetDeathCount(deaths); life.SetExitProtected(protection);
            Physics2D.SyncTransforms(); Physics2D.simulationMode = simulation;
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = acknowledgement;
        }
        result.StateRestored = Physics2D.simulationMode == simulation && body.position == position &&
            body.constraints == constraints && life.DeathCount == deaths && player.ControlsEnabled && !hud.IsUpgradeOpen;
        Check(result.StateRestored, "Body fixture did not restore simulation/player state.");
        Debug.Log("PIRATE_EXPEDITION_BODY_HAZARDS " + result +
            " realArtwork=True realPhysicsCallbacks=True pausedCallbackInjection=True pixelProof=False fullRouteProof=False");
        completed?.Invoke(result);
    }
}
