using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class NewAbilityTrialRegression : MonoBehaviour
{
    private const string Flag = "-pirateQuestAbilityTrialTest";
    private const string CannonFlag = "-pirateQuestCannonLandingTest";
    private const string SlideDoubleFlag = "-pirateQuestSlideDoubleTest";
    private const string SlideDoubleLegacyFlag = "-pirateQuestSlideDoubleLegacyPolicy";
    private const string HookEdgeFlag = "-pirateQuestHookEdgeTest";
    private const string SlideSpringFlag = "-pirateQuestSlideSpringTest";
    private const int CaseCount = 17;
    private bool cannonMode;
    private bool slideDoubleMode, legacySlidePolicy;
    private bool hookEdgeMode, slideSpringMode;
    private int ExpectedCaseCount => hookEdgeMode ? 6 : slideSpringMode || slideDoubleMode ? 2 : cannonMode ? 4 : CaseCount;
    private readonly Stack<IEnumerator> pending = new Stack<IEnumerator>();
    private readonly List<GameObject> fixtureObjects = new List<GameObject>();
    private readonly ContactPoint2D[] contacts = new ContactPoint2D[32];
    private bool finished;
    private float deadline;
    private int completedCases;
    private string evidence;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (!args.Contains(Flag) && !args.Contains(CannonFlag) && !args.Contains(SlideDoubleFlag) && !args.Contains(HookEdgeFlag) && !args.Contains(SlideSpringFlag)) return;
        if (FindFirstObjectByType<NewAbilityTrialRegression>() != null) return;
        new GameObject("Opt-in new ability physics fixtures").AddComponent<NewAbilityTrialRegression>();
    }

    private void Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        cannonMode = args.Contains(CannonFlag);
        slideDoubleMode = args.Contains(SlideDoubleFlag); legacySlidePolicy = args.Contains(SlideDoubleLegacyFlag);
        hookEdgeMode = args.Contains(HookEdgeFlag); slideSpringMode = args.Contains(SlideSpringFlag);
        int modeCount = (args.Contains(Flag) ? 1 : 0) + (cannonMode ? 1 : 0) + (slideDoubleMode ? 1 : 0) + (hookEdgeMode ? 1 : 0) + (slideSpringMode ? 1 : 0);
        if (modeCount != 1 || legacySlidePolicy && !slideDoubleMode || !PirateFrontEnd.IsAutomationRun)
        { Finish("Runner requires its explicit isolated automation flag."); return; }
        PirateTestModalAcknowledger.Install();
        deadline = Time.realtimeSinceStartup + 300f;
        pending.Push(RunAll());
        StartCoroutine(Advance());
    }

    private void Update()
    {
        if (!finished && deadline > 0f && Time.realtimeSinceStartup > deadline)
        {
            StopAllCoroutines();
            Finish("300-second realtime watchdog expired.");
        }
    }

    private IEnumerator Advance()
    {
        while (pending.Count > 0 && !finished)
        {
            object current = null;
            string failure = null;
            try
            {
                IEnumerator iterator = pending.Peek();
                if (!iterator.MoveNext())
                { pending.Pop(); (iterator as IDisposable)?.Dispose(); continue; }
                current = iterator.Current;
            }
            catch (Exception exception) { failure = exception.GetType().Name + ": " + exception.Message; }
            if (failure != null) { Finish(failure); yield break; }
            if (current is IEnumerator child) pending.Push(child); else yield return current;
        }
        if (!finished) Finish(completedCases == ExpectedCaseCount ? null : "Not all explicit fixtures completed.");
    }

    private void Finish(string error)
    {
        if (finished) return;
        finished = true;
        while (pending.Count > 0)
        {
            try { (pending.Pop() as IDisposable)?.Dispose(); }
            catch (Exception exception) { error = (error ?? "") + " Cleanup: " + exception.Message; }
        }
        ClearFixture();
        string prefix = (hookEdgeMode ? "PIRATE_HOOK_EDGE_" : slideSpringMode ? "PIRATE_SLIDE_SPRING_" : slideDoubleMode ? (legacySlidePolicy ? "PIRATE_SLIDE_DOUBLE_LEGACY_" : "PIRATE_SLIDE_DOUBLE_") :
            cannonMode ? "PIRATE_CANNON_LANDING_" : "PIRATE_ABILITY_TRIAL_") + (error == null ? "SUCCESS" : "FAILED");
        Debug.Log($"{prefix} cases={completedCases}/{ExpectedCaseCount} error={error} fixtureRelocation=True " +
            "actualProductionPhysics=True fullRouteProof=False nativeKeyboardProof=False arbitrarySpeedProof=False");
        Application.Quit(error == null ? 0 : 11);
    }

    private IEnumerator RunAll()
    {
        PirateGameFlow flow = null;
        PlayerMovement player = null;
        for (int frame = 0; frame < 600; frame++)
        {
            flow = FindFirstObjectByType<PirateGameFlow>();
            player = FindFirstObjectByType<PlayerMovement>();
            if (flow != null && flow.IsInitialized && player != null && player.ControlsEnabled && Time.timeScale == 1f) break;
            yield return null;
        }
        Require(flow != null && flow.IsInitialized && player != null && player.ControlsEnabled && Time.timeScale == 1f,
            "No live initialized unpaused player within 600 frames.");
        int savedChapter = flow.ChapterIndex;
        float savedMoveSpeed = player.MoveSpeed;
        Require(savedMoveSpeed == PirateMovementProfile.ForChapter(savedChapter),
            "The source scene must use its real chapter profile before testing another chapter's rooms.");
        string[] args = Environment.GetCommandLineArgs();
        int evidenceIndex = Array.IndexOf(args, "-pirateQuestAbilityTrialEvidence");
        if (evidenceIndex >= 0)
        {
            Require(evidenceIndex + 1 < args.Length && Path.IsPathRooted(args[evidenceIndex + 1]), "Evidence needs an absolute directory.");
            evidence = Path.GetFullPath(args[evidenceIndex + 1]);
            Require(!Directory.Exists(evidence) || !Directory.EnumerateFileSystemEntries(evidence).Any(), "Evidence directory must be new or empty.");
            Directory.CreateDirectory(evidence);
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        PlayerGrapple grapple = player.GetComponent<PlayerGrapple>();
        Vector2 savedPosition = body.position, savedVelocity = body.linearVelocity;
        Vector3 checkpoint = life.CheckpointPosition;
        PirateUpgrade[] upgrades = abilities.CaptureProgression();
        int deaths = life.DeathCount, sessionDeaths = PirateCampaignSession.Deaths;
        bool protection = life.IsExitProtected;
        float savedCapture = Time.captureDeltaTime;
        int savedFrameRate = Application.targetFrameRate, savedVsync = QualitySettings.vSyncCount;
        bool savedBackground = Application.runInBackground;
        Camera camera = Camera.main;
        CameraFollow follow = camera != null ? camera.GetComponent<CameraFollow>() : null;
        bool followEnabled = follow != null && follow.enabled;
        Vector3 cameraPosition = camera != null ? camera.transform.position : Vector3.zero;
        float cameraSize = camera != null ? camera.orthographicSize : 0f;
        try
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = 120;
            Time.captureDeltaTime = Time.fixedDeltaTime;
            player.ClearAutomationInputOverride(); grapple.ClearAutomationInputOverride(); abilities.ClearAutomationInputOverride();
            if (follow != null) follow.enabled = false;
            if (hookEdgeMode)
            {
                Debug.Log("PIRATE_HOOK_EDGE_SCOPE cases=6 seed=20260918 crownSlideRing=True crownHighHook=True crownCutHighHook=True " +
                    "fixtureRelocation=True preparedEquipment=True sharedFullRouteDriveHook=True normalInputOverrides=True " +
                    "directAttach=False assignedLaunchVelocity=False hazardsDisabled=False roomEnemyPopulationReplicated=False chapterTideReplicated=False fullRouteProof=False");
                yield return RunMixedEdgeCase(player, "HookEdge-CrownSlide-forward", "crown.slide-ring-launch", 3, 1);
                yield return RunMixedEdgeCase(player, "HookEdge-CrownSlide-mirrored", "crown.slide-ring-launch", 3, -1);
                yield return RunMixedEdgeCase(player, "HookEdge-CrownVault-forward", "crown.high-hook-vault", 3, 1);
                yield return RunMixedEdgeCase(player, "HookEdge-CrownVault-mirrored", "crown.high-hook-vault", 3, -1);
                yield return RunMixedEdgeCase(player, "HookEdge-CrownCut-forward", "crown.cut-high-hook", 3, 1);
                yield return RunMixedEdgeCase(player, "HookEdge-CrownCut-mirrored", "crown.cut-high-hook", 3, -1);
            }
            else if (slideSpringMode)
            {
                Debug.Log("PIRATE_SLIDE_SPRING_SCOPE cases=2 seed=20260918 gardenSlideSpring=True actualAction=Spring " +
                    "fixtureRelocation=True preparedEquipment=True sharedFullRouteSpringController=True normalInputOverrides=True " +
                    "directBounce=False assignedLaunchVelocity=False hazardsDisabled=False roomEnemyPopulationReplicated=False chapterTideReplicated=False fullRouteProof=False");
                yield return RunMixedEdgeCase(player, "SlideSpring-forward", "garden.slide-spring-transfer", 2, 1, true);
                yield return RunMixedEdgeCase(player, "SlideSpring-mirrored", "garden.slide-spring-transfer", 2, -1, true);
            }
            else if (slideDoubleMode)
            {
                Debug.Log($"PIRATE_SLIDE_DOUBLE_SCOPE cases=2 seed=20260918 chapter=2 scenario=garden.slide-double-balcony legacyPolicy={legacySlidePolicy} " +
                    "generatedRoomColliders=True actualSpikes=True fixtureRelocation=True preparedEquipment=True " +
                    "inputOverrides=True assignedLaunchVelocity=False hazardsDisabled=False fullRouteProof=False");
                yield return RunSlideDoubleCase(player, "SlideDouble-forward", 1);
                yield return RunSlideDoubleCase(player, "SlideDouble-mirrored", -1);
            }
            else if (cannonMode)
            {
                Debug.Log("PIRATE_CANNON_LANDING_SCOPE cases=4 seed=42 actualGeneratedCannonSelection=True dockDownstepRaisedNeighbour=True arsenalRunningCrossing=True " +
                    "generatedColliders=True activeShooting=True actualBodyHitbox=True fixtureRelocation=True " +
                    "inputOverrides=True assignedLaunchVelocity=False hazardsDisabled=False invulnerability=False fullRouteProof=False");
                yield return RunCannonLandingCase(player, "CannonLanding-forward", 1);
                yield return RunCannonLandingCase(player, "CannonLanding-mirrored", -1);
                yield return RunCannonLandingCase(player, "CannonArsenal-forward", 1, true);
                yield return RunCannonLandingCase(player, "CannonArsenal-mirrored", -1, true);
            }
            else
            {
            Debug.Log($"PIRATE_ABILITY_TRIAL_SCOPE cases={CaseCount} generatedRegionColliders=True actualSpikeCallbacks=True " +
                "fixtureRelocation=True inputOverrides=True assignedLaunchVelocity=False directBounce=False " +
                "hazardsDisabled=False fullRouteProof=False nativeKeyboardProof=False backgroundsNotReplicated=True");
            yield return RunCase(player, "SpringDrop-runup-forward", "arsenal.spring-drop-chute", 1, 1, -.8f);
            yield return RunCase(player, "SpringDrop-runup-mirrored", "arsenal.spring-drop-chute", 1, -1, -.8f);
            yield return RunCase(player, "SpringDrop-center-rest", "arsenal.spring-drop-chute", 1, 1, 0f);
            yield return RunCase(player, "DoubleWindow-forward", "crown.double-window", 3, 1, 0f);
            yield return RunCase(player, "DoubleWindow-mirrored", "crown.double-window", 3, -1, 0f);
            yield return RunCase(player, "DoubleWide-forward", "crown.double-wide-vault", 3, 1, 0f);
            yield return RunCase(player, "DoubleWide-mirrored", "crown.double-wide-vault", 3, -1, 0f);
            yield return RunCase(player, "SpringReturn-forward", "arsenal.spring-return-balcony", 1, 1, 0f);
            yield return RunCase(player, "SpringReturn-mirrored", "arsenal.spring-return-balcony", 1, -1, 0f);
            yield return RunCase(player, "SpringRecharge-forward", "arsenal.spring-recharge-relay", 1, 1, 0f);
            yield return RunCase(player, "SpringRecharge-mirrored", "arsenal.spring-recharge-relay", 1, -1, 0f);
            yield return RunCase(player, "SlideRefuges-forward", "crown.slide-refuges", 3, 1, 0f);
            yield return RunCase(player, "SlideRefuges-mirrored", "crown.slide-refuges", 3, -1, 0f);
            yield return RunCase(player, "SlideDescending-forward", "crown.slide-descending-terraces", 3, 1, 0f);
            yield return RunCase(player, "SlideDescending-mirrored", "crown.slide-descending-terraces", 3, -1, 0f);
            yield return RunCase(player, "SlideLaunch-forward", "crown.slide-launch-step", 3, 1, 0f);
            yield return RunCase(player, "SlideLaunch-mirrored", "crown.slide-launch-step", 3, -1, 0f);
            }
        }
        finally
        {
            ClearFixture(); Time.timeScale = 1f; player.SetModalInputBlocked(false);
            life.SetExitProtected(false); life.SetCheckpoint(checkpoint);
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.RestoreProgression(upgrades); abilities.ClearAutomationInputOverride();
            grapple.ClearAutomationInputOverride(); player.ClearAutomationInputOverride(); player.ResetMotion();
            player.ConfigureChapterSpeed(savedChapter);
            body.position = savedPosition; player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity; player.SetControlsEnabled(true);
            life.SetDeathCount(deaths); life.SetExitProtected(protection); PirateCampaignSession.Deaths = sessionDeaths;
            Time.captureDeltaTime = savedCapture; Application.targetFrameRate = savedFrameRate;
            QualitySettings.vSyncCount = savedVsync; Application.runInBackground = savedBackground;
            if (camera != null) { camera.transform.position = cameraPosition; camera.orthographicSize = cameraSize; }
            if (follow != null) follow.enabled = followEnabled;
            Physics2D.SyncTransforms();
            Require(player.MoveSpeed == savedMoveSpeed, "The nested fixtures did not restore the original chapter speed.");
            Debug.Log($"PIRATE_TRIAL_MOVEMENT_RESTORED chapter={savedChapter} speed={player.MoveSpeed:F2} originalSpeed={savedMoveSpeed:F2}");
        }
    }

    private static void ApplyFixtureChapterSpeed(PlayerMovement player, int chapter, string label)
    {
        float previous = player.MoveSpeed;
        player.ConfigureChapterSpeed(chapter);
        Require(player.MoveSpeed == PirateMovementProfile.ForChapter(chapter), "Wrong physical fixture profile: " + label);
        Debug.Log($"PIRATE_TRIAL_MOVEMENT_PROFILE case={label} chapter={chapter} previousSpeed={previous:F2} " +
            $"speed={player.MoveSpeed:F2} appliedBeforeGeneration=True elapsedTimeScaling=False");
    }

    private IEnumerator RunMixedEdgeCase(PlayerMovement player, string label, string scenarioId, int chapter, int mirror, bool springTransfer = false)
    {
        ClearFixture();
        ApplyFixtureChapterSpeed(player, chapter, label);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        PlayerGrapple grapple = player.GetComponent<PlayerGrapple>();
        CampaignLayout map = CampaignLayout.Create(20260918, chapter, player.JumpLaunchSpeed, player.GravityStrength, Time.fixedDeltaTime, player.MoveSpeed);
        Require(map.GeometryValid, "Mixed-edge source map is invalid: " + string.Join(" | ", map.ValidationErrors));
        CampaignLayout.Region room = map.Rooms.Find(item => item.ScenarioId == scenarioId);
        Require(room != null, "Mixed-edge fixture scenario is absent: " + scenarioId);
        string marker = springTransfer ? "PIRATE_SLIDE_SPRING" : "PIRATE_HOOK_EDGE";
        CampaignLayout.TraversalAction requiredAction = springTransfer ? CampaignLayout.TraversalAction.Spring : CampaignLayout.TraversalAction.Grapple;
        int hookNode = map.Route.FindIndex(item => item.RoomIndex == room.Id && item.Action == requiredAction);
        Require(hookNode > 0, "Mixed-edge fixture requires the actual generated action " + requiredAction + ", not its scenario name.");
        bool slideApproach = map.Route[hookNode - 1].Action == CampaignLayout.TraversalAction.SaberSlide;
        Require(!springTransfer || slideApproach, "SlideSpring fixture must begin before an actual SaberSlide node.");
        bool cutApproach = scenarioId == "crown.cut-high-hook";
        int firstTarget = slideApproach ? hookNode - 1 : cutApproach ?
            map.Route.FindIndex(item => item.RoomIndex == room.Id && item.Action == CampaignLayout.TraversalAction.Saber) : hookNode;
        Require(firstTarget > 0 && firstTarget <= hookNode, "Mixed-edge fixture has no valid approach sequence.");
        int startNode = firstTarget - 1;
        Vector2 sourceStart = map.Route[startNode].FeetPosition;
        Vector2 origin = new Vector2(15000f + completedCases * 200f, 15000f);
        Vector2 Point(Vector2 source) => origin + new Vector2((source.x - sourceStart.x) * mirror, source.y - sourceStart.y);
        Rect Rectangle(Rect source) => new Rect(Point(source.center) - source.size * .5f, source.size);
        int groundLayer = 0;
        for (int bit = 0; bit < 32; bit++) if ((player.GroundLayer.value & (1 << bit)) != 0) { groundLayer = bit; break; }
        string sourceSignature = map.Signature();
        var supports = new Dictionary<int, BoxCollider2D>();
        for (int i = 0; i < map.Platforms.Count; i++)
        {
            CampaignLayout.Platform platform = map.Platforms[i];
            if (platform.RoomIndex == room.Id || i == map.Route[startNode].PlatformIndex)
                supports.Add(i, MakeSupport(label + " actual platform " + i, Rectangle(platform.Bounds), platform.OneWay, groundLayer, chapter));
        }
        Rect expandedRoom = Rect.MinMaxRect(room.Bounds.xMin - 1f, room.Bounds.yMin - 1f, room.Bounds.xMax + 1f, room.Bounds.yMax + 1f);
        int solidCount = 0, spikeCount = 0, anchorCount = 0;
        var ropes = new List<SaberCuttable>();
        CampaignLayout.Spawn slideBed = null, springBed = null;
        foreach (RectInt solid in map.Solids)
        {
            Rect bounds = new Rect(solid.x, solid.y, solid.width, solid.height);
            if (expandedRoom.Overlaps(bounds)) MakeSupport(label + " actual solid " + solidCount++, Rectangle(bounds), false, groundLayer, chapter);
        }
        foreach (CampaignLayout.Spawn spawn in map.Spawns)
        {
            bool inRoom = spawn.NodeIndex >= room.FirstRouteNode && spawn.NodeIndex <= room.LastRouteNode;
            if (spawn.Kind == CampaignLayout.SpawnKind.Anchor && spawn.NodeIndex == hookNode - 1)
            {
                GameObject obj = PirateWorldArt.Create(label + " actual ring " + anchorCount++, PirateArtKind.Anchor, Point(spawn.Position), spawn.Size);
                fixtureObjects.Add(obj);
                BoxCollider2D trigger = obj.AddComponent<BoxCollider2D>(); trigger.size = spawn.Size; trigger.isTrigger = true;
                obj.AddComponent<HookAnchor>().Configure(true, spawn.Value);
            }
            else if (spawn.Kind == CampaignLayout.SpawnKind.Spikes && (inRoom || slideApproach && spawn.NodeIndex == firstTarget - 1))
            {
                GameObject obj = PirateWorldArt.Create(label + " actual spikes " + spikeCount++, PirateArtKind.Spikes, Point(spawn.Position), spawn.Size);
                fixtureObjects.Add(obj);
                BoxCollider2D trigger = obj.AddComponent<BoxCollider2D>(); trigger.size = spawn.Size; trigger.isTrigger = true;
                obj.AddComponent<InstantKillHazard>().Configure(HazardKind.Spikes);
                if (slideApproach && spawn.NodeIndex == firstTarget - 1) slideBed = spawn;
                if (springTransfer && spawn.NodeIndex == hookNode - 1) springBed = spawn;
            }
            else if (spawn.Kind == CampaignLayout.SpawnKind.RopeGate && cutApproach &&
                (inRoom || spawn.NodeIndex == firstTarget - 1))
            {
                GameObject obj = PirateWorldArt.Create(label + " actual rope " + ropes.Count, PirateArtKind.Rope, Point(spawn.Position), spawn.Size);
                fixtureObjects.Add(obj); obj.layer = groundLayer;
                BoxCollider2D blocker = obj.AddComponent<BoxCollider2D>(); blocker.size = spawn.Size; blocker.isTrigger = false;
                SaberCuttable rope = obj.AddComponent<SaberCuttable>(); rope.Configure(); ropes.Add(rope);
            }
        }
        Require(anchorCount == (springTransfer ? 0 : 1) && supports.ContainsKey(map.Route[hookNode].PlatformIndex) &&
            (!slideApproach || slideBed != null) && (!springTransfer || springBed != null) &&
            (!cutApproach || ropes.Count > 0), "Mixed-edge fixture omitted required physical content.");
        if (springTransfer)
        {
            var availableAtStart = typeof(CampaignLayout).GetMethod("AvailableAtChapterStart",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Require(availableAtStart != null, "Generated chapter progression predicate is unavailable.");
            bool AllowedBefore(PirateUpgrade ability, int node) => (bool)availableAtStart.Invoke(map, new object[] { ability }) ||
                map.Spawns.Any(item => item.Kind == CampaignLayout.SpawnKind.Upgrade && item.Ability == ability && item.NodeIndex <= node);
            Require(map.Route[hookNode].RequiredAbility == PirateUpgrade.SpringLeg && AllowedBefore(PirateUpgrade.SpringLeg, hookNode) &&
                AllowedBefore(PirateUpgrade.Saber2, firstTarget),
                "SlideSpring prepared equipment exceeds its actual generated progression.");
        }
        Rect reboundBounds = springBed != null ? Rectangle(new Rect(springBed.Position - springBed.Size * .5f, springBed.Size)) : default;
        float bedExit = 0f, slideDirection = 0f;
        if (slideApproach)
        {
            slideDirection = Mathf.Sign(Point(map.Route[firstTarget].FeetPosition).x - origin.x);
            Rect bedBounds = Rectangle(new Rect(slideBed.Position - slideBed.Size * .5f, slideBed.Size));
            bedExit = slideDirection > 0f ? bedBounds.xMax : bedBounds.xMin;
        }
        foreach (CampaignLayout.Platform platform in map.Platforms) platform.Bounds = Rectangle(platform.Bounds);
        foreach (CampaignLayout.RouteNode node in map.Route) node.FeetPosition = Point(node.FeetPosition);
        abilities.ResetProgression(); abilities.Apply(springTransfer ? PirateUpgrade.SpringLeg : PirateUpgrade.Hook2, false);
        if (slideApproach) abilities.Apply(PirateUpgrade.Saber2, false);
        else if (cutApproach) abilities.Apply(PirateUpgrade.Saber1, false);
        abilities.ResetTransientState(); abilities.SetAutomationSlide(false); grapple.ClearAutomationInputOverride();
        player.ResetMotion(); player.SetControlsEnabled(true); life.SetExitProtected(false);
        Vector2 start = origin + Vector2.up * (capsule.bounds.extents.y + .035f);
        body.position = start; player.transform.position = start; life.SetCheckpoint(start);
        player.SetAutomationInputOverride(Vector2.zero); Physics2D.SyncTransforms();
        GameObject contextObject = new GameObject(label + " inactive ordinary traversal context");
        contextObject.SetActive(false); fixtureObjects.Add(contextObject);
        CampaignRoutePlaytest hookController = contextObject.AddComponent<CampaignRoutePlaytest>(); hookController.enabled = false;
        if (springTransfer) hookController.ConfigureSpringFixture(map, hookNode, player);
        else hookController.ConfigureHookFixture(map, hookNode, player);
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector2 final = map.Route[hookNode].FeetPosition;
            Vector2 centre = (origin + final) * .5f + Vector2.up * 3f;
            camera.transform.position = new Vector3(centre.x, centre.y, camera.transform.position.z);
            camera.orthographicSize = Mathf.Max(10f, Mathf.Abs(final.x - origin.x) / (2f * camera.aspect) + 3f);
        }
        var step = new WaitForFixedUpdate();
        for (int frame = 0; frame < 30 && !player.IsGrounded; frame++) yield return step;
        Require(player.IsGrounded && body.linearVelocity.sqrMagnitude < .05f, "Mixed-edge fixture did not settle at its actual approach support.");
        int deathsBefore = life.DeathCount, targetIndex = firstTarget, stable = 0, slideFrames = 0, jumpInputs = 0, attachInputs = 0;
        int attachedFrames = 0, ropeBoosts = 0, attackInputs = 0, completedLandings = 0, frameAtTarget = 0;
        int springBounces = 0, cleanLipBeforeBounce = 0, separateBedBounces = 0;
        bool firstJump = false, pendingRopeJump = false, passed = false;
        float maxFeet = capsule.bounds.min.y, bounceVelocity = 0f, bounceFeet = float.NaN;
        Action bounced = () => {
            springBounces++; bounceVelocity = body.linearVelocity.y; bounceFeet = capsule.bounds.min.y;
            if (completedLandings > 0) cleanLipBeforeBounce++;
            bool onSeparateBed = capsule.bounds.max.x > reboundBounds.xMin && capsule.bounds.min.x < reboundBounds.xMax &&
                Mathf.Abs(bounceFeet - reboundBounds.yMax) < .55f;
            if (onSeparateBed) separateBedBounces++;
            Debug.Log($"{marker}_BOUNCE case={label} count={springBounces} position={body.position} velocity={body.linearVelocity} " +
                $"feet={bounceFeet:F4} completedLandings={completedLandings} separateGeneratedBed={onSeparateBed} actualProductionEvent=True directBounce=False");
        };
        if (springTransfer) abilities.SpringBounced += bounced;
        Debug.Log($"{marker}_BEGIN case={label} seed=20260918 chapter={chapter} scenario={scenarioId} actualAction={requiredAction} " +
            $"sourceSignature={sourceSignature} mirrored={mirror < 0} firstTarget={firstTarget} hookNode={hookNode} start={start} " +
            $"platforms={supports.Count} solids={solidCount} spikes={spikeCount} anchors={anchorCount} ropes={ropes.Count} directAttach=False");
        try
        {
        for (int frame = 0; frame < 1200; frame++)
        {
            Require(life.DeathCount == deathsBefore && !life.IsRespawning, label + " died through real hazard contact.");
            Require(!life.IsExitProtected && !life.IsModalProtected && Time.timeScale == 1f && !contextObject.activeSelf,
                "Mixed-edge fixture lost active physics or its inactive controller-isolation contract.");
            float feet = capsule.bounds.min.y; maxFeet = Mathf.Max(maxFeet, feet);
            if (abilities.IsSpikeSliding) slideFrames++;
            if (grapple.IsAttached) attachedFrames++;
            if (pendingRopeJump && !grapple.IsAttached && body.linearVelocity.y > PlayerGrapple.AnchorJumpLaunchSpeed - 2f && grapple.SpentAnchorCount > 0)
            { ropeBoosts++; pendingRopeJump = false; }
            CampaignLayout.RouteNode target = map.Route[targetIndex];
            BoxCollider2D landing = supports[target.PlatformIndex];
            int sampledContactNode = targetIndex;
            bool onTarget = OnFixtureSupport(player, body, capsule, landing);
            stable = onTarget ? stable + 1 : 0;
            if (stable >= (targetIndex == hookNode ? 3 : 2))
            {
                completedLandings++;
                Debug.Log($"{marker}_LANDING case={label} node={targetIndex} actualSupport=True stable={stable} " +
                    $"position={body.position} velocity={body.linearVelocity} slideFrames={slideFrames} actualCuts={ropes.Count(item => item.IsCut)}");
                if (targetIndex == hookNode) { passed = true; break; }
                targetIndex++; target = map.Route[targetIndex]; landing = supports[target.PlatformIndex];
                stable = frameAtTarget = 0; firstJump = false;
                if (targetIndex == hookNode)
                {
                    if (springTransfer) hookController.ConfigureSpringFixture(map, hookNode, player);
                    else hookController.ConfigureHookFixture(map, hookNode, player);
                }
            }
            bool jump = false, attach = false, hold = false, slide = false, attack = false;
            int phase = -1;
            float axis = CampaignRoutePlaytest.OrdinaryLandingAxis(player, body, target.FeetPosition.x);
            if (targetIndex == hookNode)
            {
                CampaignRoutePlaytest.HookFixtureInput input = springTransfer ?
                    hookController.StepSpringFixture(springBounces > 0) : hookController.StepHookFixture();
                axis = input.Axis; jump = input.Jump; attach = input.Attach; hold = input.Hold; phase = input.Phase;
                if (jump && grapple.IsAttached) pendingRopeJump = true;
            }
            else if (target.Action == CampaignLayout.TraversalAction.SaberSlide)
            {
                slide = CampaignRoutePlaytest.StillCrossingSlideBed(body.position.x, capsule.bounds.extents.x, bedExit, slideDirection);
                if (slide) axis = slideDirection;
                if (CampaignRoutePlaytest.ShouldSettleSlideExit(map, targetIndex) && !slide && !firstJump && player.IsGrounded &&
                    HasFixtureTopContact(body, capsule, landing)) { jump = true; firstJump = true; }
            }
            else if (target.Action == CampaignLayout.TraversalAction.Saber)
                attack = frameAtTarget % 18 == 0;
            else if (player.IsGrounded && !firstJump && target.FeetPosition.y > feet + .18f)
            { jump = true; firstJump = true; }
            if (jump) jumpInputs++;
            if (attach) attachInputs++;
            if (attack) { attackInputs++; abilities.SetAutomationAttackPressed(); }
            Debug.Log($"{marker}_FRAME case={label} frame={frame} node={targetIndex} phase={phase} position={body.position} " +
                $"velocity={body.linearVelocity} feet={feet:F4} axis={axis:F1} space={jump} rmbEdge={attach} rmbHeld={hold} F={attack} " +
                $"slide={slide} actualSliding={abilities.IsSpikeSliding} attached={grapple.IsAttached} ropeLength={grapple.RopeLength:F4} " +
                $"spentAnchors={grapple.SpentAnchorCount} ropeBoosts={ropeBoosts} springBounces={springBounces} grounded={player.IsGrounded} " +
                $"targetContact={onTarget} sampledContactNode={sampledContactNode} stable={stable}");
            if (feet < origin.y - 1.5f) break;
            abilities.SetAutomationSlide(slide); player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump);
            grapple.SetAutomationInputOverride(hold, attach);
            frameAtTarget++;
            yield return null;
        }
        }
        finally { if (springTransfer) abilities.SpringBounced -= bounced; }
        player.SetAutomationInputOverride(Vector2.zero); abilities.SetAutomationSlide(false); grapple.SetAutomationInputOverride(false, false);
        bool abilityContract = springTransfer ? springBounces == 1 && cleanLipBeforeBounce == 1 && separateBedBounces == 1 && bounceVelocity > 17f &&
            attachInputs == 0 && attachedFrames == 0 && !abilities.Has(PirateUpgrade.Hook2) && !abilities.Has(PirateUpgrade.DoubleJump) :
            attachedFrames > 0 && attachInputs > 0 && ropeBoosts > 0;
        bool contract = passed && completedLandings == hookNode - firstTarget + 1 && abilityContract &&
            (!slideApproach || slideFrames > 0) && (!cutApproach || attackInputs > 0 && ropes.All(item => item.IsCut));
        Debug.Log($"{marker}_CASE case={label} success={contract} actualFinalLanding={passed} " +
            $"completedLandings={completedLandings}/{hookNode - firstTarget + 1} slideFrames={slideFrames} jumpInputs={jumpInputs} " +
            $"attachInputs={attachInputs} attachedFrames={attachedFrames} ropeBoosts={ropeBoosts} attackInputs={attackInputs} actualCuts={ropes.Count(item => item.IsCut)}/{ropes.Count} " +
            $"springBounces={springBounces} cleanLipBeforeBounce={cleanLipBeforeBounce} separateBedBounces={separateBedBounces} bounceVelocity={bounceVelocity:F4} " +
            $"maxRise={maxFeet - origin.y:F4} deaths={life.DeathCount - deathsBefore} directAttach=False directBounce=False assignedVelocity=False");
        if (evidence != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            string path = Path.Combine(evidence, label + (contract ? "-pass.png" : "-fail.png"));
            ScreenCapture.CaptureScreenshot(path); yield return null; yield return null;
            Debug.Log(marker + "_CAPTURE requested=" + path + " pixelProof=False");
        }
        Require(contract && life.DeathCount == deathsBefore && !life.IsRespawning, label + " did not complete its actual ability/approach contract.");
        completedCases++;
    }

    private IEnumerator RunSlideDoubleCase(PlayerMovement player, string label, int mirror)
    {
        ClearFixture();
        ApplyFixtureChapterSpeed(player, 2, label);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        CampaignLayout map = CampaignLayout.Create(20260918, 2, player.JumpLaunchSpeed, player.GravityStrength, Time.fixedDeltaTime, player.MoveSpeed);
        Require(map.GeometryValid, "Slide-double source map is invalid.");
        CampaignLayout.Region room = map.Rooms.SingleOrDefault(item => item.ScenarioId == "garden.slide-double-balcony");
        Require(room != null, "Generated Garden slide-double scenario is missing or duplicated.");
        int[] slides = Enumerable.Range(room.FirstRouteNode, room.LastRouteNode - room.FirstRouteNode + 1)
            .Where(node => map.Route[node].Action == CampaignLayout.TraversalAction.SaberSlide).ToArray();
        Require(slides.Length == 1 && slides[0] > 0 && slides[0] + 1 <= room.LastRouteNode,
            "Garden slide-double requires exactly one slide followed by its high landing.");
        int lipIndex = slides[0], highIndex = lipIndex + 1;
        CampaignLayout.RouteNode approach = map.Route[lipIndex - 1], lipNode = map.Route[lipIndex], highNode = map.Route[highIndex];
        Require(highNode.Action == CampaignLayout.TraversalAction.DoubleJump,
            "Generated slide exit is no longer followed immediately by DoubleJump.");
        CampaignLayout.Spawn bed = map.Spawns.SingleOrDefault(item => item.Kind == CampaignLayout.SpawnKind.Spikes && item.NodeIndex == lipIndex - 1);
        Require(bed != null, "Mixed fixture omitted the actual approach spike bed.");
        Vector2 origin = new Vector2(15000f + completedCases * 200f, 15000f);
        Vector2 Point(Vector2 source) => origin + new Vector2((source.x - approach.FeetPosition.x) * mirror, source.y - approach.FeetPosition.y);
        Rect Rectangle(Rect source) => new Rect(Point(source.center) - source.size * .5f, source.size);
        int groundLayer = 0;
        for (int bit = 0; bit < 32; bit++) if ((player.GroundLayer.value & (1 << bit)) != 0) { groundLayer = bit; break; }
        var supports = new Dictionary<int, BoxCollider2D>();
        for (int i = 0; i < map.Platforms.Count; i++)
        {
            CampaignLayout.Platform platform = map.Platforms[i];
            if (platform.RoomIndex == room.Id)
                supports.Add(i, MakeSupport(label + " actual platform " + i, Rectangle(platform.Bounds), platform.OneWay, groundLayer, 2));
        }
        Rect expandedRoom = Rect.MinMaxRect(room.Bounds.xMin - 1f, room.Bounds.yMin - 1f, room.Bounds.xMax + 1f, room.Bounds.yMax + 1f);
        int solidCount = 0, spikeCount = 0;
        foreach (RectInt solid in map.Solids)
        {
            Rect bounds = new Rect(solid.x, solid.y, solid.width, solid.height);
            if (expandedRoom.Overlaps(bounds)) MakeSupport(label + " actual solid " + solidCount++, Rectangle(bounds), false, groundLayer, 2);
        }
        foreach (CampaignLayout.Spawn spawn in map.Spawns)
        {
            if (spawn.Kind != CampaignLayout.SpawnKind.Spikes || spawn.NodeIndex < room.FirstRouteNode || spawn.NodeIndex > room.LastRouteNode) continue;
            GameObject obj = PirateWorldArt.Create(label + " actual spikes " + spikeCount++, PirateArtKind.Spikes, Point(spawn.Position), spawn.Size);
            fixtureObjects.Add(obj);
            BoxCollider2D trigger = obj.AddComponent<BoxCollider2D>(); trigger.size = spawn.Size; trigger.isTrigger = true;
            obj.AddComponent<InstantKillHazard>().Configure(HazardKind.Spikes);
        }
        Require(supports.ContainsKey(approach.PlatformIndex) && supports.ContainsKey(lipNode.PlatformIndex) &&
            supports.ContainsKey(highNode.PlatformIndex) && spikeCount > 0, "Mixed fixture lacks a required generated collider.");
        abilities.ResetProgression(); abilities.Apply(PirateUpgrade.Saber2, false); abilities.Apply(PirateUpgrade.DoubleJump, false);
        abilities.ResetTransientState(); abilities.SetAutomationSlide(false);
        player.ResetMotion(); player.SetControlsEnabled(true); life.SetExitProtected(false);
        Vector2 start = origin + Vector2.up * (capsule.bounds.extents.y + .035f);
        body.position = start; player.transform.position = start; life.SetCheckpoint(start);
        player.SetAutomationInputOverride(Vector2.zero); Physics2D.SyncTransforms();
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector2 centre = (origin + Point(highNode.FeetPosition)) * .5f + Vector2.up * 2f;
            camera.transform.position = new Vector3(centre.x, centre.y, camera.transform.position.z); camera.orthographicSize = 9f;
        }
        var extraJumpsField = typeof(PlayerMovement).GetField("extraJumpsRemaining",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Require(extraJumpsField != null, "Read-only jump charge diagnostic field is unavailable.");
        var step = new WaitForFixedUpdate();
        for (int frame = 0; frame < 30 && !player.IsGrounded; frame++) yield return step;
        Require(player.IsGrounded && body.linearVelocity.sqrMagnitude < .05f, "Mixed fixture did not settle at the actual pre-slide support.");
        int deathsBefore = life.DeathCount, targetIndex = lipIndex, stable = 0, slideFrames = 0, jumpInputs = 0, observedJumps = 0;
        int completedLandings = 0, roofContactFrames = 0;
        bool firstJump = false, secondJump = false, pendingJump = false, passed = false;
        float direction = Mathf.Sign(Point(lipNode.FeetPosition).x - origin.x), maxFeet = capsule.bounds.min.y;
        Rect bedBounds = Rectangle(new Rect(bed.Position - bed.Size * .5f, bed.Size));
        float bedExit = direction > 0f ? bedBounds.xMax : bedBounds.xMin;
        Debug.Log($"PIRATE_SLIDE_DOUBLE_BEGIN case={label} seed={map.Seed} mirrored={mirror < 0} legacyPolicy={legacySlidePolicy} " +
            $"signature={map.Signature()} start={start} lip={supports[lipNode.PlatformIndex].bounds} target={supports[highNode.PlatformIndex].bounds} " +
            $"platforms={supports.Count} solids={solidCount} spikes={spikeCount} initialVelocity={body.linearVelocity}");
        for (int frame = 0; frame < 800; frame++)
        {
            Require(!life.IsRespawning && life.DeathCount == deathsBefore, label + " died in an actual hazard callback.");
            Require(!life.IsExitProtected && !life.IsModalProtected && Time.timeScale == 1f, "Mixed fixture lost its unprotected active-physics contract.");
            float feet = capsule.bounds.min.y;
            maxFeet = Mathf.Max(maxFeet, feet);
            if (abilities.IsSpikeSliding) slideFrames++;
            if (pendingJump && body.linearVelocity.y > 12f) { observedJumps++; pendingJump = false; }
            int count = capsule.GetContacts(contacts);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = contacts[i];
                if (!contact.enabled) continue;
                Vector2 normal = contact.normal;
                if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f) normal = -normal;
                if (normal.y < -.65f) roofContactFrames++;
            }
            CampaignLayout.RouteNode target = map.Route[targetIndex];
            BoxCollider2D landing = supports[target.PlatformIndex];
            int sampledContactNode = targetIndex;
            bool onTarget = OnFixtureSupport(player, body, capsule, landing);
            stable = onTarget ? stable + 1 : 0;
            if (stable >= (targetIndex == lipIndex ? 2 : 3))
            {
                completedLandings++;
                Debug.Log($"PIRATE_SLIDE_DOUBLE_LANDING case={label} node={targetIndex} position={body.position} velocity={body.linearVelocity} " +
                    $"actualSupport=True stable={stable} jumpInputs={jumpInputs} observedJumps={observedJumps} extraJumps={extraJumpsField.GetValue(player)}");
                if (targetIndex == highIndex) { passed = true; break; }
                Require(slideFrames > 0, "Mixed fixture reached its clean lip without actual spike sliding.");
                targetIndex = highIndex; target = highNode; landing = supports[target.PlatformIndex];
                stable = 0; firstJump = secondJump = false;
            }
            float axis = CampaignRoutePlaytest.OrdinaryLandingAxis(player, body, Point(target.FeetPosition).x);
            bool jump = false, slide = false;
            if (targetIndex == lipIndex)
            {
                slide = CampaignRoutePlaytest.StillCrossingSlideBed(body.position.x, capsule.bounds.extents.x, bedExit, direction);
                if (slide) axis = direction;
                bool settle = legacySlidePolicy || CampaignRoutePlaytest.ShouldSettleSlideExit(map, lipIndex);
                if (settle && !slide && !firstJump && player.IsGrounded && HasFixtureTopContact(body, capsule, landing))
                { jump = true; firstJump = true; }
            }
            else
            {
                if (!firstJump && player.IsGrounded) { jump = true; firstJump = true; }
                else if (firstJump && !secondJump && !player.IsGrounded && body.linearVelocity.y < 1.5f)
                { jump = true; secondJump = true; }
            }
            if (jump) { jumpInputs++; pendingJump = true; }
            Debug.Log($"PIRATE_SLIDE_DOUBLE_FRAME case={label} frame={frame} node={targetIndex} position={body.position} " +
                $"velocity={body.linearVelocity} feet={feet:F4} axis={axis:F1} space={jump} slide={slide} actualSliding={abilities.IsSpikeSliding} " +
                $"grounded={player.IsGrounded} targetContact={onTarget} sampledContactNode={sampledContactNode} stable={stable} inputs={jumpInputs} observed={observedJumps} " +
                $"extraJumps={extraJumpsField.GetValue(player)} roof={roofContactFrames}");
            if (feet < origin.y - 1.5f) break;
            abilities.SetAutomationSlide(slide);
            player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump);
            yield return null;
        }
        player.SetAutomationInputOverride(Vector2.zero); abilities.SetAutomationSlide(false);
        bool contract = passed && completedLandings == 2 && slideFrames > 0 && jumpInputs == 2 && observedJumps == 2;
        Debug.Log($"PIRATE_SLIDE_DOUBLE_CASE case={label} success={contract} legacyPolicy={legacySlidePolicy} actualFinalLanding={passed} " +
            $"completedLandings={completedLandings}/2 slideFrames={slideFrames} jumpInputs={jumpInputs} observedJumps={observedJumps} " +
            $"maxRise={maxFeet - origin.y:F4} roofContactFrames={roofContactFrames} deaths={life.DeathCount - deathsBefore}");
        if (evidence != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            string path = Path.Combine(evidence, label + (contract ? "-pass.png" : "-fail.png"));
            ScreenCapture.CaptureScreenshot(path); yield return null; yield return null;
            Debug.Log("PIRATE_SLIDE_DOUBLE_CAPTURE requested=" + path + " pixelProof=False");
        }
        Require(contract && life.DeathCount == deathsBefore && !life.IsRespawning,
            label + " did not complete the actual slide-to-double transition with two ordinary jumps.");
        completedCases++;
    }

    private bool HasFixtureTopContact(Rigidbody2D body, Collider2D capsule, Collider2D support)
    {
        int count = capsule.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contacts[i];
            if (!contact.enabled || contact.collider != support && contact.otherCollider != support) continue;
            Vector2 normal = contact.normal;
            if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f) normal = -normal;
            if (normal.y > .65f) return true;
        }
        return false;
    }

    private static bool TryFindCannonEncounter(CampaignLayout map, bool runningCrossing,
        out int approachNode, out int targetNode, out CampaignLayout.Spawn cannon)
    {
        approachNode = targetNode = -1;
        cannon = null;
        bool Ordinary(CampaignLayout.RouteNode node) => !node.RequiredAbility.HasValue && !node.SecondaryAbility.HasValue &&
            (node.Action == CampaignLayout.TraversalAction.Walk || node.Action == CampaignLayout.TraversalAction.Jump);
        for (int node = 2; node + 1 < map.Route.Count; node++)
        {
            CampaignLayout.RouteNode approach = map.Route[node - 2], previous = map.Route[node - 1],
                target = map.Route[node], next = map.Route[node + 1];
            if (!Ordinary(approach) || !Ordinary(previous) || !Ordinary(target) || !Ordinary(next) ||
                approach.RoomIndex != target.RoomIndex || previous.RoomIndex != target.RoomIndex || next.RoomIndex != target.RoomIndex) continue;
            float rise = target.FeetPosition.y - previous.FeetPosition.y;
            float neighbourRise = next.FeetPosition.y - target.FeetPosition.y;
            if (runningCrossing ? rise < -.05f || neighbourRise > .14f : rise >= -.2f || neighbourRise <= .14f) continue;
            float direction = Mathf.Sign(target.FeetPosition.x - previous.FeetPosition.x);
            if (direction == 0f || (previous.FeetPosition.x - approach.FeetPosition.x) * direction <= 0f ||
                (next.FeetPosition.x - target.FeetPosition.x) * direction <= 0f) continue;
            CampaignLayout.Spawn actual = map.Spawns.Find(spawn => spawn.Kind == CampaignLayout.SpawnKind.Cannon &&
                spawn.NodeIndex == node - 2 && Mathf.Abs(spawn.Position.x - target.FeetPosition.x - direction * .35f) < .01f &&
                Mathf.Abs(spawn.Position.y - target.FeetPosition.y - .6f) < .01f);
            if (actual == null) continue;
            approachNode = node - 2; targetNode = node; cannon = actual;
            return true;
        }
        return false;
    }

    private IEnumerator RunCannonLandingCase(PlayerMovement player, string label, int mirror, bool arsenal = false)
    {
        ClearFixture();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        int chapter = arsenal ? 1 : 0;
        ApplyFixtureChapterSpeed(player, chapter, label);
        CampaignLayout map = CampaignLayout.Create(42, chapter, player.JumpLaunchSpeed, player.GravityStrength, Time.fixedDeltaTime, player.MoveSpeed);
        Require(map.GeometryValid, "Cannon source map is invalid.");
        Require(TryFindCannonEncounter(map, arsenal, out int approachNode, out int targetNode, out CampaignLayout.Spawn spawn),
            arsenal ? "No generated ordinary running cannon crossing found." : "No generated cannon downstep with raised next ledge found.");
        int previousNode = targetNode - 1;
        CampaignLayout.RouteNode approach = map.Route[approachNode], previous = map.Route[previousNode], target = map.Route[targetNode];
        int adjacentPlatform = map.Route[targetNode + 1].PlatformIndex;
        Vector2 origin = new Vector2(15000f + completedCases * 200f, 15000f);
        Vector2 Point(Vector2 source) => origin + new Vector2((source.x - previous.FeetPosition.x) * mirror, source.y - previous.FeetPosition.y);
        Rect Rectangle(Rect source) => new Rect(Point(source.center) - source.size * .5f, source.size);
        Rect fixtureRegion = Rect.MinMaxRect(Mathf.Min(approach.FeetPosition.x, target.FeetPosition.x) - 5f,
            Mathf.Min(target.FeetPosition.y, approach.FeetPosition.y) - 4f,
            Mathf.Max(approach.FeetPosition.x, target.FeetPosition.x) + 5f,
            Mathf.Max(previous.FeetPosition.y, approach.FeetPosition.y) + 6f);
        var mapped = new List<CampaignLayout.Platform>();
        var supports = new Dictionary<int, BoxCollider2D>();
        int groundLayer = 0;
        for (int bit = 0; bit < 32; bit++) if ((player.GroundLayer.value & (1 << bit)) != 0) { groundLayer = bit; break; }
        for (int i = 0; i < map.Platforms.Count; i++)
        {
            CampaignLayout.Platform source = map.Platforms[i];
            var translated = new CampaignLayout.Platform { Bounds = Rectangle(source.Bounds), OneWay = source.OneWay,
                RoomIndex = source.RoomIndex, Optional = source.Optional };
            mapped.Add(translated);
            if (fixtureRegion.Overlaps(source.Bounds) || i == approach.PlatformIndex || i == previous.PlatformIndex ||
                i == target.PlatformIndex || i == adjacentPlatform)
                supports.Add(i, MakeSupport(label + " actual platform " + i, translated.Bounds, translated.OneWay, groundLayer, chapter));
        }
        int solidCount = 0;
        foreach (RectInt solid in map.Solids)
        {
            Rect bounds = new Rect(solid.x, solid.y, solid.width, solid.height);
            if (fixtureRegion.Overlaps(bounds)) MakeSupport(label + " actual solid " + solidCount++, Rectangle(bounds), false, groundLayer, chapter);
        }
        Require(supports.ContainsKey(adjacentPlatform) && supports.ContainsKey(previous.PlatformIndex) && supports.ContainsKey(target.PlatformIndex),
            "Cannon fixture omitted the previous/target support or adjacent platform.");
        abilities.ResetProgression(); abilities.ResetTransientState(); abilities.SetAutomationSlide(false);
        player.ResetMotion(); player.SetControlsEnabled(true); life.SetExitProtected(false);
        Vector2 start = Point(approach.FeetPosition) + Vector2.up * (capsule.bounds.extents.y + .035f);
        body.position = start; player.transform.position = start; life.SetCheckpoint(start);
        player.SetAutomationInputOverride(Vector2.zero);
        GameObject cannonObject = PirateWorldArt.Create(label + " actual cannon", PirateArtKind.Cannon, Point(spawn.Position), spawn.Size);
        fixtureObjects.Add(cannonObject);
        float surface = float.NegativeInfinity;
        foreach (CampaignLayout.Platform platform in map.Platforms)
            if (spawn.Position.x >= platform.Bounds.xMin && spawn.Position.x <= platform.Bounds.xMax && platform.SurfaceY <= spawn.Position.y + .01f)
                surface = Mathf.Max(surface, platform.SurfaceY);
        foreach (RectInt solid in map.Solids)
            if (spawn.Position.x >= solid.xMin && spawn.Position.x <= solid.xMax && solid.yMax <= spawn.Position.y + .01f)
                surface = Mathf.Max(surface, solid.yMax);
        Require(!float.IsNegativeInfinity(surface), "Generated cannon has no presentation floor.");
        cannonObject.GetComponent<PirateWorldVisual>().SetGroundSurface(Point(new Vector2(spawn.Position.x, surface)).y);
        BoxCollider2D marker = cannonObject.AddComponent<BoxCollider2D>(); marker.size = spawn.Size; marker.isTrigger = true;
        Cannon cannon = cannonObject.AddComponent<Cannon>();
        cannon.Initialize(player.transform, PirateWorldArt.GetSprite(PirateArtKind.Cannonball));
        cannon.SetActivationRange(spawn.Value > 0f ? spawn.Value : 18f);
        Physics2D.SyncTransforms();
        Require(cannon.enabled && cannon.BodyHitbox is PolygonCollider2D && cannon.BodyHitbox.enabled &&
            cannon.BodyHitbox.isTrigger && !marker.enabled, "Cannon is missing its active production illustrated body.");
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector2 centre = (Point(previous.FeetPosition) + Point(target.FeetPosition)) * .5f + Vector2.up * 1.5f;
            camera.transform.position = new Vector3(centre.x, centre.y, camera.transform.position.z); camera.orthographicSize = 7f;
        }
        var fixedStep = new WaitForFixedUpdate();
        for (int frame = 0; frame < 30 && !player.IsGrounded; frame++) yield return fixedStep;
        Require(player.IsGrounded && body.linearVelocity.sqrMagnitude < .05f, label + " failed to settle at its real approach support.");
        int deathsBefore = life.DeathCount, previousStable = 0, landingStable = 0, jumpInputs = 0, observedJumps = 0;
        float approachVelocity = 0f, closestBody = float.PositiveInfinity;
        bool handedOver = false, passed = false, pendingJump = false;
        float direction = Mathf.Sign(Point(target.FeetPosition).x - Point(previous.FeetPosition).x);
        BoxCollider2D previousSupport = supports[previous.PlatformIndex], landing = supports[target.PlatformIndex];
        Debug.Log($"PIRATE_CANNON_LANDING_BEGIN case={label} seed=42 chapter={chapter} node={targetNode} mirrored={mirror < 0} start={start} " +
            $"signature={map.Signature()} previous={previousSupport.bounds} target={landing.bounds} adjacentPlatform={adjacentPlatform} adjacentBounds={supports[adjacentPlatform].bounds} " +
            $"cannon={cannon.BodyHitbox.bounds} platforms={supports.Count} solids={solidCount} assignedLaunchVelocity=False");
        for (int frame = 0; frame < 500; frame++)
        {
            Require(!life.IsRespawning && life.DeathCount == deathsBefore, label + " died from an actual hazard contact.");
            Require(cannon.enabled && cannon.gameObject.activeInHierarchy && cannon.BodyHitbox.enabled && !life.IsExitProtected &&
                !life.IsModalProtected && Time.timeScale == 1f, label + " lost its live, unprotected hazard contract.");
            bool jump = false;
            if (pendingJump && body.linearVelocity.y > 12f) { observedJumps++; pendingJump = false; }
            if (!handedOver)
            {
                previousStable = OnFixtureSupport(player, body, capsule, previousSupport) ? previousStable + 1 : 0;
                if (previousStable >= 2)
                {
                    handedOver = true; approachVelocity = body.linearVelocity.x * direction;
                    Require(approachVelocity >= player.MoveSpeed - .5f, label + " did not recreate a real near-full-speed incoming approach.");
                    if (arsenal) { jump = true; pendingJump = true; jumpInputs++; }
                    Debug.Log($"PIRATE_CANNON_LANDING_HANDOVER case={label} position={body.position} velocity={body.linearVelocity} " +
                        "actualPreviousSupport=True ordinaryRunup=True assignedVelocity=False");
                }
            }
            Bounds bodyBounds = cannon.BodyHitbox.bounds;
            Rect lethal = Rect.MinMaxRect(bodyBounds.min.x, bodyBounds.min.y, bodyBounds.max.x, bodyBounds.max.y);
            float preferred = Mathf.Clamp(cannon.transform.position.x + direction * 1.4f,
                landing.bounds.min.x + capsule.bounds.extents.x + .36f, landing.bounds.max.x - capsule.bounds.extents.x - .36f);
            Require(CampaignRoutePlaytest.TryFindSafeLandingX(mapped[target.PlatformIndex], mapped, capsule.bounds.extents.x,
                capsule.bounds.size.y, preferred, out float safeX, out int blocker, new[] { lethal }), "No shared terrain-and-body-clear landing interval.");
            bool precise = CampaignRoutePlaytest.CannonLandingNeedsSetup(preferred, safeX);
            Require(arsenal ? !precise : precise, arsenal ?
                "Arsenal's running crossing incorrectly requested precise braking/setup." :
                "Dock's downstep fixture lost the required terrain-and-cannon constrained landing.");
            float axis = !handedOver ? direction : arsenal ? CampaignRoutePlaytest.OrdinaryLandingAxis(player, body, safeX) :
                CampaignRoutePlaytest.CannonLandingAxis(player, body, safeX);
            bool actualLanding = handedOver && OnFixtureSupport(player, body, capsule, landing);
            landingStable = actualLanding && Mathf.Abs(body.linearVelocity.x) < .2f ? landingStable + 1 : 0;
            closestBody = Mathf.Min(closestBody, capsule.Distance(cannon.BodyHitbox).distance);
            if (frame % 10 == 0 || landingStable == 3)
                Debug.Log($"PIRATE_CANNON_LANDING_FRAME case={label} frame={frame} position={body.position} velocity={body.linearVelocity} " +
                    $"axis={axis:F1} safeX={safeX:F4} blocker={blocker} grounded={player.IsGrounded} targetContact={actualLanding} stable={landingStable} " +
                    $"telegraph={cannon.IsTelegraphing} shots={cannon.ShotsFired} preciseSetup={precise} jumps={jumpInputs}/{observedJumps}");
            if (landingStable >= 3) { passed = true; break; }
            player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump);
            yield return null;
        }
        player.SetAutomationInputOverride(Vector2.zero);
        Debug.Log($"PIRATE_CANNON_LANDING_CASE case={label} success={passed} actualTargetSupport={passed} stable={landingStable} " +
            $"approachVelocity={approachVelocity:F4} closestBody={closestBody:F4} shotsFired={cannon.ShotsFired} " +
            $"shootingEnabled={cannon.enabled} deaths={life.DeathCount - deathsBefore} assignedVelocity=False jumpInputs={jumpInputs} observedJumps={observedJumps}");
        if (evidence != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            string path = Path.Combine(evidence, label + (passed ? "-pass.png" : "-fail.png"));
            ScreenCapture.CaptureScreenshot(path); yield return null; yield return null;
            Debug.Log("PIRATE_CANNON_LANDING_CAPTURE requested=" + path + " pixelProof=False");
        }
        Require(passed && life.DeathCount == deathsBefore && !life.IsRespawning, label + " did not stop on the actual target within 500 frames.");
        Require(!arsenal || jumpInputs == 1 && observedJumps == 1, label + " did not observe the ordinary running jump.");
        completedCases++;
    }

    private bool OnFixtureSupport(PlayerMovement player, Rigidbody2D body, Collider2D capsule, BoxCollider2D support)
    {
        if (!player.IsGrounded || Mathf.Abs(capsule.bounds.min.y - support.bounds.max.y) >= .14f ||
            body.position.x <= support.bounds.min.x + capsule.bounds.extents.x + .2f ||
            body.position.x >= support.bounds.max.x - capsule.bounds.extents.x - .2f) return false;
        int count = capsule.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contacts[i];
            if (!contact.enabled || contact.collider != support && contact.otherCollider != support) continue;
            Vector2 normal = contact.normal;
            if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f) normal = -normal;
            if (normal.y > .65f) return true;
        }
        return false;
    }

    private IEnumerator RunCase(PlayerMovement player, string label, string scenarioId, int chapter, int mirror, float startOffset)
    {
        ClearFixture();
        ApplyFixtureChapterSpeed(player, chapter, label);
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        CampaignLayout map = CampaignLayout.Create(20260918, chapter, player.JumpLaunchSpeed, player.GravityStrength, Time.fixedDeltaTime, player.MoveSpeed);
        CampaignLayout.Region room = map.Rooms.Find(item => item.ScenarioId == scenarioId);
        Require(room != null, "Generated scenario missing: " + scenarioId);
        bool spring = scenarioId.Contains("spring-");
        bool springReturn = scenarioId.Contains("spring-return");
        bool recharge = scenarioId.Contains("spring-recharge");
        bool sliding = scenarioId.Contains("slide-");
        CampaignLayout.TraversalAction action = sliding ? CampaignLayout.TraversalAction.SaberSlide :
            spring ? (scenarioId.Contains("spring-drop") ? CampaignLayout.TraversalAction.SpringDrop : CampaignLayout.TraversalAction.Spring) :
            scenarioId.Contains("double-window") ? CampaignLayout.TraversalAction.DoubleWindow : CampaignLayout.TraversalAction.DoubleJump;
        int node = map.Route.FindIndex(item => item.RoomIndex == room.Id && item.Action == action);
        Require(node > 0, "Generated trial landing missing.");
        CampaignLayout.RouteNode previous = map.Route[node - 1], target = map.Route[node];
        var trialNodes = new List<int> { node };
        if (recharge)
            for (int i = node + 1; i <= room.LastRouteNode; i++)
                if (map.Route[i].Action == CampaignLayout.TraversalAction.Spring) trialNodes.Add(i);
        if (sliding)
        {
            int last = node;
            for (int i = node + 1; i <= room.LastRouteNode; i++)
                if (map.Route[i].Action == CampaignLayout.TraversalAction.SaberSlide ||
                    scenarioId.Contains("slide-launch") && map.Route[i].Action == CampaignLayout.TraversalAction.Jump) { last = i; if (scenarioId.Contains("slide-launch")) break; }
            for (int i = node + 1; i <= last; i++) trialNodes.Add(i);
        }
        Require(!recharge || trialNodes.Count == 2, "Recharge fixture requires both real successive bounces.");
        Vector2 origin = new Vector2(15000f + completedCases * 200f, 15000f);
        Vector2 Point(Vector2 source) => origin + new Vector2((source.x - previous.FeetPosition.x) * mirror, source.y - previous.FeetPosition.y);
        Rect Rectangle(Rect source) => new Rect(Point(source.center) - source.size * .5f, source.size);
        int groundLayer = 0;
        for (int bit = 0; bit < 32; bit++) if ((player.GroundLayer.value & (1 << bit)) != 0) { groundLayer = bit; break; }
        BoxCollider2D landing = null;
        var supports = new Dictionary<int, BoxCollider2D>();
        int platformCount = 0, solidCount = 0, spikesCount = 0;
        foreach (CampaignLayout.Platform platform in map.Platforms)
        {
            if (platform.RoomIndex != room.Id) continue;
            BoxCollider2D collider = MakeSupport(label + " platform " + platformCount++, Rectangle(platform.Bounds), platform.OneWay, groundLayer, chapter);
            supports.Add(map.Platforms.IndexOf(platform), collider);
            if (ReferenceEquals(platform, map.Platforms[target.PlatformIndex])) landing = collider;
        }
        Rect roomBounds = new Rect(room.Bounds.xMin, room.Bounds.yMin, room.Bounds.width, room.Bounds.height);
        foreach (RectInt solid in map.Solids)
        {
            Rect bounds = new Rect(solid.x, solid.y, solid.width, solid.height);
            Rect expandedRoom = Rect.MinMaxRect(roomBounds.xMin - 1f, roomBounds.yMin - 1f, roomBounds.xMax + 1f, roomBounds.yMax + 1f);
            if (expandedRoom.Overlaps(bounds)) MakeSupport(label + " solid " + solidCount++, Rectangle(bounds), false, groundLayer, chapter);
        }
        foreach (CampaignLayout.Spawn spawn in map.Spawns)
        {
            if (spawn.Kind != CampaignLayout.SpawnKind.Spikes || spawn.NodeIndex < room.FirstRouteNode || spawn.NodeIndex > room.LastRouteNode) continue;
            GameObject obj = PirateWorldArt.Create(label + " actual spikes " + spikesCount++, PirateArtKind.Spikes, Point(spawn.Position), spawn.Size);
            fixtureObjects.Add(obj);
            BoxCollider2D trigger = obj.AddComponent<BoxCollider2D>(); trigger.size = spawn.Size; trigger.isTrigger = true;
            obj.AddComponent<InstantKillHazard>().Configure(HazardKind.Spikes);
        }
        Require(landing != null && (!(spring || sliding) || spikesCount > 0), "Fixture omitted its generated landing or spike bed.");
        Vector2 destination = Point(target.FeetPosition);
        float direction = Mathf.Sign(destination.x - origin.x);
        abilities.ResetProgression(); abilities.Apply(sliding ? PirateUpgrade.Saber2 : spring ? PirateUpgrade.SpringLeg : PirateUpgrade.DoubleJump, false);
        abilities.ResetTransientState(); life.SetExitProtected(false);
        player.ResetMotion(); player.SetControlsEnabled(true);
        Vector2 start = origin + new Vector2(direction * startOffset, capsule.bounds.extents.y + .035f);
        body.position = start; player.transform.position = start; life.SetCheckpoint(start);
        player.SetAutomationInputOverride(Vector2.zero); abilities.SetAutomationSlide(false);
        Physics2D.SyncTransforms();
        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector2 finalDestination = Point(map.Route[trialNodes[trialNodes.Count - 1]].FeetPosition);
            Vector2 centre = (origin + finalDestination) * .5f + Vector2.up * 2f;
            camera.transform.position = new Vector3(centre.x, centre.y, camera.transform.position.z);
            camera.orthographicSize = Mathf.Max(9f, Mathf.Abs(finalDestination.x - origin.x) / (2f * camera.aspect) + 3f,
                Mathf.Abs(finalDestination.y - origin.y) * .5f + 3f);
        }
        var step = new WaitForFixedUpdate();
        for (int frame = 0; frame < 30 && !player.IsGrounded; frame++) yield return step;
        Require(player.IsGrounded && body.linearVelocity.sqrMagnitude < .05f, label + " did not settle at its zero-velocity starting support.");
        int deathsBefore = life.DeathCount, bounces = 0, jumpInputs = 0, observedJumps = 0, roofContactFrames = 0, stable = 0;
        int stage = 0, bouncesAtStage = 0, rechargeLandings = 0, completedLandings = 0;
        int slideFrames = 0, stageSlideFrames = 0, slideStages = 0;
        float stageMaxFeet = capsule.bounds.min.y, minimumSpringRise = float.PositiveInfinity;
        bool firstJump = false, secondJump = false, pendingJump = false, passed = false;
        float maxFeet = capsule.bounds.min.y, bounceFeet = float.NaN, bounceVelocity = 0f;
        Action bounce = () => {
            bounces++; bounceFeet = capsule.bounds.min.y; bounceVelocity = body.linearVelocity.y;
            Debug.Log($"PIRATE_ABILITY_TRIAL_BOUNCE case={label} position={body.position} velocity={body.linearVelocity} " +
                $"feet={bounceFeet:F4} count={bounces} actualProductionEvent=True");
        };
        abilities.SpringBounced += bounce;
        Debug.Log($"PIRATE_ABILITY_TRIAL_BEGIN case={label} seed={map.Seed} chapter={chapter} node={node} " +
            $"signature={map.Signature()} mirrored={mirror < 0} start={start} target={destination} " +
            $"platforms={platformCount} solids={solidCount} spikes={spikesCount} startVelocity={body.linearVelocity} preparedEquipment=True");
        try
        {
            for (int frame = 0; frame < 1400; frame++)
            {
                if (life.DeathCount != deathsBefore || life.IsRespawning) throw new InvalidOperationException(label + " died in real hazard contact.");
                float feet = capsule.bounds.min.y;
                maxFeet = Mathf.Max(maxFeet, feet);
                stageMaxFeet = Mathf.Max(stageMaxFeet, feet);
                if (abilities.IsSpikeSliding) { slideFrames++; stageSlideFrames++; }
                if (pendingJump && body.linearVelocity.y > 12f) { observedJumps++; pendingJump = false; }
                int contactCount = capsule.GetContacts(contacts);
                bool actualLanding = false;
                for (int i = 0; i < contactCount; i++)
                {
                    ContactPoint2D contact = contacts[i];
                    if (!contact.enabled) continue;
                    Vector2 normal = contact.normal;
                    if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f) normal = -normal;
                    if (normal.y < -.65f) roofContactFrames++;
                    if ((contact.collider == landing || contact.otherCollider == landing) && normal.y > .65f) actualLanding = true;
                }
                bool onTarget = player.IsGrounded && actualLanding && Mathf.Abs(feet - destination.y) < .14f &&
                    body.position.x > landing.bounds.min.x + capsule.bounds.extents.x + .2f &&
                    body.position.x < landing.bounds.max.x - capsule.bounds.extents.x - .2f;
                stable = onTarget ? stable + 1 : 0;
                if (stable >= 3)
                {
                    if (spring)
                    {
                        Require(bounces == bouncesAtStage + 1 && bounceVelocity >= 17.99f,
                            label + " stage reached without its one real spring event.");
                        float rise = stageMaxFeet - bounceFeet;
                        minimumSpringRise = Mathf.Min(minimumSpringRise, rise);
                        Require(rise > 3.8f, label + " spring stage never achieved its required actual rise.");
                        if (stage + 1 < trialNodes.Count)
                        { Require(abilities.SpringAvailable, label + " safe landing did not recharge spring."); rechargeLandings++; }
                    }
                    if (sliding && target.Action == CampaignLayout.TraversalAction.SaberSlide)
                    { Require(stageSlideFrames > 0, label + " crossed a slide field without real IsSpikeSliding."); slideStages++; }
                    completedLandings++;
                    Debug.Log($"PIRATE_ABILITY_TRIAL_LANDING case={label} stage={stage} node={trialNodes[stage]} " +
                        $"actualSupport=True stable={stable} springAvailable={abilities.SpringAvailable} bounces={bounces} slideFrames={stageSlideFrames}");
                    if (++stage == trialNodes.Count) { passed = true; break; }
                    target = map.Route[trialNodes[stage]];
                    destination = Point(target.FeetPosition); landing = supports[target.PlatformIndex];
                    stable = 0; firstJump = secondJump = false; bouncesAtStage = bounces;
                    stageMaxFeet = feet; stageSlideFrames = 0;
                }
                bool jump = false;
                float stageDirection = Mathf.Sign(destination.x - Point(map.Route[trialNodes[stage] - 1].FeetPosition).x);
                float axis = spring && bounces == bouncesAtStage ? stageDirection : AxisTo(player, body, destination.x);
                if (springReturn && bounces == bouncesAtStage)
                {
                    CampaignLayout.Spawn bed = map.Spawns.Find(item => item.Kind == CampaignLayout.SpawnKind.Spikes && item.NodeIndex == trialNodes[stage] - 1);
                    Require(bed != null, "Spring-return bed missing.");
                    axis = AxisTo(player, body, Point(bed.Position).x);
                    if (!firstJump && player.IsGrounded) { jump = true; firstJump = true; }
                }
                if (sliding)
                {
                    bool slide = target.Action == CampaignLayout.TraversalAction.SaberSlide;
                    bool stillOverBed = false;
                    if (slide)
                    {
                        CampaignLayout.Spawn bed = map.Spawns.Find(item => item.Kind == CampaignLayout.SpawnKind.Spikes && item.NodeIndex == trialNodes[stage] - 1);
                        Require(bed != null, "Slide stage omitted its real associated bed.");
                        Rect bedBounds = Rectangle(new Rect(bed.Position - bed.Size * .5f, bed.Size));
                        float exit = stageDirection > 0f ? bedBounds.xMax : bedBounds.xMin;
                        stillOverBed = (body.position.x - exit) * stageDirection <= capsule.bounds.extents.x + .025f;
                        if (stillOverBed) axis = stageDirection;
                        if (!stillOverBed && !firstJump && actualLanding && player.IsGrounded)
                        { jump = true; firstJump = true; }
                    }
                    abilities.SetAutomationSlide(slide && stillOverBed);
                    if (!slide && player.IsGrounded && !firstJump &&
                        (destination.y > feet + .18f ||
                            target.Action == CampaignLayout.TraversalAction.Jump))
                    { jump = true; firstJump = true; }
                }
                else if (!spring && !firstJump && player.IsGrounded) { jump = true; firstJump = true; }
                else if (!spring && firstJump && !secondJump && !player.IsGrounded && body.linearVelocity.y < 1.5f &&
                    (action != CampaignLayout.TraversalAction.DoubleWindow || (body.position.x - origin.x) * direction > 1.9f))
                { jump = true; secondJump = true; }
                if (jump) { jumpInputs++; pendingJump = true; }
                player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump);
                if (frame % 20 == 0) Debug.Log($"PIRATE_ABILITY_TRIAL_FRAME case={label} frame={frame} position={body.position} " +
                    $"velocity={body.linearVelocity} feet={feet:F4} axis={axis:F1} bounces={bounces} grounded={player.IsGrounded} roof={roofContactFrames}");
                if (feet < origin.y - 5f) break;
                yield return null;
            }
            player.SetAutomationInputOverride(Vector2.zero);
            bool mechanic = sliding ? slideStages == spikesCount && slideFrames > 0 && bounces == 0 &&
                    (!scenarioId.Contains("slide-launch") || jumpInputs > 0 && observedJumps > 0) :
                spring ? bounces == trialNodes.Count && bounceVelocity >= 17.99f && roofContactFrames == 0 && minimumSpringRise > 3.8f &&
                    (!recharge || rechargeLandings == 1) :
                jumpInputs == 2 && observedJumps == 2;
            Debug.Log($"PIRATE_ABILITY_TRIAL_CASE case={label} success={passed && mechanic} stable={stable} actualLanding={passed} " +
                $"bounces={bounces} jumpInputs={jumpInputs} observedJumps={observedJumps} maxFeet={maxFeet:F4} " +
                $"riseFromBounce={maxFeet - bounceFeet:F4} minimumSpringStageRise={minimumSpringRise:F4} " +
                $"completedLandings={completedLandings}/{trialNodes.Count} rechargeLandings={rechargeLandings} " +
                $"slideStages={slideStages}/{spikesCount} slideFrames={slideFrames} roofContactFrames={roofContactFrames} deaths={life.DeathCount - deathsBefore}");
            if (evidence != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                string path = Path.Combine(evidence, label + (passed && mechanic ? "-pass.png" : "-fail.png"));
                ScreenCapture.CaptureScreenshot(path);
                yield return null; yield return null;
                Debug.Log("PIRATE_ABILITY_TRIAL_CAPTURE requested=" + path + " pixelProof=False");
            }
            Require(passed && mechanic, label + " failed its actual landing/mechanic contract within 1400 frames.");
            completedCases++;
        }
        finally { abilities.SpringBounced -= bounce; }
    }

    private BoxCollider2D MakeSupport(string label, Rect bounds, bool oneWay, int layer, int chapter)
    {
        GameObject obj = PirateWorldArt.CreatePlatform(label, bounds, chapter);
        fixtureObjects.Add(obj); obj.layer = layer;
        BoxCollider2D collider = obj.AddComponent<BoxCollider2D>(); collider.size = bounds.size;
        if (oneWay)
        {
            PlatformEffector2D effector = obj.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true; effector.useOneWayGrouping = true;
            effector.useSideFriction = false; effector.useSideBounce = false; effector.surfaceArc = 160f;
            collider.usedByEffector = true;
        }
        return collider;
    }

    private static float AxisTo(PlayerMovement player, Rigidbody2D body, float targetX)
    {
        float distance = targetX - body.position.x;
        float braking = player.IsGrounded ? player.Deceleration : player.AirDeceleration;
        float stopping = body.linearVelocity.x * body.linearVelocity.x / (2f * braking);
        return Mathf.Abs(distance) < .12f || distance * body.linearVelocity.x > 0f && stopping >= Mathf.Abs(distance) - .12f ? 0f : Mathf.Sign(distance);
    }

    private void ClearFixture()
    {
        foreach (GameObject obj in fixtureObjects) if (obj != null) { obj.SetActive(false); Destroy(obj); }
        fixtureObjects.Clear();
    }
    private static void Require(bool condition, string detail) { if (!condition) throw new InvalidOperationException(detail); }
}
