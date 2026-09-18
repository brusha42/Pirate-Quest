using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class ModalHazardRegression
{
    public sealed class Result
    {
        public bool DirectDeathBlocked, SpikeBlocked, SpringPreserved, SpringResumes;
        public bool SnareBlocked, RopePreserved, SnareResumes;
        public bool PlantUpdateFrozen, PlantSaberBlocked, PlantSaberResumes;
        public bool CrawlerBlocked, CrawlerResumes, CannonBlocked, CannonRetained, CannonResumes;
        public bool StateRestored;
        public int Checks, TriggerEnters, TriggerStays, SimulatedSteps;
        public float PlantPauseRequestDelta;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && DirectDeathBlocked && SpikeBlocked &&
            SpringPreserved && SpringResumes && SnareBlocked && RopePreserved && SnareResumes &&
            PlantUpdateFrozen && PlantSaberBlocked && PlantSaberResumes && CrawlerBlocked &&
            CrawlerResumes && CannonBlocked && CannonRetained && CannonResumes && StateRestored &&
            TriggerEnters >= 5 && TriggerStays >= 5;
        public override string ToString() => $"success={Success}, checks={Checks}, direct={DirectDeathBlocked}, " +
            $"spikes={SpikeBlocked}/{SpringPreserved}/{SpringResumes}, snare={SnareBlocked}/{RopePreserved}/{SnareResumes}, " +
            $"plant={PlantUpdateFrozen}/{PlantSaberBlocked}/{PlantSaberResumes}, plantRequestDelta={PlantPauseRequestDelta:F6}, " +
            $"crawler={CrawlerBlocked}/{CrawlerResumes}, cannon={CannonBlocked}/{CannonRetained}/{CannonResumes}, " +
            $"contacts={TriggerEnters}/{TriggerStays}, simulatedSteps={SimulatedSteps}, restored={StateRestored}, error={Error}";
    }

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        PirateHUD hud = flow != null ? flow.GetComponent<PirateHUD>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        if (!PirateFrontEnd.IsAutomationRun || flow == null || !flow.IsInitialized || flow.IsTransitioning ||
            flow.IsVictory || hud == null || life == null || abilities == null || grapple == null ||
            hud.IsUpgradeOpen || !player.ControlsEnabled || life.IsRespawning || life.IsSnared ||
            grapple.IsAttached || abilities.IsScouting || Time.timeScale != 1f)
        {
            result.Error = "Requires an explicit automation process and an initialized, unpaused live player/HUD.";
            completed?.Invoke(result);
            yield break;
        }

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        Vector2 originalPosition = body.position, originalVelocity = body.linearVelocity;
        Vector3 originalCheckpoint = life.CheckpointPosition;
        float originalGravity = body.gravityScale, originalCapture = Time.captureDeltaTime;
        RigidbodyConstraints2D originalConstraints = body.constraints;
        SimulationMode2D originalSimulation = Physics2D.simulationMode;
        PirateUpgrade[] originalUpgrades = abilities.CaptureProgression();
        bool originalProtection = life.IsExitProtected;
        int originalDeaths = life.DeathCount;
        bool originalAcknowledgement = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        var origin = new Vector2(7800f, 7800f);
        var objects = new List<GameObject>();
        var probes = new List<ModalHazardContactProbe>();

        void Check(bool condition, string description)
        {
            result.Checks++;
            if (!condition) result.Error = string.IsNullOrEmpty(result.Error) ? description : result.Error + "; " + description;
        }
        GameObject Make(string name, Vector2 position, Vector2 size)
        {
            var obj = new GameObject("Modal hazard fixture - " + name);
            objects.Add(obj);
            obj.transform.position = position;
            BoxCollider2D collider = obj.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = true;
            var probe = obj.AddComponent<ModalHazardContactProbe>();
            probes.Add(probe);
            return obj;
        }
        void Retire(GameObject obj)
        {
            if (obj != null) obj.SetActive(false);
        }
        void Place()
        {
            hud.ClearUpgrade();
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.ResetTransientState();
            player.ResetMotion();
            player.SetControlsEnabled(true);
            player.SetAutomationInputOverride(Vector2.zero);
            abilities.ClearAutomationInputOverride();
            grapple.SetAutomationInputOverride(true);
            body.constraints = RigidbodyConstraints2D.FreezeAll;
            body.position = origin + Vector2.up;
            player.transform.position = body.position;
            body.linearVelocity = Vector2.zero;
            life.SetCheckpoint(body.position);
            Physics2D.SyncTransforms();
        }
        void Freeze()
        {
            hud.ShowUpgrade(PirateUpgrade.Hook2);
            Check(hud.IsUpgradeOpen && player.IsModalInputBlocked && Time.timeScale == 0f,
                "The actual HUD did not synchronously freeze the fixture.");
        }
        void Step()
        {
            Physics2D.SyncTransforms();
            body.WakeUp();
            bool ran = Physics2D.Simulate(Time.fixedDeltaTime);
            if (ran) result.SimulatedSteps++;
            Check(ran, "Manual physics step did not execute outside physics callbacks.");
        }
        bool Alive(int before) => !life.IsRespawning && life.DeathCount == before;

        try
        {
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;
            Physics2D.simulationMode = SimulationMode2D.Script;
            Time.captureDeltaTime = Time.fixedDeltaTime;
            life.SetExitProtected(false);
            abilities.ResetProgression();
            abilities.Apply(PirateUpgrade.Hook2, false);
            Place();
            int deaths = life.DeathCount;
            Freeze();
            life.Die();
            result.DirectDeathBlocked = Alive(deaths);
            Check(result.DirectDeathBlocked, "Direct death bypassed the equipment modal.");
            hud.DismissUpgrade();

            abilities.Apply(PirateUpgrade.SpringLeg, false);
            GameObject spikes = Make("spikes", new Vector2(body.position.x, capsule.bounds.min.y), new Vector2(1f, .2f));
            spikes.AddComponent<InstantKillHazard>().Configure(HazardKind.Spikes);
            ModalHazardContactProbe spikeProbe = spikes.GetComponent<ModalHazardContactProbe>();
            Freeze();
            Step();
            result.SpikeBlocked = spikeProbe.Enters > 0 && Alive(deaths);
            result.SpringPreserved = abilities.SpringAvailable;
            Check(result.SpikeBlocked && result.SpringPreserved, "Paused spike enter killed or consumed the spring.");
            hud.DismissUpgrade();
            Step();
            result.SpringResumes = spikeProbe.Stays > 0 && !abilities.SpringAvailable && Alive(deaths);
            Check(result.SpringResumes, "The unchanged spike overlap did not bounce on resume.");
            Retire(spikes);

            Place();
            GameObject anchorObject = new GameObject("Modal fixture - real rope pivot");
            objects.Add(anchorObject);
            anchorObject.transform.position = body.position + Vector2.up * 2.2f;
            HookAnchor anchor = anchorObject.AddComponent<HookAnchor>();
            anchor.Configure();
            bool attached = grapple.TryAttachToAnchor(anchor);
            GameObject snareObject = Make("snare", body.position, Vector2.one);
            SnareTrap snare = snareObject.AddComponent<SnareTrap>();
            ModalHazardContactProbe snareProbe = snareObject.GetComponent<ModalHazardContactProbe>();
            float gravity = body.gravityScale;
            Freeze();
            Step();
            result.SnareBlocked = snareProbe.Enters > 0 && !snare.HasVictim && !life.IsSnared && Alive(deaths);
            result.RopePreserved = attached && grapple.IsAttached && grapple.CurrentAnchor == anchor &&
                Mathf.Abs(body.gravityScale - gravity) < .0001f;
            Check(result.SnareBlocked && result.RopePreserved, "A paused snare enter changed the victim, rope or gravity.");
            hud.DismissUpgrade();
            Step();
            result.SnareResumes = snareProbe.Stays > 0 && snare.HasVictim && life.IsSnared && !grapple.IsAttached;
            Check(result.SnareResumes, "Skipped snare enter was lost: the same overlap must catch on resume.");
            Retire(snareObject);
            Retire(anchorObject);

            Place();
            abilities.Apply(PirateUpgrade.Saber1, false);
            GameObject plantObject = Make("plant", origin + new Vector2(2f, 3.5f), new Vector2(.5f, .5f));
            HangingPlant plant = plantObject.AddComponent<HangingPlant>();
            plant.Initialize(player.transform);
            ModalHazardContactProbe plantProbe = plantObject.GetComponent<ModalHazardContactProbe>();
            for (int i = 0; i < 200 && !plant.IsAttacking; i++) yield return null;
            bool plantAttacked = plant.IsAttacking;
            bool saberStarted = abilities.TryAttack();
            Vector3 pausedPlantPosition = plant.transform.position;
            int attacks = plant.AttacksStarted;
            plantProbe.BeforeNextUpdate = () =>
            {
                pausedPlantPosition = plant.transform.position;
                result.PlantPauseRequestDelta = Time.deltaTime;
                Freeze();
            };
            for (int i = 0; i < 3 && !hud.IsUpgradeOpen; i++) yield return null;
            yield return null;
            result.PlantUpdateFrozen = plantAttacked && Time.timeScale == 0f &&
                result.PlantPauseRequestDelta > 0f && plant.transform.position == pausedPlantPosition &&
                plant.AttacksStarted == attacks;
            Check(result.PlantUpdateFrozen, "Plant Update moved/advanced after a positive-delta modal opening.");
            plant.transform.position = body.position;
            Step();
            result.PlantSaberBlocked = plantProbe.Enters > 0 && saberStarted && abilities.IsAttacking &&
                plant.IsAttacking && !plant.IsDead && Alive(deaths);
            Check(result.PlantSaberBlocked, "A queued plant contact dealt damage or cut the plant under the card.");
            hud.DismissUpgrade();
            Step();
            result.PlantSaberResumes = plantProbe.Stays > 0 && plant.IsDead && Alive(deaths);
            Check(result.PlantSaberResumes, "A still-overlapping attacking plant did not receive the active saber after resume.");
            Retire(plantObject);

            Place();
            BrineCrawler crawler = BrineCrawler.Install(null, origin + Vector2.right * 2f,
                new Rect(origin.x - 10f, origin.y - 1f, 20f, 1f), -1);
            objects.Add(crawler.gameObject);
            var crawlerProbe = crawler.gameObject.AddComponent<ModalHazardContactProbe>();
            probes.Add(crawlerProbe);
            crawler.SetTarget(life);
            for (int i = 0; i < 80 && !crawler.IsAttacking; i++) yield return null;
            bool crawlerAttacked = crawler.IsAttacking;
            Freeze();
            body.position = (Vector2)crawler.transform.position + Vector2.up;
            player.transform.position = body.position;
            Step();
            result.CrawlerBlocked = crawlerAttacked && crawlerProbe.Enters > 0 && Alive(deaths);
            Check(result.CrawlerBlocked, "A real attacking crawler contact killed under the card.");
            hud.DismissUpgrade();
            Step();
            result.CrawlerResumes = crawlerProbe.Stays > 0 && life.DeathCount == deaths + 1;
            Check(result.CrawlerResumes, "Crawler stay contact was harmless after resume.");
            Retire(crawler.gameObject);

            Place();
            deaths = life.DeathCount;
            GameObject ballWall = Make("projectile solid contact", body.position, Vector2.one * .42f);
            ballWall.GetComponent<BoxCollider2D>().isTrigger = false;
            GameObject ballObject = Make("cannonball", body.position, Vector2.one * .42f);
            Rigidbody2D ballBody = ballObject.AddComponent<Rigidbody2D>();
            ballBody.gravityScale = 0f;
            ballBody.constraints = RigidbodyConstraints2D.FreezeAll;
            ballObject.AddComponent<InstantKillHazard>();
            Cannonball ball = ballObject.AddComponent<Cannonball>();
            ball.Initialize(null);
            ModalHazardContactProbe ballProbe = ballObject.GetComponent<ModalHazardContactProbe>();
            Freeze();
            Step();
            result.CannonBlocked = ballProbe.Enters > 0 && Alive(deaths);
            yield return null;
            result.CannonRetained = ball != null && ballObject.activeInHierarchy;
            Check(result.CannonBlocked && result.CannonRetained,
                "A paused projectile killed or was consumed by its player/solid contacts before resume.");
            hud.DismissUpgrade();
            Step();
            result.CannonResumes = ballProbe != null && ballProbe.Stays > 0 && life.DeathCount == deaths + 1;
            Check(result.CannonResumes, "The retained projectile did not remain lethal on resumed stay contact.");
            foreach (ModalHazardContactProbe probe in probes)
                if (probe != null) { result.TriggerEnters += probe.Enters; result.TriggerStays += probe.Stays; }
        }
        finally
        {
            foreach (GameObject obj in objects)
                if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            hud.ClearUpgrade();
            Time.timeScale = 1f;
            life.SetCheckpoint(originalCheckpoint);
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.RestoreProgression(originalUpgrades);
            abilities.ClearAutomationInputOverride();
            grapple.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride();
            player.ResetMotion();
            player.SetControlsEnabled(true);
            body.constraints = originalConstraints;
            body.position = originalPosition;
            player.transform.position = originalPosition;
            body.gravityScale = originalGravity;
            body.linearVelocity = originalVelocity;
            life.SetDeathCount(originalDeaths);
            life.SetExitProtected(originalProtection);
            Physics2D.SyncTransforms();
            Physics2D.simulationMode = originalSimulation;
            Time.captureDeltaTime = originalCapture;
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = originalAcknowledgement;
        }
        result.StateRestored = Physics2D.simulationMode == originalSimulation && Time.timeScale == 1f &&
            !player.IsModalInputBlocked && player.ControlsEnabled && !life.IsRespawning && !life.IsSnared &&
            !hud.IsUpgradeOpen && life.DeathCount == originalDeaths && body.constraints == originalConstraints &&
            body.position == originalPosition && life.CheckpointPosition == originalCheckpoint;
        Check(result.StateRestored, "Modal hazard fixture left modified simulation/player state.");
        Debug.Log("PIRATE_MODAL_HAZARD_REGRESSION " + result +
            " realHud=True realPhysicsCallbacks=True manualQueuedContactInjection=True fixtureRelocation=True fullRouteProof=False");
        completed?.Invoke(result);
    }
}

[DefaultExecutionOrder(-200)]
public sealed class ModalHazardContactProbe : MonoBehaviour
{
    public int Enters { get; private set; }
    public int Stays { get; private set; }
    public Action BeforeNextUpdate;
    private void Update()
    {
        Action action = BeforeNextUpdate;
        BeforeNextUpdate = null;
        action?.Invoke();
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerLife>() != null) Enters++;
    }
    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerLife>() != null) Stays++;
    }
}
