using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

public sealed partial class CampaignLayout
{
    public enum TraversalAction { Walk, Jump, Chain, Grapple, Spring, DoubleJump, Saber, SaberSlide, JumpPad, SpringDouble, SpringGrapple, RingRelay, SpringDrop, DoubleWindow }
    public enum MacroPlanKind { DockGalleries, ArsenalWings, GardenWells, ShiftedObservatories }
    public enum SpawnKind { Checkpoint, Cannon, Chain, Anchor, Upgrade, Spikes, Snare, Plant, RopeGate, DarkZone, JumpPad, Hint, Exit, Decoration, Crawler }
    public enum Side { Left, Right, Bottom, Top }
    public enum StrategyKind { Pyramid, Grid, JumpPad, ChainGap, GrappleRise, SpringRise, DoubleJumpRise, SaberPassage, SaberSlide, DarkGallery }

    [Serializable]
    public struct Window
    {
        public Side Side;
        public float Coordinate;
        public float Minimum;
        public float Maximum;
        public Vector2 Midpoint => Side == Side.Left || Side == Side.Right
            ? new Vector2(Coordinate, (Minimum + Maximum) * 0.5f)
            : new Vector2((Minimum + Maximum) * 0.5f, Coordinate);

        public Window(Side side, float coordinate, float minimum, float maximum)
        { Side = side; Coordinate = coordinate; Minimum = minimum; Maximum = maximum; }
    }

    [Serializable]
    public sealed class Region
    {
        public int Id;
        public int Parent = -1;
        public int Depth;
        public int Band;
        public RectInt Bounds;
        public Vector2 Entrance;
        public Window Exit;
        public StrategyKind Strategy;
        public bool IsLeaf;
        public bool IsShaft;
        public bool HasOptionalBranch;
        public bool HasOverheadStructure;
        public int FirstRouteNode;
        public int LastRouteNode;
        public string ScenarioId;
    }

    [Serializable]
    public sealed class Platform
    {
        public Rect Bounds;
        public bool OneWay = true;
        public int RoomIndex;
        public bool Optional;
        public float SurfaceY => Bounds.yMax;
        public float CenterX => Bounds.center.x;
        public Vector2 StandingPosition(float halfHeight) => new Vector2(CenterX, SurfaceY + halfHeight + 0.02f);
    }

    [Serializable]
    public sealed class RouteNode
    {
        public int PlatformIndex;
        public int RoomIndex;
        public TraversalAction Action;
        public PirateUpgrade? RequiredAbility;
        public PirateUpgrade? SecondaryAbility;
        public Vector2 FeetPosition;
        public Vector2 StandingPosition(float halfHeight) => FeetPosition + Vector2.up * (halfHeight + 0.02f);
        public string Note;
    }

    [Serializable]
    public sealed class Spawn
    {
        public SpawnKind Kind;
        public Vector2 Position;
        public Vector2 Size = Vector2.one;
        public int Facing = 1;
        public int NodeIndex;
        public PirateUpgrade Ability;
        public float Value;
        public bool Optional;
        public string Text;
    }

    public readonly List<Region> Rooms = new List<Region>();
    public readonly List<RectInt> Solids = new List<RectInt>();
    public readonly List<Platform> Platforms = new List<Platform>();
    public readonly List<RouteNode> Route = new List<RouteNode>();
    public readonly List<Spawn> Spawns = new List<Spawn>();
    public readonly List<string> ValidationErrors = new List<string>();
    public RectInt Bounds { get; private set; }
    public int BandCount => Chapter == 1 ? 16 : Chapter >= 2 ? 12 : CampaignBandCount;
    public int PlayableRoomCount => UsesChamberPlan ? BandCount : BandCount * 2;
    public MacroPlanKind MacroPlan => (MacroPlanKind)Chapter;
    public int ScenarioVariant => (int)((unchecked((uint)Seed * 747796405u) >> 24) & 1u);
    public readonly List<Scenario> Scenarios = new List<Scenario>();
    public int Seed { get; private set; }
    public int Chapter { get; private set; }
    public string ChapterTitle { get; private set; }
    public Vector2 Start { get; private set; }
    public Vector2 Exit { get; private set; }
    public float SafeJumpHeight { get; private set; }
    public float SafeJumpDistance { get; private set; }
    public bool GeometryValid { get; private set; }
    public int AcceptedSplits { get; private set; }
    public int PlannedChambers { get; private set; }
    public int CompatibilityChecks { get; private set; }
    public int FallbackFills { get; private set; }

    private System.Random random;
    private float jumpSpeed;
    private float gravity;
    private float fixedDeltaTime;
    private float moveSpeed;

    public static CampaignLayout Create(int seed, int chapterIndex, float jumpSpeed, float gravity,
        float fixedDeltaTime, float moveSpeed = 8f)
    {
        if (jumpSpeed <= 0f || gravity <= 0f || fixedDeltaTime <= 0f || moveSpeed <= 0f)
            throw new ArgumentOutOfRangeException(nameof(jumpSpeed), "Campaign needs positive real movement parameters.");
        var layout = new CampaignLayout
        {
            Seed = seed, Chapter = Mathf.Clamp(chapterIndex, 0, 3),
            random = new System.Random(unchecked((int)(((uint)seed * 747796405u + 2891336453u) ^ ((uint)chapterIndex * 7919u)))),
            jumpSpeed = jumpSpeed, gravity = gravity, fixedDeltaTime = fixedDeltaTime, moveSpeed = moveSpeed
        };
        layout.CalculateJumpEnvelope();
        layout.Generate();
        layout.AddSecretCaches();
        layout.SealArchitectureHull();
        layout.GeometryValid = layout.Validate();
        return layout;
    }

    private void CalculateJumpEnvelope()
    {
        float velocity = jumpSpeed;
        float height = 0f;
        float apex = 0f;
        for (int i = 0; i < 1000 && velocity > 0f; i++)
        {
            velocity -= gravity * fixedDeltaTime;
            height += velocity * fixedDeltaTime;
            apex = Mathf.Max(apex, height);
        }
        SafeJumpHeight = apex * 0.78f;
        SafeJumpDistance = Mathf.Min(4.8f, moveSpeed * (2f * jumpSpeed / gravity) * 0.56f);
        if (SafeJumpHeight < 2f || SafeJumpDistance < 2.65f)
            throw new InvalidOperationException("Campaign strategy envelopes require safe rise >= 2 m and safe span >= 2.65 m; ordinary spacing is speed-dependent.");
    }

    public string Signature()
    {
        var value = new StringBuilder();
        value.Append(Chapter).Append('|');
        value.Append(MacroTopologySignature()).Append('|');
        foreach (RectInt footprint in WorldFootprint) AppendRect(value, footprint);
        foreach (Region room in Rooms)
        {
            AppendRect(value, room.Bounds);
            value.Append(':').Append((int)room.Strategy).Append(':');
            value.Append(room.ScenarioId).Append(':');
            AppendVector(value, room.Entrance);
        }
        foreach (RectInt wall in Solids) AppendRect(value, wall);
        foreach (Platform platform in Platforms)
            value.Append(platform.Bounds.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(platform.Bounds.y.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(platform.Bounds.width.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(platform.Bounds.height.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                .Append(platform.OneWay ? '1' : '0').Append(';');
        foreach (Spawn spawn in Spawns)
        {
            value.Append((int)spawn.Kind).Append(':');
            AppendVector(value, spawn.Position);
            AppendVector(value, spawn.Size);
            value.Append(':').Append((int)spawn.Ability).Append(':').Append(spawn.Value.ToString("F3", CultureInfo.InvariantCulture)).Append(';');
        }
        foreach (Scenario scenario in Scenarios)
            value.Append(scenario.Id).Append(':').Append(scenario.FirstNode).Append(':').Append(scenario.LastNode).Append(';');
        foreach (SecretCache cache in SecretCaches)
        {
            value.Append("cache:").Append(cache.Id).Append(':').Append(cache.ApproachNode).Append(':').Append(cache.Concealed ? '1' : '0').Append(':').Append((int)cache.Kind);
            AppendVector(value, cache.Bounds.min); AppendVector(value, cache.Bounds.size);
            AppendVector(value, cache.ChamberBounds.min); AppendVector(value, cache.ChamberBounds.size);
            AppendVector(value, cache.RewardBounds.min); AppendVector(value, cache.RewardBounds.size);
            AppendVector(value, cache.EntranceBounds.min); AppendVector(value, cache.EntranceBounds.size);
            AppendVector(value, cache.CoverBounds.min); AppendVector(value, cache.CoverBounds.size);
            foreach (Vector2 feet in cache.ReturnPath) AppendVector(value, feet);
        }
        ulong hash = 14695981039346656037UL;
        foreach (char character in value.ToString()) { hash ^= character; hash *= 1099511628211UL; }
        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }

    private static void AppendVector(StringBuilder value, Vector2 point)
    {
        value.Append(point.x.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
            .Append(point.y.ToString("F3", CultureInfo.InvariantCulture)).Append(';');
    }

    private static void AppendRect(StringBuilder value, RectInt rect)
    {
        value.Append(rect.x).Append(',').Append(rect.y).Append(',').Append(rect.width).Append(',').Append(rect.height).Append(';');
    }
}
