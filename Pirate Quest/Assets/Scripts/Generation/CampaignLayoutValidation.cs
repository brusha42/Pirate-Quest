using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CampaignLayout
{
    public bool Validate(out string reason)
    {
        bool valid = Validate();
        reason = valid ? "Geometry / split-window / ability-order contract passed; runtime traversal is a separate test."
            : string.Join("; ", ValidationErrors);
        return valid;
    }

    private bool Validate()
    {
        ValidationErrors.Clear();
        if (Rooms.Count == 0 || MacroStages.Count == 0 ||
            Mathf.Abs(Rooms[0].Exit.Minimum - MacroStages[MacroStages.Count - 1].Exit.Minimum) > .015f ||
            Mathf.Abs(Rooms[0].Exit.Maximum - MacroStages[MacroStages.Count - 1].Exit.Maximum) > .015f ||
            Mathf.Abs(Rooms[0].Exit.Coordinate - MacroStages[MacroStages.Count - 1].Exit.Coordinate) > .015f ||
            Rooms[0].Exit.Side != MacroStages[MacroStages.Count - 1].Exit.Side)
            ValidationErrors.Add("Root exit-window metadata does not match the actual final macro stage.");
        if (Route.Count < BandCount * (UsesChamberPlan ? 10 : 15)) ValidationErrors.Add("Route is missing rooms or transition landings.");
        if (PlannedChambers != BandCount || (Chapter == 0 ? AcceptedSplits < BandCount || CompatibilityChecks < AcceptedSplits * 2 :
            AcceptedSplits != 0 || CompatibilityChecks < BandCount))
            ValidationErrors.Add("Compatible chamber fills or the explicit macro stage graph were not built.");
        if (Vector2.Distance(Start, Exit) < 20f) ValidationErrors.Add("Chapter exit is not separated from the entrance.");
        var strategies = new HashSet<StrategyKind>();
        int horizontalRooms = 0;
        int shafts = 0;
        int branches = 0;
        foreach (Region room in Rooms)
        {
            if (!room.IsLeaf) continue;
            strategies.Add(room.Strategy);
            if (room.HasOptionalBranch) branches++;
            if (room.IsShaft) { shafts++; continue; }
            horizontalRooms++;
            if (Mathf.Abs(room.Entrance.x - (room.Exit.Side == Side.Right ? room.Bounds.xMax - 2.5f : room.Bounds.xMin + 2.5f)) < 17f)
                ValidationErrors.Add("A room does not require horizontal traversal: " + room.Id);
            if (room.FirstRouteNode < 0 || room.LastRouteNode < room.FirstRouteNode || room.LastRouteNode >= Route.Count)
                ValidationErrors.Add("Region route interval is incomplete: " + room.Id);
        }
        if (horizontalRooms != PlayableRoomCount || shafts != BandCount - 1)
            ValidationErrors.Add($"Expected {PlayableRoomCount} filled rooms and {BandCount - 1} graph connectors in {BandCount} logical stages.");
        if (!strategies.Contains(StrategyKind.Pyramid) || !strategies.Contains(StrategyKind.Grid) ||
            Chapter < 3 && !strategies.Contains(StrategyKind.JumpPad))
            ValidationErrors.Add("Pyramid, Grid and the chapter's required JumpPad strategies are not all represented.");
        if (Chapter == 3)
        {
            Region slideHook = Rooms.Find(room => room.IsLeaf && !room.IsShaft && room.ScenarioId == "crown.slide-ring-launch");
            bool actualSlideHook = slideHook != null && slideHook.Strategy == StrategyKind.SaberSlide &&
                Route.Exists(node => node.RoomIndex == slideHook.Id && node.Action == TraversalAction.SaberSlide) &&
                Route.Exists(node => node.RoomIndex == slideHook.Id && node.Action == TraversalAction.Grapple);
            if (!actualSlideHook || !Route.Exists(node => node.Action == TraversalAction.RingRelay))
                ValidationErrors.Add("Crown requires an actual slide-hook transfer and ring relay instead of a generic jump pad.");
        }
        if (branches == 0) ValidationErrors.Add("No optional grid branch was generated.");

        for (int i = 0; i < Platforms.Count; i++)
        {
            Platform platform = Platforms[i];
            Rect r = platform.Bounds;
            if (r.width <= 0f || r.height <= 0f || r.xMin < Bounds.xMin + 0.9f || r.xMax > Bounds.xMax - 0.9f || r.yMin < Bounds.yMin + 0.8f || r.yMax > Bounds.yMax - 0.9f)
                ValidationErrors.Add("Platform outside physical bounds: " + i);
        }

        var learned = new HashSet<PirateUpgrade>();
        foreach (PirateUpgrade ability in Enum.GetValues(typeof(PirateUpgrade)))
            if (AvailableAtChapterStart(ability)) learned.Add(ability);
        int gateCount = 0;
        for (int i = 0; i < Route.Count; i++)
        {
            foreach (Spawn spawn in Spawns)
                if (spawn.Kind == SpawnKind.Upgrade && spawn.NodeIndex <= i) learned.Add(spawn.Ability);
            RouteNode node = Route[i];
            if (node.PlatformIndex < 0 || node.PlatformIndex >= Platforms.Count)
            { ValidationErrors.Add("Invalid route support index: " + i); continue; }
            Platform support = Platforms[node.PlatformIndex];
            if (Mathf.Abs(support.SurfaceY - node.FeetPosition.y) > 0.015f ||
                node.FeetPosition.x < support.Bounds.xMin + 0.5f || node.FeetPosition.x > support.Bounds.xMax - 0.5f)
                ValidationErrors.Add("Feet/support surface mismatch: " + i);
            if (BodyOverlapsArchitecture(node.FeetPosition, node.PlatformIndex))
                ValidationErrors.Add("Route landing body overlaps solid architecture: " + i);
            if (node.RequiredAbility.HasValue)
            {
                gateCount++;
                if (!learned.Contains(node.RequiredAbility.Value))
                    ValidationErrors.Add("Ability gate occurs before its pickup: " + node.RequiredAbility.Value);
            }
            if (node.SecondaryAbility.HasValue && !learned.Contains(node.SecondaryAbility.Value))
                ValidationErrors.Add("Combined ability gate occurs before its pickup: " + node.SecondaryAbility.Value);
            if (i == 0) continue;
            Vector2 delta = node.FeetPosition - Route[i - 1].FeetPosition;
            if (node.Action == TraversalAction.Walk || node.Action == TraversalAction.Jump)
            {
                if (delta.y > SafeJumpHeight + 0.015f || Mathf.Abs(delta.x) > SafeJumpDistance + 0.02f || delta.y < -2.05f)
                    ValidationErrors.Add($"Ordinary movement envelope exceeded at node {i}: {delta}");
            }
            else if (node.Action == TraversalAction.DoubleJump && (delta.y < 3.7f || delta.y > 4.3f))
                ValidationErrors.Add("Double-jump gate must exceed the ordinary apex with reserve.");
            else if (node.Action == TraversalAction.Spring && (delta.y < 3.7f || delta.y > 4.1f))
                ValidationErrors.Add("Spring gate outside the 18 m/s bounce envelope.");
            else if (node.Action == TraversalAction.Chain && Mathf.Abs(delta.x) < 16.5f)
                ValidationErrors.Add("Chain gap can be bypassed by an ordinary jump/dash.");
        }
        if (gateCount < 2) ValidationErrors.Add("Chapter is missing genuine mechanic-gated transitions.");
        ValidateMacroGraph();
        ValidateCannonStations();
        ValidateRoomEnclosures();
        ValidateArchitectureHull();
        ValidateSpawnSafety();
        ValidateSlideBarriers();
        ValidateCheckpointSpacing();
        ValidateChapterIdentity();
        ValidateExpeditionScenarios();
        ValidateSecretCaches();
        return ValidationErrors.Count == 0;
    }

    private bool BodyOverlapsArchitecture(Vector2 feet, int ownSupport)
    {
        Rect body = new Rect(feet.x - 0.29f, feet.y + 0.035f, 0.58f, 0.95f);
        foreach (RectInt wall in Solids)
            if (body.Overlaps(new Rect(wall.x, wall.y, wall.width, wall.height))) return true;
        for (int i = 0; i < Platforms.Count; i++)
            if (i != ownSupport && !Platforms[i].OneWay && body.Overlaps(Platforms[i].Bounds)) return true;
        return false;
    }

    private void ValidateRoomEnclosures()
    {
        foreach (MacroStage stage in MacroStages)
        foreach (int x in new[] { stage.Bounds.xMin - 1, stage.Bounds.xMax })
        {
            bool sill = false, upperWall = false, openingBlocked = false;
            foreach (RectInt wall in Solids)
            {
                if (wall.Contains(new Vector2Int(x, stage.BaseY + 1))) sill = true;
                if (wall.Contains(new Vector2Int(x, stage.BaseY + 12))) upperWall = true;
                if (wall.Contains(new Vector2Int(x, stage.BaseY + 4))) openingBlocked = true;
            }
            if (!sill || !upperWall || openingBlocked)
                ValidationErrors.Add("Room/shaft shell has a missing wall or blocked doorway: stage " + stage.Slot + ", x=" + x);
        }
        foreach (Region room in Rooms)
            if (room.IsLeaf && !room.IsShaft &&
                (room.Strategy == StrategyKind.Pyramid || room.Strategy == StrategyKind.Grid || room.Strategy == StrategyKind.DarkGallery) &&
                !room.HasOverheadStructure)
                ValidationErrors.Add("Ordinary room is missing its compact overhead structure: " + room.Id);
    }

    private void ValidateArchitectureHull()
    {
        EnsureArchitectureEnvelope();
        RectInt env = architectureEnvelope;
        if (env.width <= 0 || env.height <= 0)
        {
            ValidationErrors.Add("Architecture envelope is empty.");
            return;
        }
        bool[,] playable = RasterizePlayableOccupancy(env);

        var covered = new bool[env.width, env.height];
        foreach (RectInt wall in Solids) StampOccupancy(covered, env, wall);
        int missingHull = 0, missingPerimeter = 0;
        for (int y = 0; y < env.height; y++)
        for (int x = 0; x < env.width; x++)
        {
            if (playable[x, y]) continue;
            if (covered[x, y]) continue;
            missingHull++;
            if (x == 0 || y == 0 || x == env.width - 1 || y == env.height - 1) missingPerimeter++;
        }
        if (missingHull > 0)
            ValidationErrors.Add("Architecture hull masonry is missing colliders: " + missingHull + " cells.");
        if (missingPerimeter > 0)
            ValidationErrors.Add("Architecture envelope is not enclosed: " + missingPerimeter + " perimeter gaps.");
    }

    private void ValidateSpawnSafety()
    {
        foreach (Spawn spawn in Spawns)
        {
            if (spawn.NodeIndex < 0 || spawn.NodeIndex >= Route.Count)
                ValidationErrors.Add("Spawn has no reachable source region: " + spawn.Kind);
            bool damaging = spawn.Kind == SpawnKind.Spikes || spawn.Kind == SpawnKind.Snare || spawn.Kind == SpawnKind.Plant || spawn.Kind == SpawnKind.Crawler;
            if (damaging && Vector2.Distance(spawn.Position, Start) < 5f)
                ValidationErrors.Add("Damaging object overlaps the starting safety zone: " + spawn.Kind);
            if (spawn.Kind == SpawnKind.Crawler)
            {
                int supportIndex = (int)spawn.Value;
                if (supportIndex < 0 || supportIndex >= Platforms.Count)
                    ValidationErrors.Add("Crawler has no physical support.");
                else
                {
                    Platform support = Platforms[supportIndex];
                    if (support.Bounds.width < 4.19f || Mathf.Abs(support.SurfaceY-spawn.Position.y)>.01f ||
                        spawn.Position.x < support.Bounds.xMin+1.175f || spawn.Position.x > support.Bounds.xMax-1.175f)
                        ValidationErrors.Add("Crawler lacks its reserved patrol width or overhangs its support.");
                    if (spawn.NodeIndex >= 0 && spawn.NodeIndex < Route.Count &&
                        !CrawlerPatrolBayIsClear(Rooms[Route[spawn.NodeIndex].RoomIndex], supportIndex, support.Bounds))
                        ValidationErrors.Add("Crawler patrol bay overlaps solid architecture.");
                }
            }
            if (spawn.Kind == SpawnKind.Cannon)
            {
                float distance = Vector2.Distance(spawn.Position, Route[spawn.NodeIndex].StandingPosition(0.5f));
                if (spawn.Value < distance + 2f || distance > 9f)
                    ValidationErrors.Add("Cannon cannot threaten its compulsory route segment.");
                bool safeDeck = false;
                foreach (Platform deck in Platforms)
                    if (Mathf.Abs(deck.SurfaceY - (spawn.Position.y - .6f)) < .015f &&
                        deck.Bounds.width >= 4.59f && spawn.Position.x > deck.Bounds.xMin + .75f &&
                        spawn.Position.x < deck.Bounds.xMax - .75f) safeDeck = true;
                if (!safeDeck) ValidationErrors.Add("Lethal cannon body has no reserved safe landing deck.");
            }
            if (spawn.Kind == SpawnKind.JumpPad && spawn.Optional)
            {
                int step = FindRecoveryStep(spawn);
                if (step < 0 || !RecoveryFootprintIsClear(spawn, Platforms[step].Bounds))
                    ValidationErrors.Add("Recovery pad trigger/support/capsule approach buried in solid geometry: node " + spawn.NodeIndex);
            }
        }
    }

    private void ValidateCheckpointSpacing()
    {
        if (!Spawns.Exists(spawn => spawn.Kind == SpawnKind.Checkpoint && spawn.NodeIndex == 0))
            ValidationErrors.Add("Chapter has lost its starting checkpoint.");
        for (int first = 0; first < Spawns.Count; first++)
        {
            if (Spawns[first].Kind != SpawnKind.Checkpoint) continue;
            for (int second = first + 1; second < Spawns.Count; second++)
                if (Spawns[second].Kind == SpawnKind.Checkpoint &&
                    CheckpointsShareSafeApproach(Spawns[first], Spawns[second]))
                    ValidationErrors.Add("Redundant neighbouring checkpoints share the same safe approach.");
        }
        foreach (RouteNode node in Route)
        {
            if (!node.RequiredAbility.HasValue || node.Action == TraversalAction.Saber) continue;
            int trial = Route.IndexOf(node);
            if (AvailableAtChapterStart(node.RequiredAbility.Value) ||
                Route.FindIndex(candidate => candidate.RequiredAbility == node.RequiredAbility) != trial) continue;
            bool checkpointBefore = false;
            foreach (Spawn checkpoint in Spawns)
            {
                if (checkpoint.Kind != SpawnKind.Checkpoint || checkpoint.NodeIndex >= trial ||
                    Vector2.Distance(checkpoint.Position, Route[trial - 1].FeetPosition + Vector2.up * .6f) > 6f) continue;
                bool crossedGate = false;
                for (int i = checkpoint.NodeIndex + 1; i < trial; i++)
                    if (Route[i].RequiredAbility.HasValue) crossedGate = true;
                if (!crossedGate) checkpointBefore = true;
            }
            if (!checkpointBefore) ValidationErrors.Add("Skill trial has lost its local safe retry checkpoint: " + trial);
        }
    }

    private void ValidateSlideBarriers()
    {
        foreach (Region room in Rooms)
        {
            if (!room.IsLeaf || room.Strategy != StrategyKind.SaberSlide) continue;
            Spawn spikes = null;
            foreach (Spawn spawn in Spawns)
                if (spawn.Kind == SpawnKind.Spikes && spawn.NodeIndex >= room.FirstRouteNode &&
                    spawn.NodeIndex <= room.LastRouteNode && spawn.Size.x > 10f) spikes = spawn;
            if (spikes == null)
                foreach (Spawn spawn in Spawns)
                    if (spawn.Kind == SpawnKind.Spikes && spawn.NodeIndex >= room.FirstRouteNode &&
                        spawn.NodeIndex <= room.LastRouteNode && spawn.Size.x >= 5.9f) { spikes = spawn; break; }
            if (spikes == null) { ValidationErrors.Add("Slide tunnel has no continuous spike bed: " + room.Id); continue; }
            Rect bed = new Rect(spikes.Position - spikes.Size * .5f, spikes.Size);
            float floor = RoomBaseY(room) + 1f;
            float top = RoomCeilingY(room);
            bool sealedBase = false, sealedRoof = false;
            foreach (Platform platform in Platforms)
            {
                if (platform.RoomIndex != room.Id || platform.OneWay) continue;
                Rect slab = platform.Bounds;
                if (slab.xMin > bed.xMin + .015f || slab.xMax < bed.xMax - .015f) continue;
                if (slab.yMin <= floor + .015f && Mathf.Abs(slab.yMax - bed.yMin) < .015f) sealedBase = true;
                float clearance = slab.yMin - bed.yMax;
                if (clearance >= 1.1f && clearance <= 1.35f && slab.yMax >= top - .015f) sealedRoof = true;
            }
            float requiredBed = room.ScenarioId != null && (room.ScenarioId.Contains("slide-double") ||
                room.ScenarioId.Contains("slide-ring") || room.ScenarioId.Contains("slide-spring") ||
                room.ScenarioId.Contains("slide-refuges") || room.ScenarioId.Contains("slide-descending") || room.ScenarioId.Contains("slide-launch")) ? 6f : 15f;
            if (!sealedBase || !sealedRoof || bed.width < requiredBed - .015f)
                ValidationErrors.Add("Slide tunnel permits a below/above bypass or lacks standing clearance: " + room.Id);
            RouteNode slide = null;
            foreach (RouteNode node in Route)
                if (node.RoomIndex == room.Id && node.Action == TraversalAction.SaberSlide) slide = node;
            if (slide == null || slide.RequiredAbility != PirateUpgrade.Saber2 ||
                (slide.FeetPosition.x >= bed.xMin && slide.FeetPosition.x <= bed.xMax))
                ValidationErrors.Add("Slide tunnel has no physical exit beyond its spike bed: " + room.Id);
        }
    }

    private void ValidateChapterIdentity()
    {
        int additionalTrials = 0;
        foreach (RouteNode node in Route)
            if (Rooms[node.RoomIndex].Band == 5 && node.RequiredAbility.HasValue) additionalTrials++;
        if (!UsesChamberPlan && BandCount >= 7 && additionalTrials < 2)
            ValidationErrors.Add("Extended chapter is missing its two additional learned-ability trials.");
        int cannons = 0, plants = 0, snares = 0, crawlers = 0;
        foreach (Spawn spawn in Spawns)
        {
            if (spawn.Kind == SpawnKind.Cannon) cannons++;
            if (spawn.Kind == SpawnKind.Plant) plants++;
            if (spawn.Kind == SpawnKind.Snare) snares++;
            if (spawn.Kind == SpawnKind.Crawler) crawlers++;
        }
        if (Chapter == 1 && cannons < 8) ValidationErrors.Add("Arsenal is missing paired artillery positions.");
        if (Chapter == 2 && (plants < 8 || snares < 8)) ValidationErrors.Add("Gardens are missing their staggered living hazards.");
        if (Chapter >= 1 && (crawlers < 1 || crawlers > (Chapter == 1 ? 2 : 3)))
            ValidationErrors.Add("Late chapter needs real crawler patrol bays, never a silent empty placement.");
        if (Chapter != 3) return;
        int darkZones = 0;
        bool fullCoverage = false, earlyParrot = false;
        foreach (Spawn spawn in Spawns)
        {
            if (spawn.Kind == SpawnKind.Upgrade && spawn.Ability == PirateUpgrade.Parrot && spawn.NodeIndex <= 1)
                earlyParrot = true;
            if (spawn.Kind == SpawnKind.DarkZone)
            {
                darkZones++;
                Rect area = new Rect(spawn.Position - spawn.Size * .5f, spawn.Size);
                fullCoverage = area.xMin <= Bounds.xMin && area.xMax >= Bounds.xMax &&
                    area.yMin <= Bounds.yMin && area.yMax >= Bounds.yMax;
            }
        }
        if (darkZones != 1 || !fullCoverage || !earlyParrot)
            ValidationErrors.Add("Crown darkness must cover complete Bounds exactly once, with an early parrot pickup.");
    }

    public static string RunDeterministicSelfTest()
    {
        int[] seeds = { 20260918, 7, 42, 2026, 18092026, -13579, 0, int.MaxValue, int.MinValue, 918 };
        int totalRoutes = 0;
        int totalGates = 0;
        var signatures = new HashSet<string>();
        foreach (int seed in seeds)
        for (int chapter = 0; chapter < 4; chapter++)
        {
            float speed = new[] { 6f, 6.6f, 7.3f, 8f }[chapter];
            CampaignLayout first = Create(seed, chapter, 14f, 34.335f, 0.02f, speed);
            CampaignLayout repeated = Create(seed, chapter, 14f, 34.335f, 0.02f, speed);
            if (!first.GeometryValid)
                throw new InvalidOperationException($"Campaign geometry failed seed={seed}, chapter={chapter}: " + string.Join("; ", first.ValidationErrors));
            if (first.Signature() != repeated.Signature()) throw new InvalidOperationException("Same seed changed campaign geometry.");
            signatures.Add(first.Signature());
            totalRoutes += first.Route.Count;
            foreach (RouteNode node in first.Route) if (node.RequiredAbility.HasValue) totalGates++;
        }
        if (signatures.Count != seeds.Length * 4) throw new InvalidOperationException("Distinct chapter/seed outputs collapsed into one template.");
        CampaignLayout buriedRecovery = Create(42, 3, 14f, 34.335f, 0.02f);
        bool rejectedBuriedPad = false;
        foreach (Spawn pad in buriedRecovery.Spawns)
        {
            if (pad.Kind != SpawnKind.JumpPad || !pad.Optional) continue;
            Platform routeSupport = buriedRecovery.Platforms[buriedRecovery.Route[pad.NodeIndex].PlatformIndex];
            if (routeSupport.OneWay) continue;
            int stepIndex = buriedRecovery.FindRecoveryStep(pad);
            Rect step = buriedRecovery.Platforms[stepIndex].Bounds;
            pad.Position = new Vector2(routeSupport.CenterX, pad.Position.y);
            buriedRecovery.Platforms[stepIndex].Bounds = new Rect(pad.Position.x - step.width * 0.5f, step.y, step.width, step.height);
            rejectedBuriedPad = !buriedRecovery.Validate(out _) &&
                buriedRecovery.ValidationErrors.Exists(error => error.StartsWith("Recovery pad trigger/support/capsule approach buried"));
            break;
        }
        if (!rejectedBuriedPad) throw new InvalidOperationException("Buried recovery pad negative regression was not rejected.");
        CampaignLayout closedDoor = Create(20260918, 0, 14f, 34.335f, .02f, 6f);
        MacroStage firstStage = closedDoor.MacroStages[0];
        closedDoor.Solids.Add(new RectInt(Mathf.RoundToInt(firstStage.Exit.Coordinate), firstStage.BaseY + 2, 1, 6));
        bool rejectedClosedDoor = !closedDoor.Validate(out _) &&
            closedDoor.ValidationErrors.Exists(error => error.StartsWith("Room/shaft shell"));
        if (!rejectedClosedDoor) throw new InvalidOperationException("Closed room doorway negative regression was not rejected.");
        CampaignLayout unsealedSlide = Create(20260918, 2, 14f, 34.335f, .02f, 7.3f);
        foreach (Platform platform in unsealedSlide.Platforms)
            if (unsealedSlide.Rooms[platform.RoomIndex].Strategy == StrategyKind.SaberSlide &&
                !platform.OneWay && platform.Bounds.width > 15f &&
                Mathf.Abs(platform.Bounds.yMin - RoomFloorY(unsealedSlide.Rooms[platform.RoomIndex])) < .1f)
            { platform.OneWay = true; break; }
        bool rejectedSlideBypass = !unsealedSlide.Validate(out _) &&
            unsealedSlide.ValidationErrors.Exists(error => error.StartsWith("Slide tunnel permits"));
        if (!rejectedSlideBypass) throw new InvalidOperationException("One-way slide-foundation negative regression was not rejected.");
        CampaignLayout duplicateCheckpoint = Create(20260918, 0, 14f, 34.335f, .02f, 6f);
        Spawn existingCheckpoint = duplicateCheckpoint.Spawns.Find(spawn => spawn.Kind == SpawnKind.Checkpoint);
        duplicateCheckpoint.Spawns.Add(new Spawn { Kind = SpawnKind.Checkpoint, Position = existingCheckpoint.Position + Vector2.right * .1f,
            Size = existingCheckpoint.Size, NodeIndex = existingCheckpoint.NodeIndex });
        bool rejectedCheckpointDuplicate = !duplicateCheckpoint.Validate(out _) &&
            duplicateCheckpoint.ValidationErrors.Exists(error => error.StartsWith("Redundant neighbouring checkpoints"));
        if (!rejectedCheckpointDuplicate) throw new InvalidOperationException("Duplicate checkpoint negative regression was not rejected.");
        CampaignLayout shortWalk = Create(20260918, 3, 14f, 34.335f, .02f, 8f);
        int approachTrial = shortWalk.Route.FindIndex(node => node.Action == TraversalAction.Grapple);
        int approachHigh = approachTrial - 1;
        int approachLow = Mathf.Max(shortWalk.Rooms[shortWalk.Route[approachTrial].RoomIndex].FirstRouteNode, approachHigh - 3);
        int approachMiddle = approachLow + 2;
        var trialCheckpoint = new Spawn { Kind = SpawnKind.Checkpoint, NodeIndex = approachHigh,
            Position = shortWalk.Route[approachHigh].FeetPosition + new Vector2(1.4f, .6f), Size = new Vector2(.8f, 1.2f) };
        shortWalk.Spawns.Add(trialCheckpoint);
        Spawn entranceCheckpoint = shortWalk.Spawns.Find(spawn => spawn.Kind == SpawnKind.Checkpoint && spawn.NodeIndex == 0);
        var shaftCheckpoint = new Spawn { Kind = SpawnKind.Checkpoint, NodeIndex = approachLow,
            Position = shortWalk.Route[approachLow].FeetPosition + new Vector2(-1f, .6f), Size = new Vector2(.8f, 1.2f) };
        shortWalk.Spawns.Add(shaftCheckpoint);
        bool rejectedShortWalkDuplicate = !shortWalk.Validate(out _) &&
            shortWalk.ValidationErrors.Exists(error => error.StartsWith("Redundant neighbouring checkpoints"));
        if (!rejectedShortWalkDuplicate || trialCheckpoint == null)
            throw new InvalidOperationException("Short continuous checkpoint approach was not recognized.");
        shortWalk.RemoveRedundantCheckpoints();
        if (shortWalk.Spawns.Contains(shaftCheckpoint) || !shortWalk.Spawns.Contains(trialCheckpoint) ||
            !shortWalk.Spawns.Contains(entranceCheckpoint))
            throw new InvalidOperationException("Checkpoint dedup removed the trial retry or chapter entrance.");
        shortWalk.Spawns.Add(shaftCheckpoint);
        shortWalk.Route[approachMiddle].RequiredAbility = PirateUpgrade.Hook2;
        if (shortWalk.CheckpointsShareSafeApproach(shaftCheckpoint, trialCheckpoint))
            throw new InvalidOperationException("Checkpoint dedup crossed an intervening ability gate.");
        shortWalk.Route[approachMiddle].RequiredAbility = null;
        var approachEnemy = new Spawn { Kind = SpawnKind.Snare, NodeIndex = approachMiddle,
            Position = shortWalk.Route[approachMiddle].FeetPosition + Vector2.up * 1.9f, Size = new Vector2(.75f, 1.8f) };
        shortWalk.Spawns.Add(approachEnemy);
        if (shortWalk.CheckpointsShareSafeApproach(shaftCheckpoint, trialCheckpoint))
            throw new InvalidOperationException("Checkpoint dedup crossed an occupied approach.");
        shortWalk.Spawns.Remove(approachEnemy);
        Platform interruptedWalk = shortWalk.Platforms[shortWalk.Route[approachMiddle].PlatformIndex];
        Rect uninterruptedBounds = interruptedWalk.Bounds;
        interruptedWalk.Bounds = new Rect(interruptedWalk.CenterX - .25f, interruptedWalk.Bounds.y, .5f, interruptedWalk.Bounds.height);
        if (shortWalk.CheckpointsShareSafeApproach(shaftCheckpoint, trialCheckpoint))
            throw new InvalidOperationException("Checkpoint dedup crossed a non-walkable gap.");
        interruptedWalk.Bounds = uninterruptedBounds;
        shortWalk.Solids.Add(new RectInt(Mathf.FloorToInt(shortWalk.Route[approachMiddle].FeetPosition.x),
            Mathf.FloorToInt(shortWalk.Route[approachMiddle].FeetPosition.y), 1, 2));
        if (shortWalk.CheckpointsShareSafeApproach(shaftCheckpoint, trialCheckpoint))
            throw new InvalidOperationException("Checkpoint dedup crossed a solid obstruction.");
        CampaignLayout missingEntrance = Create(20260918, 0, 14f, 34.335f, .02f, 6f);
        missingEntrance.Spawns.RemoveAll(spawn => spawn.Kind == SpawnKind.Checkpoint && spawn.NodeIndex == 0);
        if (missingEntrance.Validate(out _) || !missingEntrance.ValidationErrors.Exists(error => error.StartsWith("Chapter has lost its starting checkpoint")))
            throw new InvalidOperationException("Missing chapter checkpoint negative regression was not rejected.");
        CampaignLayout uncoveredCrown = Create(20260918, 3, 14f, 34.335f, .02f, 8f);
        string fullCrownSignature = uncoveredCrown.Signature();
        Spawn crownVeil = uncoveredCrown.Spawns.Find(spawn => spawn.Kind == SpawnKind.DarkZone);
        crownVeil.Size -= Vector2.one;
        bool rejectedCrownGap = !uncoveredCrown.Validate(out _) &&
            uncoveredCrown.ValidationErrors.Exists(error => error.StartsWith("Crown darkness must cover"));
        if (!rejectedCrownGap || fullCrownSignature == uncoveredCrown.Signature())
            throw new InvalidOperationException("Crown dark-coverage/signature negative regression was not rejected.");
        CampaignLayout missingCrawler = Create(394, 2, 14f, 34.335f, .02f, 7.3f);
        missingCrawler.Spawns.RemoveAll(spawn => spawn.Kind == SpawnKind.Crawler);
        bool rejectedEmptyCrawlers = !missingCrawler.Validate(out _) &&
            missingCrawler.ValidationErrors.Exists(error => error.StartsWith("Late chapter needs"));
        if (!rejectedEmptyCrawlers) throw new InvalidOperationException("Empty Garden crawler negative regression was not rejected.");
        CampaignLayout narrowCrawler = Create(20260918, 2, 14f, 34.335f, .02f, 7.3f);
        Spawn crawler = narrowCrawler.Spawns.Find(spawn => spawn.Kind == SpawnKind.Crawler);
        Platform patrol = narrowCrawler.Platforms[(int)crawler.Value];
        patrol.Bounds = new Rect(patrol.CenterX - 1.3f, patrol.Bounds.y, 2.6f, patrol.Bounds.height);
        bool rejectedNarrowPatrol = !narrowCrawler.Validate(out _) &&
            narrowCrawler.ValidationErrors.Exists(error => error.StartsWith("Crawler lacks its reserved patrol"));
        if (!rejectedNarrowPatrol) throw new InvalidOperationException("Tiny crawler patrol negative regression was not rejected.");
        CampaignLayout staleRootWindow = Create(42, 1, 14f, 34.335f, .02f, 6.6f);
        staleRootWindow.Rooms[0].Exit = new Window(Side.Right, 73,
            BandHeight * (CampaignBandCount - 1) + 4f, BandHeight * (CampaignBandCount - 1) + 7f);
        bool staleRootRejected = !staleRootWindow.Validate(out _) &&
            staleRootWindow.ValidationErrors.Exists(error => error.StartsWith("Root exit-window metadata"));
        if (!staleRootRejected) throw new InvalidOperationException("Stale seven-band root window regression was not rejected.");
        string expedition = RunExpeditionSelfTests();
        string macro = RunMacroGraphSelfTests();
        return $"CAMPAIGN_GEOMETRY_SELFTEST_SUCCESS layouts={seeds.Length * 4}, moveSpeeds=6/6.6/7.3/8, stagesByChapter=7/16/12/12, roomsByChapter=14/16/12/12, routeNodes={totalRoutes}, abilityGates={totalGates}, uniqueSignatures={signatures.Count}, buriedRecoveryRejected=True, closedDoorRejected=True, slideUnderpassRejected=True, checkpointDuplicateRejected=True, checkpointShortWalkRejected=True, checkpointUnsafeMergeRejected=True, checkpointStartPreserved=True, crownCoverageRejected=True, emptyCrawlerRejected=True, narrowCrawlerRejected=True, rootWindowRejected=True, {macro}, {expedition}; geometryOnly=True";
    }
}
