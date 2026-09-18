using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class CampaignLayout
{
    [Serializable]
    public sealed class Scenario
    {
        public string Id;
        public string Description;
        public int RoomIndex;
        public int FirstNode;
        public int LastNode;
        public PirateUpgrade[] Requirements;
        public string AlternativeRoute;
        public bool IsMixed => Requirements != null && Requirements.Length > 1;
        public bool HasKnownAlternative => !string.IsNullOrEmpty(AlternativeRoute);
    }

    private bool UsesChamberPlan => Chapter > 0;
    private static readonly int[] CrownLeft = { 11, 17, 17, 29, 29, 37, 37, 23, 23, 11, 11, 21 };
    private static readonly int[] CrownRight = { 43, 43, 59, 59, 73, 73, 67, 67, 53, 53, 47, 47 };
    private bool AvailableAtChapterStart(PirateUpgrade ability)
    {
        if (Chapter == 0) return false;
        if (ability == PirateUpgrade.Hook1 || ability == PirateUpgrade.Saber1) return true;
        if (Chapter >= 2 && (ability == PirateUpgrade.Hook2 || ability == PirateUpgrade.SpringLeg)) return true;
        return Chapter >= 3 && (ability == PirateUpgrade.DoubleJump || ability == PirateUpgrade.Saber2);
    }

    private StrategyKind WingStrategy(int band)
    {
        if (Chapter == 3)
        {
            StrategyKind[] observatory = { StrategyKind.DarkGallery, StrategyKind.SaberSlide, StrategyKind.DoubleJumpRise,
                StrategyKind.Pyramid, StrategyKind.GrappleRise, StrategyKind.SaberSlide, StrategyKind.SpringRise,
                StrategyKind.SaberSlide, StrategyKind.DoubleJumpRise, StrategyKind.SaberSlide, StrategyKind.Grid, StrategyKind.GrappleRise };
            return observatory[band];
        }
        if (Chapter == 1)
        {
            StrategyKind[] sequence = { StrategyKind.Grid, StrategyKind.GrappleRise, StrategyKind.SpringRise,
                StrategyKind.Pyramid, StrategyKind.GrappleRise, StrategyKind.SpringRise,
                StrategyKind.Grid, StrategyKind.JumpPad, StrategyKind.SpringRise,
                StrategyKind.GrappleRise, StrategyKind.Pyramid, StrategyKind.Grid,
                StrategyKind.SpringRise, StrategyKind.SpringRise, StrategyKind.Grid, StrategyKind.SpringRise };
            return sequence[band];
        }
        StrategyKind[] garden = { StrategyKind.DoubleJumpRise, StrategyKind.Grid, StrategyKind.SaberSlide,
            StrategyKind.Pyramid, StrategyKind.SpringRise, StrategyKind.SaberSlide,
            StrategyKind.Grid, StrategyKind.JumpPad, StrategyKind.DoubleJumpRise,
            StrategyKind.SaberSlide, StrategyKind.Pyramid, StrategyKind.Grid };
        return garden[band];
    }

    private string ScenarioFor(Region room)
    {
        if (Chapter == 1)
        {
            if (room.Band >= 12)
            {
                if (room.Band == 14) return "arsenal.upper-crossfire";
                int slot = room.Band == 12 ? 0 : room.Band == 13 ? 1 : 2;
                string[] spring = { "arsenal.spring-recharge-relay", "arsenal.spring-drop-chute", "arsenal.spring-return-balcony" };
                return spring[(slot + ScenarioVariant) % 3];
            }
            if (room.Band == 4) return "arsenal.cut-and-grapple";
            if (room.Band == 5) return "arsenal.cut-and-spring";
            if (room.Band == 8) return "arsenal.hook-vault-with-spring-approach";
            if (room.Band == 9) return "arsenal.ring-and-cut-exit";
        }
        if (Chapter == 2)
        {
            if (room.Band == 4) return "garden.spring-double-vault";
            if (room.Band == 5) return "garden.slide-double-balcony";
            if (room.Band == 8) return "garden.cut-double-wall";
            if (room.Band == 9) return "garden.slide-spring-transfer";
        }
        if (Chapter == 0 && room.Strategy == StrategyKind.ChainGap && room.Band == 3) return "dock.cut-then-swing";
        if (Chapter == 0 && room.Strategy == StrategyKind.ChainGap && room.Band == 5) return "dock.swing-then-cut";
        if (Chapter == 3)
        {
            string[] names = { "crown.dark-entry", "crown.slide-refuges", "crown.double-window", "crown.broken-arches",
                "crown.high-hook-vault", "crown.slide-descending-terraces", "crown.spring-double-vault", "crown.slide-ring-launch",
                "crown.double-wide-vault", "crown.slide-launch-step", "crown.high-garden", "crown.cut-high-hook" };
            if (ScenarioVariant == 1)
            {
                names[1] = "crown.slide-descending-terraces"; names[5] = "crown.slide-refuges";
                names[2] = "crown.double-wide-vault"; names[8] = "crown.double-window";
            }
            return names[room.Band];
        }
        return "chapter-" + Chapter + "." + room.Strategy.ToString().ToLowerInvariant() + "." + room.Band;
    }

    private void RegisterScenario(Region room)
    {
        var requirements = new HashSet<PirateUpgrade>();
        for (int node = room.FirstRouteNode; node <= room.LastRouteNode; node++)
        {
            if (Route[node].RequiredAbility.HasValue) requirements.Add(Route[node].RequiredAbility.Value);
            if (Route[node].SecondaryAbility.HasValue) requirements.Add(Route[node].SecondaryAbility.Value);
        }
        var ordered = new List<PirateUpgrade>(requirements);
        ordered.Sort();
        Scenarios.Add(new Scenario { Id = room.ScenarioId, RoomIndex = room.Id,
            FirstNode = room.FirstRouteNode, LastNode = room.LastRouteNode,
            Requirements = ordered.ToArray(), AlternativeRoute = KnownAlternative(room),
            Description = room.IsShaft ? "Открытый переход между секциями макрографа" : "Комната применения освоенных приёмов" });
    }

    private bool FillNamedScenario(Region room, int direction, Vector2 end)
    {
        string id = room.ScenarioId;
        if (id == "arsenal.spring-recharge-relay") { FillSpringRecharge(room, direction, end); return true; }
        if (id == "arsenal.spring-drop-chute") { FillSpringDrop(room, direction, end); return true; }
        if (id == "arsenal.spring-return-balcony") { FillSpringReturn(room, direction, end); return true; }
        if (id == "crown.slide-refuges") { FillSlideRefuges(room, direction, end, false); return true; }
        if (id == "crown.slide-descending-terraces") { FillSlideRefuges(room, direction, end, true); return true; }
        if (id == "crown.slide-launch-step") { FillSlideLaunch(room, direction, end); return true; }
        if (id == "crown.double-window") { FillDoubleWindow(room, direction, end); return true; }
        if (id == "crown.double-wide-vault") { FillDoubleWide(room, direction, end); return true; }
        if (id == "crown.high-hook-vault" || id == "crown.cut-high-hook")
        {
            if (id == "crown.cut-high-hook") AddApproachRope(room, direction);
            FillHighGate(room, direction, end, PirateUpgrade.Hook2, TraversalAction.Grapple, 6.2f);
            return true;
        }
        if (id == "dock.cut-then-swing" || id == "arsenal.cut-and-grapple" ||
            id == "arsenal.cut-and-spring" || id == "garden.cut-double-wall")
        {
            AddApproachRope(room, direction);
            if (id == "dock.cut-then-swing") FillChainGap(room, direction, end);
            else if (id == "arsenal.cut-and-grapple") FillHighGate(room, direction, end, PirateUpgrade.Hook2, TraversalAction.Grapple, 4f);
            else if (id == "arsenal.cut-and-spring") FillSpringGate(room, direction, end);
            else FillHighGate(room, direction, end, PirateUpgrade.DoubleJump, TraversalAction.DoubleJump, 4f);
            return true;
        }
        if (id == "dock.swing-then-cut" || id == "arsenal.ring-and-cut-exit")
        {
            Vector2 approachExit = end - new Vector2(direction * 1.3f, 0f);
            if (id == "dock.swing-then-cut") FillChainGap(room, direction, approachExit);
            else FillHighGate(room, direction, approachExit, PirateUpgrade.Hook2, TraversalAction.Grapple, 4f);
            AddRopeCrossing(room, direction, end);
            return true;
        }
        if (id == "garden.spring-double-vault" || id == "crown.spring-double-vault" || id == "arsenal.hook-vault-with-spring-approach")
        {
            FillCombinedSpring(room, direction, end, id == "arsenal.hook-vault-with-spring-approach");
            return true;
        }
        if (TryGetSlideTransferAction(id, out TraversalAction slideTransfer))
        {
            FillSlideTransfer(room, direction, end, slideTransfer);
            return true;
        }
        return false;
    }

    private static bool TryGetSlideTransferAction(string scenarioId, out TraversalAction action)
    {
        switch (scenarioId)
        {
            case "crown.slide-ring-launch": action = TraversalAction.Grapple; return true;
            case "garden.slide-spring-transfer": action = TraversalAction.Spring; return true;
            case "garden.slide-double-balcony":
            case "crown.slide-double-return": action = TraversalAction.DoubleJump; return true;
            default: action = default; return false;
        }
    }

    private void AddApproachRope(Region room, int direction)
    {
        AddUpgrade(PirateUpgrade.Saber1, room.Entrance + new Vector2(direction * .4f, .8f));
        Vector2 after = room.Entrance + new Vector2(direction * 1.8f, 0f);
        AddRopeCrossing(room, direction, after);
    }

    private void AddRopeCrossing(Region room, int direction, Vector2 after)
    {
        Vector2 before = Route[Route.Count - 1].FeetPosition;
        float x = (before.x + after.x) * .5f;
        AddSpawn(SpawnKind.RopeGate, new Vector2(x, RoomBaseY(room) + 8.5f),
            new Vector2(.65f, 15f), Route.Count - 1, text: "LMB: cut the rope, then keep moving.");
        AddRoutePlatform(after, 3f, room.Id, TraversalAction.Saber, PirateUpgrade.Saber1,
            "Физический канат внутри комбинированного маршрута");
    }

    private void FillCombinedSpring(Region room, int direction, Vector2 end, bool grapple)
    {
        float centre = room.Bounds.center.x, surface = room.Entrance.y;
        AddUpgrade(PirateUpgrade.SpringLeg, room.Entrance + new Vector2(direction, .8f));
        PirateUpgrade second = grapple ? PirateUpgrade.Hook2 : PirateUpgrade.DoubleJump;
        AddUpgrade(second, room.Entrance + new Vector2(direction * 1.8f, .8f));
        ConnectOrdinary(room, new Vector2(centre - direction * 4f, surface), 3.4f);
        Vector2 spikes = new Vector2(centre - direction * .8f, surface - .15f);
        AddPlatform(new Vector2(spikes.x, surface - .3f), 2.8f, room.Id, true, false);
        AddSpawn(SpawnKind.Spikes, spikes, new Vector2(2.8f, .3f), Route.Count - 1,
            text: grapple ? "Bounce on spikes. RMB: grab the ring. Space: leap." : "Bounce on spikes, then press Space near the top.");
        if (grapple) AddSpawn(SpawnKind.Anchor, new Vector2(centre + direction * .6f, surface + 8.4f),
            new Vector2(.9f, .9f), Route.Count - 1);
        int node = AddRoutePlatform(new Vector2(centre + direction * 2.4f, surface + 6.2f), 2.2f, room.Id,
            grapple ? TraversalAction.SpringGrapple : TraversalAction.SpringDouble, grapple ? PirateUpgrade.Hook2 : PirateUpgrade.SpringLeg,
            "6.2 м выше шипов: обычного прыжка и одиночного отскока недостаточно");
        Route[node].SecondaryAbility = grapple ? (PirateUpgrade?)null : second;
        SealLandingToFloor(node, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void FillSlideTransfer(Region room, int direction, Vector2 end, TraversalAction nextAction)
    {
        AddUpgrade(PirateUpgrade.Saber2, room.Entrance + new Vector2(direction, .8f));
        float centre = room.Bounds.center.x, surface = room.Entrance.y, floor = RoomBaseY(room) + 1f;
        float start = centre - direction * 7.3f, finish = centre - direction * 1.3f;
        float left = Mathf.Min(start, finish), right = Mathf.Max(start, finish);
        ConnectOrdinary(room, new Vector2(start - direction * 1.2f, surface), 2.4f);
        Platforms.Add(new Platform { Bounds = new Rect(left, floor, 6f, surface - .3f - floor),
            OneWay = false, RoomIndex = room.Id });
        AddSpawn(SpawnKind.Spikes, new Vector2((left + right) * .5f, surface - .15f), new Vector2(6f, .3f),
            Route.Count - 1, text: "Slide to gain speed. Release S on the safe ledge.");
        Platforms.Add(new Platform { Bounds = new Rect(left, surface + 1.3f, 6f,
            RoomCeilingY(room) - surface - 1.3f), OneWay = false, RoomIndex = room.Id, Optional = true });
        Vector2 lip = new Vector2(finish + direction * .9f, surface);
        AddRoutePlatform(lip, 1.8f, room.Id, TraversalAction.SaberSlide, PirateUpgrade.Saber2,
            "Защищённый разгон заканчивается реальным чистым пусковым выступом");
        float landingX = centre + direction * (nextAction == TraversalAction.Spring ? 5.5f : 3.5f);
        PirateUpgrade next = nextAction == TraversalAction.Grapple ? PirateUpgrade.Hook2 :
            nextAction == TraversalAction.Spring ? PirateUpgrade.SpringLeg : PirateUpgrade.DoubleJump;
        AddUpgrade(next, room.Entrance + new Vector2(direction * 1.8f, .8f));
        if (nextAction == TraversalAction.Grapple)
            AddSpawn(SpawnKind.Anchor, new Vector2(centre + direction * 3f, surface + 8f), new Vector2(.9f, .9f), Route.Count - 1);
        if (nextAction == TraversalAction.Spring)
        {
            Vector2 spike = new Vector2(centre + direction * 1.8f, surface - .15f);
            AddPlatform(new Vector2(spike.x, surface - .3f), 2.6f, room.Id, true, false);
            AddSpawn(SpawnKind.Spikes, spike, new Vector2(2.6f, .3f), Route.Count - 1,
                text: "Spring recharged. Release S and bounce on the next spikes.");
        }
        int node = AddRoutePlatform(new Vector2(landingX, surface + 4f), 2.6f, room.Id, nextAction, next,
            "После скольжения — отдельный высокий переход, а не конец испытания");
        SealLandingToFloor(node, room);
        ConnectOrdinary(room, end, 4.8f);
    }

    private void SealLandingToFloor(int node, Region room)
    {
        Platform wall = Platforms[Route[node].PlatformIndex];
        float floor = RoomBaseY(room) + 1f;
        wall.OneWay = false;
        wall.Bounds = new Rect(wall.Bounds.xMin, floor, wall.Bounds.width, wall.SurfaceY - floor);
    }

    private void ReserveCannonStation(Region room, int preferredNode, int threatNode, int offsetDirection,
        out RouteNode chosen, out float cannonX)
    {
        Vector2 threat = Route[threatNode].StandingPosition(0.5f);
        for (int distance = 0; distance < room.LastRouteNode - room.FirstRouteNode; distance++)
        for (int side = 0; side < (distance == 0 ? 1 : 2); side++)
        {
            int index = preferredNode + (side == 0 ? -distance : distance);
            if (index <= room.FirstRouteNode || index >= room.LastRouteNode) continue;
            RouteNode node = Route[index];
            if (node.RequiredAbility.HasValue || node.SecondaryAbility.HasValue) continue;
            Platform deck = Platforms[node.PlatformIndex];
            float width = Mathf.Max(deck.Bounds.width, 4.6f);
            bool widen = width > deck.Bounds.width + .015f;
            float centerX = widen && !deck.OneWay ? node.FeetPosition.x : deck.CenterX;
            Rect proposed = new Rect(centerX - width * .5f, deck.SurfaceY - SlabHeight, width, SlabHeight);
            float x = Mathf.Clamp(node.FeetPosition.x + offsetDirection * .35f,
                Mathf.Max(room.Bounds.xMin + 1.4f, proposed.xMin + .75f),
                Mathf.Min(room.Bounds.xMax - 1.4f, proposed.xMax - .75f));
            var body = new Rect(x - .75f, deck.SurfaceY + .001f, 1.5f, 1.2f);
            Vector2 muzzle = new Vector2(x, deck.SurfaceY + .6f);
            if (Vector2.Distance(muzzle, threat) > 9f) continue;
            if (!CannonBodyFitsArchitecture(body, deck, proposed) ||
                !CannonStationPreservesLandings(body, deck, proposed)) continue;
            if (widen)
            {
                if (deck.OneWay) deck.Bounds = proposed;
                else node.PlatformIndex = AddPlatform(node.FeetPosition, width, node.RoomIndex, true, false);
            }
            chosen = node; cannonX = x;
            return;
        }
        throw new InvalidOperationException("No body-safe cannon station in ordinary room " + room.Id);
    }

    private bool CannonBodyFitsArchitecture(Rect body, Platform edited, Rect proposed)
    {
        foreach (RectInt wall in Solids)
            if (body.Overlaps(new Rect(wall.x, wall.y, wall.width, wall.height))) return false;
        foreach (Platform platform in Platforms)
        {
            Rect bounds = platform == edited && edited.OneWay ? proposed : platform.Bounds;
            if (bounds.yMax <= body.yMin + .015f) continue;
            if (body.Overlaps(bounds)) return false;
        }
        foreach (Spawn spawn in Spawns)
            if (spawn.Kind == SpawnKind.Cannon && body.Overlaps(CannonEnvelope(spawn, edited, proposed))) return false;
        return true;
    }

    private Rect CannonEnvelope(Spawn spawn, Platform edited, Rect proposed)
    {
        float floor = float.NegativeInfinity;
        foreach (Platform platform in Platforms)
        {
            Rect bounds = platform == edited && edited.OneWay ? proposed : platform.Bounds;
            if (spawn.Position.x >= bounds.xMin && spawn.Position.x <= bounds.xMax && bounds.yMax <= spawn.Position.y + .01f)
                floor = Mathf.Max(floor, bounds.yMax);
        }
        if (edited != null && spawn.Position.x >= proposed.xMin && spawn.Position.x <= proposed.xMax && proposed.yMax <= spawn.Position.y + .01f)
            floor = Mathf.Max(floor, proposed.yMax);
        foreach (RectInt wall in Solids)
            if (spawn.Position.x >= wall.xMin && spawn.Position.x <= wall.xMax && wall.yMax <= spawn.Position.y + .01f)
                floor = Mathf.Max(floor, wall.yMax);
        if (float.IsNegativeInfinity(floor)) floor = spawn.Position.y - spawn.Size.y * .5f;
        return new Rect(spawn.Position.x - spawn.Size.x * .5f, floor + .001f, spawn.Size.x, spawn.Size.y);
    }

    private bool CannonStationPreservesLandings(Rect body, Platform edited, Rect proposed)
    {
        var withoutNew = new List<Rect>();
        foreach (Spawn spawn in Spawns)
            if (spawn.Kind == SpawnKind.Cannon) withoutNew.Add(CannonEnvelope(spawn, edited, proposed));
        var withNew = new List<Rect>(withoutNew) { body };
        foreach (RouteNode node in Route)
        {
            Platform support = Platforms[node.PlatformIndex];
            Rect landing = support == edited ? proposed : support.Bounds;
            bool near = withNew.Exists(enemy => enemy.xMax + .45f > landing.xMin &&
                enemy.xMin - .45f < landing.xMax && enemy.yMin < landing.yMax + 1.05f &&
                enemy.yMax > landing.yMax + .04f);
            if (!near) continue;
            if (HasCannonSafeRestingInterval(support, landing, withoutNew, edited, proposed) &&
                !HasCannonSafeRestingInterval(support, landing, withNew, edited, proposed)) return false;
        }
        return true;
    }

    private bool HasCannonSafeRestingInterval(Platform own, Rect landing, IReadOnlyList<Rect> enemies,
        Platform edited = null, Rect proposed = default)
    {
        var free = new List<Vector2> { new Vector2(landing.xMin + .61f, landing.xMax - .61f) };
        if (free[0].x > free[0].y) return false;
        foreach (Platform platform in Platforms)
        {
            if (platform == own) continue;
            Rect bounds = platform == edited && edited.OneWay ? proposed : platform.Bounds;
            ExcludeCannonStandingObstacle(free, landing.yMax, bounds);
        }
        if (edited != null && edited != own && !edited.OneWay) ExcludeCannonStandingObstacle(free, landing.yMax, proposed);
        foreach (RectInt wall in Solids)
            ExcludeCannonStandingObstacle(free, landing.yMax, new Rect(wall.x, wall.y, wall.width, wall.height));
        foreach (Rect enemy in enemies)
        {
            if (enemy.yMax <= landing.yMax + .04f || enemy.yMin >= landing.yMax + 1.05f) continue;
            ExcludeCannonStandingRange(free, enemy.xMin - .45f, enemy.xMax + .45f);
        }
        return free.Count > 0;
    }

    private static void ExcludeCannonStandingObstacle(List<Vector2> free, float feet, Rect bounds)
    {
        if (bounds.yMax <= feet + .14f || bounds.yMin >= feet + 1.05f) return;
        ExcludeCannonStandingRange(free, bounds.xMin - .45f, bounds.xMax + .45f);
    }

    private static void ExcludeCannonStandingRange(List<Vector2> free, float left, float right)
    {
        for (int i = free.Count - 1; i >= 0; i--)
        {
            Vector2 range = free[i];
            if (right <= range.x || left >= range.y) continue;
            free.RemoveAt(i);
            if (left > range.x) free.Add(new Vector2(range.x, Mathf.Min(left, range.y)));
            if (right < range.y) free.Add(new Vector2(Mathf.Max(right, range.x), range.y));
        }
    }

    private void ValidateCannonStations()
    {
        var enemies = new List<Rect>();
        foreach (Spawn spawn in Spawns)
            if (spawn.Kind == SpawnKind.Cannon) enemies.Add(CannonEnvelope(spawn, null, default));
        for (int index = 0; index < Route.Count; index++)
        {
            Platform support = Platforms[Route[index].PlatformIndex];
            bool near = enemies.Exists(body => body.xMax + .45f > support.Bounds.xMin &&
                body.xMin - .45f < support.Bounds.xMax && body.yMin < support.SurfaceY + 1.05f && body.yMax > support.SurfaceY + .04f);
            if (!near) continue;
            if (HasCannonSafeRestingInterval(support, support.Bounds, Array.Empty<Rect>()) &&
                !HasCannonSafeRestingInterval(support, support.Bounds, enemies))
                ValidationErrors.Add("Cannon body leaves no safe standing interval at route node: " + index);
        }
    }

    private void SelectExpeditionCheckpoints()
    {
        Spawns.RemoveAll(spawn => spawn.Kind == SpawnKind.Checkpoint && spawn.NodeIndex != 0);
        var selected = new HashSet<int> { 0 };
        foreach (Region room in Rooms)
        {
            if (!room.IsLeaf || room.IsShaft) continue;
            bool firstInBand = true;
            foreach (Region previous in Rooms)
                if (previous.IsLeaf && !previous.IsShaft && previous.Band == room.Band && previous.FirstRouteNode < room.FirstRouteNode)
                    firstInBand = false;
            bool sectionStart = Chapter == 1 ? false :
                UsesChamberPlan ? room.Band % 2 == 0 : true;
            if (firstInBand && room.Band > 0 && sectionStart) selected.Add(room.FirstRouteNode);
        }
        foreach (Spawn pickup in Spawns)
        {
            if (pickup.Kind != SpawnKind.Upgrade || pickup.Ability == PirateUpgrade.Saber1 || pickup.Ability == PirateUpgrade.Parrot) continue;
            int trial = Route.FindIndex(node => node.RequiredAbility == pickup.Ability);
            if (trial <= 0) continue;
            int before = trial - 1;
            selected.RemoveWhere(index => index > 0 && Rooms[Route[index].RoomIndex].Band == Rooms[Route[before].RoomIndex].Band);
            selected.Add(before);
        }
        if (Chapter == 1) SelectExtendedArsenalSections(selected);
        foreach (int node in selected)
        {
            if (node == 0) continue;
            AddSpawn(SpawnKind.Checkpoint, Route[node].FeetPosition + Vector2.up * .6f,
                new Vector2(.8f, 1.2f), node, text: "Section checkpoint");
        }
    }

    private void SelectExtendedArsenalSections(HashSet<int> selected)
    {
        var distance = new float[Route.Count];
        for (int node = 1; node < Route.Count; node++)
            distance[node] = distance[node - 1] + Vector2.Distance(Route[node - 1].FeetPosition, Route[node].FeetPosition);
        int current = 0;
        foreach (int checkpoint in selected) current = Mathf.Max(current, checkpoint);
        while (distance[Route.Count - 1] - distance[current] > 185f)
        {
            int candidate = -1;
            for (int node = current + 1; node < Route.Count && distance[node] - distance[current] <= 180f; node++)
            {
                RouteNode route = Route[node];
                if (route.RequiredAbility.HasValue || Platforms[route.PlatformIndex].Bounds.width < 3f) continue;
                bool safe = true;
                foreach (Spawn spawn in Spawns)
                {
                    if (spawn.Kind != SpawnKind.Cannon && spawn.Kind != SpawnKind.Plant && spawn.Kind != SpawnKind.Snare &&
                        spawn.Kind != SpawnKind.Spikes && spawn.Kind != SpawnKind.Crawler && spawn.Kind != SpawnKind.RopeGate) continue;
                    if (Mathf.Abs(spawn.Position.x - route.FeetPosition.x) < spawn.Size.x * .5f + 1.4f &&
                        Mathf.Abs(spawn.Position.y - route.FeetPosition.y - .6f) < spawn.Size.y * .5f + 1.1f)
                    { safe = false; break; }
                }
                if (safe) candidate = node;
            }
            if (candidate < 0 || candidate <= current) throw new InvalidOperationException("No safe Arsenal section checkpoint before replay budget.");
            selected.Add(candidate);
            current = candidate;
        }
    }

    private void ValidateExpeditionScenarios()
    {
        ValidateStandaloneTrials();
        int checkpoints = 0;
        var indices = new List<int>();
        foreach (Spawn spawn in Spawns)
            if (spawn.Kind == SpawnKind.Checkpoint) { checkpoints++; indices.Add(spawn.NodeIndex); }
        if (checkpoints < 3 || checkpoints > 7)
            ValidationErrors.Add("Expedition needs three to seven section checkpoints, not a lantern at every repeated gate.");
        indices.Sort();
        indices.Add(Route.Count - 1);
        for (int i = 1; i < indices.Count; i++)
        {
            float distance = 0f;
            for (int node = indices[i - 1] + 1; node <= indices[i]; node++)
                distance += Vector2.Distance(Route[node - 1].FeetPosition, Route[node].FeetPosition);
            if (distance > 190f || indices[i] - indices[i - 1] > 90)
                ValidationErrors.Add("Checkpoint section exceeds its bounded replay route.");
        }
        var ids = new HashSet<string>();
        foreach (Scenario scenario in Scenarios)
        {
            if (!ids.Add(scenario.Id)) ValidationErrors.Add("Scenario id is not unique in the chapter: " + scenario.Id);
            if (scenario.FirstNode < 0 || scenario.LastNode >= Route.Count || scenario.LastNode < scenario.FirstNode)
                ValidationErrors.Add("Scenario route interval is incomplete: " + scenario.Id);
            if (TryGetSlideTransferAction(scenario.Id, out TraversalAction expectedTransfer))
            {
                int slide = scenario.FirstNode >= 0 && scenario.LastNode < Route.Count && scenario.LastNode > scenario.FirstNode
                    ? Route.FindIndex(scenario.FirstNode, scenario.LastNode - scenario.FirstNode,
                        node => node.Action == TraversalAction.SaberSlide) : -1;
                PirateUpgrade expectedAbility = expectedTransfer == TraversalAction.Spring ? PirateUpgrade.SpringLeg :
                    expectedTransfer == TraversalAction.Grapple ? PirateUpgrade.Hook2 : PirateUpgrade.DoubleJump;
                if (slide < 0 || Route[slide + 1].Action != expectedTransfer ||
                    Route[slide + 1].RequiredAbility != expectedAbility || Route[slide + 1].SecondaryAbility.HasValue)
                    ValidationErrors.Add("Slide transfer lost its exact scenario action contract: " + scenario.Id);
                else if (expectedTransfer == TraversalAction.Spring &&
                    (!Spawns.Exists(spawn => spawn.Kind == SpawnKind.Spikes && spawn.NodeIndex == slide &&
                        Mathf.Abs(spawn.Size.x - 2.6f) < .015f) ||
                     Spawns.Exists(spawn => spawn.Kind == SpawnKind.Anchor && spawn.NodeIndex == slide)))
                    ValidationErrors.Add("Slide spring transfer lost its separate rebound bed or gained an unintended ring.");
            }
        }
        for (int index = 1; index < Route.Count; index++)
        {
            RouteNode node = Route[index];
            Vector2 delta = node.FeetPosition - Route[index - 1].FeetPosition;
            if (node.Action == TraversalAction.SpringDouble || node.Action == TraversalAction.SpringGrapple)
            {
                if (Mathf.Abs(delta.y - 6.2f) > .015f ||
                    (node.Action == TraversalAction.SpringDouble ? node.RequiredAbility != PirateUpgrade.SpringLeg || node.SecondaryAbility != PirateUpgrade.DoubleJump :
                        node.RequiredAbility != PirateUpgrade.Hook2 || node.SecondaryAbility.HasValue))
                    ValidationErrors.Add("Combined spring vault lost its ordered two-ability contract.");
                Platform barrier = Platforms[node.PlatformIndex];
                if (barrier.OneWay || barrier.Bounds.yMin > RoomFloorY(Rooms[node.RoomIndex]) + .015f)
                    ValidationErrors.Add("Combined spring vault permits a below-wall bypass.");
            }
            if (node.Action == TraversalAction.DoubleJump && Rooms[node.RoomIndex].IsShaft)
            {
                if (Mathf.Abs(delta.x) > .015f || delta.y < 3.7f)
                    ValidationErrors.Add("Vertical double-jump well became an ordinary zig-zag.");
                Vector2 previous = Route[index - 1].FeetPosition;
                for (int p = 0; p < Platforms.Count; p++)
                {
                    Platform alternate = Platforms[p];
                    if (p == node.PlatformIndex || p == Route[index - 1].PlatformIndex) continue;
                    bool between = alternate.SurfaceY > previous.y + .05f &&
                        alternate.SurfaceY < node.FeetPosition.y - .4f;
                    if (alternate.RoomIndex == node.RoomIndex && between)
                        ValidationErrors.Add("Vertical double-jump well contains an intermediate bypass support.");
                    bool blocksColumn = !alternate.OneWay &&
                        alternate.Bounds.xMin - .25f < node.FeetPosition.x &&
                        alternate.Bounds.xMax + .25f > node.FeetPosition.x &&
                        alternate.Bounds.yMax > previous.y + .2f &&
                        alternate.Bounds.yMin < node.FeetPosition.y - .2f;
                    if (alternate.RoomIndex != node.RoomIndex && blocksColumn)
                        ValidationErrors.Add("Vertical double-jump well is blocked by a solid outside the shaft room.");
                }
            }
            if (node.Action == TraversalAction.RingRelay)
            {
                int anchors = 0;
                foreach (Spawn spawn in Spawns)
                    if (spawn.Kind == SpawnKind.Anchor && spawn.NodeIndex == index - 1) anchors++;
                if (anchors != 3 || Mathf.Abs(delta.y - 10.8f) > .015f || Mathf.Abs(delta.x) > .015f)
                    ValidationErrors.Add("Ring relay lost its three-anchor, safe upper-landing contract.");
                foreach (Platform alternate in Platforms)
                    if (alternate.RoomIndex == node.RoomIndex && alternate.SurfaceY > Route[index - 1].FeetPosition.y + .1f &&
                        alternate.SurfaceY < node.FeetPosition.y - .1f)
                        ValidationErrors.Add("Ring relay contains a ground-reset bypass inside its airborne interval.");
            }
        }
    }

    private static string RunExpeditionSelfTests()
    {
        string[] transferIds = { "crown.slide-ring-launch", "garden.slide-spring-transfer",
            "garden.slide-double-balcony", "crown.slide-double-return" };
        TraversalAction[] transferActions = { TraversalAction.Grapple, TraversalAction.Spring,
            TraversalAction.DoubleJump, TraversalAction.DoubleJump };
        for (int i = 0; i < transferIds.Length; i++)
            if (!TryGetSlideTransferAction(transferIds[i], out TraversalAction actual) || actual != transferActions[i])
                throw new InvalidOperationException("Exact slide-transfer scenario classification regressed: " + transferIds[i]);
        foreach (string unknown in new[] { null, "", "spring", "ring", "garden.slide-spring-transfer.extra" })
            if (TryGetSlideTransferAction(unknown, out _))
                throw new InvalidOperationException("An unknown slide-transfer scenario was accepted by substring.");
        CampaignLayout springTransfer = Create(42, 2, 14f, 34.335f, .02f, 7.3f);
        RouteNode rebound = springTransfer.Route.Find(node => node.Action == TraversalAction.Spring &&
            springTransfer.Rooms[node.RoomIndex].ScenarioId == "garden.slide-spring-transfer");
        if (rebound == null || !springTransfer.GeometryValid)
            throw new InvalidOperationException("The generated slide-spring transfer has no validated spring route.");
        rebound.Action = TraversalAction.Grapple;
        if (springTransfer.Validate(out _) || !springTransfer.ValidationErrors.Exists(error => error.StartsWith("Slide transfer lost")))
            throw new InvalidOperationException("Slide-spring to hook substitution regression was not rejected.");
        foreach (int seed in new[] { 20260918, 42 })
        {
            CampaignLayout crownTransfers = Create(seed, 3, 14f, 34.335f, .02f, 8f);
            Region hookRoom = crownTransfers.Rooms.Find(room => room.IsLeaf && !room.IsShaft && room.Band == 7);
            if (hookRoom == null || hookRoom.ScenarioId != "crown.slide-ring-launch" || hookRoom.Strategy != StrategyKind.SaberSlide ||
                !crownTransfers.Route.Exists(node => node.RoomIndex == hookRoom.Id && node.Action == TraversalAction.Grapple) ||
                !crownTransfers.Scenarios.Exists(scenario => scenario.Id == "crown.slide-launch-step") ||
                !crownTransfers.GeometryValid)
                throw new InvalidOperationException("The Crown catalogue lost the actual slide-hook room or the standalone launch step.");
        }
        CampaignLayout genericCrown = Create(42, 3, 14f, 34.335f, .02f, 8f);
        Region removedHook = genericCrown.Rooms.Find(room => room.ScenarioId == "crown.slide-ring-launch");
        removedHook.ScenarioId = "crown.counterweight";
        removedHook.Strategy = StrategyKind.JumpPad;
        genericCrown.Route.Find(node => node.RoomIndex == removedHook.Id && node.Action == TraversalAction.Grapple).Action = TraversalAction.JumpPad;
        if (genericCrown.Validate(out _) || !genericCrown.ValidationErrors.Exists(error => error.StartsWith("Crown requires an actual")))
            throw new InvalidOperationException("Replacing the Crown slide-hook with a generic jump pad was not rejected.");
        var mixed = new HashSet<string>();
        for (int chapter = 0; chapter < 4; chapter++)
        {
            CampaignLayout layout = Create(20260918, chapter, 14f, 34.335f, .02f, new[] { 6f, 6.6f, 7.3f, 8f }[chapter]);
            foreach (Scenario scenario in layout.Scenarios)
                if (scenario.IsMixed) mixed.Add(scenario.Id);
        }
        if (mixed.Count < 10) throw new InvalidOperationException("The campaign lost its ten different mixed scenarios.");
        CampaignLayout narrowPlan = Create(42, 1, 14f, 34.335f, .02f, 6.6f);
        bool hasFlat = narrowPlan.MacroLinks.Exists(link => link.Kind == MacroLinkKind.Flat);
        bool hasDrop = narrowPlan.MacroLinks.Exists(link => link.Kind == MacroLinkKind.Drop);
        bool hasRise = narrowPlan.MacroLinks.Exists(link => link.Kind == MacroLinkKind.Rise);
        bool hasBothDirections = narrowPlan.MacroStages.Exists(stage => stage.Direction < 0) &&
            narrowPlan.MacroStages.Exists(stage => stage.Direction > 0);
        if (!hasFlat || !hasDrop || !hasRise || !hasBothDirections || narrowPlan.PlayableRoomCount != narrowPlan.BandCount)
            throw new InvalidOperationException("Seeded macro graph lost a real horizontal, rising or descending connection.");
        CampaignLayout missingSecond = Create(42, 2, 14f, 34.335f, .02f, 7.3f);
        RouteNode combined = missingSecond.Route.Find(node => node.Action == TraversalAction.SpringDouble);
        combined.SecondaryAbility = null;
        if (missingSecond.Validate(out _) || !missingSecond.ValidationErrors.Exists(error => error.StartsWith("Combined spring vault lost")))
            throw new InvalidOperationException("Combined-vault secondary-ability regression was not rejected.");
        CampaignLayout underpass = Create(42, 2, 14f, 34.335f, .02f, 7.3f);
        RouteNode tall = underpass.Route.Find(node => node.Action == TraversalAction.SpringDouble);
        underpass.Platforms[tall.PlatformIndex].OneWay = true;
        if (underpass.Validate(out _) || !underpass.ValidationErrors.Exists(error => error.StartsWith("Combined spring vault permits")))
            throw new InvalidOperationException("Combined-vault underpass regression was not rejected.");
        CampaignLayout intermediate = Create(42, 2, 14f, 34.335f, .02f, 7.3f);
        RouteNode vertical = intermediate.Route.Find(node => node.Action == TraversalAction.DoubleJump && intermediate.Rooms[node.RoomIndex].IsShaft);
        intermediate.AddPlatform(vertical.FeetPosition - Vector2.up * 1.9f, 3f, vertical.RoomIndex, true, true);
        if (intermediate.Validate(out _) || !intermediate.ValidationErrors.Exists(error => error.StartsWith("Vertical double-jump well contains")))
            throw new InvalidOperationException("Intermediate double-jump bypass regression was not rejected.");
        CampaignLayout brokenRelay = Create(42, 3, 14f, 34.335f, .02f, 8f);
        int relay = brokenRelay.Route.FindIndex(node => node.Action == TraversalAction.RingRelay);
        Spawn ring = brokenRelay.Spawns.Find(spawn => spawn.Kind == SpawnKind.Anchor && spawn.NodeIndex == relay - 1);
        brokenRelay.Spawns.Remove(ring);
        if (brokenRelay.Validate(out _) || !brokenRelay.ValidationErrors.Exists(error => error.StartsWith("Ring relay lost")))
            throw new InvalidOperationException("Missing relay anchor regression was not rejected.");
        CampaignLayout missingSection = Create(42, 2, 14f, 34.335f, .02f, 7.3f);
        missingSection.Spawns.RemoveAll(spawn => spawn.Kind == SpawnKind.Checkpoint && spawn.NodeIndex > 0);
        if (missingSection.Validate(out _) || !missingSection.ValidationErrors.Exists(error => error.StartsWith("Checkpoint section exceeds")))
            throw new InvalidOperationException("Unbounded checkpoint replay regression was not rejected.");
        string standalone = RunStandaloneTrialSelfTests();
        return $"mixedScenarios={mixed.Count}, exactSlideTransferClassifications=4, unknownSlideTransferRejected=True, springToHookSubstitutionRejected=True, crownSlideHookActuallyGenerated=True, crownStandaloneLaunchPreserved=True, crownGenericReplacementRejected=True, seededMacroLinksVerified=True, combinedOrderRejected=True, combinedUnderpassRejected=True, verticalBypassRejected=True, missingRelayRejected=True, sectionRetryRejected=True, {standalone}";
    }
}
