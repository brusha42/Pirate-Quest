using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class PirateTreasureLayout
{
    public const float MinimumRelicDetour = 1.25f;
    public const float MinimumRelicRouteSeparation = .75f;
    public const float CacheRewardInset = .6f;
    public sealed class Item
    {
        public string Key;
        public PirateTreasureKind Kind;
        public Vector2 Position;
        public int RouteNode;
        public int Support;
        public bool Optional;
        public int CacheId = -1;
    }

    public static List<Item> Create(CampaignLayout layout)
    {
        var result = new List<Item>();
        string signature = layout.Signature();
        var usedSupports = new HashSet<int>();
        for (int i = 4; i < layout.Route.Count - 2; i++)
        {
            CampaignLayout.RouteNode node = layout.Route[i];
            bool special = IsGemLanding(node.Action);
            if (!special && i % 6 != (layout.Chapter % 3)) continue;
            if (node.PlatformIndex < 0 || node.PlatformIndex >= layout.Platforms.Count || usedSupports.Contains(node.PlatformIndex)) continue;
            Vector2 position = node.FeetPosition + Vector2.up * .67f;
            if (!Clear(layout, position, .8f)) continue;
            PirateTreasureKind kind = special ? PirateTreasureKind.Gem : PirateTreasureKind.Doubloon;
            result.Add(Make(layout, signature, i, kind, position, node.PlatformIndex, false));
            usedSupports.Add(node.PlatformIndex);
        }
        var candidates = new List<Item>();
        for (int p = 0; p < layout.Platforms.Count; p++)
        {
            CampaignLayout.Platform platform = layout.Platforms[p];
            if (platform.Bounds.width < 2f || platform.Bounds.width > 12f) continue;
            foreach (float x in new[] { platform.Bounds.xMin + .65f, platform.Bounds.xMax - .65f, platform.CenterX })
            {
                Vector2 position = new Vector2(x, platform.SurfaceY + .72f);
                if (!Clear(layout, position, .8f) || DistanceToRoute(layout, position - Vector2.up * .72f) < MinimumRelicRouteSeparation ||
                    result.Any(item => Vector2.Distance(item.Position, position) < 1.2f)) continue;
                int approach = -1;
                float shortest = float.PositiveInfinity;
                for (int i = 0; i < layout.Route.Count; i++)
                {
                    float distance = Mathf.Abs(layout.Route[i].FeetPosition.x - x);
                    if (distance < MinimumRelicDetour || distance > 6f || distance >= shortest || !HasSafeWalkReturn(layout, i, p, position)) continue;
                    shortest = distance; approach = i;
                }
                if (approach >= 0)
                    candidates.Add(Make(layout, signature, approach, PirateTreasureKind.Relic, position, p, true, layout.Route.Count + p));
            }
        }
        var relicRooms = new HashSet<int>();
        foreach (Item candidate in candidates.OrderBy(item => layout.Platforms[item.Support].Optional ? 0 : 1)
            .ThenByDescending(item => Mathf.Abs(item.Position.x-layout.Route[item.RouteNode].FeetPosition.x)).ThenBy(item => item.Support))
        {
            int room = layout.Platforms[candidate.Support].RoomIndex;
            if (relicRooms.Contains(room) || result.Any(item => item.Key == candidate.Key || Vector2.Distance(item.Position, candidate.Position) < 1.2f)) continue;
            result.Add(candidate); relicRooms.Add(room);
            if (relicRooms.Count == 3) break;
        }
        foreach (CampaignLayout.SecretCache cache in layout.SecretCaches)
        {
            for (int slot = 0; slot < 6; slot++)
            {
                bool rewardOnRight = cache.ReturnPath[cache.ReturnPath.Count - 1].x > cache.ReturnPath[0].x;
                float firstX = rewardOnRight ? cache.RewardBounds.xMin + CacheRewardInset : cache.RewardBounds.xMax - CacheRewardInset;
                float lastX = rewardOnRight ? cache.RewardBounds.xMax - CacheRewardInset : cache.RewardBounds.xMin + CacheRewardInset;
                Vector2 position = new Vector2(Mathf.Lerp(firstX, lastX, slot / 5f),
                    layout.Platforms[cache.Support].SurfaceY + .72f);
                if (!Clear(layout, position, .3f)) continue;
                result.RemoveAll(item => Vector2.Distance(item.Position, position) < .7f);
                PirateTreasureKind kind = slot == 5 ? PirateTreasureKind.Relic : slot == 4 ? PirateTreasureKind.Gem : PirateTreasureKind.Doubloon;
                Item item = Make(layout, signature, cache.ApproachNode, kind, position, cache.Support, true, 6000 + cache.Id * 10 + slot);
                item.CacheId = cache.Id;
                result.Add(item);
            }
        }
        return result;
    }

    public static bool IsGemLanding(CampaignLayout.TraversalAction action) =>
        action == CampaignLayout.TraversalAction.Chain || action == CampaignLayout.TraversalAction.Saber ||
        action == CampaignLayout.TraversalAction.Spring || action == CampaignLayout.TraversalAction.DoubleJump ||
        action == CampaignLayout.TraversalAction.Grapple || action == CampaignLayout.TraversalAction.SaberSlide ||
        action == CampaignLayout.TraversalAction.SpringDouble || action == CampaignLayout.TraversalAction.SpringGrapple ||
        action == CampaignLayout.TraversalAction.RingRelay || action == CampaignLayout.TraversalAction.SpringDrop ||
        action == CampaignLayout.TraversalAction.DoubleWindow;

    private static Item Make(CampaignLayout layout, string signature, int slot, PirateTreasureKind kind,
        Vector2 position, int support, bool optional, int identitySlot = -1) => new Item {
        Key = $"t1:{layout.Chapter}:{signature}:{(identitySlot >= 0 ? identitySlot : slot)}:{(int)kind}", Kind = kind,
        Position = position, RouteNode = slot, Support = support, Optional = optional
    };

    public static bool Clear(CampaignLayout layout, Vector2 point, float margin)
    {
        Rect body = new Rect(point - Vector2.one * .35f, Vector2.one * .7f);
        if (body.xMin < layout.Bounds.xMin || body.xMax > layout.Bounds.xMax || body.yMin < layout.Bounds.yMin || body.yMax > layout.Bounds.yMax) return false;
        if (layout.Solids.Any(solid => body.Overlaps(new Rect(solid.x,solid.y,solid.width,solid.height)))) return false;
        if (layout.Platforms.Any(platform => body.Overlaps(platform.Bounds))) return false;
        foreach (CampaignLayout.Spawn spawn in layout.Spawns)
        {
            if (spawn.Kind == CampaignLayout.SpawnKind.DarkZone || spawn.Kind == CampaignLayout.SpawnKind.Hint ||
                spawn.Kind == CampaignLayout.SpawnKind.Decoration || spawn.Kind == CampaignLayout.SpawnKind.Exit) continue;
            Rect occupied = new Rect(spawn.Position-spawn.Size*.5f-Vector2.one*margin,spawn.Size+Vector2.one*margin*2f);
            if (occupied.Contains(point)) return false;
        }
        return true;
    }

    public static bool HasSafeWalkReturn(CampaignLayout layout, int routeNode, int support, Vector2 position)
    {
        if (routeNode < 0 || routeNode >= layout.Route.Count || support < 0 || support >= layout.Platforms.Count) return false;
        CampaignLayout.RouteNode approach = layout.Route[routeNode];
        CampaignLayout.Platform destination = layout.Platforms[support];
        if (approach.PlatformIndex < 0 || approach.PlatformIndex >= layout.Platforms.Count ||
            approach.RoomIndex != destination.RoomIndex || Mathf.Abs(approach.FeetPosition.y-destination.SurfaceY) > .015f ||
            Mathf.Abs(position.y-destination.SurfaceY-.72f) > .015f ||
            position.x < destination.Bounds.xMin+.35f || position.x > destination.Bounds.xMax-.35f) return false;
        float left = Mathf.Min(approach.FeetPosition.x, position.x)-.3f;
        float right = Mathf.Max(approach.FeetPosition.x, position.x)+.3f;
        float surface = destination.SurfaceY;
        float covered = left;
        foreach (CampaignLayout.Platform platform in layout.Platforms
            .Where(p => p.RoomIndex == destination.RoomIndex && Mathf.Abs(p.SurfaceY-surface)<.015f)
            .OrderBy(p => p.Bounds.xMin))
        {
            if (platform.Bounds.xMax <= covered) continue;
            if (platform.Bounds.xMin > covered+.005f) break;
            covered = platform.Bounds.xMax;
            if (covered >= right) break;
        }
        if (covered < right) return false;
        Rect corridor = new Rect(left, surface+.035f, right-left, 1.05f);
        foreach (RectInt solid in layout.Solids)
            if (corridor.Overlaps(new Rect(solid.x, solid.y, solid.width, solid.height))) return false;
        foreach (CampaignLayout.Platform platform in layout.Platforms)
            if (!platform.OneWay && corridor.Overlaps(platform.Bounds)) return false;
        foreach (CampaignLayout.Spawn spawn in layout.Spawns)
        {
            if (spawn.Kind == CampaignLayout.SpawnKind.Hint || spawn.Kind == CampaignLayout.SpawnKind.DarkZone ||
                spawn.Kind == CampaignLayout.SpawnKind.Decoration || spawn.Kind == CampaignLayout.SpawnKind.Exit ||
                spawn.Kind == CampaignLayout.SpawnKind.Upgrade || spawn.Kind == CampaignLayout.SpawnKind.Checkpoint ||
                spawn.Kind == CampaignLayout.SpawnKind.Anchor || spawn.Kind == CampaignLayout.SpawnKind.Chain) continue;
            Rect occupied = new Rect(spawn.Position-spawn.Size*.5f-Vector2.one*.3f, spawn.Size+Vector2.one*.6f);
            if (spawn.Kind == CampaignLayout.SpawnKind.Crawler && spawn.Value >= 0f && spawn.Value < layout.Platforms.Count)
            {
                Rect patrol = layout.Platforms[(int)spawn.Value].Bounds;
                occupied = new Rect(patrol.xMin-.3f, patrol.yMax-.3f, patrol.width+.6f, 1.8f);
            }
            if (corridor.Overlaps(occupied)) return false;
        }
        return true;
    }

    public static float DistanceToRoute(CampaignLayout layout, Vector2 feet) => layout.DistanceToRoute(feet);
}
