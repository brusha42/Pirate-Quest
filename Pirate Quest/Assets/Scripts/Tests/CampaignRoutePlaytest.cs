using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampaignRoutePlaytest : MonoBehaviour
{
    private const float OrdinaryLandingSteeringInset = .36f;
    private static bool installed;
    private PlayerMovement player;
    private PlayerLife life;
    private PlayerAbilities abilities;
    private PlayerGrapple grapple;
    private Rigidbody2D body;
    private Collider2D capsule;
    private PirateGameFlow flow;
    private CampaignLayout layout;
    private int targetIndex;
    private int jumps;
    private int attacks;
    private int attaches;
    private int springBounces;
    private int padLaunches;
    private int frameAtNode;
    private int stable;
    private int chainPhase;
    private bool chainDashUsed;
    private bool nodeJumped;
    private bool secondJumped;
    private bool wasAttached;
    private bool done;
    private string evidence;
    private float closestCannonball = float.PositiveInfinity;
    private float minimumX;
    private float maximumX;
    private readonly List<Cannon> cannons = new List<Cannon>();
    private int shotsSeen;
    private int normalRespawns;
    private int springsAtNode;
    private bool springNonRisingLogged;
    private bool springLandingLogged;
    private int saberSwings;
    private int scoutFlights;
    private float scoutDistance;
    private int ropeJumps;
    private bool pendingRopeJump;
    private HookAnchor relayPendingAnchor;
    private readonly HashSet<HookAnchor> relayLaunchedAnchors = new HashSet<HookAnchor>();
    private int recoveryPadsUsed;
    private int dashInputs;
    private int actualDashes;
    private bool wasDashing;
    private int floorRecoveryAttempts;
    private bool wasAirborneAtNode;
    private int lastDashDecisionNode = -1;
    private bool lastDashDecisionAllowed;
    private int lastProjectileForecastNode = -1;
    private int lastProjectileForecastFrame = -100;
    private bool lastProjectileForecastBlocked;
    private int lastTelegraphWalkConflictNode = -1;
    private int lastTelegraphWalkConflictFrame = -100;
    private int lastSteeringDecisionNode = -1;
    private float lastSteeringDecisionX;
    private bool cannonLandingSteering;
    private bool narrowFarPocketApproach;
    private bool cannonLandingWarningSeen;
    private int cannonLandingWaitStarted = -1;
    private readonly ContactPoint2D[] recoveryContacts = new ContactPoint2D[16];
    private int tideMetricsScene = -1;
    private int tideMetricsChapter = -1;
    private double actualTideRisingSeconds;
    private double scoutingPauseSeconds;
    private float minimumTideFeetWaterGap = float.PositiveInfinity;
    private int tideRisingSampleFrames;
    private int tideScoutingSampleFrames;
    private readonly HashSet<string> optionalDescentAttempts = new HashSet<string>();
    private readonly HashSet<string> oneWayDropAttempts = new HashSet<string>();
    private int optionalDescentsThisChapter;
    private int actualDashTarget = -1;
    private int retreatReplays;
    private readonly Dictionary<string, int> retreatReplayAttempts = new Dictionary<string, int>();
    private readonly Dictionary<string, int> floorRecoveryAttemptsByNode = new Dictionary<string, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureRequestedSeed()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pirateQuestRoutePlaytest")) return;
        if (int.TryParse(Argument("-pirateQuestRouteSeed"), out int seed))
        {
            PirateCampaignSession.Begin(seed);
            Debug.Log("PIRATE_ROUTE_REQUESTED_SEED " + seed);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (installed || !Environment.GetCommandLineArgs().Contains("-pirateQuestRoutePlaytest")) return;
        installed = true;
        GameObject runner = new GameObject("Actual keyboard campaign route playtest");
        DontDestroyOnLoad(runner);
        runner.AddComponent<CampaignRoutePlaytest>();
    }

    private IEnumerator Start()
    {
        PirateTestModalAcknowledger.Install();
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        string requestedFrameRate = Argument("-pirateQuestTestFrameRate");
        if (int.TryParse(requestedFrameRate, out int testFrameRate) && testFrameRate >= 1 && testFrameRate <= 120)
            Application.targetFrameRate = testFrameRate;
        Application.runInBackground = true;
        Time.captureDeltaTime = Time.fixedDeltaTime;
        Debug.Log($"PIRATE_ROUTE_TEST_FRAME_RATE requested={requestedFrameRate ?? "unset"} " +
                  $"target={Application.targetFrameRate} captureDeltaTime={Time.captureDeltaTime:F4} " +
                  "wallClockLimitOnly=True simulationStepUnchanged=True");
        evidence = Argument("-pirateQuestRouteEvidence");
        if (!RunDriverGeometrySelfChecks(out string driverCheck))
        { Fail("driver-only geometry self-check: " + driverCheck); yield break; }
        Debug.Log("PIRATE_ROUTE_DRIVER_GEOMETRY_SELFCHECK " + driverCheck + " runtimeContactProof=False");
        int chapters = int.TryParse(Argument("-pirateQuestRouteChapters"), out int requested) ? Mathf.Clamp(requested, 1, 4) : 1;
        Debug.Log("PIRATE_ROUTE_SCOPE chapters=" + chapters + " fullRouteAttempt=True keyboardAxesOnly=True " +
                  "testTeleports=0 normalCheckpointRespawnsAllowed=20 abilityGrants=0 directCutCalls=0 directAttachCalls=0 hazardsDisabled=0 exitBypass=0");
        for (int chapter = 0; chapter < chapters && !done; chapter++)
        {
            float deadline = Time.realtimeSinceStartup + 60f;
            while (Time.realtimeSinceStartup < deadline)
            {
                flow = FindFirstObjectByType<PirateGameFlow>();
                if (flow != null && flow.IsInitialized && flow.ChapterIndex == chapter) break;
                yield return null;
            }
            if (flow == null || !flow.IsInitialized || flow.ChapterIndex != chapter)
            { Fail("campaign entrance did not initialize"); yield break; }
            player = FindFirstObjectByType<PlayerMovement>();
            body = player.GetComponent<Rigidbody2D>();
            capsule = player.GetComponent<Collider2D>();
            life = player.GetComponent<PlayerLife>();
            abilities = player.GetComponent<PlayerAbilities>();
            grapple = player.GetComponent<PlayerGrapple>();
            layout = flow.Generator.Campaign;
            if (layout == null || !layout.GeometryValid) { Fail("invalid generated geometry"); yield break; }
            abilities.SpringBounced += CountSpring;
            abilities.SaberSwung += CountSaber;
            targetIndex = 0;
            optionalDescentsThisChapter = 0;
            ResetNodeState();
            minimumX = maximumX = body.position.x;
            int deathsAtStart = life.DeathCount;
            float chapterSimulationStarted = Time.time;
            int observedDeaths = deathsAtStart;
            int highestReached = 0;
            int chapterAttaches = attaches;
            int chapterAttacks = attacks;
            int initialScene = SceneManager.GetActiveScene().buildIndex;
            ResetTidePhaseMetrics(chapter, initialScene);
            cannons.Clear();
            cannons.AddRange(FindObjectsByType<Cannon>(FindObjectsSortMode.None));
            string signature = layout.Signature();
            bool chapterComplete = false;
            if (layout.Spawns.Any(spawn => spawn.Kind == CampaignLayout.SpawnKind.DarkZone))
            {
                yield return ScoutDarkGallery();
                if (done) yield break;
            }
            for (int frame = 0; frame < 36000 && !done; frame++)
            {
                if (flow.HUD != null && flow.HUD.IsUpgradeOpen) { yield return null; continue; }
                if (SceneManager.GetActiveScene().buildIndex != initialScene)
                { chapterComplete = true; break; }
                if (life.DeathCount != observedDeaths || life.IsRespawning)
                {
                    normalRespawns++;
                    if (normalRespawns > 20) { Fail("more than 20 normal checkpoint retries without completing the campaign"); yield break; }
                    observedDeaths = life.DeathCount;
                    optionalDescentsThisChapter = 0;
                    Debug.Log($"PIRATE_ROUTE_NORMAL_DEATH chapter={chapter} node={targetIndex} retry={normalRespawns} " +
                              $"checkpoint={life.CheckpointPosition} testTeleport=False");
                    LogNearbyThreatState();
                    player.SetAutomationInputOverride(Vector2.zero);
                    grapple.SetAutomationInputOverride(false);
                    abilities.SetAutomationSlide(false);
                    float respawnDeadline = Time.realtimeSinceStartup + 5f;
                    while (life.IsRespawning && Time.realtimeSinceStartup < respawnDeadline) yield return null;
                    if (life.IsRespawning) { Fail("normal game respawn did not complete"); yield break; }
                    float nearest = float.PositiveInfinity;
                    int checkpointNode = 0;
                    for (int i = 0; i <= Mathf.Min(highestReached + 3, layout.Route.Count - 1); i++)
                    {
                        float distance = Vector2.Distance(layout.Route[i].StandingPosition(capsule.bounds.extents.y), life.CheckpointPosition);
                        if (distance < nearest) { nearest = distance; checkpointNode = i; }
                    }
                    if (nearest > 1f) { Fail("normal checkpoint does not correspond to a previously cleared route region"); yield break; }
                    targetIndex = checkpointNode;
                    ResetNodeState();
                    continue;
                }
                minimumX = Mathf.Min(minimumX, body.position.x);
                maximumX = Mathf.Max(maximumX, body.position.x);
                ObserveThreats();
                if (player.IsDashing && !wasDashing)
                { actualDashes++; actualDashTarget = targetIndex; }
                wasDashing = player.IsDashing;

                if (Vector2.Distance(body.position, layout.Exit + Vector2.up * 0.5f) < 2.4f && !player.ControlsEnabled)
                {
                    Capture($"chapter-{chapter}-actual-exit.png");
                    chapterComplete = true;
                    break;
                }
                if (!player.ControlsEnabled)
                { Fail("controls unavailable away from the actual portal"); yield break; }

                CampaignLayout.RouteNode target = layout.Route[targetIndex];
                CampaignLayout.Platform support = layout.Platforms[target.PlatformIndex];
                float feet = capsule.bounds.min.y;
                if (springBounces > springsAtNode && IsSpringTraversal(target.Action))
                {
                    if (!springNonRisingLogged && !player.IsGrounded && body.linearVelocity.y <= 0f)
                    {
                        springNonRisingLogged = true;
                        Debug.Log($"PIRATE_ROUTE_SPRING_FIRST_NONRISING chapter={chapter} node={targetIndex} " +
                            $"position={body.position} feet={feet:F3} velocity={body.linearVelocity} target={target.FeetPosition} " +
                            "observationOnly=True apexOrCeilingContactNotDistinguished=True");
                    }
                    if (!springLandingLogged && player.IsGrounded)
                    {
                        springLandingLogged = true;
                        Debug.Log($"PIRATE_ROUTE_SPRING_FIRST_GROUND chapter={chapter} node={targetIndex} " +
                            $"position={body.position} feet={feet:F3} velocity={body.linearVelocity} " +
                            $"onStrictTarget={OnSafeSupport(support)} observationOnly=True");
                    }
                }
                bool ordinaryTarget = !target.RequiredAbility.HasValue &&
                    (target.Action == CampaignLayout.TraversalAction.Walk || target.Action == CampaignLayout.TraversalAction.Jump);
                if (ordinaryTarget && TryFindActualRetreatSupport(out int retreatSource))
                {
                    yield return ReplayOrdinaryRetreat(retreatSource);
                    if (done) yield break;
                    continue;
                }
                if (ordinaryTarget && frameAtNode > 90 && player.IsGrounded &&
                    Mathf.Abs(body.linearVelocity.y) < 0.2f && feet > target.FeetPosition.y + 0.14f &&
                    TryPlanNearbyOneWayDrop(out int upperDropPlatform, out float oneWayDropX))
                {
                    yield return DropFromNearbyOneWaySupport(upperDropPlatform, oneWayDropX);
                    if (done) yield break;
                    continue;
                }
                if (ordinaryTarget && frameAtNode > 90 && player.IsGrounded &&
                    Mathf.Abs(body.linearVelocity.y) < 0.2f && feet > target.FeetPosition.y + 0.14f &&
                    TryPlanOptionalDescent(target, out OptionalDescentPlan descent))
                {
                    yield return DescendFromOptionalSupport(descent);
                    if (done) yield break;
                    continue;
                }
                CampaignLayout.Platform previousSupport = layout.Platforms[Previous.PlatformIndex];
                bool landedBackOnPrevious = wasAirborneAtNode && player.IsGrounded &&
                    Mathf.Abs(feet - previousSupport.SurfaceY) < 0.14f &&
                    body.position.x > previousSupport.Bounds.xMin + capsule.bounds.extents.x + 0.12f &&
                    body.position.x < previousSupport.Bounds.xMax - capsule.bounds.extents.x - 0.12f;
                wasAirborneAtNode = !player.IsGrounded;
                if (frameAtNode > 140 && player.IsGrounded &&
                    (target.RequiredAbility.HasValue || ordinaryTarget) && feet < Previous.FeetPosition.y - 1f &&
                    !OnSafeSupport(support) && Mathf.Abs(feet - target.FeetPosition.y) > 0.25f)
                {
                    yield return RecoverUsingRoomPad();
                    if (done) yield break;
                    continue;
                }
                bool grounded = player.IsGrounded && Mathf.Abs(body.linearVelocity.y) < 0.2f;
                bool landed = grounded && Mathf.Abs(feet - target.FeetPosition.y) < 0.14f &&
                    body.position.x > support.Bounds.xMin + capsule.bounds.extents.x + 0.2f &&
                    body.position.x < support.Bounds.xMax - capsule.bounds.extents.x - 0.2f;
                if (target.Action == CampaignLayout.TraversalAction.RingRelay && landed && relayLaunchedAnchors.Count != 3)
                { Fail("ring relay landed without three observed distinct normal-input anchor launches"); yield break; }
                bool alternateLanding = grounded && targetIndex < layout.Route.Count - 1 &&
                    (target.Action == CampaignLayout.TraversalAction.Walk || target.Action == CampaignLayout.TraversalAction.Jump) &&
                    feet > target.FeetPosition.y + 0.14f && feet <= target.FeetPosition.y + 3.75f &&
                    Mathf.Abs(body.position.x - target.FeetPosition.x) < 0.8f &&
                    layout.Platforms.Any(candidate => candidate.Optional && candidate.OneWay &&
                        Mathf.Abs(candidate.SurfaceY - feet) < 0.14f &&
                        body.position.x > candidate.Bounds.xMin + capsule.bounds.extents.x + 0.12f &&
                        body.position.x < candidate.Bounds.xMax - capsule.bounds.extents.x - 0.12f);
                landed |= alternateLanding;
                bool overlappingNextSupport = false;
                if (grounded && targetIndex + 1 < layout.Route.Count && !target.RequiredAbility.HasValue &&
                    (target.Action == CampaignLayout.TraversalAction.Walk || target.Action == CampaignLayout.TraversalAction.Jump))
                {
                    CampaignLayout.RouteNode next = layout.Route[targetIndex + 1];
                    CampaignLayout.Platform nextSupport = layout.Platforms[next.PlatformIndex];
                    overlappingNextSupport = !next.RequiredAbility.HasValue && next.RoomIndex == target.RoomIndex &&
                        (next.Action == CampaignLayout.TraversalAction.Walk || next.Action == CampaignLayout.TraversalAction.Jump) &&
                        nextSupport.Bounds.Contains(new Vector2(target.FeetPosition.x, nextSupport.Bounds.center.y)) &&
                        Mathf.Abs(feet - nextSupport.SurfaceY) < 0.14f &&
                        body.position.x > nextSupport.Bounds.xMin + capsule.bounds.extents.x + 0.12f &&
                        body.position.x < nextSupport.Bounds.xMax - capsule.bounds.extents.x - 0.12f;
                    landed |= overlappingNextSupport;
                }
                stable = landed ? stable + 1 : 0;
                bool onPad = targetIndex + 1 < layout.Route.Count &&
                    layout.Route[targetIndex + 1].Action == CampaignLayout.TraversalAction.JumpPad &&
                    Mathf.Abs(body.position.x - target.FeetPosition.x) < 1.1f && body.linearVelocity.y > 14.1f;
                if ((stable >= 2 || onPad) && targetIndex < layout.Route.Count - 1)
                {
                    if (onPad) padLaunches++;
                    Debug.Log($"PIRATE_ROUTE_NODE chapter={chapter} node={targetIndex}/{layout.Route.Count - 1} " +
                              $"action={target.Action} position={body.position} grounded={player.IsGrounded} " +
                              $"alternateLanding={alternateLanding} overlappingNextSupport={overlappingNextSupport}");
                    highestReached = Mathf.Max(highestReached, targetIndex);
                    if (target.RequiredAbility.HasValue || targetIndex % 15 == 0)
                        Capture($"chapter-{chapter}-node-{targetIndex:000}.png");
                    targetIndex++;
                    ResetNodeState();
                    target = layout.Route[targetIndex];
                    support = layout.Platforms[target.PlatformIndex];
                }

                float steeringX = OrdinarySteeringX(target);
                float axis = cannonLandingSteering ? CannonLandingAxis(player, body, steeringX) : AxisTo(steeringX);
                bool jump = false;
                bool jumpReleased = false;
                bool dash = false;
                bool attach = false;
                bool hold = false;
                bool slide = false;
                switch (target.Action)
                {
                    case CampaignLayout.TraversalAction.Chain:
                    case CampaignLayout.TraversalAction.Grapple:
                    case CampaignLayout.TraversalAction.SpringGrapple:
                        DriveHook(target, ref axis, ref jump, ref attach, ref hold, ref dash);
                        break;
                    case CampaignLayout.TraversalAction.RingRelay:
                        DriveRingRelay(target, ref axis, ref jump, ref attach, ref hold);
                        break;
                    case CampaignLayout.TraversalAction.Saber:
                        if (frameAtNode % 18 == 0) { abilities.SetAutomationAttackPressed(); attacks++; }
                        break;
                    case CampaignLayout.TraversalAction.SaberSlide:
                        float slideDirection = Mathf.Sign(target.FeetPosition.x - Previous.FeetPosition.x);
                        CampaignLayout.Spawn slideBed = layout.Spawns.Find(item =>
                            item.Kind == CampaignLayout.SpawnKind.Spikes && item.NodeIndex == targetIndex - 1);
                        if (slideBed == null) { Fail("slide node omitted its associated real spike field"); yield break; }
                        float bedExit = slideBed.Position.x + slideDirection * slideBed.Size.x * .5f;
                        slide = StillCrossingSlideBed(body.position.x, capsule.bounds.extents.x, bedExit, slideDirection);
                        axis = slide ? slideDirection : AxisTo(target.FeetPosition.x);
                        if (!slide && !nodeJumped && ShouldSettleSlideExit(layout, targetIndex) &&
                            player.IsGrounded && HasActualSupportContact(support, out _, out _))
                        { jump = true; nodeJumped = true; }
                        break;
                    case CampaignLayout.TraversalAction.DoubleJump:
                    case CampaignLayout.TraversalAction.DoubleWindow:
                        if (landedBackOnPrevious) { nodeJumped = false; secondJumped = false; }
                        if (!nodeJumped && player.IsGrounded) { jump = true; nodeJumped = true; }
                        else if (nodeJumped && !secondJumped && body.linearVelocity.y < 1.5f && !player.IsGrounded)
                        {
                            bool pastHood = target.Action != CampaignLayout.TraversalAction.DoubleWindow ||
                                (body.position.x - Previous.FeetPosition.x) * Mathf.Sign(target.FeetPosition.x - Previous.FeetPosition.x) > 1.9f;
                            if (pastHood) { jump = true; secondJumped = true; }
                        }
                        if (Previous.Action == CampaignLayout.TraversalAction.SaberSlide && (frameAtNode < 20 || jump))
                            Debug.Log($"PIRATE_ROUTE_SLIDE_DOUBLE_TRANSITION node={targetIndex} frame={frameAtNode} position={body.position} " +
                                $"velocity={body.linearVelocity} grounded={player.IsGrounded} jumpRequested={jump} " +
                                $"firstRequested={nodeJumped} secondRequested={secondJumped} inputOnly=True");
                        break;
                    case CampaignLayout.TraversalAction.Spring:
                    case CampaignLayout.TraversalAction.SpringDrop:
                    case CampaignLayout.TraversalAction.SpringDouble:
                        DriveSpring(target, landedBackOnPrevious, previousSupport, ref axis, ref jump, ref dash, ref jumpReleased);
                        break;
                    case CampaignLayout.TraversalAction.JumpPad:
                        break;
                    default:
                        bool needsJump = target.FeetPosition.y > feet + 0.18f ||
                            target.Action == CampaignLayout.TraversalAction.Jump && target.FeetPosition.y >= feet - 0.1f &&
                            !HasContinuousWalkTo(target);
                        if (player.IsGrounded && needsJump && (!nodeJumped || landedBackOnPrevious || frameAtNode % 60 == 0))
                        { jump = true; nodeJumped = true; }
                        break;
                }

                if (frameAtNode % 3 == 0 && ShouldSwingAtThreat())
                {
                    abilities.SetAutomationAttackPressed();
                    attacks++;
                }

                if (!jump && !target.RequiredAbility.HasValue &&
                    (target.Action == CampaignLayout.TraversalAction.Walk || target.Action == CampaignLayout.TraversalAction.Jump))
                {
                    if (player.IsGrounded && (ShouldJumpCannonBody(target) || ShouldJumpCannonWarning(target))) { jump = true; nodeJumped = true; }
                    else if (!player.IsGrounded && !player.IsDashing && TryAirDashOverIncomingBall(target, out float evadeDirection))
                    {
                        dash = true; axis = evadeDirection; dashInputs++;
                        Debug.Log($"PIRATE_ROUTE_DASH_REQUEST chapter={layout.Chapter} node={targetIndex} " +
                                  $"position={body.position} velocity={body.linearVelocity} axis={axis:F0} keyboardShift=True");
                    }
                }

                if (jump && player.IsGrounded && cannonLandingSteering &&
                    WaitForCannonLandingWarning(target))
                {
                    jump = false; axis = 0f; nodeJumped = false;
                }
                if (jump && player.IsGrounded && narrowFarPocketApproach &&
                    Mathf.Abs(body.linearVelocity.x) > 1.2f)
                {
                    jump = false; axis = 0f; nodeJumped = false;
                }
                if (jump && player.IsGrounded && !target.RequiredAbility.HasValue &&
                    (target.Action == CampaignLayout.TraversalAction.Walk || target.Action == CampaignLayout.TraversalAction.Jump) &&
                    JumpWouldMeetProjectile(target))
                {
                    jump = false; nodeJumped = false;
                    axis = ChasingBallThreatensJump(target) ? 0f : EvadeAxisAlongSupport(support, target);
                    if (frameAtNode % 20 == 0) Debug.Log($"PIRATE_ROUTE_WAIT_FOR_SHOT chapter={chapter} node={targetIndex}");
                }
                if (jump) jumps++;
                if (grapple.IsAttached && !wasAttached) attaches++;
                wasAttached = grapple.IsAttached;
                player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump, jumpReleased: jumpReleased, dashPressed: dash);
                grapple.SetAutomationInputOverride(hold, attach);
                abilities.SetAutomationSlide(slide);
                yield return null;
                frameAtNode++;
                if (frameAtNode > (target.Action == CampaignLayout.TraversalAction.Chain || target.Action == CampaignLayout.TraversalAction.Grapple ||
                    target.Action == CampaignLayout.TraversalAction.SpringGrapple || target.Action == CampaignLayout.TraversalAction.RingRelay ? 850 : 500))
                { Fail("node timeout; actual movement did not reach the next support"); yield break; }
            }
            abilities.SpringBounced -= CountSpring;
            abilities.SaberSwung -= CountSaber;
            if (!chapterComplete) { Fail("chapter did not reach its physical exit"); yield break; }
            if (chapter == 0 && (attaches <= chapterAttaches || attacks <= chapterAttacks))
            { Fail("first chapter exited without exercising both chain and saber inputs"); yield break; }
            if (maximumX - minimumX < 55f || shotsSeen == 0)
            { Fail("route did not exercise horizontal traversal and active cannons"); yield break; }
            Debug.Log($"PIRATE_ROUTE_CHAPTER_SUCCESS seed={layout.Seed} chapter={chapter} signature={signature} routeNodes={layout.Route.Count} " +
                $"simulationSeconds={Time.time - chapterSimulationStarted:F2} humanDurationProof=False " +
                      $"deaths={life.DeathCount - deathsAtStart} normalCheckpointRetries={normalRespawns} horizontalSpan={maximumX - minimumX:F2} jumps={jumps} saberInputs={attacks} " +
                      $"actualSaberSwings={saberSwings} snaresCut={FindObjectsByType<SnareTrap>(FindObjectsSortMode.None).Count(trap => trap.IsCut)} " +
                      $"plantsDefeated={FindObjectsByType<HangingPlant>(FindObjectsSortMode.None).Count(plant => plant.IsDead)} " +
                      $"scoutFlights={scoutFlights} scoutDistance={scoutDistance:F2} " +
                      $"grappleAttachments={attaches} actualRopeJumps={ropeJumps} springBounces={springBounces} " +
                      $"padLaunches={padLaunches} recoveryPadsUsed={recoveryPadsUsed} floorRecoveryAttempts={floorRecoveryAttempts} retreatReplays={retreatReplays} dashInputs={dashInputs} actualDashes={actualDashes} " +
                      $"cannonShotsThisChapter={cannons.Where(cannon => cannon != null).Sum(cannon => cannon.ShotsFired)} cannonShotsObservedMax={shotsSeen} " +
                      $"closestCannonball={closestCannonball:F2} testTeleports=0 hazardsDisabled=0 exitBypass=0 " +
                      TidePhaseMetricFields());
            if (chapter + 1 < chapters)
                while (SceneManager.GetActiveScene().buildIndex == initialScene) yield return null;
        }
        if (!done)
        {
            if (PirateTestModalAcknowledger.InvariantFailures != 0) { Fail("tutorial modal did not safely block input/time"); yield break; }
            done = true;
            Debug.Log("PIRATE_ROUTE_PLAYTEST_SUCCESS chapters=" + chapters + " actualProductionPhysics=True");
            Application.Quit(0);
        }
    }

    private CampaignLayout.RouteNode Previous => layout.Route[Mathf.Max(0, targetIndex - 1)];

    private void ResetTidePhaseMetrics(int chapter, int scene)
    {
        tideMetricsChapter = chapter;
        tideMetricsScene = scene;
        actualTideRisingSeconds = scoutingPauseSeconds = 0d;
        minimumTideFeetWaterGap = float.PositiveInfinity;
        tideRisingSampleFrames = tideScoutingSampleFrames = 0;
        if (chapter == 3)
            Debug.Log("PIRATE_ROUTE_TIDE_METRIC_DEFINITIONS risingSeconds=sumScaledDeltaOfEligibleRisingUpdateFrames " +
                "minFeetWaterGap=minActualCapsuleFeetMinusActualSurfaceInSameFrames " +
                "scoutingPauseSeconds=sumScaledDeltaOfActiveAliveScoutingUpdateFramesIncludingCalm " +
                "excluded=paused_respawning_exitProtected_inactive_otherScenes " +
                "sampledFrameMetrics=True humanDifficultyProof=False");
    }

    private void Update()
    {
        if (done || tideMetricsChapter != 3 || SceneManager.GetActiveScene().buildIndex != tideMetricsScene ||
            flow == null || !flow.IsInitialized || flow.ChapterIndex != 3 ||
            layout == null || layout.Chapter != 3 || flow.Generator == null || flow.Generator.Campaign != layout ||
            Time.timeScale <= 0f || life == null || life.IsRespawning || life.IsExitProtected ||
            body == null || !body.simulated || capsule == null || !capsule.enabled) return;
        BlackTide tide = flow.Tide;
        if (tide == null || !tide.IsActive) return;
        float delta = Time.deltaTime;
        if (delta <= 0f || float.IsNaN(delta) || float.IsInfinity(delta)) return;
        if (tide.IsScoutingPaused || abilities != null && abilities.IsScouting)
        {
            scoutingPauseSeconds += delta;
            tideScoutingSampleFrames++;
            return;
        }
        if (tide.CalmRemainingSeconds > 0f) return;
        float gap = capsule.bounds.min.y - tide.SurfaceY;
        if (float.IsNaN(gap) || float.IsInfinity(gap)) return;
        actualTideRisingSeconds += delta;
        minimumTideFeetWaterGap = Mathf.Min(minimumTideFeetWaterGap, gap);
        tideRisingSampleFrames++;
    }

    private string TidePhaseMetricFields()
    {
        string gap = tideRisingSampleFrames > 0 ? minimumTideFeetWaterGap.ToString("F3",
            System.Globalization.CultureInfo.InvariantCulture) : "not-sampled";
        return $"actualTideRisingSeconds={actualTideRisingSeconds:F3} minFeetWaterGap={gap} " +
            $"scoutingPauseSeconds={scoutingPauseSeconds:F3} tideRisingSampleFrames={tideRisingSampleFrames} " +
            $"tideScoutingSampleFrames={tideScoutingSampleFrames} " +
            "tideMetricScope=CrownActiveAlivePhaseFrames sampledFrameMetrics=True humanDifficultyProof=False";
    }

    private void DriveHook(CampaignLayout.RouteNode target, ref float axis, ref bool jump, ref bool attach, ref bool hold, ref bool dash)
    {
        float direction = Mathf.Sign(target.FeetPosition.x - Previous.FeetPosition.x);
        CampaignLayout.Platform takeoff = layout.Platforms[Previous.PlatformIndex];
        HookAnchor anchor = FindObjectsByType<HookAnchor>(FindObjectsSortMode.None)
            .OrderBy(item => Vector2.Distance(item.transform.position, (target.FeetPosition + Previous.FeetPosition) * 0.5f)).FirstOrDefault();
        if (anchor == null) return;
        axis = direction;
        CampaignLayout.Platform targetWall = layout.Platforms[target.PlatformIndex];
        float nearWallX = direction > 0 ? targetWall.Bounds.xMin : targetWall.Bounds.xMax;
        float stagingX = nearWallX - direction * 1.5f;
        bool springRise = target.Action == CampaignLayout.TraversalAction.SpringGrapple;
        bool rangedRise = target.Action == CampaignLayout.TraversalAction.Grapple || springRise;
        bool hangingChain = target.Action == CampaignLayout.TraversalAction.Chain;
        if (rangedRise && !springRise)
            stagingX = HookStagingX(takeoff.Bounds, targetWall.Bounds, direction, capsule.bounds.extents.x);
        if (chainPhase == 0)
        {
            float edge = direction > 0 ? takeoff.Bounds.xMax - 0.55f : takeoff.Bounds.xMin + 0.55f;
            bool ready = (body.position.x - edge) * direction >= -0.12f;
            if (rangedRise)
            {
                axis = springRise ? AxisTo(stagingX) : RangedHookApproachAxis(stagingX);
                ready = RangedHookTakeoffReady(HasActualSupportContact(takeoff, out _, out _),
                    Previous.Action == CampaignLayout.TraversalAction.SaberSlide, body.position.x, stagingX, body.linearVelocity.x);
            }
            if (springRise)
            {
                axis = direction;
                if (springBounces > springsAtNode && !player.IsGrounded && body.linearVelocity.y > 1f)
                { chainPhase = 1; nodeJumped = true; }
            }
            else if (player.IsGrounded && ready)
            {
                jump = true; nodeJumped = true; chainPhase = 1;
                Debug.Log($"PIRATE_ROUTE_HOOK_TAKEOFF chapter={layout.Chapter} node={targetIndex} position={body.position} " +
                    $"velocity={body.linearVelocity} stagingX={stagingX:F3} fromSlide={Previous.Action == CampaignLayout.TraversalAction.SaberSlide} " +
                    "actualPreviousSupportRequiredForRanged=True ordinarySpace=True");
            }
        }
        if (chainPhase == 1)
        {
            hold = true;
            if (rangedRise) axis = springRise ? AxisTo(stagingX) : RangedHookApproachAxis(stagingX);
            else if (hangingChain) axis = AxisTo(anchor.transform.position.x);
            float reach = abilities.HookLevel >= 2
                ? Vector2.Distance(body.position, anchor.transform.position)
                : Vector2.Distance(body.position, anchor.NearestChainPoint(body.position));
            float minGrabAboveTakeoff = rangedRise ? 0.6f : 0.15f;
            bool airborne = !player.IsGrounded && capsule.bounds.min.y > Previous.FeetPosition.y + minGrabAboveTakeoff;
            float grabHeight = Mathf.Min(target.FeetPosition.y - 1.8f, Previous.FeetPosition.y + 1.8f);
            bool readyToGrab = airborne && (!rangedRise || capsule.bounds.min.y > grabHeight);
            if (hangingChain && !chainDashUsed && !player.IsDashing && !grapple.IsAttached && airborne &&
                body.linearVelocity.y < 5f && reach > grapple.GrabRange + 0.35f)
            {
                float horizontal = (anchor.transform.position.x - body.position.x) * direction;
                if (horizontal > 0.35f && horizontal < 6.2f)
                {
                    dash = true;
                    chainDashUsed = true;
                    axis = direction;
                    dashInputs++;
                    Debug.Log($"PIRATE_ROUTE_HOOK_DASH chapter={layout.Chapter} node={targetIndex} position={body.position} " +
                              $"velocity={body.linearVelocity} reach={reach:F2} horizontal={horizontal:F2} ordinaryShift=True");
                }
            }
            if (!grapple.IsAttached && !player.IsDashing && readyToGrab && reach < grapple.GrabRange - 0.03f) attach = true;
            if (grapple.IsAttached)
            {
                chainPhase = 2;
                Debug.Log($"PIRATE_ROUTE_HOOK_ATTACHED chapter={layout.Chapter} node={targetIndex} position={body.position} rope={grapple.RopeLength:F2}");
            }
        }
        if (chainPhase == 2)
        {
            hold = true;
            if (!grapple.IsAttached)
            {
                chainPhase = 1;
                if (rangedRise) axis = springRise ? AxisTo(stagingX) : RangedHookApproachAxis(stagingX);
                else if (hangingChain) axis = AxisTo(anchor.transform.position.x);
                return;
            }
            float travelBeyondPivot = (body.position.x - anchor.transform.position.x) * direction;
            bool highEnough = capsule.bounds.min.y > target.FeetPosition.y - 1.5f;
            bool release = travelBeyondPivot > 3.7f && highEnough && body.linearVelocity.y > -0.2f;
            if (rangedRise)
            {
                float releaseHeight = Mathf.Min(target.FeetPosition.y - 1.8f, Previous.FeetPosition.y + 2.3f);
                release = capsule.bounds.min.y > releaseHeight && body.linearVelocity.y > -1f;
            }
            else if (hangingChain)
            {
                float remaining = (target.FeetPosition.x - body.position.x) * direction;
                bool forwardArc = body.linearVelocity.x * direction > 2.4f && body.linearVelocity.y > -1.1f;
                release = highEnough && forwardArc && travelBeyondPivot > 2.2f &&
                    remaining > 0.25f && remaining < 8.5f;
                if (!release && highEnough && forwardArc && travelBeyondPivot > 4.2f && remaining > 0.25f)
                    release = true;
            }
            if (release)
            {
                jump = true; hold = true; chainPhase = 3; pendingRopeJump = true;
                Debug.Log($"PIRATE_ROUTE_HOOK_SPACE chapter={layout.Chapter} node={targetIndex} position={body.position} velocity={body.linearVelocity}");
            }
        }
        if (chainPhase == 3)
        {
            axis = AxisTo(target.FeetPosition.x);
            hold = grapple.IsAttached;
            if (pendingRopeJump && !grapple.IsAttached && body.linearVelocity.y > player.JumpLaunchSpeed - 2f)
            {
                pendingRopeJump = false;
                ropeJumps++;
                Debug.Log($"PIRATE_ROUTE_HOOK_JUMP_FIRED chapter={layout.Chapter} node={targetIndex} position={body.position} velocity={body.linearVelocity}");
            }
        }
    }

    internal static float HookStagingX(Rect takeoff, Rect landing, float direction, float halfWidth)
    {
        float nearWall = direction > 0f ? landing.xMin : landing.xMax;
        float requested = nearWall - direction * 1.5f;
        float inset = halfWidth + OrdinaryLandingSteeringInset;
        float left = takeoff.xMin + inset, right = takeoff.xMax - inset;
        return left <= right ? Mathf.Clamp(requested, left, right) : takeoff.center.x;
    }

    private static bool RangedHookTakeoffReady(bool actualPreviousContact, bool fromSlide, float x, float stagingX, float velocity)
        => actualPreviousContact && (fromSlide || Mathf.Abs(x - stagingX) < .22f && Mathf.Abs(velocity) < 1.5f);

    private float RangedHookApproachAxis(float stagingX)
    {
        if ((stagingX - body.position.x) * body.linearVelocity.x < 0f && Mathf.Abs(body.linearVelocity.x) > 1.5f)
            return 0f;
        return AxisTo(stagingX);
    }

    internal readonly struct HookFixtureInput
    {
        internal readonly float Axis;
        internal readonly bool Jump, Attach, Hold;
        internal readonly int Phase;
        internal HookFixtureInput(float axis, bool jump, bool attach, bool hold, int phase)
        { Axis = axis; Jump = jump; Attach = attach; Hold = hold; Phase = phase; }
    }

    private void DriveSpring(CampaignLayout.RouteNode target, bool landedBackOnPrevious,
        CampaignLayout.Platform previousSupport, ref float axis, ref bool jump, ref bool dash,
        ref bool jumpReleased)
    {
        axis = Mathf.Sign(target.FeetPosition.x - Previous.FeetPosition.x);
        axis = SpringTraversalAxis(springBounces > springsAtNode, axis, AxisTo(target.FeetPosition.x));
        if (springBounces == springsAtNode && TryFindSpringReturnBed(layout, targetIndex, out Vector2 returnBed))
        {
            axis = AxisTo(returnBed.x);
            if (landedBackOnPrevious) nodeJumped = false;
            if (!nodeJumped && OnSafeSupport(previousSupport))
            {
                jump = true;
                nodeJumped = true;
                Debug.Log($"PIRATE_ROUTE_SPRING_RETURN_APPROACH_JUMP chapter={layout.Chapter} node={targetIndex} " +
                    $"position={body.position} velocity={body.linearVelocity} spikeTop={returnBed} " +
                    "ordinarySpaceRequest=True actualBounceStillRequired=True");
            }
        }
        if (target.Action == CampaignLayout.TraversalAction.SpringDouble && springBounces > springsAtNode &&
            !secondJumped && !player.IsGrounded && body.linearVelocity.y < 1.5f)
        { jump = true; secondJumped = true; axis = AxisTo(target.FeetPosition.x); }
        if (target.Action == CampaignLayout.TraversalAction.SpringDrop)
            DriveSpringDropChute(previousSupport, landedBackOnPrevious, ref axis, ref jump, ref jumpReleased, ref dash);
    }

    private void DriveSpringDropChute(CampaignLayout.Platform takeoff, bool landedBackOnPrevious,
        ref float axis, ref bool jump, ref bool jumpReleased, ref bool dash)
    {
        float direction = Mathf.Sign(layout.Route[targetIndex].FeetPosition.x - Previous.FeetPosition.x);
        if (direction == 0f) direction = 1f;
        if (landedBackOnPrevious) nodeJumped = false;
        bool bounced = springBounces > springsAtNode;
        if (bounced) return;
        jump = false;
        jumpReleased = false;
        float startX = SpringDropDashStartX(takeoff.Bounds, direction, player.DashSpeed, player.DashDuration);
        bool onTakeoff = player.IsGrounded && Mathf.Abs(capsule.bounds.min.y - takeoff.SurfaceY) < 0.14f &&
            capsule.bounds.max.x > takeoff.Bounds.xMin && capsule.bounds.min.x < takeoff.Bounds.xMax;
        if (!nodeJumped && onTakeoff && !player.IsDashing)
        {
            axis = AxisTo(startX);
            bool ready = Mathf.Abs(body.position.x - startX) < 0.22f && Mathf.Abs(body.linearVelocity.x) < 1.8f;
            if (ready)
            {
                dash = true;
                axis = direction;
                nodeJumped = true;
                dashInputs++;
                Debug.Log($"PIRATE_ROUTE_SPRING_DROP_RUNWAY_DASH chapter={layout.Chapter} node={targetIndex} " +
                    $"position={body.position} velocity={body.linearVelocity} startX={startX:F3} " +
                    "keyboardShift=True holdUntilSlow=True actualBounceStillRequired=True");
            }
            return;
        }
        if (onTakeoff || player.IsDashing)
        {
            axis = direction;
            return;
        }
        axis = 0f;
        if (frameAtNode % 15 == 0)
            Debug.Log($"PIRATE_ROUTE_SPRING_DROP_FAR_EDGE chapter={layout.Chapter} node={targetIndex} " +
                $"position={body.position} velocity={body.linearVelocity} " +
                "coastUntilBounce=True resumeForwardRejected=True actualBounceStillRequired=True");
    }

    internal static float SpringDropDashStartX(Rect takeoff, float direction, float dashSpeed, float dashDuration)
    {
        float lip = direction > 0f ? takeoff.xMax : takeoff.xMin;
        return lip - direction * dashSpeed * dashDuration;
    }

    internal static float SpringDropLaunchX(Rect takeoff, float direction)
        => SpringDropDashStartX(takeoff, direction, 18f, 0.16f);

    internal static bool SpringDropLipJumpReady(float x, float launchX, float lipX, float direction)
    {
        bool atStart = (x - launchX) * direction >= -0.22f && (x - launchX) * direction <= 0.22f;
        bool beforeLip = (x - lipX) * direction < 0f;
        return atStart && beforeLip;
    }

    internal void ConfigureSpringFixture(CampaignLayout fixtureMap, int fixtureNode, PlayerMovement fixturePlayer)
    {
        if (enabled || !Environment.GetCommandLineArgs().Contains("-pirateQuestSlideSpringTest"))
            throw new InvalidOperationException("Spring fixture context requires a disabled opt-in test driver.");
        if (fixtureMap == null || fixtureNode <= 0 || fixtureNode >= fixtureMap.Route.Count || fixturePlayer == null ||
            fixtureMap.Route[fixtureNode].Action != CampaignLayout.TraversalAction.Spring)
            throw new ArgumentException("A spring route node and actual fixture player are required.");
        layout = fixtureMap; targetIndex = fixtureNode; player = fixturePlayer;
        body = player.GetComponent<Rigidbody2D>(); capsule = player.GetComponent<Collider2D>();
        springBounces = 0;
        ResetNodeState();
    }

    internal HookFixtureInput StepSpringFixture(bool observedBounce)
    {
        if (enabled || !Environment.GetCommandLineArgs().Contains("-pirateQuestSlideSpringTest") || layout == null)
            throw new InvalidOperationException("Spring fixture step requires its disabled configured opt-in driver.");
        springBounces = observedBounce ? 1 : 0;
        float axis = 0f; bool jump = false; bool dash = false; bool jumpReleased = false;
        DriveSpring(layout.Route[targetIndex], false, layout.Platforms[Previous.PlatformIndex], ref axis, ref jump, ref dash, ref jumpReleased);
        frameAtNode++;
        return new HookFixtureInput(axis, jump, false, false, 0);
    }

    internal void ConfigureHookFixture(CampaignLayout fixtureMap, int fixtureNode, PlayerMovement fixturePlayer)
    {
        if (enabled || !Environment.GetCommandLineArgs().Contains("-pirateQuestHookEdgeTest"))
            throw new InvalidOperationException("Hook fixture context requires a disabled opt-in test driver.");
        if (fixtureMap == null || fixtureNode <= 0 || fixtureNode >= fixtureMap.Route.Count || fixturePlayer == null ||
            fixtureMap.Route[fixtureNode].Action != CampaignLayout.TraversalAction.Grapple)
            throw new ArgumentException("A ranged-hook route node and actual fixture player are required.");
        layout = fixtureMap; targetIndex = fixtureNode; player = fixturePlayer;
        body = player.GetComponent<Rigidbody2D>(); capsule = player.GetComponent<Collider2D>();
        abilities = player.GetComponent<PlayerAbilities>(); grapple = player.GetComponent<PlayerGrapple>();
        life = player.GetComponent<PlayerLife>();
        ResetNodeState();
    }

    internal HookFixtureInput StepHookFixture()
    {
        if (enabled || !Environment.GetCommandLineArgs().Contains("-pirateQuestHookEdgeTest") || layout == null)
            throw new InvalidOperationException("Hook fixture step requires its disabled configured opt-in driver.");
        float axis = 0f; bool jump = false, attach = false, hold = false, dash = false;
        DriveHook(layout.Route[targetIndex], ref axis, ref jump, ref attach, ref hold, ref dash);
        frameAtNode++;
        return new HookFixtureInput(axis, jump, attach, hold, chainPhase);
    }

    private float AxisTo(float targetX)
        => OrdinaryLandingAxis(player, body, targetX);

    internal static float OrdinaryLandingAxis(PlayerMovement movement, Rigidbody2D rigidbody, float targetX)
    {
        float distance = targetX - rigidbody.position.x;
        float braking = movement.IsGrounded ? movement.Deceleration : movement.AirDeceleration;
        float stopping = rigidbody.linearVelocity.x * rigidbody.linearVelocity.x / (2f * Mathf.Max(1f, braking));
        bool brake = distance * rigidbody.linearVelocity.x > 0f && stopping >= Mathf.Abs(distance) - 0.12f;
        return Mathf.Abs(distance) < 0.12f || brake ? 0f : Mathf.Sign(distance);
    }

    internal static bool CannonLandingNeedsSetup(float preferredX, float safeX)
        => Mathf.Abs(preferredX - safeX) > .01f;

    internal static float CannonLandingAxis(PlayerMovement movement, Rigidbody2D rigidbody, float targetX)
        => CannonLandingAxis(rigidbody.position.x, rigidbody.linearVelocity.x, targetX,
            movement.MoveSpeed, Mathf.Min(movement.Deceleration, movement.AirDeceleration), Time.fixedDeltaTime);

    private static float CannonLandingAxis(float x, float velocity, float targetX, float maxSpeed, float braking, float step)
    {
        float distance = targetX - x;
        if (Mathf.Abs(distance) < .10f) return 0f;
        float speed = Mathf.Abs(velocity);
        float stopping = speed * speed / (2f * Mathf.Max(1f, braking));
        float queuedTravel = Mathf.Max(speed, maxSpeed) * step * 2f;
        return distance * velocity > 0f && stopping + queuedTravel >= Mathf.Abs(distance) - .10f
            ? 0f : Mathf.Sign(distance);
    }

    internal static bool StillCrossingSlideBed(float x, float halfWidth, float bedExit, float direction)
        => (x - bedExit) * direction <= halfWidth + .025f;

    internal static bool ShouldSettleSlideExit(CampaignLayout map, int slideIndex)
    {
        if (slideIndex < 0 || slideIndex >= map.Route.Count ||
            map.Route[slideIndex].Action != CampaignLayout.TraversalAction.SaberSlide) return false;
        if (slideIndex + 1 >= map.Route.Count) return true;
        CampaignLayout.RouteNode next = map.Route[slideIndex + 1];
        return next.RoomIndex != map.Route[slideIndex].RoomIndex ||
            !next.RequiredAbility.HasValue && !next.SecondaryAbility.HasValue;
    }

    private void ResetNodeState()
    {
        frameAtNode = stable = chainPhase = 0;
        chainDashUsed = false;
        wasAirborneAtNode = false;
        actualDashTarget = -1;
        lastDashDecisionNode = -1;
        nodeJumped = secondJumped = false;
        narrowFarPocketApproach = false;
        cannonLandingWarningSeen = false;
        cannonLandingWaitStarted = -1;
        wasAttached = false;
        springsAtNode = springBounces;
        springNonRisingLogged = springLandingLogged = false;
        pendingRopeJump = false;
        relayPendingAnchor = null;
        relayLaunchedAnchors.Clear();
    }

    private void DriveRingRelay(CampaignLayout.RouteNode target, ref float axis, ref bool jump, ref bool attach, ref bool hold)
    {
        axis = AxisTo(target.FeetPosition.x);
        if (relayPendingAnchor != null && !grapple.IsAttached && !grapple.IsAnchorJumpAvailable(relayPendingAnchor) &&
            body.linearVelocity.y > PlayerGrapple.AnchorJumpLaunchSpeed - 2f)
        {
            relayLaunchedAnchors.Add(relayPendingAnchor);
            ropeJumps++;
            Debug.Log($"PIRATE_ROUTE_RELAY_LAUNCH chapter={layout.Chapter} node={targetIndex} count={relayLaunchedAnchors.Count} " +
                $"anchor={relayPendingAnchor.transform.position} position={body.position} velocity={body.linearVelocity} normalRmbSpace=True");
            relayPendingAnchor = null;
            chainPhase = 1;
        }
        if (!nodeJumped && player.IsGrounded)
        {
            if (Mathf.Abs(body.position.x - Previous.FeetPosition.x) < .2f && Mathf.Abs(body.linearVelocity.x) < 1f)
            { jump = true; nodeJumped = true; chainPhase = 1; }
            else axis = AxisTo(Previous.FeetPosition.x);
            return;
        }
        if (grapple.IsAttached)
        {
            hold = true;
            HookAnchor current = grapple.CurrentAnchor;
            if (relayPendingAnchor == null && grapple.IsAnchorJumpAvailable(current) &&
                body.position.y > current.transform.position.y - .8f && body.linearVelocity.y > -.5f)
            {
                jump = true;
                relayPendingAnchor = current;
            }
            return;
        }
        if (relayLaunchedAnchors.Count >= 3) return;
        if (nodeJumped && !player.IsGrounded && capsule.bounds.min.y > Previous.FeetPosition.y + .6f &&
            relayPendingAnchor == null && frameAtNode % 2 == 0)
        {
            attach = hold = true;
        }
        else hold = relayPendingAnchor != null;
    }

    private void ObserveThreats()
    {
        int current = 0;
        foreach (Cannon cannon in cannons) if (cannon != null) current += cannon.ShotsFired;
        shotsSeen = Mathf.Max(shotsSeen, current);
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
            closestCannonball = Mathf.Min(closestCannonball, Vector2.Distance(ball.transform.position, body.position));
    }

    private bool HasContinuousWalkTo(CampaignLayout.RouteNode target)
    {
        if (!player.IsGrounded || target.FeetPosition.y > capsule.bounds.min.y + 0.15f) return false;
        CampaignLayout.Platform next = layout.Platforms[target.PlatformIndex];
        foreach (CampaignLayout.Platform current in layout.Platforms)
        {
            if (Mathf.Abs(current.SurfaceY - capsule.bounds.min.y) > 0.14f ||
                body.position.x < current.Bounds.xMin || body.position.x > current.Bounds.xMax) continue;
            if (current.Bounds.xMax >= next.Bounds.xMin - 0.03f && current.Bounds.xMin <= next.Bounds.xMax + 0.03f)
                return true;
        }
        return false;
    }

    private float OrdinarySteeringX(CampaignLayout.RouteNode target)
    {
        cannonLandingSteering = false;
        narrowFarPocketApproach = false;
        float targetX = target.FeetPosition.x;
        if (!target.RequiredAbility.HasValue &&
            (target.Action == CampaignLayout.TraversalAction.Walk || target.Action == CampaignLayout.TraversalAction.Jump))
        {
            CampaignLayout.Platform landing = layout.Platforms[target.PlatformIndex];
            float cannonMargin = capsule.bounds.extents.x + OrdinaryLandingSteeringInset;
            bool cannonAdjusted = false;
            foreach (Cannon cannon in cannons)
            {
                if (cannon == null || Mathf.Abs(cannon.transform.position.y - target.FeetPosition.y) > 2.4f ||
                    Mathf.Abs(cannon.transform.position.x - targetX) > 1.8f) continue;
                float forward = Mathf.Sign(target.FeetPosition.x - Previous.FeetPosition.x);
                if (forward == 0f) forward = 1f;
                targetX = Mathf.Clamp(cannon.transform.position.x + forward * 1.4f,
                    landing.Bounds.xMin + cannonMargin, landing.Bounds.xMax - cannonMargin);
                cannonAdjusted = true;
            }
            if (cannonAdjusted)
            {
                float preferred = targetX;
                var cannonBodies = new List<Rect>();
                foreach (Cannon cannon in cannons)
                {
                    Collider2D hitbox = cannon != null ? cannon.BodyHitbox : null;
                    if (hitbox == null || !hitbox.enabled || !hitbox.gameObject.activeInHierarchy) continue;
                    Bounds bounds = hitbox.bounds;
                    cannonBodies.Add(Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y));
                }
                bool safe = TryFindSafeLandingX(landing, layout.Platforms, capsule.bounds.extents.x,
                    capsule.bounds.size.y, preferred, out float safeX, out int blocker, cannonBodies);
                if (!safe)
                {
                    Fail("driver found no terrain-and-body-clear resting interval on the intended cannon support");
                    return body.position.x;
                }
                targetX = safeX;
                cannonLandingSteering = CannonLandingNeedsSetup(preferred, safeX);
                bool holdNarrowPocket = false;
                float half = capsule.bounds.extents.x;
                float inset = half + 0.2f;
                float travel = Mathf.Sign(targetX - body.position.x);
                if (Mathf.Abs(travel) < 0.1f) travel = Mathf.Sign(preferred - body.position.x);
                foreach (Rect obstacle in cannonBodies)
                {
                    if (obstacle.yMax <= landing.SurfaceY + 0.04f ||
                        obstacle.yMin >= landing.SurfaceY + capsule.bounds.size.y + 0.05f) continue;
                    if (obstacle.xMax < landing.Bounds.xMin + 0.05f ||
                        obstacle.xMin > landing.Bounds.xMax - 0.05f) continue;
                    float farEdge = travel > 0f
                        ? landing.Bounds.xMax - (half + OrdinaryLandingSteeringInset)
                        : landing.Bounds.xMin + (half + OrdinaryLandingSteeringInset);
                    float pocket = travel > 0f
                        ? farEdge - (obstacle.xMax + inset)
                        : (obstacle.xMin - inset) - farEdge;
                    bool between = (obstacle.center.x - body.position.x) * travel > 0.2f &&
                        (targetX - obstacle.center.x) * travel > -0.05f;
                    if (!between || pocket >= 0.45f) continue;
                    float near = travel > 0f ? obstacle.xMin - inset : obstacle.xMax + inset;
                    near = Mathf.Clamp(near, landing.Bounds.xMin + half + OrdinaryLandingSteeringInset,
                        landing.Bounds.xMax - half - OrdinaryLandingSteeringInset);
                    if ((near - body.position.x) * travel < -0.05f) continue;
                    bool alreadyOnLanding = OnSafeSupport(landing);
                    float stepUp = landing.SurfaceY - capsule.bounds.min.y;
                    if (!alreadyOnLanding && stepUp <= 0.20f) continue;
                    targetX = near;
                    holdNarrowPocket = true;
                    narrowFarPocketApproach = !alreadyOnLanding;
                    cannonLandingSteering = alreadyOnLanding || player.IsGrounded;
                    if (lastSteeringDecisionNode != targetIndex || Mathf.Abs(lastSteeringDecisionX - targetX) > .01f)
                    {
                        Debug.Log($"PIRATE_ROUTE_NARROW_FAR_POCKET chapter={layout.Chapter} node={targetIndex} " +
                            $"preferredX={preferred:F3} nearX={targetX:F3} pocket={pocket:F3} " +
                            $"cannon=({obstacle.xMin:F2},{obstacle.xMax:F2}) sameSupportHold={alreadyOnLanding} " +
                            $"approachHold={narrowFarPocketApproach}");
                        lastSteeringDecisionNode = targetIndex;
                        lastSteeringDecisionX = targetX;
                    }
                    break;
                }
                if (holdNarrowPocket)
                    return targetX;
                if (lastSteeringDecisionNode != targetIndex || Mathf.Abs(lastSteeringDecisionX - targetX) > .01f)
                {
                    Debug.Log($"PIRATE_ROUTE_STEERING_CLEARANCE chapter={layout.Chapter} node={targetIndex} " +
                        $"decision={(safe ? "same-support-clearance" : "retain-witness-no-free-interval")} " +
                        $"preferredX={preferred:F3} safeX={targetX:F3} blockingPlatform={blocker} " +
                        $"restingXMin={targetX - .12f:F3} restingXMax={targetX + .12f:F3} " +
                        $"strictLandingXMin={landing.Bounds.xMin + capsule.bounds.extents.x + .2f:F3} " +
                        $"strictLandingXMax={landing.Bounds.xMax - capsule.bounds.extents.x - .2f:F3} " +
                        $"support={landing.Bounds} cannonBodiesConsidered={cannonBodies.Count} " +
                        $"precisionLandingSetup={cannonLandingSteering} " +
                        "terrainAndBodyIntervalsCombined=True acceptanceUnchanged=True ordinaryKeyboardInput=True");
                    lastSteeringDecisionNode = targetIndex;
                    lastSteeringDecisionX = targetX;
                }
            }
        }
        if (!player.IsGrounded || target.RequiredAbility.HasValue || targetIndex + 1 >= layout.Route.Count ||
            (target.Action != CampaignLayout.TraversalAction.Walk && target.Action != CampaignLayout.TraversalAction.Jump))
            return targetX;
        CampaignLayout.RouteNode next = layout.Route[targetIndex + 1];
        CampaignLayout.Platform nextSupport = layout.Platforms[next.PlatformIndex];
        if (next.RequiredAbility.HasValue || next.RoomIndex != target.RoomIndex ||
            (next.Action != CampaignLayout.TraversalAction.Walk && next.Action != CampaignLayout.TraversalAction.Jump) ||
            !nextSupport.Bounds.Contains(new Vector2(targetX, nextSupport.Bounds.center.y)) ||
            Mathf.Abs(capsule.bounds.min.y - nextSupport.SurfaceY) > 0.14f)
            return targetX;
        float margin = capsule.bounds.extents.x + 0.36f;
        return Mathf.Clamp(targetX, nextSupport.Bounds.xMin + margin, nextSupport.Bounds.xMax - margin);
    }

    internal static bool TryFindSafeLandingX(CampaignLayout.Platform landing,
        IReadOnlyList<CampaignLayout.Platform> platforms, float halfWidth, float bodyHeight,
        float preferred, out float safeX, out int blocker, IReadOnlyList<Rect> lethalBodies = null)
    {
        safeX = preferred;
        blocker = -1;
        float margin = halfWidth + OrdinaryLandingSteeringInset;
        var free = new List<Vector2> { new Vector2(landing.Bounds.xMin + margin, landing.Bounds.xMax - margin) };
        if (free[0].x > free[0].y) return false;
        for (int i = 0; i < platforms.Count; i++)
        {
            CampaignLayout.Platform raised = platforms[i];
            if (raised == landing || raised.SurfaceY <= landing.SurfaceY + .14f ||
                raised.Bounds.yMin >= landing.SurfaceY + bodyHeight + .05f) continue;
            float left = raised.Bounds.xMin - halfWidth - .2f;
            float right = raised.Bounds.xMax + halfWidth + .2f;
            if (ExcludeLandingInterval(free, left, right)) blocker = i;
        }
        if (lethalBodies != null)
        {
            foreach (Rect obstacle in lethalBodies)
            {
                if (obstacle.yMax <= landing.SurfaceY + .04f ||
                    obstacle.yMin >= landing.SurfaceY + bodyHeight + .05f) continue;
                ExcludeLandingInterval(free, obstacle.xMin - halfWidth - .2f, obstacle.xMax + halfWidth + .2f);
            }
        }
        float best = float.PositiveInfinity;
        foreach (Vector2 range in free)
        {
            float candidate = Mathf.Clamp(preferred, range.x, range.y);
            float distance = Mathf.Abs(candidate - preferred);
            if (distance < best) { best = distance; safeX = candidate; }
        }
        return !float.IsPositiveInfinity(best);
    }

    private static bool ExcludeLandingInterval(List<Vector2> free, float left, float right)
    {
        bool changed = false;
        for (int interval = free.Count - 1; interval >= 0; interval--)
        {
            Vector2 range = free[interval];
            if (right <= range.x || left >= range.y) continue;
            changed = true;
            free.RemoveAt(interval);
            if (left > range.x) free.Add(new Vector2(range.x, Mathf.Min(left, range.y)));
            if (right < range.y) free.Add(new Vector2(Mathf.Max(right, range.x), range.y));
        }
        return changed;
    }

    private static bool RunDriverGeometrySelfChecks(out string detail)
    {
        var lower = new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(31.45f, 4.6f, 35.55f, 5f) };
        var raised = new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(35.03334f, 5.1f, 38.63334f, 5.5f) };
        bool avoidedCorner = TryFindSafeLandingX(lower, new[] { lower, raised }, .25f, 1f, 35f,
            out float safeX, out int blocker) && blocker == 1 && safeX <= 34.58335f &&
            safeX > lower.Bounds.xMin + .45f && safeX < lower.Bounds.xMax - .45f;
        bool unchangedClear = TryFindSafeLandingX(lower, new[] { lower }, .25f, 1f, 33.5f,
            out float clearX, out _) && Mathf.Abs(clearX - 33.5f) < .0001f;
        float gardenDropX = 23.043f - .25f - .12f;
        bool gardenDropInside = gardenDropX > 21.857f + .45f && gardenDropX < 24.857f - .45f;
        bool gardenEdgeOverlap = 23.263f + .25f > 23.043f && 23.263f - .25f < 25.643f;
        bool gardenNotInterior = !(23.263f > 23.043f + .25f + .12f);
        var garden182 = new CampaignLayout.Platform {
            Bounds = Rect.MinMaxRect(30.66667f, 103.32f, 33.66667f, 103.5f)
        };
        float acceptedMin = garden182.Bounds.xMin + .25f + .2f;
        float acceptedMax = garden182.Bounds.xMax - .25f - .2f;
        bool oldWindowEscaped = garden182.Bounds.xMax - .25f - .3f + .12f >= acceptedMax;
        bool restingWindowInside = TryFindSafeLandingX(garden182, new[] { garden182 }, .25f, 1f, 33.11667f,
            out float restingTarget, out _) && restingTarget - .12f > acceptedMin && restingTarget + .12f < acceptedMax;
        bool originalPointStillRejected = 33.2307f >= acceptedMax;
        bool telegraphWalk = RunTelegraphWalkSelfCheck(out string telegraphCheck);
        bool oneWayDrop = RunNearbyOneWayDropSelfCheck(out string dropCheck);
        bool cannonDownstep = RunCannonBodyDownstepSelfCheck(out string bodyCheck);
        bool springSteering = RunSpringSteeringSelfCheck(out string springCheck);
        bool combinedLanding = RunCannonLandingIntervalSelfCheck(out string landingCheck);
        bool returnApproach = RunSpringReturnApproachSelfCheck(out string returnCheck);
        bool springDropApproach = RunSpringDropApproachSelfCheck(out string dropChuteCheck);
        bool delayedBrake = RunCannonDelayedBrakeSelfCheck(out string delayedCheck);
        bool setupScope = RunCannonSetupScopeSelfCheck(out string setupCheck);
        bool slideHandover = RunSlideHandoverSelfCheck(out string handoverCheck);
        bool hookStage = RunHookStageSelfCheck(out string hookStageCheck);
        bool slideGuard = StillCrossingSlideBed(9.9f, .25f, 10f, 1f) &&
            StillCrossingSlideBed(10.2f, .25f, 10f, 1f) &&
            !StillCrossingSlideBed(10.28f, .25f, 10f, 1f) &&
            StillCrossingSlideBed(-10.2f, .25f, -10f, -1f) &&
            !StillCrossingSlideBed(-10.28f, .25f, -10f, -1f);
        detail = $"cornerAvoided={avoidedCorner} safeX={safeX:F3} clearUnchanged={unchangedClear} " +
            $"gardenDropInside={gardenDropInside} gardenDropX={gardenDropX:F3} " +
            $"gardenEdgeOverlap={gardenEdgeOverlap} strictInteriorStillFalse={gardenNotInterior} " +
            $"oldRestingWindowEscaped={oldWindowEscaped} restingWindowInside={restingWindowInside} " +
            $"restingTarget={restingTarget:F3} originalPointStillRejected={originalPointStillRejected} {telegraphCheck} {dropCheck} {bodyCheck} {springCheck} {landingCheck} {returnCheck} {dropChuteCheck} {delayedCheck} {setupCheck} {handoverCheck} {hookStageCheck} slideCompleteCapsuleGuard={slideGuard}";
        return avoidedCorner && unchangedClear && gardenDropInside && gardenEdgeOverlap && gardenNotInterior &&
            oldWindowEscaped && restingWindowInside && originalPointStillRejected && telegraphWalk && oneWayDrop && cannonDownstep && springSteering && combinedLanding && returnApproach && springDropApproach && delayedBrake && setupScope && slideGuard && slideHandover && hookStage;
    }

    private static bool RunHookStageSelfCheck(out string detail)
    {
        bool inside = true, mirrored = true, allChanged = true, namedUnique = true;
        var namedCases = new HashSet<string>();
        string[] required = { "crown.slide-ring-launch", "crown.high-hook-vault", "crown.cut-high-hook" };
        int cases = 0;
        foreach (int seed in new[] { 20260918, 42 })
        {
            foreach (int chapter in new[] { 2, 3 })
            {
                CampaignLayout map = CampaignLayout.Create(seed, chapter, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(chapter));
                for (int i = 1; i < map.Route.Count; i++)
                {
                    CampaignLayout.RouteNode target = map.Route[i], previous = map.Route[i - 1];
                    if (target.Action != CampaignLayout.TraversalAction.Grapple) continue;
                    Rect takeoff = map.Platforms[previous.PlatformIndex].Bounds, landing = map.Platforms[target.PlatformIndex].Bounds;
                    float direction = Mathf.Sign(target.FeetPosition.x - previous.FeetPosition.x);
                    float oldX = (direction > 0f ? landing.xMin : landing.xMax) - direction * 1.5f;
                    float x = HookStagingX(takeoff, landing, direction, .25f);
                    Rect mirrorTakeoff = Rect.MinMaxRect(-takeoff.xMax, takeoff.yMin, -takeoff.xMin, takeoff.yMax);
                    Rect mirrorLanding = Rect.MinMaxRect(-landing.xMax, landing.yMin, -landing.xMin, landing.yMax);
                    inside &= x - .12f > takeoff.xMin + .45f && x + .12f < takeoff.xMax - .45f;
                    mirrored &= Mathf.Abs(HookStagingX(mirrorTakeoff, mirrorLanding, -direction, .25f) + x) < .001f;
                    string scenario = map.Rooms[target.RoomIndex].ScenarioId;
                    if (chapter == 3 && Array.IndexOf(required, scenario) >= 0)
                    {
                        allChanged &= Mathf.Abs(x - oldX) > .3f;
                        namedUnique &= namedCases.Add(seed + ":" + scenario);
                    }
                    cases++;
                }
            }
        }
        bool fastSlideLaunch = RangedHookTakeoffReady(true, true, 23.79f, 23.89f, 13f);
        bool wrongFloorRejected = !RangedHookTakeoffReady(false, true, 24.716f, 23.89f, 0f);
        bool ordinaryNeedsRest = !RangedHookTakeoffReady(true, false, 49f, 48.99f, 8f) &&
            RangedHookTakeoffReady(true, false, 49f, 48.99f, 0f);
        detail = $"hookSafeStagingInside={inside} hookStageMirrored={mirrored} hookEdgeStagesCorrected={allChanged} " +
            $"hookStageCases={cases} hookFastSlideLaunch={fastSlideLaunch} hookWrongFloorRejected={wrongFloorRejected} " +
            $"hookOrdinaryRestPolicyPreserved={ordinaryNeedsRest} hookStageRuntimeProof=False";
        bool namedCoverage = namedUnique && namedCases.Count == required.Length * 2;
        detail += " namedEdgeCoverage=" + namedCoverage + " namedEdgeCases=" + namedCases.Count;
        return inside && mirrored && allChanged && namedCoverage && fastSlideLaunch && wrongFloorRejected && ordinaryNeedsRest;
    }

    private static bool RunSlideHandoverSelfCheck(out string detail)
    {
        bool doubleDeferred = true, springDeferred = true, hookDeferred = true, ordinaryUnchanged = true, invalidRejected = true;
        int standaloneSlides = 0;
        bool catalogueCoverage = true;
        foreach (int seed in new[] { 20260918, 42 })
        {
            CampaignLayout garden = CampaignLayout.Create(seed, 2, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(2));
            int doubleSlide = UniqueScenarioAction(garden, "garden.slide-double-balcony", CampaignLayout.TraversalAction.SaberSlide);
            int springSlide = UniqueScenarioAction(garden, "garden.slide-spring-transfer", CampaignLayout.TraversalAction.SaberSlide);
            doubleDeferred &= doubleSlide > 0 && doubleSlide + 1 < garden.Route.Count &&
                garden.Route[doubleSlide + 1].Action == CampaignLayout.TraversalAction.DoubleJump && !ShouldSettleSlideExit(garden, doubleSlide);
            springDeferred &= springSlide > 0 && springSlide + 1 < garden.Route.Count &&
                garden.Route[springSlide + 1].Action == CampaignLayout.TraversalAction.Spring && !ShouldSettleSlideExit(garden, springSlide);
            invalidRejected &= doubleSlide > 0 && !ShouldSettleSlideExit(garden, doubleSlide + 1) && !ShouldSettleSlideExit(garden, -1);
            CampaignLayout crown = CampaignLayout.Create(seed, 3, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(3));
            var named = new Dictionary<string, int> { { "crown.slide-refuges", 0 }, { "crown.slide-descending-terraces", 0 }, { "crown.slide-launch-step", 0 } };
            int ringTransfers = 0;
            for (int i = 0; i < crown.Route.Count; i++)
            {
                if (crown.Route[i].Action != CampaignLayout.TraversalAction.SaberSlide) continue;
                if (crown.Rooms[crown.Route[i].RoomIndex].ScenarioId == "crown.slide-ring-launch")
                {
                    ringTransfers++;
                    hookDeferred &= i + 1 < crown.Route.Count && crown.Route[i + 1].Action == CampaignLayout.TraversalAction.Grapple &&
                        !ShouldSettleSlideExit(crown, i);
                    continue;
                }
                standaloneSlides++;
                ordinaryUnchanged &= ShouldSettleSlideExit(crown, i);
                string scenario = crown.Rooms[crown.Route[i].RoomIndex].ScenarioId;
                if (named.ContainsKey(scenario)) named[scenario]++;
            }
            catalogueCoverage &= ringTransfers == 1 && named["crown.slide-refuges"] == 2 &&
                named["crown.slide-descending-terraces"] == 2 && named["crown.slide-launch-step"] == 1;
        }
        detail = $"slideDoubleFirstJumpOwnedByNext={doubleDeferred} slideSpringFirstInputOwnedByNext={springDeferred} slideHookFirstJumpOwnedByNext={hookDeferred} " +
            $"slideStandalonePolicyPreserved={ordinaryUnchanged} standaloneSlidesChecked={standaloneSlides} " +
            $"slideHandoverInvalidNodeRejected={invalidRejected} slideHandoverRuntimeProof=False";
        detail += " slideNamedCatalogueCoverage=" + catalogueCoverage;
        return doubleDeferred && springDeferred && hookDeferred && ordinaryUnchanged && catalogueCoverage && invalidRejected;
    }

    private static int UniqueScenarioAction(CampaignLayout map, string scenario, CampaignLayout.TraversalAction action)
    {
        int found = -1;
        for (int node = 0; node < map.Route.Count; node++)
        {
            CampaignLayout.RouteNode candidate = map.Route[node];
            if (candidate.Action != action || map.Rooms[candidate.RoomIndex].ScenarioId != scenario) continue;
            if (found >= 0) return -1;
            found = node;
        }
        return found;
    }

    private static bool RunCannonSetupScopeSelfCheck(out string detail)
    {
        CampaignLayout arsenal = RecordedCannonArithmetic(false);
        CampaignLayout.Platform landing = arsenal.Platforms[2];
        var actualBodyBounds = new[] { Rect.MinMaxRect(64.16f, 5.54f, 65.56f, 6.44f) };
        bool combined = TryFindSafeLandingX(landing, arsenal.Platforms, .25f, 1f, 66.19f,
            out float safe, out _, actualBodyBounds);
        bool runningCrossingUnchanged = combined && Mathf.Abs(safe - 66.19f) < .001f &&
            !CannonLandingNeedsSetup(66.19f, safe) && !CannonLandingNeedsSetup(-66.19f, -safe);
        bool nearSideStillPrecise = CannonLandingNeedsSetup(65.94f, 63.46f) &&
            CannonLandingNeedsSetup(-65.94f, -63.46f);
        bool arsenal207StillBaits = CannonLandingNeedsSetup(30.45f, 32.99f);
        bool tinyRoundingIgnored = !CannonLandingNeedsSetup(66.19f, 66.195f);
        detail = $"cannonSetupRunningCrossingUnchanged={runningCrossingUnchanged} cannonSetupNearSidePreserved={nearSideStillPrecise} " +
            $"cannonSetupArsenal207Preserved={arsenal207StillBaits} cannonSetupTinyRoundingIgnored={tinyRoundingIgnored} " +
            $"arsenal5Preferred=66.190 arsenal5Safe={safe:F3} immutableR44UnitFixture=True cannonSetupRuntimeProof=False";
        return runningCrossingUnchanged && nearSideStillPrecise && arsenal207StillBaits && tinyRoundingIgnored;
    }

    private static bool RunCannonDelayedBrakeSelfCheck(out string detail)
    {
        bool oldTransientUnsafe = true, newTransientClear = true, newRestStrict = true, mirrored = true;
        float oldMaximum = 0f, newMaximum = 0f;
        for (int delay = 1; delay <= 2; delay++)
        {
            for (int mirror = -1; mirror <= 1; mirror += 2)
            {
                for (int policy = 0; policy <= 1; policy++)
                {
                    float x = 60.23f * mirror, velocity = 8f * mirror, maximum = 60.23f;
                    var pending = new Queue<float>();
                    for (int i = 0; i < delay; i++) pending.Enqueue(mirror);
                    for (int frame = 0; frame < 160; frame++)
                    {
                        float braking = x * mirror < 63.075f ? 60f : 45f;
                        float distance = 63.46f * mirror - x;
                        float stopping = velocity * velocity / (2f * braking);
                        float axis = policy == 1 ? CannonLandingAxis(x, velocity, 63.46f * mirror, 8f, 45f, .02f) :
                            Mathf.Abs(distance) < .12f || distance * velocity > 0f && stopping >= Mathf.Abs(distance) - .12f
                                ? 0f : Mathf.Sign(distance);
                        pending.Enqueue(axis);
                        float consumed = pending.Dequeue();
                        velocity = Mathf.MoveTowards(velocity, consumed * 8f, (consumed == 0f ? braking : braking * 5f / 6f) * .02f);
                        x += velocity * .02f;
                        maximum = Mathf.Max(maximum, x * mirror);
                    }
                    if (policy == 0) { oldTransientUnsafe &= maximum > 63.66f; oldMaximum = Mathf.Max(oldMaximum, maximum); }
                    else
                    {
                        newTransientClear &= maximum < 63.56f;
                        newRestStrict &= x * mirror > 62.4f && x * mirror < 63.56f && Mathf.Abs(velocity) < .01f;
                        newMaximum = Mathf.Max(newMaximum, maximum);
                    }
                }
            }
        }
        mirrored = CannonLandingAxis(63f, 8f, 63.46f, 8f, 45f, .02f) ==
            -CannonLandingAxis(-63f, -8f, -63.46f, 8f, 45f, .02f);
        detail = $"delayedBrakeOldTransientUnsafe={oldTransientUnsafe} delayedBrakeNewTransientClear={newTransientClear} " +
            $"delayedBrakeNewRestStrict={newRestStrict} delayedBrakeMirrored={mirrored} oldTransientMax={oldMaximum:F3} " +
            $"newTransientMax={newMaximum:F3} inputDelaySteps=1,2 transientRuntimeProof=False";
        return oldTransientUnsafe && newTransientClear && newRestStrict && mirrored;
    }

    private static bool RunCannonLandingIntervalSelfCheck(out string detail)
    {
        CampaignLayout map = RecordedCannonArithmetic(true);
        CampaignLayout.Platform landing = map.Platforms[2];
        Rect cannon = Rect.MinMaxRect(63.89f, 37.04f, 65.31f, 37.94f);
        bool oldPlanned = TryFindSafeLandingX(landing, map.Platforms, .25f, 1f, 65.94f, out float oldX, out _);
        bool oldBodyOverlap = oldPlanned && oldX + .12f + .25f > cannon.xMin && oldX - .12f - .25f < cannon.xMax;
        bool combined = TryFindSafeLandingX(landing, map.Platforms, .25f, 1f, 65.94f,
            out float safeX, out _, new[] { cannon });
        Rect restingCapsule = Rect.MinMaxRect(safeX - .12f - .25f, landing.SurfaceY + .02f,
            safeX + .12f + .25f, landing.SurfaceY + 1f);
        bool strictAndClear = combined && safeX - .12f > landing.Bounds.xMin + .45f &&
            safeX + .12f < landing.Bounds.xMax - .45f && !restingCapsule.Overlaps(cannon);
        foreach (CampaignLayout.Platform obstacle in map.Platforms)
            if (obstacle != landing && restingCapsule.Overlaps(obstacle.Bounds)) strictAndClear = false;
        var mirrored = new List<CampaignLayout.Platform>();
        foreach (CampaignLayout.Platform platform in map.Platforms)
            mirrored.Add(new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(-platform.Bounds.xMax,
                platform.Bounds.yMin, -platform.Bounds.xMin, platform.Bounds.yMax) });
        bool mirror = TryFindSafeLandingX(mirrored[2], mirrored, .25f, 1f, -65.94f,
            out float mirrorX, out _, new[] { Rect.MinMaxRect(-cannon.xMax, cannon.yMin, -cannon.xMin, cannon.yMax) }) &&
            Mathf.Abs(mirrorX + safeX) < .001f;
        bool coveredRejected = !TryFindSafeLandingX(landing, map.Platforms, .25f, 1f, 65.94f, out _, out _,
            new[] { Rect.MinMaxRect(landing.Bounds.xMin, 37.1f, landing.Bounds.xMax, 38f) });
        bool overheadIgnored = TryFindSafeLandingX(landing, map.Platforms, .25f, 1f, 65.94f,
            out float overheadX, out _, new[] { Rect.MinMaxRect(63.89f, 40f, 65.31f, 41f) }) && Mathf.Abs(overheadX - oldX) < .001f;
        detail = $"cannonLandingOldBodyOverlap={oldBodyOverlap} cannonLandingCombined={combined} " +
            $"cannonLandingStrictAndClear={strictAndClear} cannonLandingMirror={mirror} cannonLandingCoveredRejected={coveredRejected} " +
            $"cannonLandingOverheadIgnored={overheadIgnored} cannonLandingOldX={oldX:F3} cannonLandingNewX={safeX:F3} immutableR44UnitFixture=True landingRuntimeProof=False";
        return oldBodyOverlap && combined && strictAndClear && mirror && coveredRejected && overheadIgnored;
    }

    private static CampaignLayout RecordedCannonArithmetic(bool dock)
    {
        var map = new CampaignLayout();
        if (dock)
        {
            AddRecordedSupport(map, new Rect(56.4f, 37.82f, 3.2f, .18f));
            AddRecordedSupport(map, new Rect(59.525f, 37.82f, 3.2f, .18f));
            AddRecordedSupport(map, new Rect(61.95f, 36.82f, 4.6f, .18f));
            AddRecordedSupport(map, new Rect(65.675f, 37.32f, 3.4f, .18f));
        }
        else
        {
            AddRecordedSupport(map, new Rect(56.7f, 4.82f, 3.6000001f, .18f));
            AddRecordedSupport(map, new Rect(59.45f, 4.82f, 4.1f, .18f));
            AddRecordedSupport(map, new Rect(62.2f, 5.32f, 4.6f, .18f));
            AddRecordedSupport(map, new Rect(65.65f, 4.82f, 3.7f, .18f));
        }
        return map;
    }

    private static void AddRecordedSupport(CampaignLayout map, Rect bounds)
    {
        int index = map.Platforms.Count;
        map.Platforms.Add(new CampaignLayout.Platform { Bounds = bounds, OneWay = true, RoomIndex = 0 });
        map.Route.Add(new CampaignLayout.RouteNode { PlatformIndex = index, RoomIndex = 0,
            FeetPosition = new Vector2(bounds.center.x, bounds.yMax), Action = CampaignLayout.TraversalAction.Jump });
    }

    private bool TryPlanNearbyOneWayDrop(out int upperIndex, out float dropX)
    {
        upperIndex = -1;
        dropX = 0f;
        string key = layout.Chapter + ":" + targetIndex + ":" + life.DeathCount;
        if (oneWayDropAttempts.Contains(key)) return false;
        for (int index = 0; index < layout.Platforms.Count; index++)
        {
            if (!HasActualSupportContact(layout.Platforms[index], out _, out _) ||
                !TryFindNearbyOneWayDropX(layout, targetIndex, index, body.position.x,
                    capsule.bounds.extents.x, capsule.bounds.size.y, out dropX)) continue;
            upperIndex = index;
            oneWayDropAttempts.Add(key);
            return true;
        }
        return false;
    }

    private static bool IsSpringTraversal(CampaignLayout.TraversalAction action)
    {
        return action == CampaignLayout.TraversalAction.Spring || action == CampaignLayout.TraversalAction.SpringDrop ||
            action == CampaignLayout.TraversalAction.SpringDouble;
    }

    private static float SpringTraversalAxis(bool observedBounce, float approachAxis, float landingAxis)
    {
        return observedBounce ? landingAxis : approachAxis;
    }

    private static bool TryFindSpringReturnBed(CampaignLayout map, int nodeIndex, out Vector2 bedTop)
    {
        bedTop = Vector2.zero;
        if (nodeIndex < 1 || nodeIndex >= map.Route.Count) return false;
        CampaignLayout.RouteNode target = map.Route[nodeIndex];
        if (target.Action != CampaignLayout.TraversalAction.Spring ||
            map.Rooms[target.RoomIndex].ScenarioId != "arsenal.spring-return-balcony") return false;
        foreach (CampaignLayout.Spawn spawn in map.Spawns)
        {
            if (spawn.Kind != CampaignLayout.SpawnKind.Spikes || spawn.NodeIndex != nodeIndex - 1) continue;
            bedTop = spawn.Position + Vector2.up * spawn.Size.y * .5f;
            return true;
        }
        return false;
    }

    private static bool RunSpringReturnApproachSelfCheck(out string detail)
    {
        bool bothDirections = true, beyondGap = true, noWrongTrial = true, missingBedRejected = true;
        int directionMask = 0;
        foreach (int seed in new[] { 20260918, 42 })
        foreach (int mirror in new[] { 1, -1 })
        {
            CampaignLayout map = CampaignLayout.Create(seed, 1, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(1));
            if (mirror < 0)
            {
                foreach (CampaignLayout.RouteNode item in map.Route)
                    item.FeetPosition = new Vector2(-item.FeetPosition.x, item.FeetPosition.y);
                foreach (CampaignLayout.Platform item in map.Platforms)
                    item.Bounds = new Rect(-item.Bounds.xMax, item.Bounds.yMin, item.Bounds.width, item.Bounds.height);
                foreach (CampaignLayout.Spawn item in map.Spawns)
                    item.Position = new Vector2(-item.Position.x, item.Position.y);
            }
            int node = map.Route.FindIndex(item => item.Action == CampaignLayout.TraversalAction.Spring &&
                map.Rooms[item.RoomIndex].ScenarioId == "arsenal.spring-return-balcony");
            bool found = TryFindSpringReturnBed(map, node, out Vector2 bed);
            CampaignLayout.RouteNode previous = map.Route[node - 1], target = map.Route[node];
            float direction = Mathf.Sign(bed.x - previous.FeetPosition.x);
            directionMask |= direction > 0f ? 1 : 2;
            bothDirections &= found && Mathf.Abs(previous.FeetPosition.y - bed.y) < .015f &&
                (target.FeetPosition.x - bed.x) * direction < 0f;
            CampaignLayout.Spawn spikes = map.Spawns.Find(item => item.Kind == CampaignLayout.SpawnKind.Spikes && item.NodeIndex == node - 1);
            CampaignLayout.Platform approach = map.Platforms[previous.PlatformIndex];
            float gap = Mathf.Abs(bed.x - approach.CenterX) - approach.Bounds.width * .5f - spikes.Size.x * .5f;
            beyondGap &= gap > 1.7f && gap < map.SafeJumpDistance;
            int drop = map.Route.FindIndex(item => item.Action == CampaignLayout.TraversalAction.SpringDrop);
            noWrongTrial &= !TryFindSpringReturnBed(map, drop, out _) && !TryFindSpringReturnBed(map, node - 1, out _);
            map.Spawns.Remove(spikes);
            missingBedRejected &= !TryFindSpringReturnBed(map, node, out _);
        }
        bothDirections &= directionMask == 3;
        detail = $"springReturnBothDirections={bothDirections} springReturnGapNeedsJump={beyondGap} " +
            $"springReturnOtherTrialsUnchanged={noWrongTrial} springReturnMissingBedRejected={missingBedRejected} explicitMirroredArithmetic=True returnJumpRuntimeProof=False";
        return bothDirections && beyondGap && noWrongTrial && missingBedRejected;
    }

    private static bool RunSpringDropApproachSelfCheck(out string detail)
    {
        const float speed = 6.6f, g = 34.335f, drop = 3f, dashSpeed = 18f, dashDuration = 0.16f;
        const float hoodHalf = 2.1f, takeoffHalf = 1.7f, dt = 0.02f, airDecel = 45f, airAccel = 37.5f;
        float lipToHood = 4f - takeoffHalf + hoodHalf;
        float walkDx = speed * Mathf.Sqrt(2f * drop / g);
        float fallTime = Mathf.Sqrt(2f * drop / g);
        float extra = dashSpeed * dt;
        float stopTime = dashSpeed / airDecel;
        float coastDx = stopTime < fallTime ? 0.5f * dashSpeed * stopTime : dashSpeed * fallTime - 0.5f * airDecel * fallTime * fallTime;
        float landPastLip = extra + coastDx;
        float resumeBrakeTime = Mathf.Min(fallTime, (dashSpeed - speed) / airDecel);
        float resumeDx = extra + (dashSpeed + speed) * 0.5f * resumeBrakeTime + speed * (fallTime - resumeBrakeTime);
        const float headRiseTime = 0.237f;
        float bounceAccelTime = Mathf.Min(headRiseTime, speed / airAccel);
        float bounceDx = 0.5f * airAccel * bounceAccelTime * bounceAccelTime + speed * (headRiseTime - bounceAccelTime);
        bool walkHitsHood = walkDx + speed * headRiseTime < lipToHood + 0.25f;
        bool dashLandsOnBed = landPastLip > hoodHalf && landPastLip < lipToHood;
        bool dashClearsHood = landPastLip + bounceDx > lipToHood + 0.25f;
        bool resumeOvershoots = resumeDx > lipToHood;
        bool launchOnTakeoff = true, bothSeeds = true, readyWindow = true;
        foreach (int seed in new[] { 20260918, 42 })
        {
            CampaignLayout map = CampaignLayout.Create(seed, 1, 14f, 34.335f, .02f, PirateMovementProfile.ForChapter(1));
            int node = map.Route.FindIndex(item => item.Action == CampaignLayout.TraversalAction.SpringDrop);
            bothSeeds &= node > 0;
            if (node < 1) continue;
            CampaignLayout.Platform takeoff = map.Platforms[map.Route[node - 1].PlatformIndex];
            float direction = Mathf.Sign(map.Route[node].FeetPosition.x - map.Route[node - 1].FeetPosition.x);
            float startX = SpringDropDashStartX(takeoff.Bounds, direction, dashSpeed, dashDuration);
            float lipX = direction > 0f ? takeoff.Bounds.xMax : takeoff.Bounds.xMin;
            launchOnTakeoff &= startX > takeoff.Bounds.xMin + 0.2f && startX < takeoff.Bounds.xMax - 0.2f;
            readyWindow &= SpringDropLipJumpReady(startX, startX, lipX, direction) &&
                !SpringDropLipJumpReady(startX - direction * 0.5f, startX, lipX, direction) &&
                !SpringDropLipJumpReady(lipX + direction * 0.05f, startX, lipX, direction);
        }
        detail = $"springDropWalkHitsHood={walkHitsHood} springDropDashLandsOnBed={dashLandsOnBed} " +
            $"springDropDashClearsHood={dashClearsHood} springDropResumeOvershoots={resumeOvershoots} " +
            $"springDropLaunchOnTakeoff={launchOnTakeoff} springDropReadyWindow={readyWindow} springDropBothSeeds={bothSeeds} " +
            $"springDropWalkDx={walkDx:F3} springDropCoastDx={landPastLip:F3} springDropBounceDx={bounceDx:F3} " +
            "springDropApexDashRemoved=True springDropTakeoffJumpRejected=True springDropRuntimeProof=False";
        return walkHitsHood && dashLandsOnBed && dashClearsHood && resumeOvershoots && launchOnTakeoff && readyWindow && bothSeeds;
    }

    private static bool RunSpringSteeringSelfCheck(out string detail)
    {
        bool approachUnchanged = SpringTraversalAxis(false, 1f, -1f) == 1f;
        bool nonRisingBrake = SpringTraversalAxis(true, 1f, 0f) == 0f;
        bool overshootCorrected = SpringTraversalAxis(true, 1f, -1f) == -1f;
        var landing = new CampaignLayout.Platform { Bounds = new Rect(-1.1f, 0f, 2.2f, .18f) };
        float oldX = 0f, newX = oldX, oldVelocity = 0f, newVelocity = 0f;
        for (int step = 0; step < 15; step++)
        {
            oldVelocity = Mathf.MoveTowards(oldVelocity, 8f, 65f * .02f);
            newVelocity = Mathf.MoveTowards(newVelocity, SpringTraversalAxis(true, 1f, 0f) * 8f, 65f * .02f);
            oldX += oldVelocity * .02f;
            newX += newVelocity * .02f;
        }
        bool oldRunsOff = oldX > landing.Bounds.xMax + .25f;
        bool newStrictInterior = newX > landing.Bounds.xMin + .45f && newX < landing.Bounds.xMax - .45f;
        detail = $"springApproachUnchanged={approachUnchanged} springNonRisingBrake={nonRisingBrake} " +
            $"springOvershootCorrected={overshootCorrected} springOldRunsOff={oldRunsOff} springNewStrictInterior={newStrictInterior} " +
            $"springOldX={oldX:F3} springNewX={newX:F3} immutableR44WidthFixture=True springArcRuntimeProof=False";
        return approachUnchanged && nonRisingBrake && overshootCorrected && oldRunsOff && newStrictInterior;
    }

    private static bool TryFindNearbyOneWayDropX(CampaignLayout map, int expectedIndex, int upperIndex,
        float currentX, float halfWidth, float height, out float dropX)
    {
        dropX = 0f;
        if (expectedIndex < 0 || expectedIndex >= map.Route.Count || upperIndex < 0 || upperIndex >= map.Platforms.Count) return false;
        CampaignLayout.RouteNode expected = map.Route[expectedIndex];
        CampaignLayout.Platform upper = map.Platforms[upperIndex];
        CampaignLayout.Platform lower = map.Platforms[expected.PlatformIndex];
        if (expected.RequiredAbility.HasValue || expected.SecondaryAbility.HasValue ||
            (expected.Action != CampaignLayout.TraversalAction.Walk && expected.Action != CampaignLayout.TraversalAction.Jump) ||
            !upper.OneWay || upper.RoomIndex != expected.RoomIndex ||
            upper.SurfaceY - lower.SurfaceY <= .14f || upper.SurfaceY - lower.SurfaceY > 5f) return false;
        bool foundNearbyOrdinarySupport = false;
        if (expectedIndex > 0)
        {
            CampaignLayout.RouteNode previous = map.Route[expectedIndex - 1];
            foundNearbyOrdinarySupport = previous.PlatformIndex == upperIndex && previous.RoomIndex == expected.RoomIndex &&
                !previous.RequiredAbility.HasValue && !previous.SecondaryAbility.HasValue &&
                (previous.Action == CampaignLayout.TraversalAction.Walk || previous.Action == CampaignLayout.TraversalAction.Jump);
        }
        for (int node = expectedIndex; node <= Mathf.Min(expectedIndex + 4, map.Route.Count - 1); node++)
        {
            CampaignLayout.RouteNode step = map.Route[node];
            if (step.RoomIndex != expected.RoomIndex || step.RequiredAbility.HasValue || step.SecondaryAbility.HasValue ||
                (step.Action != CampaignLayout.TraversalAction.Walk && step.Action != CampaignLayout.TraversalAction.Jump)) break;
            if (step.PlatformIndex == upperIndex) { foundNearbyOrdinarySupport = true; break; }
        }
        if (!foundNearbyOrdinarySupport) return false;
        float inset = halfWidth + OrdinaryLandingSteeringInset;
        float left = Mathf.Max(upper.Bounds.xMin, lower.Bounds.xMin) + inset;
        float right = Mathf.Min(upper.Bounds.xMax, lower.Bounds.xMax) - inset;
        if (left >= right) return false;
        float[] candidates = { Mathf.Clamp(currentX, left, right), (left + right) * .5f, left, right };
        foreach (float x in candidates)
        {
            Rect walk = Rect.MinMaxRect(Mathf.Min(currentX, x) - halfWidth - .03f, upper.SurfaceY + .04f,
                Mathf.Max(currentX, x) + halfWidth + .03f, upper.SurfaceY + height + .05f);
            Rect fall = Rect.MinMaxRect(x - halfWidth - .15f, lower.SurfaceY + .04f,
                x + halfWidth + .15f, upper.SurfaceY + height + .05f);
            if (!OneWayDropCorridorClear(map, walk, upperIndex, expected.PlatformIndex) ||
                !OneWayDropCorridorClear(map, fall, upperIndex, expected.PlatformIndex)) continue;
            dropX = x;
            return true;
        }
        return false;
    }

    private static bool OneWayDropCorridorClear(CampaignLayout map, Rect corridor, int upperIndex, int lowerIndex)
    {
        foreach (RectInt wall in map.Solids)
            if (corridor.Overlaps(new Rect(wall.x, wall.y, wall.width, wall.height))) return false;
        for (int index = 0; index < map.Platforms.Count; index++)
            if (index != upperIndex && index != lowerIndex && corridor.Overlaps(map.Platforms[index].Bounds)) return false;
        foreach (CampaignLayout.Spawn spawn in map.Spawns)
        {
            bool obstacle = spawn.Kind == CampaignLayout.SpawnKind.Spikes || spawn.Kind == CampaignLayout.SpawnKind.Snare ||
                spawn.Kind == CampaignLayout.SpawnKind.Plant || spawn.Kind == CampaignLayout.SpawnKind.Crawler ||
                spawn.Kind == CampaignLayout.SpawnKind.Cannon || spawn.Kind == CampaignLayout.SpawnKind.RopeGate;
            if (obstacle && corridor.Overlaps(new Rect(spawn.Position - spawn.Size * .5f, spawn.Size))) return false;
        }
        return true;
    }

    private static bool RunNearbyOneWayDropSelfCheck(out string detail)
    {
        CampaignLayout map = RecordedOneWayArithmetic(false);
        int upperIndex = 2;
        CampaignLayout.Platform upper = map.Platforms[upperIndex];
        CampaignLayout.Platform lower = map.Platforms[0];
        bool planned = TryFindNearbyOneWayDropX(map, 0, upperIndex, 44.5286f, .25f, 1f, out float x);
        bool strictWindow = planned && x - .12f > lower.Bounds.xMin + .45f && x + .12f < lower.Bounds.xMax - .45f;
        upper.OneWay = false;
        bool solidRejected = !TryFindNearbyOneWayDropX(map, 0, upperIndex, 44.5286f, .25f, 1f, out _);
        upper.OneWay = true;
        map.Route[1].RequiredAbility = PirateUpgrade.Hook2;
        bool gateRejected = !TryFindNearbyOneWayDropX(map, 0, upperIndex, 44.5286f, .25f, 1f, out _);
        map.Route[1].RequiredAbility = null;
        var obstruction = new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(42f, 21.5f, 46f, 21.7f), OneWay = true };
        map.Platforms.Add(obstruction);
        bool interveningRejected = !TryFindNearbyOneWayDropX(map, 0, upperIndex, 44.5286f, .25f, 1f, out _);
        map.Platforms.Remove(obstruction);
        map.Spawns.Add(new CampaignLayout.Spawn { Kind = CampaignLayout.SpawnKind.Spikes,
            Position = new Vector2(44f, 21.6f), Size = new Vector2(4f, .3f) });
        bool hazardRejected = !TryFindNearbyOneWayDropX(map, 0, upperIndex, 44.5286f, .25f, 1f, out _);
        CampaignLayout descending = RecordedOneWayArithmetic(true);
        int previousUpper = 0;
        bool previousPlanned = TryFindNearbyOneWayDropX(descending, 1, previousUpper, 31.6834f, .25f, 1f, out float previousX);
        CampaignLayout.Platform previousLower = descending.Platforms[1];
        bool previousStrict = previousPlanned && previousX - .12f > previousLower.Bounds.xMin + .45f &&
            previousX + .12f < previousLower.Bounds.xMax - .45f;
        descending.Route[0].RequiredAbility = PirateUpgrade.SpringLeg;
        bool previousGateRejected = !TryFindNearbyOneWayDropX(descending, 1, previousUpper, 31.6834f, .25f, 1f, out _);
        descending.Route[0].RequiredAbility = null;
        descending.Route[1].RequiredAbility = PirateUpgrade.SpringLeg;
        bool targetGateRejected = !TryFindNearbyOneWayDropX(descending, 1, previousUpper, 31.6834f, .25f, 1f, out _);
        descending.Route[1].RequiredAbility = null;
        descending.Platforms[previousUpper].OneWay = false;
        bool previousSolidRejected = !TryFindNearbyOneWayDropX(descending, 1, previousUpper, 31.6834f, .25f, 1f, out _);
        descending.Platforms[previousUpper].OneWay = true;
        descending.Spawns.Add(new CampaignLayout.Spawn { Kind = CampaignLayout.SpawnKind.Spikes,
            Position = new Vector2(31.3f, 248.6f), Size = new Vector2(3f, .3f) });
        bool previousHazardRejected = !TryFindNearbyOneWayDropX(descending, 1, previousUpper, 31.6834f, .25f, 1f, out _);
        detail = $"arsenalOneWayDropPlanned={planned} dropX={x:F3} dropStrictWindow={strictWindow} " +
            $"dropSolidRejected={solidRejected} dropGateRejected={gateRejected} " +
            $"dropInterveningRejected={interveningRejected} dropHazardRejected={hazardRejected} " +
            $"previousOneWayDropPlanned={previousPlanned} previousDropX={previousX:F3} previousDropStrict={previousStrict} " +
            $"previousDropGateRejected={previousGateRejected} dropTargetGateRejected={targetGateRejected} " +
            $"previousDropSolidRejected={previousSolidRejected} previousDropHazardRejected={previousHazardRejected} immutableR44UnitFixture=True dropRuntimeProof=False";
        return planned && strictWindow && solidRejected && gateRejected && interveningRejected && hazardRejected &&
            previousPlanned && previousStrict && previousGateRejected && targetGateRejected && previousSolidRejected && previousHazardRejected;
    }

    private static CampaignLayout RecordedOneWayArithmetic(bool descending)
    {
        var map = new CampaignLayout();
        if (descending)
        {
            AddRecordedSupport(map, new Rect(28.524998f, 249.52002f, 3.4f, .18f));
            AddRecordedSupport(map, new Rect(29.949999f, 247.62001f, 3.4f, .18f));
            AddRecordedSupport(map, new Rect(31.375f, 245.72f, 3.4f, .18f));
        }
        else
        {
            AddRecordedSupport(map, new Rect(42.8f, 20.486666f, 3.4f, .18f));
            AddRecordedSupport(map, new Rect(40.5f, 20.82f, 3f, .18f));
            AddRecordedSupport(map, new Rect(41.9f, 22.82f, 3.2f, .18f));
            AddRecordedSupport(map, new Rect(38.9f, 24.82f, 3.2f, .18f));
            AddRecordedSupport(map, new Rect(41.9f, 26.82f, 3.2f, .18f));
        }
        return map;
    }

    private IEnumerator DropFromNearbyOneWaySupport(int upperIndex, float dropX)
    {
        int expectedIndex = targetIndex, deathsAtStart = life.DeathCount;
        CampaignLayout.Platform upper = layout.Platforms[upperIndex];
        CampaignLayout.Platform lower = layout.Platforms[layout.Route[expectedIndex].PlatformIndex];
        bool pressed = false, observedDrop = false;
        int landingFrames = 0;
        grapple.SetAutomationInputOverride(false);
        abilities.SetAutomationSlide(false);
        Debug.Log($"PIRATE_ROUTE_ONEWAY_DROP_START chapter={layout.Chapter} node={expectedIndex} upper={upperIndex} " +
            $"dropX={dropX:F3} from={upper.Bounds} to={lower.Bounds} targetIndexUnchanged=True hazardsActive=True");
        for (int frame = 0; frame < 240; frame++)
        {
            if (life.IsRespawning || life.DeathCount != deathsAtStart) yield break;
            if (targetIndex != expectedIndex || !player.ControlsEnabled)
            { Fail("one-way drop lost its unchanged target or controls"); yield break; }
            ObserveThreats();
            observedDrop |= pressed && (player.IsDroppingThrough || capsule.bounds.min.y < upper.SurfaceY - .2f);
            landingFrames = pressed && observedDrop && OnSafeSupport(lower) ? landingFrames + 1 : 0;
            if (landingFrames >= 2)
            {
                player.SetAutomationInputOverride(Vector2.zero);
                ResetNodeState();
                Debug.Log($"PIRATE_ROUTE_ONEWAY_DROP_LANDED chapter={layout.Chapter} node={expectedIndex} " +
                    $"position={body.position} actualDropObserved={observedDrop} ordinarySAndSpace=True targetIndexUnchanged=True");
                yield break;
            }
            bool press = !pressed && OnSafeSupport(upper) && Mathf.Abs(body.position.x - dropX) < .12f &&
                Mathf.Abs(body.linearVelocity.x) < .25f;
            player.SetAutomationInputOverride(new Vector2(AxisTo(dropX), press ? -1f : 0f), jumpPressed: press);
            if (press) pressed = true;
            if (frame % 3 == 0 && ShouldSwingAtThreat()) { abilities.SetAutomationAttackPressed(); attacks++; }
            yield return null;
        }
        Fail("bounded S+Space drop did not physically land on its unchanged expected support");
    }

    private sealed class OptionalDescentPlan
    {
        public int ExpectedNode;
        public int UpperPlatform;
        public int LandingPlatform;
        public float DropX;
        public bool ReturnRequired;
    }

    private bool TryPlanOptionalDescent(CampaignLayout.RouteNode expected, out OptionalDescentPlan plan)
    {
        plan = null;
        string key = layout.Chapter + ":" + targetIndex + ":" + life.DeathCount;
        if (optionalDescentAttempts.Contains(key) || optionalDescentsThisChapter >= 2) return false;
        int upperIndex = -1;
        Vector2 actualPoint = Vector2.zero, actualNormal = Vector2.zero;
        for (int candidateIndex = 0; candidateIndex < layout.Platforms.Count; candidateIndex++)
        {
            CampaignLayout.Platform candidate = layout.Platforms[candidateIndex];
            if (!candidate.Optional || !candidate.OneWay || candidate.RoomIndex != expected.RoomIndex ||
                !HasActualSupportContact(candidate, out actualPoint, out actualNormal)) continue;
            upperIndex = candidateIndex;
            break;
        }
        if (upperIndex < 0) return false;
        CampaignLayout.Platform upper = layout.Platforms[upperIndex];
        Debug.Log($"PIRATE_ROUTE_OPTIONAL_SUPPORT_CONTACT chapter={layout.Chapter} node={targetIndex} " +
            $"platform={upperIndex} position={body.position} feet={capsule.bounds.min.y:F3} " +
            $"contact={actualPoint} normal={actualNormal} support={upper.Bounds} " +
            "recoveryDetectionOnly=True acceptanceUnchanged=True");
        for (int useNext = 0; useNext <= 1; useNext++)
        {
            int landingNode = targetIndex + useNext;
            if (landingNode >= layout.Route.Count) continue;
            CampaignLayout.RouteNode node = layout.Route[landingNode];
            if (node.RequiredAbility.HasValue || node.RoomIndex != expected.RoomIndex ||
                (node.Action != CampaignLayout.TraversalAction.Walk && node.Action != CampaignLayout.TraversalAction.Jump)) continue;
            CampaignLayout.Platform landing = layout.Platforms[node.PlatformIndex];
            for (int side = -1; side <= 1; side += 2)
            {
                float halfWidth = capsule.bounds.extents.x;
                float dropX = side < 0 ? upper.Bounds.xMin - halfWidth - 0.12f : upper.Bounds.xMax + halfWidth + 0.12f;
                string rejection = null;
                if (dropX <= landing.Bounds.xMin + halfWidth + 0.2f || dropX >= landing.Bounds.xMax - halfWidth - 0.2f)
                    rejection = "edge_outside_lower_safe_support";
                else if (upper.SurfaceY - landing.SurfaceY <= 0f || upper.SurfaceY - landing.SurfaceY > 5f)
                    rejection = "drop_height_outside_local_bound";
                Rect walk = Rect.MinMaxRect(Mathf.Min(body.position.x, dropX) - halfWidth - 0.03f, upper.SurfaceY + 0.04f,
                    Mathf.Max(body.position.x, dropX) + halfWidth + 0.03f, upper.SurfaceY + capsule.bounds.size.y + 0.05f);
                Rect fall = Rect.MinMaxRect(dropX - halfWidth - 0.03f, landing.SurfaceY + 0.04f,
                    dropX + halfWidth + 0.03f, upper.SurfaceY + capsule.bounds.size.y + 0.05f);
                if (rejection == null) rejection = OptionalCorridorBlock(walk, upperIndex, node.PlatformIndex);
                if (rejection == null) rejection = OptionalCorridorBlock(fall, upperIndex, node.PlatformIndex);
                if (rejection == null && useNext == 1)
                {
                    float rise = expected.FeetPosition.y - landing.SurfaceY;
                    if (rise < -0.05f || rise > layout.SafeJumpHeight || Mathf.Abs(dropX - expected.FeetPosition.x) > layout.SafeJumpDistance)
                        rejection = "return_outside_ordinary_jump_envelope";
                    else
                    {
                        Rect returnArc = Rect.MinMaxRect(Mathf.Min(dropX, expected.FeetPosition.x) - halfWidth - 0.03f,
                            landing.SurfaceY + 0.04f, Mathf.Max(dropX, expected.FeetPosition.x) + halfWidth + 0.03f,
                            landing.SurfaceY + layout.SafeJumpHeight / 0.78f + capsule.bounds.size.y + 0.05f);
                        rejection = OptionalCorridorBlock(returnArc, node.PlatformIndex, expected.PlatformIndex);
                    }
                }
                Debug.Log($"PIRATE_ROUTE_OPTIONAL_DESCENT_DECISION chapter={layout.Chapter} node={targetIndex} " +
                          $"mode={(useNext == 0 ? "direct" : "next_then_return")} side={side} " +
                          $"decision={(rejection == null ? "allow" : "reject")} reason={rejection ?? "clear_capsule_corridors"} " +
                          $"upper={upper.Bounds} landing={landing.Bounds} dropX={dropX:F3}");
                if (rejection != null) continue;
                optionalDescentAttempts.Add(key);
                plan = new OptionalDescentPlan { ExpectedNode = targetIndex, UpperPlatform = upperIndex,
                    LandingPlatform = node.PlatformIndex, DropX = dropX, ReturnRequired = useNext == 1 };
                return true;
            }
        }
        return false;
    }

    private bool HasActualSupportContact(CampaignLayout.Platform support, out Vector2 point, out Vector2 normal)
    {
        point = normal = Vector2.zero;
        Bounds bounds = capsule.bounds;
        if (!player.IsGrounded || Mathf.Abs(body.linearVelocity.y) >= .2f ||
            Mathf.Abs(bounds.min.y - support.SurfaceY) >= .14f ||
            bounds.max.x <= support.Bounds.xMin || bounds.min.x >= support.Bounds.xMax) return false;
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = player.GroundLayer, useTriggers = false };
        int count = capsule.GetContacts(filter, recoveryContacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = recoveryContacts[i];
            if (!contact.enabled || Mathf.Abs(contact.point.y - support.SurfaceY) >= .14f ||
                contact.point.x < support.Bounds.xMin - .03f || contact.point.x > support.Bounds.xMax + .03f) continue;
            Collider2D other = contact.collider != null && contact.collider.attachedRigidbody != body
                ? contact.collider : contact.otherCollider;
            if (other == null || other.isTrigger ||
                Mathf.Abs(other.bounds.min.x - support.Bounds.xMin) > .03f ||
                Mathf.Abs(other.bounds.max.x - support.Bounds.xMax) > .03f ||
                Mathf.Abs(other.bounds.max.y - support.SurfaceY) > .03f) continue;
            Vector2 towardPlayer = contact.normal;
            if (Vector2.Dot(towardPlayer, body.worldCenterOfMass - contact.point) < 0f) towardPlayer = -towardPlayer;
            if (towardPlayer.y < .65f) continue;
            point = contact.point;
            normal = towardPlayer;
            return true;
        }
        return false;
    }

    private string OptionalCorridorBlock(Rect corridor, int ignoredFirst, int ignoredSecond)
    {
        foreach (RectInt wall in layout.Solids)
            if (corridor.Overlaps(new Rect(wall.x, wall.y, wall.width, wall.height))) return "solid_wall=" + wall;
        for (int i = 0; i < layout.Platforms.Count; i++)
            if (i != ignoredFirst && i != ignoredSecond && corridor.Overlaps(layout.Platforms[i].Bounds))
                return "intervening_support=" + i + ":" + layout.Platforms[i].Bounds;
        return null;
    }

    private bool OnSafeSupport(CampaignLayout.Platform support)
    {
        return player.IsGrounded && Mathf.Abs(body.linearVelocity.y) < 0.2f &&
            Mathf.Abs(capsule.bounds.min.y - support.SurfaceY) < 0.14f &&
            body.position.x > support.Bounds.xMin + capsule.bounds.extents.x + 0.2f &&
            body.position.x < support.Bounds.xMax - capsule.bounds.extents.x - 0.2f;
    }

    private bool TryFindActualRetreatSupport(out int sourceNode)
    {
        sourceNode = -1;
        if (actualDashTarget != targetIndex || player.IsDashing || !player.IsGrounded || targetIndex < 2) return false;
        if (OnSafeSupport(layout.Platforms[layout.Route[targetIndex].PlatformIndex]) ||
            OnSafeSupport(layout.Platforms[Previous.PlatformIndex])) return false;
        for (int candidate = targetIndex - 2; candidate >= Mathf.Max(0, targetIndex - 3); candidate--)
        {
            CampaignLayout.RouteNode source = layout.Route[candidate];
            if (!OnSafeSupport(layout.Platforms[source.PlatformIndex])) continue;
            bool valid = true;
            for (int node = candidate; node <= targetIndex; node++)
            {
                CampaignLayout.RouteNode step = layout.Route[node];
                if (step.RoomIndex != layout.Route[targetIndex].RoomIndex || step.RequiredAbility.HasValue ||
                    (step.Action != CampaignLayout.TraversalAction.Walk && step.Action != CampaignLayout.TraversalAction.Jump))
                { valid = false; break; }
                if (node == candidate) continue;
                Vector2 delta = step.FeetPosition - layout.Route[node - 1].FeetPosition;
                if (delta.y > layout.SafeJumpHeight || delta.y < -2.05f || Mathf.Abs(delta.x) > layout.SafeJumpDistance)
                { valid = false; break; }
            }
            if (!valid) continue;
            sourceNode = candidate;
            return true;
        }
        return false;
    }

    private IEnumerator ReplayOrdinaryRetreat(int sourceNode)
    {
        int expectedNode = targetIndex;
        string key = layout.Chapter + ":" + expectedNode;
        retreatReplayAttempts.TryGetValue(key, out int previousAttempts);
        int attempt = previousAttempts + 1;
        retreatReplayAttempts[key] = attempt;
        if (attempt > 2 || retreatReplays >= 8)
        { Fail("bounded retreat replay exhausted (two per target, eight per campaign)"); yield break; }
        retreatReplays++;
        CampaignLayout.Platform sourceSupport = layout.Platforms[layout.Route[sourceNode].PlatformIndex];
        Debug.Log($"PIRATE_ROUTE_RETREAT_REPLAY_START chapter={layout.Chapter} expectedNode={expectedNode} " +
                  $"actualSourceNode={sourceNode} sourceBounds={sourceSupport.Bounds} position={body.position} " +
                  $"steps={expectedNode - sourceNode} attempt={attempt} actualGrounded=True targetIndexUnchanged=True hazardsActive=True");
        grapple.SetAutomationInputOverride(false);
        abilities.SetAutomationSlide(false);
        int fromNode = sourceNode;
        int stageFrames = 0;
        int arrivalFrames = 0;
        bool staged = false;
        bool jumpPressed = false;
        for (int frame = 0; frame < 360; frame++)
        {
            if (life.IsRespawning)
            {
                Debug.Log($"PIRATE_ROUTE_RETREAT_REPLAY_ABORT chapter={layout.Chapter} expectedNode={expectedNode} " +
                          "reason=normal_death targetIndexUnchanged=True");
                yield break;
            }
            if (targetIndex != expectedNode || !player.ControlsEnabled)
            { Fail("retreat replay lost its unchanged expected node or production controls"); yield break; }
            ObserveThreats();
            CampaignLayout.RouteNode from = layout.Route[fromNode];
            CampaignLayout.RouteNode next = layout.Route[fromNode + 1];
            CampaignLayout.Platform fromSupport = layout.Platforms[from.PlatformIndex];
            CampaignLayout.Platform nextSupport = layout.Platforms[next.PlatformIndex];
            float axis;
            bool jump = false;
            if (!staged)
            {
                axis = AxisTo(from.FeetPosition.x);
                staged = OnSafeSupport(fromSupport) && Mathf.Abs(body.linearVelocity.x) < 0.25f &&
                    Mathf.Abs(body.position.x - from.FeetPosition.x) < 0.2f;
            }
            else axis = AxisTo(next.FeetPosition.x);
            if (staged)
            {
                axis = AxisTo(next.FeetPosition.x);
                bool needsJump = next.FeetPosition.y > capsule.bounds.min.y + 0.18f || !HasContinuousWalkTo(next);
                if (!jumpPressed && OnSafeSupport(fromSupport) && needsJump)
                { jump = true; jumpPressed = true; jumps++; }
                arrivalFrames = OnSafeSupport(nextSupport) ? arrivalFrames + 1 : 0;
                if (arrivalFrames >= 2)
                {
                    fromNode++;
                    Debug.Log($"PIRATE_ROUTE_RETREAT_REPLAY_ARRIVAL chapter={layout.Chapter} expectedNode={expectedNode} " +
                              $"physicalNode={fromNode} support={nextSupport.Bounds} position={body.position} " +
                              "actualGrounded=True targetIndexUnchanged=True");
                    if (fromNode == expectedNode)
                    {
                        player.SetAutomationInputOverride(Vector2.zero);
                        ResetNodeState();
                        Debug.Log($"PIRATE_ROUTE_RETREAT_REPLAY_RETURNED chapter={layout.Chapter} expectedNode={expectedNode} " +
                                  $"position={body.position} actualGrounded=True targetIndexUnchanged=True");
                        yield break;
                    }
                    stageFrames = arrivalFrames = 0;
                    staged = jumpPressed = false;
                    player.SetAutomationInputOverride(Vector2.zero);
                    yield return null;
                    continue;
                }
            }
            player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump);
            if (frame % 3 == 0 && ShouldSwingAtThreat()) { abilities.SetAutomationAttackPressed(); attacks++; }
            yield return null;
            if (++stageFrames > 120)
            { Fail("ordinary retreat replay did not physically reach its next support within 120 frames"); yield break; }
        }
        Fail("ordinary retreat replay exceeded its 360-frame bound");
    }

    private IEnumerator DescendFromOptionalSupport(OptionalDescentPlan plan)
    {
        optionalDescentsThisChapter++;
        CampaignLayout.Platform upper = layout.Platforms[plan.UpperPlatform];
        CampaignLayout.Platform landing = layout.Platforms[plan.LandingPlatform];
        CampaignLayout.RouteNode expected = layout.Route[plan.ExpectedNode];
        CampaignLayout.Platform expectedSupport = layout.Platforms[expected.PlatformIndex];
        Debug.Log($"PIRATE_ROUTE_OPTIONAL_DESCENT_START chapter={layout.Chapter} expectedNode={targetIndex} " +
                  $"upper={upper.Bounds} lower={landing.Bounds} dropX={plan.DropX:F3} targetIndexUnchanged=True hazardsActive=True");
        grapple.SetAutomationInputOverride(false);
        abilities.SetAutomationSlide(false);
        bool lowerReached = false;
        bool returnJumpPressed = false;
        int stableLanding = 0;
        for (int frame = 0; frame < 240; frame++)
        {
            if (life.IsRespawning) yield break;
            if (targetIndex != plan.ExpectedNode || !player.ControlsEnabled)
            { Fail("optional descent lost its original target or controls"); yield break; }
            ObserveThreats();
            bool jump = false;
            if (!lowerReached)
            {
                stableLanding = OnSafeSupport(landing) ? stableLanding + 1 : 0;
                if (stableLanding >= 2)
                {
                    lowerReached = true;
                    stableLanding = 0;
                    Debug.Log($"PIRATE_ROUTE_OPTIONAL_DESCENT_LANDED chapter={layout.Chapter} expectedNode={targetIndex} " +
                              $"position={body.position} support={landing.Bounds} targetIndexUnchanged=True");
                    if (!plan.ReturnRequired)
                    {
                        ResetNodeState();
                        stable = 2;
                        Debug.Log($"PIRATE_ROUTE_OPTIONAL_DESCENT_RETURNED chapter={layout.Chapter} expectedNode={targetIndex} " +
                                  "direct=True targetIndexUnchanged=True");
                        yield break;
                    }
                    if (Mathf.Abs(body.position.x - expected.FeetPosition.x) > layout.SafeJumpDistance)
                    { Fail("optional descent drifted outside the safe return-jump envelope"); yield break; }
                }
            }
            if (lowerReached)
            {
                if (!returnJumpPressed && OnSafeSupport(landing) && Mathf.Abs(body.linearVelocity.x) < 0.25f)
                { jump = true; returnJumpPressed = true; jumps++; }
                stableLanding = returnJumpPressed && OnSafeSupport(expectedSupport) ? stableLanding + 1 : 0;
                if (stableLanding >= 2)
                {
                    ResetNodeState();
                    Debug.Log($"PIRATE_ROUTE_OPTIONAL_DESCENT_RETURNED chapter={layout.Chapter} expectedNode={targetIndex} " +
                              $"position={body.position} support={expectedSupport.Bounds} targetIndexUnchanged=True ordinarySpace=True");
                    yield break;
                }
            }
            float axis = lowerReached && !returnJumpPressed ? 0f : AxisTo(lowerReached ? expected.FeetPosition.x : plan.DropX);
            player.SetAutomationInputOverride(new Vector2(axis, 0f), jumpPressed: jump);
            if (frame % 3 == 0 && ShouldSwingAtThreat()) { abilities.SetAutomationAttackPressed(); attacks++; }
            yield return null;
        }
        Fail("bounded optional descent did not physically return to its unchanged expected support");
    }

    private bool ShouldJumpCannonWarning(CampaignLayout.RouteNode target)
    {
        Vector2 contactExtents = ProjectileContactExtents();
        bool descendingStep = target.FeetPosition.y < capsule.bounds.min.y - 0.2f && HasContinuousWalkTo(target);
        if (target.FeetPosition.y < capsule.bounds.min.y - .18f &&
            TelegraphThreatensDownwardWalk(target)) return true;
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
        {
            Rigidbody2D projectile = ball.GetComponent<Rigidbody2D>();
            if (projectile == null) continue;
            Vector2 relative = body.position - projectile.position;
            Vector2 closing = projectile.linearVelocity - body.linearVelocity;
            float time = Vector2.Dot(relative, closing) / Mathf.Max(0.01f, closing.sqrMagnitude);
            Vector2 miss = relative - closing * time;
            if (time >= 0.18f && time <= 0.6f && Mathf.Abs(miss.y) < contactExtents.y && Mathf.Abs(miss.x) < contactExtents.x &&
                !(descendingStep && relative.magnitude < 3f))
                return true;
            if (descendingStep && relative.magnitude > 3f && Mathf.Abs(closing.x) > 0.1f)
            {
                float crossingTime = relative.x / closing.x;
                float ballYAtCrossing = projectile.position.y + projectile.linearVelocity.y * crossingTime;
                float nextStandingY = target.FeetPosition.y + capsule.bounds.extents.y;
                if (crossingTime >= 0.18f && crossingTime <= 0.6f &&
                    Mathf.Abs(ballYAtCrossing - nextStandingY) < contactExtents.y)
                    return true;
            }
        }

        foreach (Cannon cannon in cannons)
        {
            if (cannon == null || !cannon.IsTelegraphing) continue;
            if (cannon.TelegraphTimeRemaining > .18f) continue;
            float distance = Vector2.Distance(cannon.transform.position, body.position);
            if (distance < 2.2f || distance > 8f || descendingStep && distance < 3f) continue;
            LineRenderer warning = cannon.GetComponent<LineRenderer>();
            if (warning == null || !warning.enabled || warning.positionCount < 2) continue;
            Vector2 from = warning.GetPosition(0);
            Vector2 segment = (Vector2)warning.GetPosition(1) - from;
            float along = Mathf.Clamp01(Vector2.Dot(body.position - from, segment) / Mathf.Max(0.01f, segment.sqrMagnitude));
            if (Vector2.Distance(body.position, from + segment * along) < contactExtents.y) return true;
        }
        return false;
    }

    private bool ShouldJumpCannonBody(CampaignLayout.RouteNode target)
    {
        float targetX = OrdinarySteeringX(target);
        foreach (Cannon cannon in cannons)
        {
            if (cannon == null) continue;
            Collider2D obstacle = cannon.BodyHitbox;
            if (obstacle == null || !obstacle.enabled) continue;
            Bounds bounds = obstacle.bounds;
            if (CannonBodyNeedsGroundJump(body.position.x, capsule.bounds.min.y, targetX, target.FeetPosition.y,
                Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y)))
                return true;
        }
        return false;
    }

    private bool WaitForCannonLandingWarning(CampaignLayout.RouteNode target)
    {
        if (!cannonLandingSteering || cannonLandingWarningSeen || target.RequiredAbility.HasValue ||
            !OnSafeSupport(layout.Platforms[Previous.PlatformIndex])) return false;
        Cannon nearby = null;
        foreach (Cannon candidate in cannons)
        {
            Collider2D hitbox = candidate != null ? candidate.BodyHitbox : null;
            if (hitbox == null || !hitbox.enabled || !hitbox.gameObject.activeInHierarchy ||
                Mathf.Abs(candidate.transform.position.y - target.FeetPosition.y) > 2.4f ||
                Mathf.Abs(candidate.transform.position.x - target.FeetPosition.x) > 1.8f ||
                Vector2.Distance(candidate.transform.position, body.position) > candidate.ActivationRange - .25f) continue;
            nearby = candidate;
            break;
        }
        if (nearby == null) return false;
        if (nearby.IsTelegraphing)
        {
            cannonLandingWarningSeen = true;
            Debug.Log($"PIRATE_ROUTE_CANNON_LANDING_WARNING chapter={layout.Chapter} node={targetIndex} " +
                $"position={body.position} origin={nearby.LockedShotOrigin} direction={nearby.LockedShotDirection} " +
                $"remaining={nearby.TelegraphTimeRemaining:F3} actualVisibleWarning=True projectileVetoStillRequired=True");
            return false;
        }
        if (cannonLandingWaitStarted < 0)
        {
            cannonLandingWaitStarted = frameAtNode;
            Debug.Log($"PIRATE_ROUTE_CANNON_LANDING_BAIT chapter={layout.Chapter} node={targetIndex} " +
                $"position={body.position} cannon={nearby.transform.position} previousActualSupport=True " +
                "ordinaryNeutralInput=True cannonClockUnchanged=True waitingForVisibleWarning=True");
        }
        if ((frameAtNode - cannonLandingWaitStarted) * Time.fixedDeltaTime > 5f)
            Fail("nearby active cannon did not produce a visible warning during bounded landing setup");
        return true;
    }

    private static bool CannonBodyNeedsGroundJump(float currentX, float currentFeet, float targetX,
        float targetFeet, Rect cannonBody)
    {
        float direction = Mathf.Sign(targetX - currentX);
        float ahead = (cannonBody.center.x - currentX) * direction;
        if (ahead < .65f || ahead > 3.2f || (targetX - cannonBody.center.x) * direction < -.2f) return false;
        float lowestPlannedFeet = Mathf.Min(currentFeet, targetFeet);
        return cannonBody.yMax > lowestPlannedFeet + .1f && cannonBody.yMin < targetFeet + 1f;
    }

    private static bool RunCannonBodyDownstepSelfCheck(out string detail)
    {
        Rect bodyBounds = Rect.MinMaxRect(63.91f, 5.04f, 65.31f, 5.94f);
        bool plannedDownstep = CannonBodyNeedsGroundJump(61.5f, 6.005f, 65.94f, 5f, bodyBounds);
        bool oldMissReproduced = !(bodyBounds.yMax > 6.005f + .1f);
        bool farRejected = !CannonBodyNeedsGroundJump(60.05f, 6.005f, 65.94f, 5f, bodyBounds);
        bool highRejected = !CannonBodyNeedsGroundJump(61.5f, 6.005f, 65.94f, 5f,
            Rect.MinMaxRect(63.91f, 8.04f, 65.31f, 8.94f));
        bool lowRejected = !CannonBodyNeedsGroundJump(61.5f, 6.005f, 65.94f, 5f,
            Rect.MinMaxRect(63.91f, 2.04f, 65.31f, 2.94f));
        bool beyondTargetRejected = !CannonBodyNeedsGroundJump(61.5f, 6.005f, 62.5f, 5f, bodyBounds);
        bool mirroredDownstep = CannonBodyNeedsGroundJump(-61.5f, 6.005f, -65.94f, 5f,
            Rect.MinMaxRect(-65.31f, 5.04f, -63.91f, 5.94f));
        detail = $"cannonBodyDownstepPlanned={plannedDownstep} oldBodyDownstepMiss={oldMissReproduced} " +
            $"cannonBodyFarRejected={farRejected} cannonBodyHighRejected={highRejected} cannonBodyLowRejected={lowRejected} " +
            $"cannonBodyBeyondTargetRejected={beyondTargetRejected} cannonBodyMirroredDownstep={mirroredDownstep} bodyJumpRuntimeProof=False";
        return plannedDownstep && oldMissReproduced && farRejected && highRejected && lowRejected && beyondTargetRejected && mirroredDownstep;
    }

    private struct WalkingForecastSettings
    {
        public float HalfHeight, MoveSpeed, GroundAcceleration, GroundDeceleration;
        public float AirAcceleration, AirDeceleration, Gravity, Step;
        public Vector2 ContactExtents;
    }

    private struct WalkingShotConflict
    {
        public float Time, Clearance;
        public Vector2 Pirate, Shot;
    }

    private bool TelegraphThreatensDownwardWalk(CampaignLayout.RouteNode target)
    {
        if (!player.IsGrounded || target.RequiredAbility.HasValue ||
            (target.Action != CampaignLayout.TraversalAction.Walk && target.Action != CampaignLayout.TraversalAction.Jump)) return false;
        var settings = new WalkingForecastSettings {
            HalfHeight = capsule.bounds.extents.y, MoveSpeed = player.MoveSpeed,
            GroundAcceleration = player.GroundAcceleration, GroundDeceleration = player.Deceleration,
            AirAcceleration = player.AirAcceleration, AirDeceleration = player.AirDeceleration,
            Gravity = player.GravityStrength, Step = Time.fixedDeltaTime, ContactExtents = ProjectileContactExtents()
        };
        float targetX = OrdinarySteeringX(target);
        foreach (Cannon cannon in cannons)
        {
            if (cannon == null || !cannon.IsTelegraphing ||
                Vector2.Distance(body.position, cannon.LockedShotOrigin) > 14f) continue;
            float delay = cannon.TelegraphTimeRemaining;
            Vector2 shotVelocity = cannon.LockedShotDirection * 8f;
            float travel = Vector2.Distance(cannon.LockedShotOrigin, cannon.TelegraphEnd);
            if (!WalkingWouldMeetShot(body.position, body.linearVelocity, targetX, layout.Platforms, settings,
                cannon.LockedShotOrigin, shotVelocity, delay, travel, out WalkingShotConflict conflict)) continue;
            if (lastTelegraphWalkConflictNode != targetIndex || Time.frameCount - lastTelegraphWalkConflictFrame >= 25)
            {
                Debug.Log($"PIRATE_ROUTE_TELEGRAPH_WALK_CONFLICT chapter={layout.Chapter} node={targetIndex} " +
                    $"decision=request-Space-pending-jump-veto remaining={delay:F3} predictedContactAt={conflict.Time:F3} " +
                    $"position={body.position} velocity={body.linearVelocity} steeringX={targetX:F3} " +
                    $"lockedOrigin={cannon.LockedShotOrigin} lockedVelocity={shotVelocity} " +
                    $"predictedPirate={conflict.Pirate} predictedShot={conflict.Shot} clearance={conflict.Clearance:F3} " +
                    $"horizontalForecastPadding={settings.MoveSpeed * settings.Step * 2f:F3} " +
                    "fullVisibleWarning=True actualSupportClamp=True predictionOnly=True ordinaryKeyboardInput=True");
                lastTelegraphWalkConflictNode = targetIndex;
                lastTelegraphWalkConflictFrame = Time.frameCount;
            }
            return true;
        }
        return false;
    }

    private static bool WalkingWouldMeetShot(Vector2 position, Vector2 velocity, float targetX,
        IReadOnlyList<CampaignLayout.Platform> platforms, WalkingForecastSettings settings,
        Vector2 origin, Vector2 shotVelocity, float delay, float travel, out WalkingShotConflict conflict)
    {
        conflict = default;
        bool grounded = true;
        float shotSpeed = shotVelocity.magnitude;
        if (settings.Step <= 0f || shotSpeed <= .01f || delay < 0f || delay > .76f) return false;
        float horizon = delay + .3f;
        Vector2 extents = settings.ContactExtents;
        extents.x += settings.MoveSpeed * settings.Step * 2f;
        for (int frame = 1; frame <= Mathf.CeilToInt(horizon / settings.Step); frame++)
        {
            float time = frame * settings.Step;
            float braking = grounded ? settings.GroundDeceleration : settings.AirDeceleration;
            float acceleration = grounded ? settings.GroundAcceleration : settings.AirAcceleration;
            float dx = targetX - position.x;
            float stopping = velocity.x * velocity.x / (2f * Mathf.Max(1f, braking));
            float input = Mathf.Abs(dx) < .12f || dx * velocity.x > 0f && stopping >= Mathf.Abs(dx) - .12f
                ? 0f : Mathf.Sign(dx);
            velocity.x = Mathf.MoveTowards(velocity.x, input * settings.MoveSpeed,
                (input == 0f ? braking : acceleration) * settings.Step);
            velocity.y -= settings.Gravity * settings.Step;
            float previousFeet = position.y - settings.HalfHeight;
            position += velocity * settings.Step;
            grounded = ClampForecastToSupport(platforms, settings.HalfHeight, previousFeet, ref position, ref velocity);
            if (time < delay || (time - delay) * shotSpeed > travel) continue;
            Vector2 shot = origin + shotVelocity * (time - delay);
            float clearance = Mathf.Max(Mathf.Abs(position.x - shot.x) - extents.x,
                Mathf.Abs(position.y - shot.y) - extents.y);
            if (clearance >= 0f) continue;
            conflict = new WalkingShotConflict { Time = time, Clearance = clearance, Pirate = position, Shot = shot };
            return true;
        }
        return false;
    }

    private static bool RunTelegraphWalkSelfCheck(out string detail)
    {
        var platforms = new[] {
            new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(27.60556f, 70.1f, 30.50556f, 70.5f) },
            new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(30.51667f, 69.6f, 33.81667f, 70f) },
            new CampaignLayout.Platform { Bounds = Rect.MinMaxRect(33.72778f, 69.6f, 36.82778f, 70f) }
        };
        var settings = new WalkingForecastSettings { HalfHeight = .5f, MoveSpeed = 8f,
            GroundAcceleration = 50f, GroundDeceleration = 60f, AirAcceleration = 37.5f, AirDeceleration = 45f,
            Gravity = 34.335f, Step = .02f, ContactExtents = new Vector2(.5f, .745f) };
        Vector2 start = new Vector2(29.017f, 71.005f);
        Vector2 origin = new Vector2(31.86f, 70.754f);
        Vector2 shotVelocity = new Vector2(-7.66f, 2.31f);
        const float visibleDelay = .5f;
        bool anticipated = WalkingWouldMeetShot(start, Vector2.zero, 33.26667f, platforms, settings,
            origin, shotVelocity, visibleDelay, 40f, out WalkingShotConflict conflict);
        bool highShotRejected = !WalkingWouldMeetShot(start, Vector2.zero, 33.26667f, platforms, settings,
            origin + Vector2.up * 5f, shotVelocity, visibleDelay, 40f, out _);
        detail = $"downstepTelegraphAnticipated={anticipated} contactAt={conflict.Time:F3} " +
            $"warningOutsideOldWindow={visibleDelay > .18f} highShotRejected={highShotRejected}";
        return anticipated && conflict.Time >= visibleDelay && highShotRejected;
    }

    private bool JumpWouldMeetProjectile(CampaignLayout.RouteNode target)
    {
        var shots = new List<(Vector2 position, Vector2 velocity, float delay)>();
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
        {
            Rigidbody2D projectile = ball.GetComponent<Rigidbody2D>();
            if (projectile != null && Vector2.Distance(body.position,projectile.position) < 14f)
                shots.Add((projectile.position,projectile.linearVelocity,0f));
        }
        foreach (Cannon cannon in cannons)
            if (cannon != null && cannon.IsTelegraphing && Vector2.Distance(body.position,cannon.LockedShotOrigin) < 14f)
                shots.Add((cannon.LockedShotOrigin,cannon.LockedShotDirection*8f,cannon.TelegraphTimeRemaining));
        if (shots.Count == 0) return false;
        Vector2 predicted = body.position;
        Vector2 velocity = new Vector2(body.linearVelocity.x,player.JumpLaunchSpeed);
        Vector2 contactExtents = ProjectileContactExtents();
        float targetX = OrdinarySteeringX(target);
        bool launchCollision = false;
        bool imminentStandingCollision = false;
        float landingTime = -1f;
        float firstContact = -1f;
        float minimumClearance = float.PositiveInfinity;
        Vector2 contactPirate = Vector2.zero, contactBall = Vector2.zero;
        float horizon = Mathf.Clamp(2f * player.JumpLaunchSpeed / player.GravityStrength + .35f, .6f, 1.5f);
        int predictionFrames = Mathf.CeilToInt(horizon / Time.fixedDeltaTime);
        for (int frame = 1; frame <= predictionFrames; frame++)
        {
            float dt = Time.fixedDeltaTime;
            float time = frame*dt;
            float dx = targetX-predicted.x;
            float stop = velocity.x*velocity.x/(2f*player.AirDeceleration);
            float input = Mathf.Abs(dx)<.12f || dx*velocity.x>0f && stop>=Mathf.Abs(dx)-.12f ? 0f : Mathf.Sign(dx);
            velocity.x = Mathf.MoveTowards(velocity.x,input*player.MoveSpeed,
                (input==0f ? player.AirDeceleration : player.AirAcceleration)*dt);
            velocity.y -= player.GravityStrength*dt;
            float previousFeet = predicted.y - capsule.bounds.extents.y;
            predicted += velocity*dt;
            if (ClampForecastToRealSupport(previousFeet, ref predicted, ref velocity) && landingTime < 0f)
                landingTime = time;
            foreach (var shot in shots)
            {
                if (time < shot.delay) continue;
                Vector2 point = shot.position+shot.velocity*(time-shot.delay);
                float clearance = Mathf.Max(Mathf.Abs(point.x-predicted.x)-contactExtents.x,
                    Mathf.Abs(point.y-predicted.y)-contactExtents.y);
                minimumClearance = Mathf.Min(minimumClearance, clearance);
                if (clearance < 0f)
                {
                    launchCollision = true;
                    if (firstContact < 0f) { firstContact = time; contactPirate = predicted; contactBall = point; }
                }
                if (time<=.22f && Mathf.Abs(point.x-body.position.x)<contactExtents.x &&
                    Mathf.Abs(point.y-body.position.y)<contactExtents.y)
                    imminentStandingCollision=true;
            }
            if (landingTime >= 0f && time >= landingTime + .3f) break;
        }
        bool wait = launchCollision || imminentStandingCollision || ChasingBallThreatensJump(target);
        if (lastProjectileForecastNode != targetIndex || lastProjectileForecastBlocked != wait ||
            Time.frameCount - lastProjectileForecastFrame >= 25)
        {
            Debug.Log($"PIRATE_ROUTE_PROJECTILE_FORECAST chapter={layout.Chapter} node={targetIndex} " +
                $"decision={(wait ? "wait-before-jump" : "jump-window")} position={body.position} velocity={body.linearVelocity} " +
                $"landingAt={landingTime:F3} firstContactAt={firstContact:F3} minClearance={minimumClearance:F3} " +
                $"contactPirate={contactPirate} contactBall={contactBall} contactExtents={contactExtents} " +
                $"standingImminent={imminentStandingCollision} shots={shots.Count} postLandingLookahead=.30 " +
                "predictionOnly=True ordinaryKeyboardInput=True");
            lastProjectileForecastNode = targetIndex;
            lastProjectileForecastBlocked = wait;
            lastProjectileForecastFrame = Time.frameCount;
        }
        return wait;
    }

    private bool ChasingBallThreatensJump(CampaignLayout.RouteNode target)
    {
        float travel = Mathf.Sign(OrdinarySteeringX(target) - body.position.x);
        if (Mathf.Abs(travel) < 0.1f) travel = Mathf.Sign(body.linearVelocity.x);
        if (Mathf.Abs(travel) < 0.1f) travel = 1f;
        Vector2 extents = ProjectileContactExtents();
        float hang = Mathf.Clamp(2f * player.JumpLaunchSpeed / player.GravityStrength, 0.55f, 1.2f);
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
        {
            Rigidbody2D projectile = ball.GetComponent<Rigidbody2D>();
            if (projectile == null) continue;
            float closing = projectile.linearVelocity.x * travel;
            if (closing <= 4f) continue;
            float behind = (body.position.x - projectile.position.x) * travel;
            if (behind <= 0.05f) continue;
            float catchRange = closing * hang + extents.x + 0.6f;
            if (behind > catchRange) continue;
            if (Mathf.Abs(projectile.position.y - body.position.y) > extents.y + 1.2f) continue;
            return true;
        }
        return false;
    }

    private float EvadeAxisAlongSupport(CampaignLayout.Platform support, CampaignLayout.RouteNode target)
    {
        Cannonball nearest = null;
        float best = float.PositiveInfinity;
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
        {
            float distance = Vector2.Distance(ball.transform.position, body.position);
            if (distance >= best || distance > 10f) continue;
            best = distance;
            nearest = ball;
        }
        if (nearest == null) return 0f;
        float away = Mathf.Sign(body.position.x - nearest.transform.position.x);
        if (Mathf.Abs(away) < 0.1f) away = Mathf.Sign(target.FeetPosition.x - body.position.x);
        if (Mathf.Abs(away) < 0.1f) away = 1f;
        float inset = capsule.bounds.extents.x + 0.35f;
        float nextX = body.position.x + away * 0.8f;
        if (nextX < support.Bounds.xMin + inset || nextX > support.Bounds.xMax - inset) away = -away;
        nextX = body.position.x + away * 0.8f;
        if (nextX < support.Bounds.xMin + inset || nextX > support.Bounds.xMax - inset) return 0f;
        return away;
    }

    private Vector2 ProjectileContactExtents() => (Vector2)capsule.bounds.extents +
        Vector2.one * (Cannon.ProjectileRadius + .035f);

    private bool ClampForecastToRealSupport(float previousFeet, ref Vector2 position, ref Vector2 velocity)
        => ClampForecastToSupport(layout.Platforms, capsule.bounds.extents.y, previousFeet, ref position, ref velocity);

    private static bool ClampForecastToSupport(IReadOnlyList<CampaignLayout.Platform> platforms, float halfHeight,
        float previousFeet, ref Vector2 position, ref Vector2 velocity)
    {
        if (velocity.y > 0f) return false;
        float feet = position.y - halfHeight;
        float highest = float.NegativeInfinity;
        foreach (CampaignLayout.Platform support in platforms)
        {
            if (support.SurfaceY > previousFeet + .025f || support.SurfaceY < feet ||
                position.x < support.Bounds.xMin || position.x > support.Bounds.xMax) continue;
            highest = Mathf.Max(highest, support.SurfaceY);
        }
        if (float.IsNegativeInfinity(highest)) return false;
        position.y = highest + halfHeight;
        velocity.y = 0f;
        return true;
    }

    private bool TryAirDashOverIncomingBall(CampaignLayout.RouteNode target, out float direction)
    {
        direction = 0f;
        if (body.linearVelocity.y > -1f) return false;
        Vector2 contactExtents = ProjectileContactExtents();
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
        {
            Rigidbody2D projectile = ball.GetComponent<Rigidbody2D>();
            if (projectile == null || body.position.y - projectile.position.y < contactExtents.y + .1f) continue;
            bool threatensLanding = false;
            float predictedContact = -1f;
            Vector2 forecastPosition = body.position;
            Vector2 forecastVelocity = body.linearVelocity;
            for (int sample = 1; sample <= 20; sample++)
            {
                float time = sample * Time.fixedDeltaTime;
                float previousFeet = forecastPosition.y - capsule.bounds.extents.y;
                forecastVelocity.y -= player.GravityStrength * Time.fixedDeltaTime;
                forecastPosition += forecastVelocity * Time.fixedDeltaTime;
                ClampForecastToRealSupport(previousFeet, ref forecastPosition, ref forecastVelocity);
                Vector2 cannonball = projectile.position + projectile.linearVelocity * time;
                if (Mathf.Abs(forecastPosition.x - cannonball.x) < contactExtents.x &&
                    Mathf.Abs(forecastPosition.y - cannonball.y) < contactExtents.y)
                { threatensLanding = true; predictedContact = time; break; }
            }
            if (!threatensLanding) continue;
            float away = Mathf.Sign(body.position.x - projectile.position.x);
            if (away == 0f) continue;
            const float dashSpeed = 18f;
            const float dashDuration = 0.16f;
            float brakingAcceleration = Mathf.Max(0.01f, Mathf.Min(player.AirAcceleration, player.AirDeceleration));
            float brakingTime = dashSpeed / brakingAcceleration;
            float brakingDistance = dashSpeed * dashSpeed / (2f * brakingAcceleration);
            float estimatedX = body.position.x + away * (dashSpeed * dashDuration + brakingDistance);
            bool safeLanding = false;
            int safeSupportNode = -1;
            for (int i = Mathf.Max(0, targetIndex - 2); i <= Mathf.Min(layout.Route.Count - 1, targetIndex + 1); i++)
            {
                CampaignLayout.RouteNode node = layout.Route[i];
                if (node.RequiredAbility.HasValue || node.RoomIndex != target.RoomIndex) continue;
                CampaignLayout.Platform support = layout.Platforms[node.PlatformIndex];
                float drop = capsule.bounds.min.y - support.SurfaceY;
                if (drop <= 0f || drop >= 3f) continue;
                float timeToLand = Mathf.Min(brakingTime, Mathf.Sqrt(2f * drop / player.GravityStrength));
                float landingX = body.position.x + away * (dashSpeed * dashDuration + dashSpeed * timeToLand -
                    0.5f * brakingAcceleration * timeToLand * timeToLand);
                float margin = capsule.bounds.extents.x + 0.2f;
                if (estimatedX > support.Bounds.xMin + margin && estimatedX < support.Bounds.xMax - margin &&
                    landingX > support.Bounds.xMin + margin && landingX < support.Bounds.xMax - margin)
                { safeLanding = true; safeSupportNode = i; break; }
            }
            if (lastDashDecisionNode != targetIndex || lastDashDecisionAllowed != safeLanding)
                Debug.Log($"PIRATE_ROUTE_DASH_SAFETY chapter={layout.Chapter} node={targetIndex} " +
                          $"decision={(safeLanding ? "allow" : "reject")} reason=fullMomentumStoppingPath " +
                          $"position={body.position} axis={away:F0} predictedStopX={estimatedX:F2} " +
                          $"brakingAcceleration={brakingAcceleration:F2} brakingDistance={brakingDistance:F2} supportNode={safeSupportNode} " +
                          $"predictedContactAt={predictedContact:F3} predictedContactPirate={forecastPosition} " +
                          $"ball={projectile.position} ballVelocity={projectile.linearVelocity} contactExtents={contactExtents}");
            lastDashDecisionNode = targetIndex;
            lastDashDecisionAllowed = safeLanding;
            if (safeLanding) { direction = away; return true; }
        }
        return false;
    }

    private void CountSpring()
    {
        springBounces++;
        if (body != null && layout != null && targetIndex >= 0 && targetIndex < layout.Route.Count)
            Debug.Log($"PIRATE_ROUTE_SPRING_BOUNCED chapter={layout.Chapter} node={targetIndex} count={springBounces} " +
                $"position={body.position} feet={capsule.bounds.min.y:F3} velocity={body.linearVelocity} " +
                $"target={layout.Route[targetIndex].FeetPosition} actualProductionEvent=True");
    }
    private void CountSaber() => saberSwings++;

    private bool ShouldSwingAtThreat()
    {
        if (abilities.SaberLevel == 0 || !player.ControlsEnabled) return false;
        foreach (BrineCrawler crawler in FindObjectsByType<BrineCrawler>(FindObjectsSortMode.None))
        {
            if (crawler.IsDefeated) continue;
            Collider2D hitbox = crawler.GetComponent<Collider2D>();
            if (hitbox != null && Vector2.Distance(hitbox.ClosestPoint(body.position),body.position) < 1.8f) return true;
        }
        foreach (SnareTrap noose in FindObjectsByType<SnareTrap>(FindObjectsSortMode.None))
        {
            if (noose.IsCut) continue;
            Collider2D hitbox = noose.GetComponent<Collider2D>();
            if (noose.HasVictim || hitbox != null && Vector2.Distance(hitbox.ClosestPoint(body.position), body.position) < 1.65f)
                return true;
        }
        foreach (HangingPlant plant in FindObjectsByType<HangingPlant>(FindObjectsSortMode.None))
        {
            if (plant.IsDead) continue;
            Collider2D hitbox = plant.BodyHitbox;
            if (hitbox != null && hitbox.enabled && Vector2.Distance(hitbox.ClosestPoint(body.position), body.position) < 1.8f)
                return true;
        }
        return false;
    }

    private IEnumerator ScoutDarkGallery()
    {
        CampaignLayout.Spawn pickup = layout.Spawns.FirstOrDefault(spawn =>
            spawn.Kind == CampaignLayout.SpawnKind.Upgrade && spawn.Ability == PirateUpgrade.Parrot);
        if (pickup == null) { Fail("dark gallery has no reachable parrot pickup"); yield break; }
        for (int frame = 0; frame < 180 && !abilities.HasParrot; frame++)
        {
            player.SetAutomationInputOverride(new Vector2(AxisTo(pickup.Position.x), 0f));
            yield return null;
        }
        if (!abilities.HasParrot) { Fail("could not physically collect the parrot at the dark-gallery entrance"); yield break; }
        while (flow.HUD != null && flow.HUD.IsUpgradeOpen) yield return null;
        player.SetAutomationInputOverride(Vector2.zero);
        for (int frame = 0; frame < 90 && (!player.IsGrounded || Mathf.Abs(body.linearVelocity.x) > 0.1f); frame++) yield return null;
        abilities.SetAutomationScoutPressed();
        yield return null;
        yield return null;
        if (!abilities.IsScouting) { Fail("Q did not start the grounded parrot scout"); yield break; }
        Vector2 pirateStart = body.position;
        for (int frame = 0; frame < 60; frame++)
        {
            abilities.Scout.SetAutomationInput(frame < 45 ? Vector2.right : Vector2.up);
            yield return null;
            scoutDistance = Mathf.Max(scoutDistance, Vector2.Distance(abilities.Scout.transform.position, pirateStart));
            if (frame == 45) Capture("actual-parrot-reconnaissance.png");
        }
        abilities.Scout.SetAutomationInput(Vector2.zero);
        abilities.SetAutomationScoutPressed();
        yield return null;
        yield return null;
        abilities.Scout.ClearAutomationInput();
        if (abilities.IsScouting || !player.ControlsEnabled || Vector2.Distance(body.position, pirateStart) > 0.15f || scoutDistance < 6f)
        { Fail("parrot Q/WASD reconnaissance did not return control cleanly"); yield break; }
        scoutFlights++;
        Debug.Log($"PIRATE_ROUTE_SCOUT_USED chapter={layout.Chapter} distance={scoutDistance:F2} " +
                  "pickupByTrigger=True QEdges=2 birdAxesKeyboard=True pirateMoved=False");
        Capture("parrot-returned-control.png");
    }

    private IEnumerator RecoverUsingRoomPad()
    {
        string attemptKey = layout.Chapter + ":" + targetIndex;
        floorRecoveryAttemptsByNode.TryGetValue(attemptKey, out int attemptsAtNode);
        floorRecoveryAttemptsByNode[attemptKey] = ++attemptsAtNode;
        floorRecoveryAttempts++;
        if (attemptsAtNode > 3 || floorRecoveryAttempts > 12)
        { Fail("bounded floor recovery exhausted (three attempts per node, twelve per campaign)"); yield break; }

        CampaignLayout.Spawn pad = layout.Spawns.Where(IsAllowedRecoveryPad)
            .OrderBy(spawn => Mathf.Abs(spawn.Position.x - body.position.x)).FirstOrDefault();
        if (pad == null) { Fail("fallen route has no previously reached local springboard without crossing an ability gate"); yield break; }
        int recoveryTarget = pad.NodeIndex;
        CampaignLayout.RouteNode entrance = layout.Route[recoveryTarget];
        Debug.Log($"PIRATE_ROUTE_RECOVERY_ATTEMPT chapter={layout.Chapter} node={targetIndex} " +
                  $"floorPosition={body.position} springboard={pad.Position} recoveryNode={recoveryTarget} " +
                  $"attempt={attemptsAtNode} testTeleport=False forwardRouteSkip=False");
        grapple.SetAutomationInputOverride(false);
        abilities.SetAutomationSlide(false);
        for (int frame = 0; frame < 700; frame++)
        {
            if (life.IsRespawning) yield break;
            if (player.IsGrounded && Mathf.Abs(capsule.bounds.min.y - entrance.FeetPosition.y) < 0.14f &&
                Mathf.Abs(body.position.x - entrance.FeetPosition.x) < 1.2f)
            {
                recoveryPadsUsed++;
                targetIndex = recoveryTarget;
                ResetNodeState();
                Debug.Log($"PIRATE_ROUTE_RECOVERY_SUCCESS chapter={layout.Chapter} entranceNode={recoveryTarget} " +
                          $"position={body.position} normalPhysics=True testTeleport=False");
                yield break;
            }
            bool jump = player.IsGrounded && capsule.bounds.min.y < pad.Position.y - 0.35f &&
                        Mathf.Abs(body.position.x - pad.Position.x) < 1.6f && frame % 15 == 0;
            if (jump) jumps++;
            float recoveryX = capsule.bounds.min.y > entrance.FeetPosition.y + 0.08f ? entrance.FeetPosition.x : pad.Position.x;
            player.SetAutomationInputOverride(new Vector2(AxisTo(recoveryX), 0f), jumpPressed: jump);
            if (frame % 3 == 0 && ShouldSwingAtThreat()) { abilities.SetAutomationAttackPressed(); attacks++; }
            yield return null;
        }
        Fail("could not physically return from the bay floor via its recovery springboard");
    }

    private bool IsAllowedRecoveryPad(CampaignLayout.Spawn pad)
    {
        if (pad.Kind != CampaignLayout.SpawnKind.JumpPad || !pad.Optional ||
            pad.NodeIndex < 0 || pad.NodeIndex > targetIndex) return false;
        CampaignLayout.RouteNode target = layout.Route[targetIndex];
        CampaignLayout.Region room = layout.Rooms[target.RoomIndex];
        int padRoomIndex = layout.Route[pad.NodeIndex].RoomIndex;
        CampaignLayout.Region padRoom = layout.Rooms[padRoomIndex];
        int previousRoomIndex = room.FirstRouteNode > 0 ? layout.Route[room.FirstRouteNode - 1].RoomIndex : -1;
        if (padRoomIndex != target.RoomIndex && (!room.IsShaft || padRoomIndex != previousRoomIndex)) return false;
        if (padRoom.Band != room.Band ||
            (pad.NodeIndex != padRoom.FirstRouteNode && pad.NodeIndex != padRoom.LastRouteNode)) return false;
        if (Mathf.Abs(pad.Position.y - 1.15f - capsule.bounds.min.y) > 1.2f) return false;
        if (target.RequiredAbility.HasValue && pad.NodeIndex != room.FirstRouteNode) return false;
        for (int node = pad.NodeIndex + 1; node < targetIndex; node++)
            if (layout.Route[node].RequiredAbility.HasValue) return false;
        return true;
    }

    private void Fail(string message)
    {
        if (done) return;
        done = true;
        if (player != null)
        {
            player.SetAutomationInputOverride(Vector2.zero);
            grapple.SetAutomationInputOverride(false);
        }
        Capture("route-failure.png");
        if (body != null) LogNearbyThreatState();
        if (player != null) Debug.Log(PlayerContactDiagnostics.Describe(player));
        string details = body != null && layout != null
            ? $" chapter={layout.Chapter} node={targetIndex} action={layout.Route[targetIndex].Action} " +
              $"pos={body.position} velocity={body.linearVelocity} feet={capsule.bounds.min.y:F3} grounded={player.IsGrounded} " +
              $"target={layout.Route[targetIndex].FeetPosition} chainPhase={chainPhase} attached={grapple.IsAttached}"
            : string.Empty;
        Debug.LogError("PIRATE_ROUTE_PLAYTEST_FAILED " + message + details);
        Application.Quit(8);
    }

    private void LogNearbyThreatState()
    {
        foreach (HangingPlant plant in FindObjectsByType<HangingPlant>(FindObjectsSortMode.None))
            if (Vector2.Distance(plant.transform.position, body.position) < 5f)
                Debug.Log($"PIRATE_ROUTE_NEARBY_PLANT position={plant.transform.position} dead={plant.IsDead} " +
                          $"windingUp={plant.IsWindingUp} attacking={plant.IsAttacking}");
        foreach (Cannonball ball in FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
            if (Vector2.Distance(ball.transform.position, body.position) < 5f)
            {
                Rigidbody2D projectile = ball.GetComponent<Rigidbody2D>();
                Debug.Log($"PIRATE_ROUTE_NEARBY_BALL position={ball.transform.position} " +
                          $"velocity={(projectile != null ? projectile.linearVelocity : Vector2.zero)}");
            }
    }

    private static string Argument(string key)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i] == key) return args[i + 1];
        return null;
    }

    private void Capture(string name)
    {
        if (string.IsNullOrWhiteSpace(evidence) || Camera.main == null ||
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        Directory.CreateDirectory(evidence);
        Camera camera = Camera.main;
        RenderTexture render = new RenderTexture(1280, 720, 24);
        Texture2D pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        camera.targetTexture = render;
        camera.Render();
        RenderTexture.active = render;
        pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        pixels.Apply();
        camera.targetTexture = oldTarget;
        RenderTexture.active = oldActive;
        File.WriteAllBytes(Path.Combine(evidence, name), pixels.EncodeToPNG());
        Destroy(render);
        Destroy(pixels);
    }
}
