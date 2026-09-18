using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public sealed partial class CampaignLayout
{
    public enum MacroLinkKind { Flat, Rise, Drop }

    [Serializable]
    public sealed class MacroStage
    {
        public int Slot;
        public RectInt Bounds;
        public int Direction;
        public Vector2 Entrance;
        public Window Exit;
        public int BaseY => Bounds.yMin - 1;
    }

    [Serializable]
    public sealed class MacroLink
    {
        public int FromSlot, ToSlot;
        public MacroLinkKind Kind;
        public RectInt Bounds;
        public float ShaftX;
        public int RoomIndex = -1;
        public bool Exposed;
    }

    public readonly List<MacroStage> MacroStages = new List<MacroStage>();
    public readonly List<MacroLink> MacroLinks = new List<MacroLink>();
    public readonly List<RectInt> WorldFootprint = new List<RectInt>();
    private int initialMacroFootprintCount;
    private RectInt architectureEnvelope;
    private bool architectureEnvelopeValid;
    public IReadOnlyList<RectInt> PlayableFootprint => WorldFootprint;
    public static int RoomFloorY(Region room) => room.Bounds.yMin;
    public static int RoomBaseY(Region room) => RoomFloorY(room) - 1;
    public static int RoomCeilingY(Region room) => room.Bounds.yMax;
    private float OrdinaryStride => Mathf.Min(3.4f, SafeJumpDistance * .94f);

    public void IncludeWorldFootprint(RectInt rect)
    {
        if (rect.width <= 0 || rect.height <= 0) return;
        WorldFootprint.Add(rect);
        architectureEnvelopeValid = false;
    }

    public bool ContainsWorldCell(int x, int y)
    {
        Vector2Int point = new Vector2Int(x, y);
        foreach (RectInt rect in WorldFootprint) if (rect.Contains(point)) return true;
        return false;
    }

    public bool ContainsArchitectureCell(int x, int y)
    {
        EnsureArchitectureEnvelope();
        return architectureEnvelope.Contains(new Vector2Int(x, y));
    }

    private static RectInt ExpandedStageBounds(MacroStage stage) =>
        new RectInt(stage.Bounds.xMin - 1, stage.Bounds.yMin - 1, stage.Bounds.width + 2, stage.Bounds.height + 2);

    private void EnsureArchitectureEnvelope()
    {
        if (architectureEnvelopeValid) return;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool any = false;
        foreach (MacroStage stage in MacroStages)
        {
            Accumulate(ExpandedStageBounds(stage), ref minX, ref minY, ref maxX, ref maxY);
            any = true;
        }
        foreach (MacroLink link in MacroLinks)
        {
            Accumulate(link.Bounds, ref minX, ref minY, ref maxX, ref maxY);
            any = true;
        }
        for (int i = initialMacroFootprintCount; i < WorldFootprint.Count; i++)
        {
            Accumulate(WorldFootprint[i], ref minX, ref minY, ref maxX, ref maxY);
            any = true;
        }
        if (!any)
        {
            architectureEnvelope = new RectInt();
            architectureEnvelopeValid = true;
            return;
        }
        minX = Mathf.Max(Bounds.xMin, minX - 1);
        minY = Mathf.Max(Bounds.yMin, minY - 1);
        maxX = Mathf.Min(Bounds.xMax, maxX + 1);
        maxY = Mathf.Min(Bounds.yMax, maxY + 1);
        architectureEnvelope = new RectInt(minX, minY, maxX - minX, maxY - minY);
        architectureEnvelopeValid = true;
    }

    private void SealArchitectureHull()
    {
        EnsureArchitectureEnvelope();
        RectInt env = architectureEnvelope;
        if (env.width <= 0 || env.height <= 0) return;
        bool[,] occupied = RasterizePlayableOccupancy(env);
        for (int y = 0; y < env.height; y++)
        {
            int x = 0;
            while (x < env.width)
            {
                if (occupied[x, y]) { x++; continue; }
                int x1 = x + 1;
                while (x1 < env.width && !occupied[x1, y]) x1++;
                int y1 = y + 1;
                bool grow = true;
                while (y1 < env.height && grow)
                {
                    for (int i = x; i < x1; i++)
                        if (occupied[i, y1]) { grow = false; break; }
                    if (grow) y1++;
                }
                for (int yy = y; yy < y1; yy++)
                for (int xx = x; xx < x1; xx++)
                    occupied[xx, yy] = true;
                Solids.Add(new RectInt(env.xMin + x, env.yMin + y, x1 - x, y1 - y));
                x = x1;
            }
        }
    }

    private bool[,] RasterizePlayableOccupancy(RectInt env)
    {
        var occupied = new bool[env.width, env.height];
        foreach (RectInt rect in WorldFootprint) StampOccupancy(occupied, env, rect);
        foreach (RouteNode node in Route)
            StampOccupancy(occupied, env, CellsOf(new Rect(node.FeetPosition.x - .29f, node.FeetPosition.y + .035f, .58f, .95f)));
        foreach (SecretCache cache in SecretCaches)
        {
            StampOccupancy(occupied, env, CellsOf(CacheExpanded(cache.ChamberBounds, .4f)));
            for (int i = 0; i < cache.ReturnPath.Count; i++)
                StampOccupancy(occupied, env, CellsOf(CacheBody(cache.ReturnPath[i])));
            for (int i = 1; i < cache.ReturnPath.Count; i++)
            {
                Vector2 a = cache.ReturnPath[i - 1], b = cache.ReturnPath[i];
                StampOccupancy(occupied, env, CellsOf(CacheSweptBody(a, b)));
                if (Mathf.Abs(a.y - b.y) >= .015f)
                    StampCacheJumpOccupancy(occupied, env, a.y < b.y ? a : b, a.y < b.y ? b : a);
            }
        }
        foreach (MacroStage stage in MacroStages)
        foreach (int wallX in new[] { stage.Bounds.xMin - 1, stage.Bounds.xMax })
            StampOccupancy(occupied, env, new RectInt(wallX, stage.BaseY + 2, 1, 6));
        return occupied;
    }

    private void StampCacheJumpOccupancy(bool[,] occupied, RectInt env, Vector2 low, Vector2 high)
    {
        if (high.y - low.y > SafeJumpHeight + .01f || Mathf.Abs(high.x - low.x) > SafeJumpDistance * .75f) return;
        float duration = (jumpSpeed + Mathf.Sqrt(Mathf.Max(0f, jumpSpeed * jumpSpeed - 2f * gravity * (high.y - low.y)))) / gravity;
        for (float t = 0f; t <= duration; t += fixedDeltaTime)
        {
            float height = jumpSpeed * t - .5f * gravity * t * (t + fixedDeltaTime);
            float x = Mathf.Lerp(low.x, high.x, Mathf.Clamp01(t / (duration * .7f)));
            StampOccupancy(occupied, env, CellsOf(CacheBody(new Vector2(x,
                low.y + Mathf.Max(height, t > duration * .5f ? high.y - low.y : 0f)))));
        }
    }

    private static void StampOccupancy(bool[,] grid, RectInt env, RectInt rect)
    {
        int x0 = Mathf.Max(rect.xMin, env.xMin);
        int y0 = Mathf.Max(rect.yMin, env.yMin);
        int x1 = Mathf.Min(rect.xMax, env.xMax);
        int y1 = Mathf.Min(rect.yMax, env.yMax);
        for (int y = y0; y < y1; y++)
        for (int x = x0; x < x1; x++)
            grid[x - env.xMin, y - env.yMin] = true;
    }

    private static RectInt CellsOf(Rect area)
    {
        int x = Mathf.FloorToInt(area.xMin);
        int y = Mathf.FloorToInt(area.yMin);
        return new RectInt(x, y, Mathf.Max(1, Mathf.CeilToInt(area.xMax) - x), Mathf.Max(1, Mathf.CeilToInt(area.yMax) - y));
    }

    private void PlanMacroGraph()
    {
        var macroRandom = new System.Random(unchecked(Seed * 486187739 + Chapter * 16777619 + 93647));
        int direction = Chapter == 2 ? -1 : 1;
        int x = 0, baseY = 0;
        for (int slot = 0; slot < BandCount; slot++)
        {
            int width = Chapter == 0 ? 62 : Chapter == 3 ? CrownRight[slot] - CrownLeft[slot] : 26;
            if (slot > 0)
            {
                MacroStage previous = MacroStages[slot - 1];
                bool forcedRise = Chapter == 3 && (slot == 1 || slot == 5);
                bool flat = !forcedRise && (Chapter == 3 ? slot == 4 : slot % 4 == 1);
                bool drop = Chapter < 3 && slot == (Chapter == 0 ? 3 : 6 + (ScenarioVariant & 1));
                MacroLinkKind kind = drop ? MacroLinkKind.Drop : flat ? MacroLinkKind.Flat : MacroLinkKind.Rise;
                int rise = kind == MacroLinkKind.Drop ? -8 : kind == MacroLinkKind.Flat ? 0 :
                    Chapter == 3 ? 16 : macroRandom.Next(2) == 0 ? 16 : 24;
                int plannedDrop = Chapter == 0 ? 3 : 6 + (ScenarioVariant & 1);
                bool beforeDrop = Chapter < 3 && slot == plannedDrop - 1;
                direction = kind != MacroLinkKind.Rise || forcedRise || beforeDrop ? previous.Direction :
                    macroRandom.Next(3) == 0 ? previous.Direction : -previous.Direction;
                baseY = previous.BaseY + rise;
                x = PlaceStageX(previous, width, direction, kind);
                RectInt candidate = new RectInt(x, baseY + 1, width, BandHeight - 1);
                MacroStage next = MakeStage(slot, candidate, direction);
                MacroLink link = MakeLink(previous, next, kind);
                if (!StageSpaceIsFree(candidate) || !LinkSpaceIsFree(link))
                {
                    int skyline = 0;
                    foreach (MacroStage stage in MacroStages) skyline = Mathf.Max(skyline, stage.Bounds.yMax);
                    baseY = skyline + 8;
                    kind = MacroLinkKind.Rise;
                    x = PlaceStageX(previous, width, direction, kind);
                    candidate = new RectInt(x, baseY + 1, width, BandHeight - 1);
                    next = MakeStage(slot, candidate, direction);
                    link = MakeLink(previous, next, kind);
                    if (!StageSpaceIsFree(candidate) || !LinkSpaceIsFree(link))
                        throw new InvalidOperationException("Macro skyline reservation failed at slot " + slot);
                }
                MacroStages.Add(next);
                MacroLinks.Add(link);
            }
            else MacroStages.Add(MakeStage(slot, new RectInt(x, baseY + 1, width, BandHeight - 1), direction));
        }

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (MacroStage stage in MacroStages) Accumulate(stage.Bounds, ref minX, ref minY, ref maxX, ref maxY);
        foreach (MacroLink link in MacroLinks) Accumulate(link.Bounds, ref minX, ref minY, ref maxX, ref maxY);
        Vector2Int shift = new Vector2Int(24 - minX, 24 - minY);
        foreach (MacroStage stage in MacroStages)
        {
            stage.Bounds = ShiftRect(stage.Bounds, shift);
            stage.Entrance += (Vector2)shift;
            stage.Exit = new Window(stage.Exit.Side, stage.Exit.Coordinate + shift.x,
                stage.Exit.Minimum + shift.y, stage.Exit.Maximum + shift.y);
            IncludeWorldFootprint(new RectInt(stage.Bounds.xMin - 1, stage.Bounds.yMin - 1,
                stage.Bounds.width + 2, stage.Bounds.height + 2));
        }
        foreach (MacroLink link in MacroLinks)
        {
            link.Bounds = ShiftRect(link.Bounds, shift);
            link.ShaftX += shift.x;
            IncludeWorldFootprint(link.Bounds);
        }
        Bounds = new RectInt(0, 0, maxX - minX + 48, maxY - minY + 48);
        initialMacroFootprintCount = WorldFootprint.Count;
    }

    private static MacroStage MakeStage(int slot, RectInt bounds, int direction) => new MacroStage
    {
        Slot = slot, Bounds = bounds, Direction = direction,
        Entrance = new Vector2(direction > 0 ? bounds.xMin + 2.5f : bounds.xMax - 2.5f, bounds.yMin + 3f),
        Exit = new Window(direction > 0 ? Side.Right : Side.Left,
            direction > 0 ? bounds.xMax : bounds.xMin, bounds.yMin + 3f, bounds.yMin + 6f)
    };

    private static int PlaceStageX(MacroStage previous, int width, int direction, MacroLinkKind kind)
    {
        if (direction != previous.Direction)
            return previous.Direction > 0 ? previous.Bounds.xMax - width : previous.Bounds.xMin;
        int gap = kind == MacroLinkKind.Flat ? 8 : 14;
        return direction > 0 ? previous.Bounds.xMax + gap : previous.Bounds.xMin - gap - width;
    }

    private bool StageSpaceIsFree(RectInt candidate)
    {
        Rect expanded = new Rect(candidate.xMin - 1, candidate.yMin - 1, candidate.width + 2, candidate.height + 2);
        foreach (MacroStage stage in MacroStages)
            if (expanded.Overlaps(new Rect(stage.Bounds.x, stage.Bounds.y, stage.Bounds.width, stage.Bounds.height))) return false;
        foreach (MacroLink link in MacroLinks)
            if (expanded.Overlaps(new Rect(link.Bounds.x, link.Bounds.y, link.Bounds.width, link.Bounds.height))) return false;
        return true;
    }

    private bool LinkSpaceIsFree(MacroLink candidate)
    {
        foreach (MacroStage stage in MacroStages)
            if (stage.Slot != candidate.FromSlot && candidate.Bounds.Overlaps(stage.Bounds)) return false;
        foreach (MacroLink link in MacroLinks)
            if (candidate.Bounds.Overlaps(link.Bounds)) return false;
        return true;
    }

    private static MacroLink MakeLink(MacroStage from, MacroStage to, MacroLinkKind kind)
    {
        int fromEdge = from.Direction > 0 ? from.Bounds.xMax : from.Bounds.xMin;
        int toEdge = to.Direction > 0 ? to.Bounds.xMin : to.Bounds.xMax;
        float shaftX = fromEdge + from.Direction * 7f;
        int left = Mathf.Min(fromEdge, toEdge), right = Mathf.Max(fromEdge, toEdge);
        left = Mathf.Min(left, Mathf.FloorToInt(shaftX - 5f));
        right = Mathf.Max(right, Mathf.CeilToInt(shaftX + 5f));
        int bottom = Mathf.Min(from.BaseY, to.BaseY) + 1;
        int top = Mathf.Max(from.BaseY + 9, to.BaseY + 9);
        return new MacroLink { FromSlot = from.Slot, ToSlot = to.Slot, Kind = kind,
            Bounds = new RectInt(left, bottom, right - left, top - bottom), ShaftX = shaftX, Exposed = true };
    }

    private static RectInt ShiftRect(RectInt rect, Vector2Int delta) =>
        new RectInt(rect.x + delta.x, rect.y + delta.y, rect.width, rect.height);
    private static void Accumulate(RectInt rect, ref int minX, ref int minY, ref int maxX, ref int maxY)
    { minX = Mathf.Min(minX, rect.xMin); minY = Mathf.Min(minY, rect.yMin); maxX = Mathf.Max(maxX, rect.xMax); maxY = Mathf.Max(maxY, rect.yMax); }

    private void BuildMacroGraph(Region root)
    {
        for (int slot = 0; slot < MacroStages.Count; slot++)
        {
            MacroStage stage = MacroStages[slot];
            PlannedChambers++;
            Region room = AddRegion(stage.Bounds, root.Id, 1, slot, stage.Entrance, stage.Exit);
            if (Chapter == 0) FillRegion(room, stage.Direction, slot, 0);
            else
            {
                room.Strategy = WingStrategy(slot);
                CompatibilityChecks++;
                if (!TryFill(room.Strategy, room.Bounds, room.Entrance, room.Exit))
                    throw new InvalidOperationException("Macro chamber has no compatible fill: " + slot);
                FillLeaf(room, stage.Direction);
            }
            AddMacroShell(stage);
            if (slot < MacroLinks.Count) FillMacroLink(root, MacroLinks[slot]);
        }
    }

    private void AddMacroShell(MacroStage stage)
    {
        int floor = stage.BaseY + 1, top = stage.Bounds.yMax;
        Solids.Add(new RectInt(stage.Bounds.xMin - 1, floor - 1, stage.Bounds.width + 2, 1));
        Solids.Add(new RectInt(stage.Bounds.xMin - 1, top, stage.Bounds.width + 2, 1));
        foreach (int wallX in new[] { stage.Bounds.xMin - 1, stage.Bounds.xMax })
        {
            Solids.Add(new RectInt(wallX, floor, 1, 1));
            Solids.Add(new RectInt(wallX, stage.BaseY + 8, 1, top - stage.BaseY - 8));
        }
    }

    private void FillMacroLink(Region root, MacroLink link)
    {
        MacroStage from = MacroStages[link.FromSlot], to = MacroStages[link.ToSlot];
        Region room = AddRegion(link.Bounds, root.Id, 1, from.Slot, Route[Route.Count - 1].FeetPosition,
            new Window(to.Direction > 0 ? Side.Right : Side.Left, to.Entrance.x, to.Entrance.y, to.Entrance.y + 3));
        link.RoomIndex = room.Id;
        room.IsLeaf = room.IsShaft = true;
        room.Strategy = StrategyKind.Grid;
        room.ScenarioId = "macro." + Chapter + "." + link.FromSlot + "." + link.Kind.ToString().ToLowerInvariant();
        room.FirstRouteNode = Route.Count;
        if (link.Kind == MacroLinkKind.Rise)
        {
            Vector2 staging = new Vector2(link.ShaftX, from.BaseY + 5f);
            ConnectOrdinary(room, staging, 3.2f);
            bool rings = Chapter == 3 && (from.Slot == 0 || from.Slot == 4);
            bool doubles = Chapter >= 2;
            if (rings)
            {
                room.ScenarioId = "crown.three-ring-relay-" + from.Slot;
                int takeoff = Route.Count - 1;
                for (int i = 0; i < 3; i++)
                    AddSpawn(SpawnKind.Anchor, new Vector2(link.ShaftX + (i % 2 == 0 ? -.4f : .4f), from.BaseY + 7.6f + i * 3.4f),
                        new Vector2(.9f, .9f), takeoff, value: i, text: "RMB: grab. Space: leap. Release RMB for the next ring.");
                AddRoutePlatform(new Vector2(link.ShaftX, from.BaseY + 15.8f), 3f, room.Id, TraversalAction.RingRelay, PirateUpgrade.Hook2);
                AddRoutePlatform(new Vector2(link.ShaftX, from.BaseY + 19.6f), 3f, room.Id, TraversalAction.DoubleJump, PirateUpgrade.DoubleJump);
            }
            else if (doubles)
            {
                int count = Mathf.Max(1, Mathf.FloorToInt((to.Entrance.y + 2.2f - staging.y) / 3.8f));
                for (int i = 1; i <= count; i++)
                    AddRoutePlatform(staging + Vector2.up * (i * 3.8f), 3f, room.Id, TraversalAction.DoubleJump, PirateUpgrade.DoubleJump);
            }
            else
            {
                int count = Mathf.Max(1, Mathf.CeilToInt((to.Entrance.y - staging.y) / 2f));
                float amplitude = Mathf.Min(1.5f, OrdinaryStride * .42f);
                for (int i = 1; i <= count; i++)
                    AddRoutePlatform(staging + new Vector2((i % 2 == 0 ? -1f : 1f) * amplitude, i * 2f),
                        3.2f, room.Id, TraversalAction.Jump);
            }
        }
        ConnectOrdinary(room, to.Entrance, 4.8f);
        room.LastRouteNode = Route.Count - 1;
        RegisterScenario(room);
    }

    public string MacroTopologySignature()
    {
        var text = new StringBuilder();
        foreach (MacroStage stage in MacroStages)
        { text.Append(stage.Slot).Append(':').Append(stage.Direction).Append(':'); AppendRect(text, stage.Bounds); }
        foreach (MacroLink link in MacroLinks)
        { text.Append((int)link.Kind).Append(':').Append(link.FromSlot).Append('>').Append(link.ToSlot).Append(';'); AppendRect(text, link.Bounds); AppendVector(text, new Vector2(link.ShaftX, link.Exposed ? 1f : 0f)); }
        return text.ToString();
    }

    private void ValidateMacroGraph()
    {
        if (MacroStages.Count != BandCount || MacroLinks.Count != BandCount - 1)
            ValidationErrors.Add("Macro graph lost its complete stage/link chain.");
        for (int index = 0; index < MacroStages.Count; index++)
        {
            MacroStage stage = MacroStages[index];
            if (stage.Slot != index || stage.Bounds.width < 24 || stage.Bounds.height != BandHeight - 1)
                ValidationErrors.Add("Macro stage metadata is invalid: " + index);
            for (int other = index + 1; other < MacroStages.Count; other++)
                if (stage.Bounds.Overlaps(MacroStages[other].Bounds)) ValidationErrors.Add("Macro chambers overlap: " + index + "/" + other);
            if (!ContainsWorldCell(stage.Bounds.xMin, stage.Bounds.yMin) || !ContainsWorldCell(stage.Bounds.xMax - 1, stage.Bounds.yMax - 1))
                ValidationErrors.Add("Macro chamber lost its rendered footprint: " + index);
        }
        bool continued = false, flat = false, drop = false, exposed = false;
        for (int index = 0; index < MacroLinks.Count; index++)
        {
            MacroLink link = MacroLinks[index];
            if (link.FromSlot != index || link.ToSlot != index + 1 || link.RoomIndex < 0 || link.RoomIndex >= Rooms.Count)
            { ValidationErrors.Add("Macro link does not join consecutive reachable stages: " + index); continue; }
            MacroStage from = MacroStages[index], to = MacroStages[index + 1];
            Region connector = Rooms[link.RoomIndex];
            continued |= from.Direction == to.Direction;
            flat |= link.Kind == MacroLinkKind.Flat && from.BaseY == to.BaseY && from.Direction == to.Direction;
            drop |= link.Kind == MacroLinkKind.Drop && to.BaseY < from.BaseY;
            exposed |= link.Exposed;
            if (Chapter == 3 && to.BaseY < from.BaseY)
                ValidationErrors.Add("Crown macro link descends into an earlier tide altitude.");
            if (connector.FirstRouteNode < 0 || connector.LastRouteNode < connector.FirstRouteNode || connector.LastRouteNode >= Route.Count ||
                Vector2.Distance(Route[connector.LastRouteNode].FeetPosition, to.Entrance) > .015f)
                ValidationErrors.Add("Macro connector has no exact arrival witness: " + index);
            for (int other = 0; other < MacroStages.Count; other++)
                if (other != index && other != index + 1 && link.Bounds.Overlaps(MacroStages[other].Bounds))
                    ValidationErrors.Add("Macro connector crosses an unrelated chamber: " + index + "/" + other);
            for (int other = index + 1; other < MacroLinks.Count; other++)
                if (link.Bounds.Overlaps(MacroLinks[other].Bounds))
                    ValidationErrors.Add("Macro connectors intersect: " + index + "/" + other);
        }
        if (!continued || !flat || !exposed) ValidationErrors.Add("Macro graph regressed to obligatory zig-zag or lost its open fall links.");
        if (Chapter < 3 && !drop) ValidationErrors.Add("Macro exploration chapter lost its genuine downward link.");
    }

    private static string RunMacroGraphSelfTests()
    {
        CampaignLayout broken = Create(42, 2, 14f, 34.335f, .02f, 7.3f);
        string signature = broken.Signature();
        broken.MacroLinks[0].ToSlot = 3;
        if (broken.Validate(out _) || !broken.ValidationErrors.Exists(error => error.StartsWith("Macro link does not join")) || signature == broken.Signature())
            throw new InvalidOperationException("Macro disconnected-link corruption was not rejected.");
        broken = Create(42, 3, 14f, 34.335f, .02f, 8f);
        broken.WorldFootprint.Clear();
        if (broken.Validate(out _) || !broken.ValidationErrors.Exists(error => error.StartsWith("Macro chamber lost")))
            throw new InvalidOperationException("Macro missing-footprint corruption was not rejected.");
        broken = Create(42, 1, 14f, 34.335f, .02f, 6.6f);
        broken.MacroLinks[0].Bounds = broken.MacroStages[3].Bounds;
        if (broken.Validate(out _) || !broken.ValidationErrors.Exists(error => error.StartsWith("Macro connector crosses")))
            throw new InvalidOperationException("Macro cross-chamber connector corruption was not rejected.");
        broken = Create(42, 0, 14f, 34.335f, .02f, 6f);
        foreach (MacroLink link in broken.MacroLinks) if (link.Kind == MacroLinkKind.Drop) link.Kind = MacroLinkKind.Rise;
        if (broken.Validate(out _) || !broken.ValidationErrors.Exists(error => error.StartsWith("Macro exploration chapter lost")))
            throw new InvalidOperationException("Macro missing-descent corruption was not rejected.");
        return "macroDisconnectedRejected=True, macroMissingFootprintRejected=True, macroCrossingRejected=True, macroMissingDescentRejected=True";
    }
}
