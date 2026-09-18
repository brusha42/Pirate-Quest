using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public sealed partial class CampaignLayout
{
    public enum SecretEntranceKind { OpenBranch, UnderStairs, DropHatch, SideSlit }

    [Serializable]
    public sealed class SecretCache
    {
        public int Id;
        public int ApproachNode;
        public int Support;
        public Rect Bounds;
        public Rect ChamberBounds;
        public Rect RewardBounds;
        public Rect EntranceBounds;
        public Rect CoverBounds;
        public SecretEntranceKind Kind;
        public bool Concealed;
        public readonly List<Vector2> ReturnPath = new List<Vector2>();
    }

    public readonly List<SecretCache> SecretCaches = new List<SecretCache>();
    public const int MinimumConcealedCaches = 3;
    public const int MaximumConcealedCaches = 5;
    public const int MaximumSecretCaches = 7;

    public float DistanceToRoute(Vector2 feet)
    {
        float distance = float.PositiveInfinity;
        for (int i = 0; i < Route.Count; i++)
        {
            Vector2 start = Route[i].FeetPosition;
            if (i == Route.Count - 1) { distance = Mathf.Min(distance, Vector2.Distance(feet, start)); continue; }
            Vector2 segment = Route[i + 1].FeetPosition - start;
            float t = segment.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector2.Dot(feet - start, segment) / segment.sqrMagnitude) : 0f;
            distance = Mathf.Min(distance, Vector2.Distance(feet, start + segment * t));
        }
        return distance;
    }
}
