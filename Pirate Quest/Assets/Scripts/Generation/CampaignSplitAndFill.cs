using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CampaignLayout
{
    private const int BandHeight = 16;
    private const int CampaignBandCount = 7;
    private const float SlabHeight = 0.18f;

    private void Generate()
    {
        string[] titles = { "Dock", "Arsenal", "Hanging Gardens", "The Crown" };
        ChapterTitle = titles[Chapter];
        PlanMacroGraph();
        var root = AddRegion(new RectInt(1, 1, Bounds.width - 2, Bounds.height - 2), -1, 0, 0,
            MacroStages[0].Entrance, MacroStages[MacroStages.Count - 1].Exit);
        BuildMacroGraph(root);
        PlaceRecoveryPadsInFreeFloorSpace();
        Start = Route[0].FeetPosition;
        Exit = Route[Route.Count - 1].FeetPosition;
        AddSpawn(SpawnKind.Exit, Exit + Vector2.up * 1.1f, new Vector2(1.4f, 2.2f), Route.Count - 1,
            text: Chapter == 3 ? "Deliver the letter" : "Next chapter");
        AddSpawn(SpawnKind.Checkpoint, Start + Vector2.right * 0.4f + Vector2.up * 0.6f,
            new Vector2(0.8f, 1.2f), 0);
        RemoveRedundantCheckpoints();
        SelectExpeditionCheckpoints();
        PlaceBrineCrawlers();
        AddSpawn(SpawnKind.Hint, Start + Vector2.up * 2f, Vector2.one, 0,
            text: Chapter == 0 ? "A/D: move. Hold Space: high jump. Shift: dash. Find the way up." : ChapterTitle);
        if (Chapter == 3)
        {
            Spawns.RemoveAll(spawn => spawn.Kind == SpawnKind.DarkZone);
            AddSpawn(SpawnKind.DarkZone, Bounds.center, new Vector2(Bounds.width, Bounds.height), 0,
                text: "The Crown is dark. Send your parrot ahead to scout.");
        }
    }

    private void PlaceBrineCrawlers()
    {
        if (Chapter < 1) return;
        int placed = 0;
        foreach (Region room in Rooms)
        {
            if (!room.IsLeaf || room.IsShaft || room.Band < 1 ||
                (room.Strategy != StrategyKind.Grid && room.Strategy != StrategyKind.Pyramid &&
                    room.Strategy != StrategyKind.DarkGallery)) continue;
            for (int nodeIndex = room.FirstRouteNode + 3; nodeIndex < room.LastRouteNode - 2; nodeIndex++)
            {
                RouteNode node = Route[nodeIndex];
                Platform support = Platforms[node.PlatformIndex];
                if (node.RequiredAbility.HasValue || support.Optional || support.Bounds.width < 2.55f) continue;
                Vector2 position = new Vector2(support.CenterX, support.SurfaceY);
                bool occupied = false;
                foreach (Spawn existing in Spawns)
                {
                    if (existing.Kind == SpawnKind.Hint || existing.Kind == SpawnKind.Decoration || existing.Kind == SpawnKind.DarkZone) continue;
                    if (Mathf.Abs(existing.Position.y-position.y) < 2.5f &&
                        Mathf.Abs(existing.Position.x-position.x) < 3.7f + existing.Size.x * .5f)
                    { occupied = true; break; }
                }
                if (occupied) continue;
                Rect patrol = support.Bounds;
                if (patrol.width < 4.2f)
                    patrol = new Rect(support.CenterX - 2.1f, patrol.yMin, 4.2f, patrol.height);
                if (!CrawlerPatrolBayIsClear(room, node.PlatformIndex, patrol)) continue;
                support.Bounds = patrol;
                AddSpawn(SpawnKind.Crawler, position, new Vector2(2.35f,1.4f), nodeIndex,
                    value: node.PlatformIndex, text: "Brine crawler: dodge its claws. Slash or jump over it.");
                placed++;
                break;
            }
            if (placed >= (Chapter == 1 ? 2 : 3)) break;
        }
    }

    private bool CrawlerPatrolBayIsClear(Region room, int ownSupport, Rect patrol)
    {
        if (patrol.xMin < room.Bounds.xMin + .6f || patrol.xMax > room.Bounds.xMax - .6f) return false;
        Rect clearance = new Rect(patrol.xMin, patrol.yMax + .015f, patrol.width, 1.5f);
        foreach (RectInt wall in Solids)
            if (clearance.Overlaps(new Rect(wall.x, wall.y, wall.width, wall.height))) return false;
        for (int i = 0; i < Platforms.Count; i++)
            if (i != ownSupport && !Platforms[i].OneWay && clearance.Overlaps(Platforms[i].Bounds)) return false;
        return true;
    }

    private sealed class SplitCandidate
    {
        public RectInt First;
        public RectInt Second;
        public float JoinSurface;
        public StrategyKind FirstStrategy;
        public StrategyKind SecondStrategy;
        public int SplitX;
    }

    private void FillRegion(Region region, int direction, int band, int traversalOrdinal)
    {
        StrategyKind fallback = ChooseFallback(region, band, traversalOrdinal);
        region.Strategy = fallback;
        var candidates = new List<SplitCandidate>();
        if (region.Bounds.width >= 52)
        {
            int mid = Mathf.RoundToInt(region.Bounds.center.x);
            for (int attempt = 0; attempt < 7; attempt++)
            {
                int cut = Mathf.Clamp(mid + random.Next(-5, 6), region.Bounds.xMin + 25, region.Bounds.xMax - 26);
                int joinY = region.Bounds.yMin + random.Next(3, 6);
                RectInt left = new RectInt(region.Bounds.xMin, region.Bounds.yMin, cut - region.Bounds.xMin, region.Bounds.height);
                RectInt right = new RectInt(cut + 1, region.Bounds.yMin, region.Bounds.xMax - cut - 1, region.Bounds.height);
                RectInt first = direction > 0 ? left : right;
                RectInt second = direction > 0 ? right : left;
                StrategyKind firstKind = StrategyForLeaf(band, 0);
                StrategyKind secondKind = StrategyForLeaf(band, 1);
                Vector2 sharedEntry = new Vector2(direction > 0 ? cut + 3.5f : cut - 2.5f, joinY);
                Window sharedWindow = new Window(direction > 0 ? Side.Right : Side.Left, cut, joinY, joinY + 3f);
                CompatibilityChecks += 2;
                if (!TryFill(firstKind, first, region.Entrance, sharedWindow) ||
                    !TryFill(secondKind, second, sharedEntry, region.Exit)) continue;
                candidates.Add(new SplitCandidate
                {
                    First = first, Second = second, JoinSurface = joinY,
                    FirstStrategy = firstKind, SecondStrategy = secondKind, SplitX = cut
                });
            }
        }

        if (candidates.Count > 0)
        {
            SplitCandidate chosen = candidates[random.Next(candidates.Count)];
            AcceptedSplits++;
            Window join = new Window(direction > 0 ? Side.Right : Side.Left,
                chosen.SplitX, chosen.JoinSurface, chosen.JoinSurface + 3f);
            Region first = AddRegion(chosen.First, region.Id, region.Depth + 1, band, region.Entrance, join);
            first.Strategy = chosen.FirstStrategy;
            FillLeaf(first, direction);

            int floor = region.Bounds.yMin;
            int ceiling = region.Bounds.yMax;
            int sill = Mathf.RoundToInt(chosen.JoinSurface);
            if (sill > floor) Solids.Add(new RectInt(chosen.SplitX, floor, 1, sill - floor));
            if (ceiling > sill + 3) Solids.Add(new RectInt(chosen.SplitX, sill + 3, 1, ceiling - sill - 3));
            ConnectOrdinary(region, new Vector2(chosen.SplitX + 0.5f, chosen.JoinSurface), 3f);

            Vector2 secondEntry = new Vector2(direction > 0 ? chosen.Second.xMin + 2.5f : chosen.Second.xMax - 2.5f,
                chosen.JoinSurface);
            Region second = AddRegion(chosen.Second, region.Id, region.Depth + 1, band, secondEntry, region.Exit);
            second.Strategy = chosen.SecondStrategy;
            FillLeaf(second, direction);
            return;
        }

        FallbackFills++;
        if (!TryFill(fallback, region.Bounds, region.Entrance, region.Exit))
            throw new InvalidOperationException("No compatible fallback strategy for campaign region.");
        FillLeaf(region, direction);
    }

    private StrategyKind ChooseFallback(Region region, int band, int ordinal)
    {
        return StrategyKind.Pyramid;
    }

    private StrategyKind StrategyForLeaf(int band, int ordinal)
    {
        if (band == 5)
        {
            if (Chapter == 0) return ordinal == 0 ? StrategyKind.ChainGap : StrategyKind.SaberPassage;
            if (Chapter == 1) return ordinal == 0 ? StrategyKind.GrappleRise : StrategyKind.SpringRise;
            if (Chapter == 2) return ordinal == 0 ? StrategyKind.DoubleJumpRise : StrategyKind.SaberSlide;
            return ordinal == 0 ? StrategyKind.GrappleRise : StrategyKind.SaberSlide;
        }
        if (band == 6)
            return ordinal == 1 ? StrategyKind.JumpPad : Chapter == 1 ? StrategyKind.Pyramid : StrategyKind.Grid;
        if (band == 3)
        {
            if (Chapter == 0) return ordinal == 0 ? StrategyKind.ChainGap : StrategyKind.SaberPassage;
            if (Chapter == 1) return ordinal == 0 ? StrategyKind.SpringRise : StrategyKind.GrappleRise;
            if (Chapter == 2) return ordinal == 0 ? StrategyKind.SaberSlide : StrategyKind.DoubleJumpRise;
            return ordinal == 0 ? StrategyKind.SaberSlide : StrategyKind.GrappleRise;
        }
        if (band == 4)
            return ordinal == 1 ? StrategyKind.JumpPad : Chapter % 2 == 0 ? StrategyKind.Grid : StrategyKind.Pyramid;
        if (Chapter == 0)
        {
            if (band == 1) return ordinal == 0 ? StrategyKind.SaberPassage : StrategyKind.ChainGap;
            if (band == 2 && ordinal == 0) return StrategyKind.JumpPad;
        }
        else if (Chapter == 1)
        {
            if (band == 0 && ordinal == 1) return StrategyKind.GrappleRise;
            if (band == 1 && ordinal == 1) return StrategyKind.SpringRise;
            if (band == 2 && ordinal == 0) return StrategyKind.JumpPad;
        }
        else if (Chapter == 2)
        {
            if (band == 0 && ordinal == 1) return StrategyKind.DoubleJumpRise;
            if (band == 1 && ordinal == 0) return StrategyKind.SaberSlide;
            if (band == 2 && ordinal == 0) return StrategyKind.JumpPad;
        }
        else
        {
            if (band == 0 && ordinal == 0) return StrategyKind.DarkGallery;
            if (band == 1 && ordinal == 0) return StrategyKind.GrappleRise;
            if (band == 1 && ordinal == 1) return StrategyKind.SaberSlide;
            if (band == 2 && ordinal == 0) return StrategyKind.JumpPad;
        }
        return (band * 2 + ordinal + Chapter) % 2 == 0 ? StrategyKind.Pyramid : StrategyKind.Grid;
    }

    private bool TryFill(StrategyKind strategy, RectInt region, Vector2 entrance, Window exit)
    {
        if (region.width < 24 || region.height < 13 || exit.Maximum - exit.Minimum < 2.5f)
            return false;
        float floor = region.yMin;
        if (entrance.y < floor + 2f || entrance.y > floor + 6f || exit.Minimum < floor + 2f || exit.Minimum > floor + 6f)
            return false;
        if (strategy == StrategyKind.ChainGap && region.width < 25) return false;
        if (strategy == StrategyKind.SaberSlide && region.width < 25) return false;
        return true;
    }

    private void FillLeaf(Region room, int direction)
    {
        room.IsLeaf = true;
        room.FirstRouteNode = Route.Count;
        room.ScenarioId = ScenarioFor(room);
        float endX = direction > 0 ? room.Bounds.xMax - 2.5f : room.Bounds.xMin + 2.5f;
        Vector2 end = new Vector2(endX, room.Exit.Minimum);
        if (Route.Count > 0 && Vector2.Distance(Route[Route.Count - 1].FeetPosition, room.Entrance) > .1f)
            ConnectOrdinary(room, room.Entrance, 4.8f);
        else AddRoutePlatform(room.Entrance, 4.8f, room.Id, TraversalAction.Walk);

        bool custom = FillNamedScenario(room, direction, end);
        if (!custom) switch (room.Strategy)
        {
            case StrategyKind.ChainGap: FillChainGap(room, direction, end); break;
            case StrategyKind.GrappleRise: FillHighGate(room, direction, end, PirateUpgrade.Hook2, TraversalAction.Grapple, 4f); break;
            case StrategyKind.SpringRise: FillSpringGate(room, direction, end); break;
            case StrategyKind.DoubleJumpRise: FillHighGate(room, direction, end, PirateUpgrade.DoubleJump, TraversalAction.DoubleJump, 4f); break;
            case StrategyKind.SaberPassage: FillSaberGate(room, direction, end); break;
            case StrategyKind.SaberSlide: FillSlideGate(room, direction, end); break;
            case StrategyKind.JumpPad: FillJumpPad(room, direction, end); break;
            case StrategyKind.DarkGallery:
                AddUpgrade(PirateUpgrade.Parrot, room.Entrance + new Vector2(direction, 0.8f));
                FillOrdinary(room, direction, end, false);
                AddSpawn(SpawnKind.DarkZone, room.Bounds.center, new Vector2(room.Bounds.width - 1f, room.Bounds.height - 1f), room.FirstRouteNode,
                    text: "Q: scout ahead with your parrot.");
                break;
            default: FillOrdinary(room, direction, end, room.Strategy == StrategyKind.Pyramid); break;
        }

        room.LastRouteNode = Route.Count - 1;
        RegisterScenario(room);
        PopulateLeaf(room, direction);
        AddOrdinaryRoomCeiling(room);
        AddRecoveryPad(room, room.Entrance.x, room.FirstRouteNode);
        AddRecoveryPad(room, end.x, room.LastRouteNode);
    }

    private void AddOrdinaryRoomCeiling(Region room)
    {
        if (room.Strategy != StrategyKind.Pyramid && room.Strategy != StrategyKind.Grid &&
            room.Strategy != StrategyKind.DarkGallery) return;
        int bandTop = RoomCeilingY(room);
        float fullJumpClearance = SafeJumpHeight / 0.78f + 1.2f;
        float reach = moveSpeed * (2f * jumpSpeed / gravity) + 0.6f;
        int runStart = room.Bounds.xMin;
        int runBottom = -1;
        for (int x = room.Bounds.xMin; x <= room.Bounds.xMax; x++)
        {
            int bottom = bandTop;
            if (x < room.Bounds.xMax)
            {
                float clearance = RoomBaseY(room) + 9f;
                foreach (Platform platform in Platforms)
                    if (platform.RoomIndex == room.Id && platform.Bounds.xMax >= x - reach &&
                        platform.Bounds.xMin <= x + 1f + reach)
                        clearance = Mathf.Max(clearance, platform.SurfaceY + fullJumpClearance);
                foreach (Spawn spawn in Spawns)
                    if (spawn.NodeIndex >= room.FirstRouteNode && spawn.NodeIndex <= room.LastRouteNode &&
                        spawn.Kind != SpawnKind.DarkZone && spawn.Kind != SpawnKind.Hint &&
                        spawn.Kind != SpawnKind.Decoration &&
                        spawn.Position.x + spawn.Size.x * .5f >= x - 1f &&
                        spawn.Position.x - spawn.Size.x * .5f <= x + 2f)
                        clearance = Mathf.Max(clearance, spawn.Position.y + spawn.Size.y * .5f + .6f);
                bottom = Mathf.Min(bandTop, Mathf.CeilToInt(clearance));
            }
            if (bottom == runBottom) continue;
            if (runBottom >= 0 && runBottom < bandTop && x > runStart)
            {
                Solids.Add(new RectInt(runStart, runBottom, x - runStart, bandTop - runBottom));
                room.HasOverheadStructure = true;
            }
            runStart = x;
            runBottom = bottom;
        }
    }

    private void FillOrdinary(Region room, int direction, Vector2 end, bool pyramid)
    {
        Vector2 start = Route[Route.Count - 1].FeetPosition;
        int segments = Mathf.CeilToInt(Mathf.Abs(end.x - start.x) / OrdinaryStride);
        float previousY = start.y;
        int baseY = RoomBaseY(room);
        for (int i = 1; i <= segments; i++)
        {
            float fraction = i / (float)segments;
            float x = Mathf.Lerp(start.x, end.x, fraction);
            float linearY = Mathf.Lerp(start.y, end.y, fraction);
            float profileHeight = pyramid ? (Chapter == 1 ? 2f : Chapter == 3 ? 3.5f : 3f) :
                Chapter == 1 ? 1.25f : Chapter == 2 ? 3f : Chapter == 3 ? 2.75f : 2f;
            float arch = Mathf.Sin(fraction * Mathf.PI) * profileHeight;
            float jitter = pyramid || i == segments ? 0f : random.Next(-1, 2) * 0.5f;
            float y = i == segments ? end.y : Mathf.Round((linearY + arch + jitter) * 2f) * 0.5f;
            y = Mathf.Clamp(y, previousY - 2f, previousY + 2f);
            y = Mathf.Clamp(y, end.y - (segments - i) * 2f, end.y + (segments - i) * 2f);
            float width = pyramid ? 3.8f : Chapter == 1 ? random.Next(36, 42) * 0.1f :
                Chapter == 2 ? random.Next(28, 34) * 0.1f : random.Next(30, 39) * 0.1f;
            int index = AddRoutePlatform(new Vector2(x, y), width, room.Id, TraversalAction.Jump);
            if (pyramid)
            {
                Platform step = Platforms[Route[index].PlatformIndex];
                step.OneWay = false;
                step.Bounds = new Rect(step.Bounds.x, baseY + 1f, step.Bounds.width, y - baseY - 1f);
            }
            previousY = y;
        }
        if (!pyramid) AddBranch(room, direction);
    }

    private void FillChainGap(Region room, int direction, Vector2 end)
    {
        float centre = room.Bounds.center.x;
        float startY = room.Entrance.y;
        float left = centre - 6.5f;
        float right = centre + 6.5f;
        float takeoffX = direction > 0 ? left - 2f : right + 2f;
        float landingX = direction > 0 ? right + 2f : left - 2f;
        float chainX = centre - direction * 2.6f;
        AddUpgrade(PirateUpgrade.Hook1, room.Entrance + new Vector2(direction, 0.8f));
        ConnectOrdinary(room, new Vector2(takeoffX, startY), 4f);
        AddSpawn(SpawnKind.Checkpoint, new Vector2(takeoffX - direction * 1.6f, startY + 0.6f), new Vector2(0.8f, 1.2f), Route.Count - 1);
        AddSpawn(SpawnKind.Chain, new Vector2(chainX, RoomBaseY(room) + 13f), new Vector2(0.5f, 9f), Route.Count - 1,
            value: 9f, text: "Jump near the chain. Hold RMB to grab. A/D: swing. Space: leap.");
        AddSpawn(SpawnKind.Spikes, new Vector2(centre, RoomBaseY(room) + 1.2f), new Vector2(13f, 0.4f), Route.Count - 1);
        AddRoutePlatform(new Vector2(landingX, end.y), 4f, room.Id, TraversalAction.Chain,
            PirateUpgrade.Hook1, "13-метровый провал: обязательная раскачка на свисающей цепи");
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillHighGate(Region room, int direction, Vector2 end, PirateUpgrade ability, TraversalAction action, float rise)
    {
        float centre = room.Bounds.center.x;
        float low = room.Entrance.y;
        Vector2 takeoff = new Vector2(centre - direction * 3.4f, low);
        Vector2 landing = new Vector2(centre + direction * 1.4f, low + rise);
        AddUpgrade(ability, room.Entrance + new Vector2(direction, 0.8f));
        ConnectOrdinary(room, takeoff, 4f);
        AddSpawn(SpawnKind.Checkpoint, takeoff + new Vector2(-direction * 1.4f, 0.6f), new Vector2(0.8f, 1.2f), Route.Count - 1);
        if (ability == PirateUpgrade.Hook2)
            AddSpawn(SpawnKind.Anchor, new Vector2(centre + direction * (rise > 4.1f ? -1.5f : 1.5f), low + 8f), new Vector2(0.9f, 0.9f), Route.Count - 1,
                text: "Jump, hold RMB near a ring, then press Space to leap over the wall.");
        else
            AddSpawn(SpawnKind.Hint, takeoff + Vector2.up * 2f, Vector2.one, Route.Count - 1,
                text: "Press Space again near the top of your jump.");
        int node = AddRoutePlatform(landing, rise > 4.1f ? 2.6f : 4f, room.Id, action, ability,
            "Сплошная высокая стена: " + rise.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " м над стартом");
        Platform wall = Platforms[Route[node].PlatformIndex];
        wall.OneWay = false;
        wall.Bounds = new Rect(wall.Bounds.x, RoomBaseY(room) + 1f, wall.Bounds.width, landing.y - RoomBaseY(room) - 1f);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSpringGate(Region room, int direction, Vector2 end)
    {
        float centre = room.Bounds.center.x;
        float surface = room.Entrance.y;
        AddUpgrade(PirateUpgrade.SpringLeg, room.Entrance + new Vector2(direction, 0.8f));
        Vector2 takeoff = new Vector2(centre - direction * 4f, surface);
        ConnectOrdinary(room, takeoff, 3.4f);
        AddSpawn(SpawnKind.Checkpoint, takeoff + new Vector2(-direction, 0.6f), new Vector2(0.8f, 1.2f), Route.Count - 1);
        Vector2 spikeCentre = new Vector2(centre - direction * 0.6f, surface - 0.15f);
        AddPlatform(new Vector2(spikeCentre.x, surface - 0.3f), 3f, room.Id, true, false);
        AddSpawn(SpawnKind.Spikes, spikeCentre, new Vector2(3f, 0.3f), Route.Count - 1,
            text: "Drop onto spikes to bounce once. Land on safe ground to recharge.");
        Vector2 landing = new Vector2(centre + direction * 3.6f, surface + 4f);
        int node = AddRoutePlatform(landing, 3.8f, room.Id, TraversalAction.Spring,
            PirateUpgrade.SpringLeg, "Отскок от шипов на четырёхметровый уступ");
        Platform wall = Platforms[Route[node].PlatformIndex];
        wall.OneWay = false;
        wall.Bounds = new Rect(wall.Bounds.x, RoomBaseY(room) + 1f, wall.Bounds.width, landing.y - RoomBaseY(room) - 1f);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSaberGate(Region room, int direction, Vector2 end)
    {
        AddUpgrade(PirateUpgrade.Saber1, room.Entrance + new Vector2(direction, 0.8f));
        float centre = room.Bounds.center.x;
        Vector2 before = new Vector2(centre - direction * 2f, room.Entrance.y);
        ConnectOrdinary(room, before, 4f);
        AddSpawn(SpawnKind.RopeGate, new Vector2(centre, RoomBaseY(room) + 8.5f),
            new Vector2(0.65f, 15f), Route.Count - 1, text: "LMB: slash. Cut the rope to open the way.");
        AddRoutePlatform(new Vector2(centre + direction * 2f, before.y), 4f, room.Id, TraversalAction.Saber,
            PirateUpgrade.Saber1, "Сабля разрезает физический канат поперёк прохода");
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSlideGate(Region room, int direction, Vector2 end)
    {
        AddUpgrade(PirateUpgrade.Saber2, room.Entrance + new Vector2(direction, 0.8f));
        float centre = room.Bounds.center.x;
        float surface = room.Entrance.y;
        float left = centre - 7.75f;
        float right = centre + 7.75f;
        Vector2 before = new Vector2(direction > 0 ? left - 1.3f : right + 1.3f, surface);
        ConnectOrdinary(room, before, 3f);
        AddSpawn(SpawnKind.Checkpoint, before + new Vector2(-direction, 0.6f), new Vector2(0.8f, 1.2f), Route.Count - 1);
        float floor = RoomBaseY(room) + 1f;
        Platforms.Add(new Platform
        {
            Bounds = new Rect(left, floor, right - left, surface - .3f - floor),
            OneWay = false, RoomIndex = room.Id, Optional = false
        });
        AddSpawn(SpawnKind.Spikes, new Vector2(centre, surface - 0.15f), new Vector2(15.5f, 0.3f), Route.Count - 1,
            text: "Hold S + A/D to slide across spikes and gain speed.");
        float ceilingBottom = surface + 1.3f;
        Platforms.Add(new Platform
        {
            Bounds = new Rect(left, ceilingBottom, right - left, RoomCeilingY(room) - ceilingBottom),
            OneWay = false, RoomIndex = room.Id, Optional = true
        });
        AddRoutePlatform(new Vector2(direction > 0 ? right + 1.3f : left - 1.3f, surface), 3f,
            room.Id, TraversalAction.SaberSlide, PirateUpgrade.Saber2,
            "Низкий коридор 1,3 м над шипами — прыжок не заменяет скольжение");
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillJumpPad(Region room, int direction, Vector2 end)
    {
        Vector2 start = Route[Route.Count - 1].FeetPosition;
        Vector2 pad = start + new Vector2(direction * 3f, 0f);
        ConnectOrdinary(room, pad, 4f);
        AddSpawn(SpawnKind.JumpPad, pad + Vector2.up * 0.15f, new Vector2(1.4f, 0.3f), Route.Count - 1,
            value: 18f, text: "The spring pad launches you up. Steer toward the ledge.");
        AddRoutePlatform(pad + new Vector2(direction * 3.4f, 3.5f), 3.8f,
            room.Id, TraversalAction.JumpPad, null, "JumpPadStrategy: трамплин под выходным балконом");
        ConnectOrdinary(room, end, 4.8f);
        AddBranch(room, direction);
    }

    private void ConnectOrdinary(Region room, Vector2 target, float lastWidth)
    {
        Vector2 from = Route[Route.Count - 1].FeetPosition;
        Platform source = Platforms[Route[Route.Count - 1].PlatformIndex];
        if (!source.OneWay && target.y < from.y - .05f && Mathf.Abs(target.x - from.x) > .1f)
        {
            float direction = Mathf.Sign(target.x - from.x);
            float edge = direction > 0 ? source.Bounds.xMax + .36f : source.Bounds.xMin - .36f;
            if ((target.x - edge) * direction > .1f)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(edge - from.x) / OrdinaryStride));
                for (int i = 1; i <= steps; i++)
                    AddRoutePlatform(new Vector2(Mathf.Lerp(from.x, edge, i / (float)steps), from.y),
                        3.4f, room.Id, TraversalAction.Walk);
                from = Route[Route.Count - 1].FeetPosition;
            }
        }
        float distance = Vector2.Distance(from, target);
        if (distance < 0.1f) return;
        int count = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(target.x - from.x) / OrdinaryStride),
            Mathf.CeilToInt(Mathf.Abs(target.y - from.y) / 2f));
        for (int i = 1; i <= count; i++)
        {
            Vector2 position = Vector2.Lerp(from, target, i / (float)count);
            AddRoutePlatform(position, i == count ? lastWidth : 3.4f, room.Id,
                Mathf.Abs(target.y - from.y) < 0.05f ? TraversalAction.Walk : TraversalAction.Jump);
        }
    }

    private void AddBranch(Region room, int direction)
    {
        if (Route.Count - room.FirstRouteNode < 4) return;
        int sourceIndex = room.FirstRouteNode;
        for (int i = room.FirstRouteNode; i < Route.Count; i++)
            if (Route[i].FeetPosition.y > Route[sourceIndex].FeetPosition.y) sourceIndex = i;
        RouteNode source = Route[sourceIndex];
        Vector2 branch = source.FeetPosition + new Vector2(-direction * Mathf.Min(2.3f, OrdinaryStride), 2f);
        branch.x = Mathf.Clamp(branch.x, room.Bounds.xMin + 2f, room.Bounds.xMax - 2f);
        if (branch.y + 2f >= room.Bounds.yMax) return;
        AddPlatform(branch, random.Next(25, 29) * 0.1f, room.Id, true, true);
        Vector2 second = branch + new Vector2(direction * Mathf.Min(2.8f, OrdinaryStride), 1.5f);
        if (second.y + 2f < room.Bounds.yMax && second.x > room.Bounds.xMin + 2f && second.x < room.Bounds.xMax - 2f)
            AddPlatform(second, 2.8f, room.Id, true, true);
        room.HasOptionalBranch = true;
        AddSpawn(SpawnKind.Decoration, branch + Vector2.up * 0.65f, new Vector2(1.1f, 1.1f), sourceIndex,
            optional: true, text: "Side balcony");
    }

    private void AddRecoveryPad(Region room, float x, int node)
    {
        float floor = RoomBaseY(room) + 1f;
        Vector2 step = new Vector2(x, floor + 1f);
        AddPlatform(step, 2.4f, room.Id, true, true);
        AddSpawn(SpawnKind.JumpPad, step + Vector2.up * 0.15f, new Vector2(1.4f, 0.3f), node,
            value: 18f, optional: true, text: "Missed a jump? This spring pad returns you to the ledge.");
    }

    private void PlaceRecoveryPadsInFreeFloorSpace()
    {
        foreach (Spawn pad in Spawns)
        {
            if (pad.Kind != SpawnKind.JumpPad || !pad.Optional) continue;
            int stepIndex = FindRecoveryStep(pad);
            if (stepIndex < 0) throw new InvalidOperationException("Recovery pad lost its one-metre support.");
            Platform step = Platforms[stepIndex];
            if (RecoveryFootprintIsClear(pad, step.Bounds)) continue;
            Region room = Rooms[Route[pad.NodeIndex].RoomIndex];
            int direction = room.Exit.Side == Side.Right ? 1 : -1;
            int outward = pad.NodeIndex == room.FirstRouteNode ? -direction : direction;
            Vector2 original = pad.Position;
            bool placed = false;
            for (int variant = 0; variant < 2 && !placed; variant++)
            {
                float stepWidth = variant == 0 ? 2.4f : 0.5f;
                float padWidth = variant == 0 ? 1.4f : 0.42f;
                for (int sample = 0; sample <= 40; sample++)
                {
                    float x = original.x + outward * sample * 0.1f;
                    MacroStage stage = MacroStages[room.Band];
                    bool externalExit = outward > 0 ? room.Bounds.xMax == stage.Bounds.xMax : room.Bounds.xMin == stage.Bounds.xMin;
                    if (!externalExit && (x < room.Bounds.xMin + 0.29f || x > room.Bounds.xMax - 0.29f)) continue;
                    pad.Position = new Vector2(x, original.y);
                    pad.Size = new Vector2(padWidth, pad.Size.y);
                    Rect candidate = new Rect(x - stepWidth * 0.5f, step.Bounds.y, stepWidth, step.Bounds.height);
                    if (!RecoveryFootprintIsClear(pad, candidate)) continue;
                    step.Bounds = candidate;
                    placed = true;
                    break;
                }
            }
            if (!placed) throw new InvalidOperationException("No free local floor for recovery pad at route node " + pad.NodeIndex);
        }
    }

    private int FindRecoveryStep(Spawn pad)
    {
        int room = Route[pad.NodeIndex].RoomIndex;
        for (int i = 0; i < Platforms.Count; i++)
        {
            Platform platform = Platforms[i];
            if (platform.RoomIndex == room && platform.Optional && platform.OneWay &&
                Mathf.Abs(platform.CenterX - pad.Position.x) < 0.015f &&
                Mathf.Abs(platform.SurfaceY - (pad.Position.y - 0.15f)) < 0.015f) return i;
        }
        return -1;
    }

    private bool RecoveryFootprintIsClear(Spawn pad, Rect step)
    {
        float floor = RoomFloorY(Rooms[Route[pad.NodeIndex].RoomIndex]);
        Rect trigger = new Rect(pad.Position - pad.Size * 0.5f, pad.Size);
        Rect approach = new Rect(pad.Position.x - 0.29f, floor + 0.02f, 0.58f,
            trigger.yMax + 1f - floor - 0.02f);
        if (step.xMin < Bounds.xMin + 1.01f || step.xMax > Bounds.xMax - 1.01f ||
            approach.xMin < Bounds.xMin + 1.01f || approach.xMax > Bounds.xMax - 1.01f) return false;
        bool supportedFloor = false;
        foreach (RectInt solid in Solids)
        {
            Rect bounds = new Rect(solid.x, solid.y, solid.width, solid.height);
            if (Mathf.Abs(bounds.yMax - floor) < 0.015f && bounds.xMin <= approach.xMin && bounds.xMax >= approach.xMax)
                supportedFloor = true;
            if (trigger.Overlaps(bounds) || step.Overlaps(bounds) || approach.Overlaps(bounds)) return false;
        }
        foreach (Platform platform in Platforms)
            if (!platform.OneWay && (trigger.Overlaps(platform.Bounds) || step.Overlaps(platform.Bounds) || approach.Overlaps(platform.Bounds)))
                return false;
        return supportedFloor;
    }

    private void PopulateLeaf(Region room, int direction)
    {
        if (room.LastRouteNode <= room.FirstRouteNode + 2) return;
        bool safeOrdinary = room.Strategy == StrategyKind.Pyramid || room.Strategy == StrategyKind.Grid || room.Strategy == StrategyKind.DarkGallery;
        if (safeOrdinary)
        {
            int middle = room.FirstRouteNode + Mathf.Max(2, (room.LastRouteNode - room.FirstRouteNode) / 2);
            RouteNode target = Route[middle];
            int station = Mathf.Min(room.LastRouteNode - 1, middle + 2);
            ReserveCannonStation(room, station, middle, direction,
                out RouteNode cannonSupport, out float cannonX);
            if (Vector2.Distance(new Vector2(cannonX, target.FeetPosition.y), Route[0].FeetPosition) > 9f)
            {
                AddSpawn(SpawnKind.Cannon, new Vector2(cannonX, cannonSupport.FeetPosition.y + 0.6f), new Vector2(1.5f, 1.2f), middle,
                    facing: -direction, value: 16f, text: "Watch the muzzle flash. Take cover or jump over the cannonball.");
            }
            if (Chapter == 1)
            {
                int rearIndex = room.FirstRouteNode + 1;
                int rearTarget = Mathf.Min(room.LastRouteNode - 1, rearIndex + 2);
                ReserveCannonStation(room, rearIndex, rearTarget, -direction, out RouteNode rear, out float rearX);
                AddSpawn(SpawnKind.Cannon, new Vector2(rearX, rear.FeetPosition.y + .6f),
                    new Vector2(1.5f, 1.2f), rearTarget, facing: direction, value: 16f,
                    text: "Watch the cannons. Use the ledges for cover.");
            }
            if (Chapter >= 1)
            {
                RouteNode victim = Route[Mathf.Min(room.LastRouteNode - 1, middle + 2)];
                AddSpawn(SpawnKind.Snare, victim.FeetPosition + Vector2.up * 1.9f, new Vector2(0.75f, 1.8f), middle + 2);
            }
            if (Chapter >= 2)
                AddSpawn(SpawnKind.Plant, target.FeetPosition + new Vector2(-direction * 1.5f, 3.1f), new Vector2(1.3f, 2.5f), middle);
            if (Chapter == 2)
            {
                int earlyIndex = room.FirstRouteNode + (room.Band == 0 ? 2 : 1);
                int lateIndex = Mathf.Max(earlyIndex + 3, room.LastRouteNode - 1);
                RouteNode early = Route[earlyIndex];
                RouteNode late = Route[Mathf.Min(lateIndex, room.LastRouteNode - 1)];
                AddSpawn(SpawnKind.Snare, early.FeetPosition + Vector2.up * 1.9f,
                    new Vector2(.75f, 1.8f), earlyIndex);
                AddSpawn(SpawnKind.Plant, late.FeetPosition + new Vector2(direction * .7f, 3.1f),
                    new Vector2(1.3f, 2.5f), Mathf.Min(lateIndex, room.LastRouteNode - 1));
            }
        }
        for (int i = 0; i < 3; i++)
        {
            float x = Mathf.Lerp(room.Bounds.xMin + 2f, room.Bounds.xMax - 2f, (i + 0.5f) / 3f);
            AddSpawn(SpawnKind.Decoration, new Vector2(x, RoomBaseY(room) + 1.6f), new Vector2(1.2f, 1.2f), room.FirstRouteNode,
                value: (Chapter + i) % 4, optional: true, text: i % 2 == 0 ? "Barrels and crates" : "Wall lantern");
        }
    }

    private Region AddRegion(RectInt bounds, int parent, int depth, int band, Vector2 entrance, Window exit)
    {
        var region = new Region { Id = Rooms.Count, Bounds = bounds, Parent = parent, Depth = depth, Band = band, Entrance = entrance, Exit = exit };
        Rooms.Add(region);
        return region;
    }

    private void RemoveRedundantCheckpoints()
    {
        var ordered = new List<Spawn>();
        foreach (Spawn spawn in Spawns)
            if (spawn.Kind == SpawnKind.Checkpoint) ordered.Add(spawn);
        ordered.Sort((a, b) => a.NodeIndex.CompareTo(b.NodeIndex));
        for (int i = 0; i < ordered.Count; i++)
        {
            Spawn previous = ordered[i];
            if (!Spawns.Contains(previous) || previous.NodeIndex == 0) continue;
            for (int next = i + 1; next < ordered.Count; next++)
            {
                Spawn candidate = ordered[next];
                if (!Spawns.Contains(candidate) || !CheckpointsShareSafeApproach(previous, candidate)) continue;
                Spawns.Remove(previous);
                break;
            }
        }
    }

    private bool CheckpointsShareSafeApproach(Spawn first, Spawn second)
    {
        if (Mathf.Abs(first.Position.y - second.Position.y) > .35f) return false;
        int low = Mathf.Min(first.NodeIndex, second.NodeIndex);
        int high = Mathf.Max(first.NodeIndex, second.NodeIndex);
        if (low < 0 || high >= Route.Count) return false;
        if (low == 0 && high > 0) return false;
        for (int node = low + 1; node <= high; node++)
            if (Route[node].RequiredAbility.HasValue) return false;
        if (Vector2.Distance(first.Position, second.Position) < 6f) return true;
        return CheckpointsShareShortWalk(low, high);
    }

    private bool CheckpointsShareShortWalk(int low, int high)
    {
        if (low == 0 || high - low > 4 || high <= low) return false;
        RouteNode start = Route[low];
        Region room = Rooms[Route[high].RoomIndex];
        if (room.IsShaft) return false;
        if (start.RoomIndex != room.Id &&
            (!Rooms[start.RoomIndex].IsShaft || low != Rooms[start.RoomIndex].LastRouteNode ||
                Vector2.Distance(start.FeetPosition, room.Entrance) > .015f)) return false;
        float surface = start.FeetPosition.y;
        float direction = Mathf.Sign(Route[high].FeetPosition.x - start.FeetPosition.x);
        float left = start.FeetPosition.x, right = left;
        Platform previous = Platforms[start.PlatformIndex];
        for (int i = low + 1; i <= high; i++)
        {
            RouteNode node = Route[i];
            Platform support = Platforms[node.PlatformIndex];
            if (node.RoomIndex != room.Id || node.Action != TraversalAction.Walk ||
                node.RequiredAbility.HasValue || Mathf.Abs(node.FeetPosition.y - surface) > .015f ||
                (node.FeetPosition.x - Route[i - 1].FeetPosition.x) * direction < -.015f ||
                support.Bounds.xMin > previous.Bounds.xMax + .015f ||
                support.Bounds.xMax < previous.Bounds.xMin - .015f) return false;
            left = Mathf.Min(left, node.FeetPosition.x);
            right = Mathf.Max(right, node.FeetPosition.x);
            previous = support;
        }
        Rect corridor = Rect.MinMaxRect(left - .29f, surface + .035f, right + .29f, surface + 1.05f);
        foreach (RectInt wall in Solids)
            if (corridor.Overlaps(new Rect(wall.x, wall.y, wall.width, wall.height))) return false;
        foreach (Platform platform in Platforms)
            if (corridor.Overlaps(platform.Bounds)) return false;
        foreach (Spawn spawn in Spawns)
        {
            bool enemy = spawn.Kind == SpawnKind.Cannon || spawn.Kind == SpawnKind.Plant ||
                spawn.Kind == SpawnKind.Snare || spawn.Kind == SpawnKind.Crawler;
            bool obstacle = enemy || spawn.Kind == SpawnKind.Spikes ||
                spawn.Kind == SpawnKind.RopeGate || spawn.Kind == SpawnKind.JumpPad;
            if (!obstacle) continue;
            if (enemy && spawn.NodeIndex >= 0 && spawn.NodeIndex < Route.Count &&
                Route[spawn.NodeIndex].RoomIndex == room.Id) return false;
            if ((spawn.Kind != SpawnKind.JumpPad && spawn.NodeIndex > low && spawn.NodeIndex < high) ||
                corridor.Overlaps(new Rect(spawn.Position - spawn.Size * .5f, spawn.Size))) return false;
        }
        return true;
    }

    private int AddPlatform(Vector2 feet, float width, int room, bool oneWay, bool optional)
    {
        Platforms.Add(new Platform
        {
            Bounds = new Rect(feet.x - width * 0.5f, feet.y - SlabHeight, width, SlabHeight),
            RoomIndex = room, OneWay = oneWay, Optional = optional
        });
        return Platforms.Count - 1;
    }

    private int AddRoutePlatform(Vector2 feet, float width, int room, TraversalAction action,
        PirateUpgrade? required = null, string note = null)
    {
        int platform = AddPlatform(feet, width, room, true, false);
        Route.Add(new RouteNode { PlatformIndex = platform, RoomIndex = room, FeetPosition = feet,
            Action = action, RequiredAbility = required, Note = note });
        return Route.Count - 1;
    }

    private void AddUpgrade(PirateUpgrade upgrade, Vector2 position)
    {
        if (AvailableAtChapterStart(upgrade)) return;
        foreach (Spawn spawn in Spawns)
            if (spawn.Kind == SpawnKind.Upgrade && spawn.Ability == upgrade) return;
        AddSpawn(SpawnKind.Upgrade, position, new Vector2(0.9f, 0.9f), Route.Count - 1,
            ability: upgrade, text: upgrade.ToString());
    }

    private void AddSpawn(SpawnKind kind, Vector2 position, Vector2 size, int node, int facing = 1,
        PirateUpgrade ability = default, float value = 0f, bool optional = false, string text = null)
    {
        Spawns.Add(new Spawn { Kind = kind, Position = position, Size = size, NodeIndex = node,
            Facing = facing, Ability = ability, Value = value, Optional = optional, Text = text });
    }
}
