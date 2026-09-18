using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampaignTraversalRegression : MonoBehaviour
{
    private int checks;
    private bool failed;
    private IEnumerator Start()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pirateQuestCampaignTest")) { Destroy(gameObject); yield break; }
        PirateTestModalAcknowledger.Install();
        Application.runInBackground = true;
        if (int.TryParse(GetArgument("-pirateQuestTestFrameRate"), out int requestedFrameRate) && requestedFrameRate >= 1 && requestedFrameRate <= 120)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = requestedFrameRate;
            Debug.Log("PIRATE_CAMPAIGN_TEST_PACING targetFrameRate=" + requestedFrameRate + " captureStep=" + Time.fixedDeltaTime);
        }
        Time.captureDeltaTime = Time.fixedDeltaTime;
        if (!Check(PirateSaveStore.RunIsolatedDiskRegression(out string saveDetail),"Isolated durable save: "+saveDetail)) yield break;
        if (!Check(PirateTreasureRegression.Verify(out string treasureDetail), "Deterministic treasure and ledger: " + treasureDetail)) yield break;
        PirateRunStatisticsRegression.Result statistics = PirateRunStatisticsRegression.Run();
        if (!Check(statistics.Success, "Run scoring, legacy migration and bounded statistics: " + statistics)) yield break;
        Debug.Log("PIRATE_CAMPAIGN_TEST_SCOPE: scene integration, input jump, inventory persistence, art/collision, respawn. fullTraversalProof=False; sceneExitBypass=True");
        yield return null;
        PirateSaveData sessionBeforeFixtures = new PirateSaveData {
            seed = PirateCampaignSession.Seed, chapter = PirateCampaignSession.Chapter,
            deaths = PirateCampaignSession.Deaths,
            upgrades = PirateCampaignSession.Earned.Select(upgrade => (int)upgrade).ToArray(),
            treasureReceipts = PirateCampaignSession.TreasureLedger.Capture(),
            introSeen = PirateCampaignSession.IntroSeen
        };
        PirateCampaignSession.Statistics.CaptureInto(sessionBeforeFixtures);
        PlayerMovement fixturePlayer = FindFirstObjectByType<PlayerMovement>();
        PirateMovementProfileRegression.Result movementProfile = PirateMovementProfileRegression.Run(fixturePlayer, 0);
        if (!Check(movementProfile.Success, "Chapter movement profile: " + movementProfile)) yield break;
        GroundDashRegression.Result groundDash = null;
        yield return GroundDashRegression.Run(fixturePlayer, value => groundDash = value);
        if (!Check(groundDash != null && groundDash.Success, "Ground/air dash input fixtures: " + groundDash)) yield break;
        CornerContactRegression.Result cornerContact = null;
        yield return CornerContactRegression.Run(fixturePlayer, value => cornerContact = value);
        if (!Check(cornerContact != null && cornerContact.Success, "Capsule corner friction and solid contacts: " + cornerContact)) yield break;
        BrineCrawlerRegression.Result crawler = null;
        yield return BrineCrawlerRegression.Run(fixturePlayer, value => crawler = value);
        if (!Check(crawler != null && crawler.Success, "Brine crawler physics/art: " + crawler)) yield break;
        BlackTideRegression.Result tide = null;
        yield return BlackTideRegression.Run(fixturePlayer, value => tide = value);
        if (!Check(tide != null && tide.Success, "Black tide pressure/respawn: " + tide)) yield break;
        PirateHudRegression.Result hudResult = null;
        yield return PirateHudRegression.Run(FindFirstObjectByType<PirateGameFlow>(), fixturePlayer, value => hudResult = value);
        if (!Check(hudResult != null && hudResult.Success, "HUD modal lifecycle: " + hudResult)) yield break;
        ModalHazardRegression.Result modalHazards = null;
        yield return ModalHazardRegression.Run(FindFirstObjectByType<PirateGameFlow>(), fixturePlayer, value => modalHazards = value);
        if (!Check(modalHazards != null && modalHazards.Success, "Queued hazards respect modal pause and resume: " + modalHazards)) yield break;
        CampaignMechanicsRegression.Result mechanics = null;
        yield return CampaignMechanicsRegression.Run(FindFirstObjectByType<PlayerMovement>(), result => mechanics = result);
        PirateCampaignSession.Load(sessionBeforeFixtures);
        PirateCampaignSession.ConsumeRestore();
        Debug.Log("PIRATE_CAMPAIGN_MECHANICS_RESULT "+mechanics);
        if (!Check(mechanics != null && mechanics.Success,"Physical mechanics fixtures")) yield break;
        ExpeditionControlsRegression.Result expeditionControls = null;
        yield return ExpeditionControlsRegression.Run(fixturePlayer, result => expeditionControls = result);
        if (!Check(expeditionControls != null && expeditionControls.Success,
            "RMB/LMB/Space production control events: " + expeditionControls)) yield break;
        ExpeditionBodyHazardRegression.Result expeditionBodies = null;
        yield return ExpeditionBodyHazardRegression.Run(FindFirstObjectByType<PirateGameFlow>(), fixturePlayer, result => expeditionBodies = result);
        if (!Check(expeditionBodies != null && expeditionBodies.Success,
            "Idle cannon/plant body hazards and modal protection: " + expeditionBodies)) yield break;
        TreasurePickupRegression.Result treasurePickup = null;
        yield return TreasurePickupRegression.Run(FindFirstObjectByType<PirateGameFlow>(), fixturePlayer, result => treasurePickup = result);
        if (!Check(treasurePickup != null && treasurePickup.Success,
            "Treasure trigger idempotence, pause and death retention: " + treasurePickup)) yield break;
        SecretCacheTraversalRegression.Result secretCache = null;
        yield return SecretCacheTraversalRegression.Run(FindFirstObjectByType<PirateGameFlow>(), fixturePlayer, result => secretCache = result);
        if (!Check(secretCache != null && secretCache.Success, "Hidden stair cache: actual drop, discovery and return: " + secretCache)) yield break;
        PirateRunClockRegression.Result runClock = null;
        yield return PirateRunClockRegression.Run(FindFirstObjectByType<PirateGameFlow>(), fixturePlayer, result => runClock = result);
        if (!Check(runClock != null && runClock.Success, "Active run time and real pause gates: " + runClock)) yield break;
        PirateCampaignSession.Load(sessionBeforeFixtures);
        PirateCampaignSession.ConsumeRestore();
        for (int chapterIndex = 0; chapterIndex < 4; chapterIndex++)
        {
            PirateGameFlow flow = null;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                flow = FindFirstObjectByType<PirateGameFlow>();
                if (flow != null && flow.IsInitialized && flow.ChapterIndex == chapterIndex) break;
                yield return null;
            }
            if (!Check(flow != null && flow.IsInitialized && flow.ChapterIndex == chapterIndex, "Scene loaded " + chapterIndex)) yield break;
            yield return new WaitForSecondsRealtime(.5f);
            LevelGenerator generator = flow.Generator;
            CampaignLayout layout = generator.Campaign;
            PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
            PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
            PlayerLife life = player.GetComponent<PlayerLife>();
            if (!Check(!life.IsExitProtected,"New chapter begins without terminal protection")) yield break;
            if (!Check(layout != null && layout.GeometryValid, "Campaign layout strategy envelopes")) yield break;
            if (!Check(Mathf.Abs(player.MoveSpeed - PirateMovementProfile.ForChapter(chapterIndex)) < .001f,
                "Actual player uses chapter speed before generation")) yield break;
            if (!Check(CampaignBackdropRegression.Verify(generator, out string backdropDetail),
                "Occupied architecture backdrop: " + backdropDetail)) yield break;
            if (!Check(WorldVisualRegression.Verify(out string visualDetail), "Opaque art floor placement: " + visualDetail)) yield break;
            SnareTrap[] chapterSnares = FindObjectsByType<SnareTrap>(FindObjectsSortMode.None);
            int expectedSnares = layout.Spawns.Count(spawn => spawn.Kind == CampaignLayout.SpawnKind.Snare);
            if (!Check(chapterSnares.Length == expectedSnares,
                $"Snare spawn count matches current chapter: actual={chapterSnares.Length}, expected={expectedSnares}")) yield break;
            foreach (SnareTrap snare in chapterSnares)
            {
                const float attachmentTolerance = .03f;
                PirateWorldVisual artwork = snare.GetComponent<PirateWorldVisual>();
                float loopTop = artwork != null ? artwork.OpaqueWorldBounds.max.y : float.PositiveInfinity;
                Vector2 point = snare.AttachmentPoint;
                bool aboveLoop = artwork != null && point.y >= loopTop - attachmentTolerance;
                bool alignedWithLoop = Mathf.Abs(point.x - snare.transform.position.x) <= attachmentTolerance;
                bool platformUnderside = layout.Platforms.Any(platform =>
                    point.x >= platform.Bounds.xMin - attachmentTolerance && point.x <= platform.Bounds.xMax + attachmentTolerance &&
                    Mathf.Abs(point.y - platform.Bounds.yMin) <= attachmentTolerance);
                bool solidUnderside = layout.Solids.Any(solid =>
                    point.x >= solid.xMin - attachmentTolerance && point.x <= solid.xMax + attachmentTolerance &&
                    Mathf.Abs(point.y - solid.yMin) <= attachmentTolerance);
                if (!Check(snare.HasOverheadAttachment && aboveLoop && alignedWithLoop && (platformUnderside || solidUnderside),
                    $"Snare tether meets current geometry: position={snare.transform.position}, attachment={point}, " +
                    $"loopTop={loopTop:F3}, tether={snare.HasOverheadAttachment}, above={aboveLoop}, aligned={alignedWithLoop}, " +
                    $"platformUnderside={platformUnderside}, solidUnderside={solidUnderside}, tolerance={attachmentTolerance}")) yield break;
            }
            if (chapterIndex == 3)
            {
                DarkZone darkness = FindFirstObjectByType<DarkZone>();
                if (!Check(darkness != null && darkness.WorldBounds == new Rect(layout.Bounds.x,layout.Bounds.y,layout.Bounds.width,layout.Bounds.height),
                    "Continuous Crown darkness covers upper floors and shafts")) yield break;
                if (!Check(flow.Tide != null && flow.Tide.IsActive, "Final chapter has active Black Tide pressure")) yield break;
            }
            if (!Check(SceneManager.GetActiveScene().name == CampaignChapter.SceneNames[chapterIndex], "Distinct real scene name")) yield break;
            if (!Check(abilities.Has(PirateUpgrade.Hook1) && abilities.Has(PirateUpgrade.Saber1), "Starting kit / retained kit")) yield break;
            if (chapterIndex > 0)
                foreach (PirateUpgrade upgrade in PirateCampaignSession.Earned)
                    if (!Check(abilities.Has(upgrade), "Retained upgrade " + upgrade)) yield break;

            foreach (PirateWorldVisual visual in FindObjectsByType<PirateWorldVisual>(FindObjectsSortMode.None))
                if (!Check(visual.Renderer != null && visual.Renderer.sprite != null && visual.Renderer.sprite.texture.width > 8,
                    "Production sprite " + visual.Kind)) yield break;

            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            float groundedDeadline = Time.realtimeSinceStartup + 3f;
            while (!player.IsGrounded && Time.realtimeSinceStartup < groundedDeadline) yield return new WaitForFixedUpdate();
            if (!Check(player.IsGrounded, "Ground contact at chapter entrance")) yield break;
            float startY = body.position.y;
            float apex = startY;
            player.SetAutomationInputOverride(Vector2.zero, jumpPressed:true);
            yield return null;
            player.SetAutomationInputOverride(Vector2.zero);
            for (int frame = 0; frame < 60; frame++)
            {
                apex = Mathf.Max(apex,body.position.y);
                yield return new WaitForFixedUpdate();
            }
            player.ClearAutomationInputOverride();
            if (!Check(apex - startY > 2.4f && !life.IsRespawning, "Real full-height jump " + (apex-startY).ToString("F2"))) yield break;

            AbilityPickup[] chapterPickups = FindObjectsByType<AbilityPickup>(FindObjectsSortMode.None)
                .OrderBy(pickup => layout.Spawns.Where(spawn => spawn.Kind == CampaignLayout.SpawnKind.Upgrade && spawn.Ability == pickup.Upgrade)
                    .Select(spawn => spawn.NodeIndex).DefaultIfEmpty(int.MaxValue).First()).ToArray();
            Debug.Log($"PIRATE_PICKUP_FIXTURE_ORDER chapter={chapterIndex} pickups={string.Join(",", chapterPickups.Select(pickup => pickup.Upgrade))} fixtureRelocation=True routeProof=False");
            foreach (AbilityPickup pickup in chapterPickups)
            {
                if (pickup == null) continue;
                Debug.Log($"PIRATE_PICKUP_FIXTURE_BEGIN chapter={chapterIndex} upgrade={pickup.Upgrade} position={pickup.transform.position} " +
                    $"respawning={life.IsRespawning} collider={player.GetComponent<Collider2D>().enabled} tide={(flow.Tide != null ? flow.Tide.SurfaceY : -999f):F2}");
                body.position = pickup.transform.position;
                player.transform.position = body.position;
                player.ResetMotion();
                Physics2D.SyncTransforms();
                yield return new WaitForFixedUpdate();
                yield return new WaitForFixedUpdate();
                while (flow.HUD != null && flow.HUD.IsUpgradeOpen) yield return null;
            }
            foreach (CampaignLayout.Spawn spawn in layout.Spawns.Where(s => s.Kind == CampaignLayout.SpawnKind.Upgrade))
                if (!Check(abilities.Has(spawn.Ability), "Pickup trigger grants " + spawn.Ability)) yield break;

            life.RespawnImmediately();
            yield return new WaitForFixedUpdate();
            Vector3 checkpoint = life.CheckpointPosition;
            life.Die();
            yield return new WaitForSecondsRealtime(.6f);
            if (!Check(!life.IsRespawning && Vector2.Distance(player.transform.position,checkpoint) < .2f, "Local respawn")) yield break;
            if (!Check(player.GetComponent<SpriteRenderer>() == null || !player.GetComponent<SpriteRenderer>().enabled,
                "Animated source stays hidden after respawn")) yield break;
            string evidence = GetArgument("-pirateQuestCampaignEvidence");
            if (!string.IsNullOrWhiteSpace(evidence))
            {
                yield return new WaitForSecondsRealtime(.5f);
                Capture(Path.Combine(evidence,"chapter-" + chapterIndex + ".png"));
            }
            Debug.Log($"PIRATE_CAMPAIGN_SCENE_INTEGRATION_PASS chapter={chapterIndex} name={SceneManager.GetActiveScene().name} " +
                $"signature={layout.Signature()} upgrades={string.Join(",",abilities.CaptureProgression())} fixtureRelocations=True fullTraversalProof=False");
            flow.CompleteChapterForIntegrationTest();
            Vector2 terminalPosition = body.position;
            int terminalDeaths = life.DeathCount;
            float terminalWorldTime = Time.time;
            string terminalSaveInputs = CaptureSaveInputs(flow,life,abilities);
            SpriteRenderer[] terminalRenderers = player.GetComponentsInChildren<SpriteRenderer>(true);
            bool[] terminalVisibility = terminalRenderers.Select(renderer => renderer.enabled).ToArray();
            int lateDeathEvents = 0, lateRespawnEvents = 0, lateRestoreEvents = 0;
            Action onLateDeath = () => lateDeathEvents++;
            Action onLateRespawn = () => lateRespawnEvents++;
            Action onLateRestore = () => lateRestoreEvents++;
            life.Died += onLateDeath;
            life.Respawned += onLateRespawn;
            life.CheckpointRestoring += onLateRestore;
            life.Die();
            life.RespawnImmediately();
            if (!Check(life.IsExitProtected && !life.IsRespawning && life.DeathCount == terminalDeaths &&
                lateDeathEvents == 0 && lateRestoreEvents == 0,
                "Same-frame death and direct checkpoint restore are rejected after exit")) yield break;
            flow.SetMenuBlocked(true);
            flow.SetMenuBlocked(false);
            flow.RestartFromCheckpoint();
            player.SetAutomationInputOverride(Vector2.right,jumpPressed:true,dashPressed:true);
            if (!Check(Time.timeScale == 0f && !player.ControlsEnabled && body.linearVelocity.sqrMagnitude < .0001f,
                "Portal freezes physics/input even after pause-resume")) yield break;
            if (chapterIndex == 3)
            {
                yield return new WaitForSecondsRealtime(1.2f);
                if (!Check(flow.IsVictory && !flow.IsTransitioning, "King goal wins only final chapter")) yield break;
                if (!Check(Time.timeScale == 0f && Mathf.Abs(Time.time-terminalWorldTime) < .0001f &&
                    life.DeathCount == terminalDeaths && !life.IsRespawning && !player.ControlsEnabled &&
                    Vector2.Distance(body.position,terminalPosition) < .001f && body.linearVelocity.sqrMagnitude < .0001f,
                    "Victory remains frozen/alive for 1.2 realtime seconds; checkpoint cannot relocate winner")) yield break;
                Goal king = FindFirstObjectByType<Goal>();
                Camera view = Camera.main;
                Bounds kingBounds = king.GetComponent<Collider2D>().bounds;
                Vector3 bottom = view.WorldToViewportPoint(kingBounds.min);
                Vector3 top = view.WorldToViewportPoint(kingBounds.max);
                if (!Check(bottom.x >= 0f && bottom.y >= 0f && top.x <= 1f && top.y <= 1f,
                    "Victory camera includes the complete king")) yield break;
                Debug.Log("PIRATE_CAMPAIGN_TERMINAL_FREEZE_PASS victoryRealtime=1.2 pauseResumeFrozen=True deathsStable=True inputBlocked=True fullTraversalProof=False");
            }
            else
            {
                yield return new WaitForSecondsRealtime(.5f);
                if (!Check(flow.IsTransitioning && !flow.IsVictory && Time.timeScale == 0f &&
                    Mathf.Abs(Time.time-terminalWorldTime) < .0001f && !player.ControlsEnabled &&
                    life.DeathCount == terminalDeaths && Vector2.Distance(body.position,terminalPosition) < .001f,
                    "Transition pending: world, checkpoint and digital input remain blocked")) yield break;
            }
            if (!Check(life.IsExitProtected && !life.IsRespawning && !life.IsSnared &&
                life.DeathCount == terminalDeaths && lateDeathEvents == 0 && lateRespawnEvents == 0 && lateRestoreEvents == 0 &&
                Vector2.Distance(body.position,terminalPosition) < .001f && body.simulated &&
                player.GetComponent<Collider2D>().enabled &&
                terminalRenderers.Select((renderer,index) => renderer != null && renderer.enabled == terminalVisibility[index]).All(value => value),
                "Late terminal hit preserves death state, body, position and renderer visibility")) yield break;
            if (!Check(CaptureSaveInputs(flow,life,abilities) == terminalSaveInputs,
                "Late terminal hit preserves serialized campaign save inputs and emits no autosave death event")) yield break;
            life.Died -= onLateDeath;
            life.Respawned -= onLateRespawn;
            life.CheckpointRestoring -= onLateRestore;
            Debug.Log($"PIRATE_CAMPAIGN_EXIT_PROTECTION_PASS chapter={chapterIndex} sameFrameDeathRejected=True directRespawnRejected=True " +
                "renderersStable=True saveInputsStable=True deathEvents=0 fixtureDiskWrite=False fullTraversalProof=False");
            player.ClearAutomationInputOverride();
        }
        PirateGameFlow finishedFlow = FindFirstObjectByType<PirateGameFlow>();
        finishedFlow.RestartWithSeed(PirateCampaignSession.Seed);
        PirateGameFlow newRunFlow = null;
        float restartDeadline = Time.realtimeSinceStartup + 30f;
        while (Time.realtimeSinceStartup < restartDeadline)
        {
            newRunFlow = FindFirstObjectByType<PirateGameFlow>();
            if (newRunFlow != null && newRunFlow.IsInitialized && newRunFlow.ChapterIndex == 0) break;
            yield return null;
        }
        if (!Check(newRunFlow != null && newRunFlow.IsInitialized && newRunFlow.ChapterIndex == 0 &&
            !newRunFlow.IsVictory && !newRunFlow.IsTransitioning && Time.timeScale == 1f,
            "Confirmed new expedition exits terminal state and loads Dock")) yield break;
        PlayerMovement restartedPlayer = FindFirstObjectByType<PlayerMovement>();
        PlayerLife restartedLife = restartedPlayer.GetComponent<PlayerLife>();
        if (!Check(!restartedLife.IsExitProtected && restartedPlayer.ControlsEnabled && restartedLife.DeathCount == 0,
            "New expedition removes exit protection and resets death count")) yield break;
        Vector3 restartedCheckpoint = restartedLife.CheckpointPosition;
        restartedLife.Die();
        if (!Check(restartedLife.IsRespawning && restartedLife.DeathCount == 1,
            "Normal death is accepted after starting a new expedition")) yield break;
        yield return new WaitForSecondsRealtime(.6f);
        if (!Check(!restartedLife.IsRespawning && !restartedLife.IsExitProtected && restartedLife.DeathCount == 1 &&
            restartedPlayer.ControlsEnabled && Vector2.Distance(restartedPlayer.transform.position,restartedCheckpoint) < .2f,
            "Ordinary checkpoint respawn works after terminal-protection reset")) yield break;
        Debug.Log("PIRATE_CAMPAIGN_EXIT_PROTECTION_RESET_PASS confirmedNewRun=True normalDeath=True normalRespawn=True");
        if (!Check(PirateTestModalAcknowledger.InvariantFailures == 0, "All acknowledged tutorials froze input/time")) yield break;
        Debug.Log($"PIRATE_CAMPAIGN_INTEGRATION_SUCCESS scenes=4 checks={checks} fullTraversalProof=False");
        Application.Quit(0);
    }

    private bool Check(bool condition,string label)
    {
        checks++;
        if (condition) return true;
        failed = true;
        Debug.LogError("PIRATE_CAMPAIGN_INTEGRATION_FAILED " + label);
        Application.Quit(2);
        return false;
    }

    private static string CaptureSaveInputs(PirateGameFlow flow,PlayerLife life,PlayerAbilities abilities)
    {
        Vector3 checkpoint = life.CheckpointPosition;
        PirateSaveData snapshot = new PirateSaveData {
            seed = PirateCampaignSession.Seed,
            chapter = flow.IsTransitioning ? flow.ChapterIndex+1 : flow.ChapterIndex,
            upgrades = abilities.CaptureProgression().Select(upgrade => (int)upgrade).OrderBy(value => value).ToArray(),
            deaths = life.DeathCount,
            hasCheckpoint = !flow.IsTransitioning,
            checkpointX = checkpoint.x,checkpointY = checkpoint.y,
            layoutSignature = flow.IsTransitioning ? string.Empty : flow.Generator.Campaign.Signature(),
            completed = flow.IsVictory
        };
        return JsonUtility.ToJson(snapshot)+"|session="+PirateCampaignSession.Chapter+":"+PirateCampaignSession.Deaths+":"+
            string.Join(",",PirateCampaignSession.Earned.OrderBy(upgrade => (int)upgrade));
    }

    private void OnDestroy()
    {
        if (failed) Debug.LogError("Campaign integration terminated after failed assertion.");
    }

    private static string GetArgument(string name)
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length-1; i++) if (arguments[i] == name) return arguments[i+1];
        return null;
    }

    private static void Capture(string path)
    {
        Camera camera = Camera.main;
        if (camera == null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        RenderTexture target = new RenderTexture(1280,720,24);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        Texture2D pixels = new Texture2D(1280,720,TextureFormat.RGB24,false);
        camera.targetTexture = target; RenderTexture.active = target;
        camera.Render(); pixels.ReadPixels(new Rect(0,0,1280,720),0,0); pixels.Apply();
        camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
        File.WriteAllBytes(path,pixels.EncodeToPNG());
        Destroy(target); Destroy(pixels);
    }
}
