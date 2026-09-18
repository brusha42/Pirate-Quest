using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class CampaignLayout
{
    private void AddSecretCaches()
    {
        Region[] ordinary = Rooms.Where(r => r.IsLeaf && !r.IsShaft &&
            (r.Strategy == StrategyKind.Pyramid || r.Strategy == StrategyKind.Grid || r.Strategy == StrategyKind.DarkGallery))
            .OrderBy(r => r.FirstRouteNode).ToArray();
        var occupied = new HashSet<int>();
        foreach (Region room in ordinary.Where(r => r.Strategy == StrategyKind.Pyramid))
            if (SecretCaches.Count < 2 && TryStairCache(room)) occupied.Add(room.Id);
        foreach (Region room in ordinary)
            if (SecretCaches.Count < 4 && !occupied.Contains(room.Id) && TrySideCache(room)) occupied.Add(room.Id);
        foreach (Region room in ordinary)
            if (SecretCaches.Count < 4 && !occupied.Contains(room.Id) && TryDropCache(room)) occupied.Add(room.Id);
        foreach (Region room in ordinary)
            if (SecretCaches.Count < MinimumConcealedCaches && TryDropCache(room)) occupied.Add(room.Id);
        AddVisibleCaches(ordinary);
        AddEarlySpikeHazards(ordinary);
    }

    private bool CacheRoomHasGate(Region room) => Route.Skip(room.FirstRouteNode)
        .Take(room.LastRouteNode - room.FirstRouteNode + 1).Any(n => n.RequiredAbility.HasValue || n.SecondaryAbility.HasValue);

    private SecretCache CacheDescription(Region room, int approach, SecretEntranceKind kind,
        Rect chamber, Rect rewards, Rect entrance, Rect cover, params Vector2[] path)
    {
        var cache = new SecretCache { Id = SecretCaches.Count, ApproachNode = approach, Support = Platforms.Count,
            Kind = kind, Concealed = kind != SecretEntranceKind.OpenBranch, Bounds = rewards,
            ChamberBounds = chamber, RewardBounds = rewards, EntranceBounds = entrance, CoverBounds = cover };
        cache.ReturnPath.AddRange(path);
        return cache;
    }

    private static Platform CachePiece(Region room, Rect bounds, bool oneWay = false) =>
        new Platform { Bounds = bounds, OneWay = oneWay, Optional = true, RoomIndex = room.Id };

    private bool TryStairCache(Region room)
    {
        int node = room.FirstRouteNode;
        if (CacheRoomHasGate(room) || node + 3 > room.LastRouteNode) return false;
        Vector2 start = Route[node].FeetPosition;
        Platform first = Platforms[Route[node + 1].PlatformIndex], second = Platforms[Route[node + 2].PlatformIndex],
            third = Platforms[Route[node + 3].PlatformIndex];
        int direction = Route[node + 1].FeetPosition.x > start.x ? 1 : -1;
        float entry = direction > 0 ? first.Bounds.xMin : first.Bounds.xMax;
        float end = direction > 0 ? third.Bounds.xMin : third.Bounds.xMax;
        float floor = start.y - 1.35f, roof = start.y - .08f;
        if (first.OneWay || second.OneWay || third.OneWay || Mathf.Abs(end - entry) < 4.7f ||
            first.SurfaceY < roof + 1f || second.SurfaceY < roof + 1f || floor < RoomFloorY(room) + .5f) return false;
        Rect oldFirst = first.Bounds, oldSecond = second.Bounds;
        first.Bounds = new Rect(oldFirst.x, roof, oldFirst.width, oldFirst.yMax - roof);
        second.Bounds = new Rect(oldSecond.x, roof, oldSecond.width, oldSecond.yMax - roof);
        float returnX = entry - direction * .7f;
        Rect chamber = Rect.MinMaxRect(Mathf.Min(entry, end), floor, Mathf.Max(entry, end), roof);
        Rect rewards = chamber;
        Rect cover = new Rect(chamber.x, floor - .3f, chamber.width, chamber.height + .3f);
        var cache = CacheDescription(room, node, SecretEntranceKind.UnderStairs, chamber, rewards,
            new Rect(entry - .3f, floor, .6f, 1.25f), cover, start, new Vector2(returnX, floor), new Vector2(end - direction * .7f, floor));
        float left = Mathf.Min(returnX - direction * .5f, end), right = Mathf.Max(returnX - direction * .5f, end);
        bool accepted = TryCommitCache(cache, new[] { CachePiece(room, new Rect(left, floor - .18f, right - left, .18f), true) });
        if (!accepted) { first.Bounds = oldFirst; second.Bounds = oldSecond; }
        return accepted;
    }

    private bool TrySideCache(Region room)
    {
        return TrySideCacheAt(room, room.FirstRouteNode) || TrySideCacheAt(room, room.LastRouteNode);
    }

    private bool TrySideCacheAt(Region room, int node)
    {
        if (CacheRoomHasGate(room)) return false;
        Vector2 fork = Route[node].FeetPosition;
        int direction = fork.x < room.Bounds.center.x ? -1 : 1;
        float edge = direction < 0 ? room.Bounds.xMin : room.Bounds.xMax;
        foreach (float drop in new[] { 0f, 1.35f }) foreach (float width in new[] { 10f, 7f })
        {
            float floor = fork.y - drop, height = drop > 0f ? 1.15f : 3.2f;
            if (floor < RoomFloorY(room) + .5f || drop > 0f && !Platforms[Route[node].PlatformIndex].OneWay) continue;
            float near = edge + direction * 2f, far = near + direction * width;
            Rect chamber = Rect.MinMaxRect(Mathf.Min(near, far), floor, Mathf.Max(near, far), floor + height);
            if (Rooms.Any(r => r.IsLeaf && r.Id != room.Id &&
                chamber.Overlaps(new Rect(r.Bounds.x, r.Bounds.y, r.Bounds.width, r.Bounds.height)))) continue;
            Rect rewards = new Rect(chamber.xMin + .4f, floor, chamber.width - .8f, Mathf.Min(1.3f, height));
            Rect cover = new Rect(chamber.xMin - .25f, floor - .25f, chamber.width + .5f, height + .37f);
            var path = new List<Vector2> { fork };
            if (drop > 0f) path.Add(new Vector2(fork.x, floor));
            path.Add(new Vector2(near + direction * .75f, floor)); path.Add(new Vector2(far - direction * .8f, floor));
            var cache = CacheDescription(room, node, SecretEntranceKind.SideSlit, chamber, rewards,
                new Rect(near - .3f, floor, .6f, 1.25f), cover, path.ToArray());
            float left = Mathf.Min(fork.x, near + direction), right = Mathf.Max(fork.x, near + direction);
            var pieces = new List<Platform> {
                CachePiece(room, new Rect(chamber.xMin - .25f, floor - .25f, chamber.width + .5f, .25f)),
                CachePiece(room, new Rect(chamber.xMin - .25f, chamber.yMax, chamber.width + .5f, .12f)),
                CachePiece(room, new Rect(far - .15f, floor, .3f, height)),
                CachePiece(room, new Rect(left - .4f, floor - .18f, right - left + .8f, .18f), true)
            };
            if (height > 1.5f) pieces.Add(CachePiece(room, new Rect(near - .15f, floor + 1.35f, .3f, height - 1.35f)));
            if (TryCommitCache(cache, pieces)) return true;
        }
        return false;
    }

    private bool TryDropCache(Region room)
    {
        if (CacheRoomHasGate(room)) return false;
        for (int node = room.FirstRouteNode; node <= room.LastRouteNode; node++)
        {
            RouteNode source = Route[node];
            if (!Platforms[source.PlatformIndex].OneWay) continue;
            foreach (int direction in new[] { 1, -1 }) foreach (float drop in new[] { 1.35f, 2f })
            {
                Vector2 fork = source.FeetPosition;
                float floor = fork.y - drop, near = fork.x - direction * 1.1f, far = fork.x + direction * 7.2f;
                if (floor < RoomFloorY(room) + .6f) continue;
                Rect chamber = Rect.MinMaxRect(Mathf.Min(near, far), floor, Mathf.Max(near, far), floor + 1.15f);
                if (chamber.xMin < room.Bounds.xMin + .35f || chamber.xMax > room.Bounds.xMax - .35f) continue;
                float rewardNear = fork.x + direction * 1.25f, rewardFar = far - direction * .45f;
                Rect rewards = Rect.MinMaxRect(Mathf.Min(rewardNear, rewardFar), floor, Mathf.Max(rewardNear, rewardFar), chamber.yMax);
                var cache = CacheDescription(room, node, SecretEntranceKind.DropHatch, chamber, rewards,
                    new Rect(fork.x - .75f, floor + .65f, 1.5f, .75f),
                    new Rect(chamber.xMin - .25f, floor - .25f, chamber.width + .5f, 1.5f),
                    fork, new Vector2(fork.x, floor), new Vector2(far - direction * .8f, floor));
                var pieces = new List<Platform> {
                    CachePiece(room, new Rect(chamber.xMin - .25f, floor - .25f, chamber.width + .5f, .25f)),
                    CachePiece(room, new Rect(chamber.xMin - .25f, floor, .25f, 1.15f)),
                    CachePiece(room, new Rect(chamber.xMax, floor, .25f, 1.15f))
                };
                float roofLeft = fork.x - .8f, roofRight = fork.x + .8f;
                if (roofLeft > chamber.xMin) pieces.Add(CachePiece(room, new Rect(chamber.xMin, chamber.yMax, roofLeft - chamber.xMin, .12f)));
                if (roofRight < chamber.xMax) pieces.Add(CachePiece(room, new Rect(roofRight, chamber.yMax, chamber.xMax - roofRight, .12f)));
                if (TryCommitCache(cache, pieces)) return true;
            }
        }
        return false;
    }

    private bool TryCommitCache(SecretCache cache, IEnumerable<Platform> proposed)
    {
        if (SecretCaches.Count >= MaximumSecretCaches || cache.RewardBounds.width < 4.7f ||
            !CacheInsideWorld(CacheExpanded(cache.ChamberBounds, .35f)) ||
            SecretCaches.Any(other => CacheExpanded(other.ChamberBounds, 1f).Overlaps(cache.ChamberBounds)) ||
            !CacheArchitectureClear(CacheInset(cache.ChamberBounds, .03f)) || !CacheHazardsClear(cache.ChamberBounds) ||
            cache.Concealed && Route.Any(node => CacheBody(node.FeetPosition).Overlaps(cache.CoverBounds))) return false;
        Platform[] pieces = proposed.ToArray();
        foreach (Platform piece in pieces)
            if (!CacheInsideWorld(piece.Bounds) || !piece.OneWay && !CacheSolidPreservesRoute(piece.Bounds)) return false;
        int before = Platforms.Count;
        cache.Support = before;
        Platforms.AddRange(pieces);
        bool recoveryClear = Spawns.Where(s => s.Kind == SpawnKind.JumpPad && s.Optional).All(pad => {
            int step = FindRecoveryStep(pad); return step >= 0 && RecoveryFootprintIsClear(pad, Platforms[step].Bounds);
        });
        if (!recoveryClear || !HasSafeCacheReturn(cache) || !CacheRewardsFit(cache)) { Platforms.RemoveRange(before, pieces.Length); return false; }
        SecretCaches.Add(cache);
        IncludeCacheFootprint(CacheExpanded(cache.ChamberBounds, .4f));
        foreach (Platform piece in pieces) IncludeCacheFootprint(piece.Bounds);
        for (int i = 1; i < cache.ReturnPath.Count; i++) IncludeCacheFootprint(CacheSweptBody(cache.ReturnPath[i - 1], cache.ReturnPath[i]));
        return true;
    }

    private void IncludeCacheFootprint(Rect rect) => IncludeWorldFootprint(new RectInt(Mathf.FloorToInt(rect.xMin),
        Mathf.FloorToInt(rect.yMin), Mathf.CeilToInt(rect.xMax) - Mathf.FloorToInt(rect.xMin), Mathf.CeilToInt(rect.yMax) - Mathf.FloorToInt(rect.yMin)));

    private void AddVisibleCaches(IEnumerable<Region> rooms)
    {
        int added = 0;
        foreach (Region room in rooms)
        {
            if (added >= 2 || SecretCaches.Count >= MaximumSecretCaches) break;
            if (CacheRoomHasGate(room)) continue;
            Vector2 fork = Route[room.FirstRouteNode].FeetPosition;
            int direction = fork.x < room.Bounds.center.x ? -1 : 1;
            float far = fork.x + direction * 10f;
            Rect chamber = Rect.MinMaxRect(Mathf.Min(fork.x + direction * 3f, far), fork.y,
                Mathf.Max(fork.x + direction * 3f, far), fork.y + 1.3f);
            if (DistanceToRoute(new Vector2(far, fork.y)) < 2f) continue;
            var cache = CacheDescription(room, room.FirstRouteNode, SecretEntranceKind.OpenBranch, chamber, chamber, chamber,
                default, fork, new Vector2(far - direction * .7f, fork.y));
            float left = Mathf.Min(fork.x, far) - .5f, right = Mathf.Max(fork.x, far) + .5f;
            if (TryCommitCache(cache, new[] { CachePiece(room, new Rect(left, fork.y - .18f, right - left, .18f), true) })) added++;
        }
    }

    public bool HasSafeCacheReturn(SecretCache cache)
    {
        if (cache == null || cache.ApproachNode < 0 || cache.ApproachNode >= Route.Count || cache.Support < 0 ||
            cache.Support >= Platforms.Count || cache.ReturnPath.Count < 2 ||
            Vector2.Distance(cache.ReturnPath[0], Route[cache.ApproachNode].FeetPosition) > .01f) return false;
        foreach (Vector2 point in cache.ReturnPath)
            if (!CacheArchitectureClear(CacheBody(point)) || !CacheHazardsClear(CacheBody(point)) || !CacheSupported(point)) return false;
        for (int i = 1; i < cache.ReturnPath.Count; i++)
        {
            Vector2 a = cache.ReturnPath[i - 1], b = cache.ReturnPath[i];
            if (Mathf.Abs(a.y - b.y) < .015f)
            {
                int count = Mathf.CeilToInt(Mathf.Abs(a.x - b.x) / .12f);
                for (int j = 0; j <= count; j++)
                {
                    Vector2 feet = Vector2.Lerp(a, b, count == 0 ? 0f : j / (float)count);
                    if (!CacheSupported(feet) || !CacheArchitectureClear(CacheBody(feet)) || !CacheHazardsClear(CacheBody(feet))) return false;
                }
            }
            else if (!CacheJumpClear(a.y < b.y ? a : b, a.y < b.y ? b : a)) return false;
        }
        return true;
    }

    private bool CacheJumpClear(Vector2 low, Vector2 high)
    {
        if (high.y - low.y > SafeJumpHeight + .01f || Mathf.Abs(high.x - low.x) > SafeJumpDistance * .75f) return false;
        float duration = (jumpSpeed + Mathf.Sqrt(Mathf.Max(0f, jumpSpeed * jumpSpeed - 2f * gravity * (high.y - low.y)))) / gravity;
        for (float t = 0f; t <= duration; t += fixedDeltaTime)
        {
            float height = jumpSpeed * t - .5f * gravity * t * (t + fixedDeltaTime);
            float x = Mathf.Lerp(low.x, high.x, Mathf.Clamp01(t / (duration * .7f)));
            Rect body = CacheBody(new Vector2(x, low.y + Mathf.Max(height, t > duration * .5f ? high.y - low.y : 0f)));
            if (!CacheArchitectureClear(body) || !CacheHazardsClear(body)) return false;
        }
        return true;
    }

    private bool CacheSupported(Vector2 feet) => Platforms.Any(p => Mathf.Abs(p.SurfaceY - feet.y) < .025f &&
        feet.x >= p.Bounds.xMin + .29f && feet.x <= p.Bounds.xMax - .29f) || Solids.Any(s =>
        Mathf.Abs(s.yMax - feet.y) < .025f && feet.x >= s.xMin + .29f && feet.x <= s.xMax - .29f);

    private bool CacheRewardsFit(SecretCache cache)
    {
        for (int slot = 0; slot < 6; slot++)
        {
            float x = Mathf.Lerp(cache.RewardBounds.xMin + PirateTreasureLayout.CacheRewardInset,
                cache.RewardBounds.xMax - PirateTreasureLayout.CacheRewardInset, slot / 5f);
            if (!PirateTreasureLayout.Clear(this, new Vector2(x, cache.RewardBounds.yMin + .72f), .3f)) return false;
        }
        return true;
    }

    private bool CacheSolidPreservesRoute(Rect solid)
    {
        foreach (RouteNode node in Route) if (solid.Overlaps(CacheBody(node.FeetPosition))) return false;
        for (int i = 1; i < Route.Count; i++)
        {
            RouteNode to = Route[i]; Vector2 a = Route[i - 1].FeetPosition, b = to.FeetPosition;
            if (to.Action != TraversalAction.Jump && to.Action != TraversalAction.Walk &&
                to.Action != TraversalAction.DoubleJump && to.Action != TraversalAction.DoubleWindow &&
                to.Action != TraversalAction.SpringDrop) continue;
            float height = to.Action == TraversalAction.Walk ? 0f : SafeJumpHeight / .78f;
            Rect corridor = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - .32f, Mathf.Min(a.y, b.y) + .035f,
                Mathf.Max(a.x, b.x) + .32f, Mathf.Max(a.y, b.y) + height + 1.06f);
            if (solid.Overlaps(corridor)) return false;
        }
        return true;
    }

    private bool CacheArchitectureClear(Rect area) => !Solids.Any(s => area.Overlaps(new Rect(s.x, s.y, s.width, s.height))) &&
        !Platforms.Any(p => !p.OneWay && area.Overlaps(p.Bounds));

    private bool CacheHazardsClear(Rect area)
    {
        foreach (Spawn spawn in Spawns)
        {
            if (spawn.Kind == SpawnKind.DarkZone || spawn.Kind == SpawnKind.Decoration || spawn.Kind == SpawnKind.Hint ||
                spawn.Kind == SpawnKind.Anchor || spawn.Kind == SpawnKind.Chain || spawn.Kind == SpawnKind.Exit ||
                spawn.Kind == SpawnKind.Checkpoint || spawn.Kind == SpawnKind.Upgrade) continue;
            Rect occupied = new Rect(spawn.Position - spawn.Size * .5f, spawn.Size);
            if (spawn.Kind == SpawnKind.Crawler && spawn.Value >= 0 && spawn.Value < Platforms.Count)
            { Rect patrol = Platforms[(int)spawn.Value].Bounds; occupied = new Rect(patrol.x, patrol.yMax, patrol.width, 1.5f); }
            if (CacheExpanded(occupied, .3f).Overlaps(area)) return false;
        }
        return true;
    }

    private bool CacheInsideWorld(Rect area) => area.xMin >= Bounds.xMin && area.xMax <= Bounds.xMax &&
        area.yMin >= Bounds.yMin && area.yMax <= Bounds.yMax;
    private static Rect CacheBody(Vector2 feet) => new Rect(feet.x - .29f, feet.y + .035f, .58f, 1.04f);
    private static Rect CacheSweptBody(Vector2 a, Vector2 b) => Rect.MinMaxRect(Mathf.Min(a.x, b.x) - .32f,
        Mathf.Min(a.y, b.y) - .25f, Mathf.Max(a.x, b.x) + .32f, Mathf.Max(a.y, b.y) + 1.1f);
    private static Rect CacheExpanded(Rect area, float amount) => new Rect(area.min - Vector2.one * amount, area.size + Vector2.one * amount * 2f);
    private static Rect CacheInset(Rect area, float amount) => CacheExpanded(area, -amount);

    private void AddEarlySpikeHazards(IEnumerable<Region> ordinary)
    {
        if (Chapter > 1) return;
        int ability = Spawns.Where(s => s.Kind == SpawnKind.Upgrade &&
            (s.Ability == PirateUpgrade.SpringLeg || s.Ability == PirateUpgrade.Saber2))
            .Select(s => s.NodeIndex).DefaultIfEmpty(Route.Count).Min();
        int added = 0;
        foreach (Region room in ordinary)
        {
            if (added >= 6) break;
            if (room.FirstRouteNode >= ability || room.Strategy == StrategyKind.Pyramid || CacheRoomHasGate(room)) continue;
            for (int node = room.FirstRouteNode + 1; node <= room.LastRouteNode && node < ability && added < 6; node++)
            {
                Vector2 a = Route[node - 1].FeetPosition, b = Route[node].FeetPosition;
                if (Route[node].Action != TraversalAction.Jump) continue;
                float floor = RoomFloorY(room), x = (a.x + b.x) * .5f;
                Rect trap = new Rect(x - .65f, floor + .01f, 1.3f, .35f);
                if (Mathf.Min(a.y, b.y) < floor + 1.6f || !EarlyTrapClear(trap)) continue;
                AddSpawn(SpawnKind.Spikes, trap.center, trap.size, node, optional: true, text: "early-pit"); added++;
            }
        }
    }

    private bool EarlyTrapClear(Rect trap)
    {
        if (!CacheArchitectureClear(trap) || !CacheHazardsClear(CacheExpanded(trap, .65f)) ||
            CacheExpanded(trap, 5f).Contains(Start) || Route.Any(n => CacheExpanded(trap, 1.1f).Contains(n.FeetPosition)) ||
            SecretCaches.Any(c => CacheExpanded(c.ChamberBounds, .8f).Overlaps(trap) || c.ReturnPath.Any(p => CacheExpanded(trap, 2f).Contains(p)))) return false;
        if (Spawns.Any(s => (s.Kind == SpawnKind.JumpPad || s.Kind == SpawnKind.Checkpoint || s.Kind == SpawnKind.Upgrade) &&
            Mathf.Abs(s.Position.x - trap.center.x) < 2.8f && Mathf.Abs(s.Position.y - trap.center.y) < 6f)) return false;
        return Solids.Any(s => Mathf.Abs(s.yMax - (trap.yMin - .01f)) < .025f && trap.xMin >= s.xMin && trap.xMax <= s.xMax);
    }

    private void ValidateSecretCaches()
    {
        int concealed = SecretCaches.Count(c => c.Concealed);
        if (concealed < MinimumConcealedCaches || concealed > MaximumConcealedCaches || SecretCaches.Count > MaximumSecretCaches)
            ValidationErrors.Add($"Secret chamber budget invalid: concealed={concealed}, total={SecretCaches.Count}.");
        foreach (SecretCache cache in SecretCaches)
            if (!HasSafeCacheReturn(cache) || !CacheRewardsFit(cache)) ValidationErrors.Add($"Cache {cache.Id} has no supported safe reward/return certificate.");
        if (Chapter < 2 && !Spawns.Any(s => s.Kind == SpawnKind.Spikes && s.Text == "early-pit"))
            ValidationErrors.Add("No early lethal pit before spring/slide progression.");
    }
}
