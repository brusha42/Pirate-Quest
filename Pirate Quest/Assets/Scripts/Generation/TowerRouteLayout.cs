using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TowerRouteLayout
{
    public sealed class Step
    {
        public RectInt Tiles;
        public int Section;
        public float SurfaceY => Tiles.yMax;
        public float CenterX => Tiles.xMin + Tiles.width * 0.5f;
        public Vector2 StandingPosition(float halfHeight) => new Vector2(CenterX, SurfaceY + halfHeight + 0.02f);
    }

    public readonly List<Step> Steps = new List<Step>();
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int SectionCount { get; private set; }
    public float SafeJumpHeight { get; private set; }
    public bool GeometryValid { get; private set; }

    public static TowerRouteLayout Create(int seed, int sectionCount, float jumpSpeed, float gravity, float fixedDeltaTime)
    {
        if (jumpSpeed <= 0f || gravity <= 0f)
            throw new InvalidOperationException("Tower generation needs positive jump speed and gravity.");

        float velocity = jumpSpeed;
        float height = 0f;
        float apex = 0f;
        for (int frame = 0; frame < 1000 && velocity > 0f; frame++)
        {
            velocity -= gravity * fixedDeltaTime;
            height += velocity * fixedDeltaTime;
            apex = Mathf.Max(apex, height);
        }

        int rise = Mathf.Min(2, Mathf.FloorToInt(apex * 0.78f));
        if (rise < 1)
            throw new InvalidOperationException($"Jump apex {apex:F2} m is too low for one-metre tower steps.");

        var layout = new TowerRouteLayout { Width = 22, SectionCount = Mathf.Clamp(sectionCount, 4, 12), SafeJumpHeight = apex * 0.78f };
        var random = new System.Random(seed);
        int center = 11;
        int tileY = 1;
        layout.Steps.Add(new Step { Tiles = new RectInt(1, tileY, 20, 1), Section = 0 });

        for (int i = 1; i <= layout.SectionCount * 4; i++)
        {
            int shift = i <= 3 ? 2 : random.Next(2, 4);
            int direction = random.Next(0, 2) == 0 ? -1 : 1;
            if (center + direction * shift < 6 || center + direction * shift > 16)
                direction = -direction;
            center += direction * shift;
            tileY += i <= 2 ? 1 : rise;
            int width = i <= 2 ? 7 : (i <= 4 || i % 4 == 0 ? 5 : 3);
            layout.Steps.Add(new Step
            {
                Tiles = new RectInt(center - width / 2, tileY, width, 1),
                Section = Mathf.Min((i - 1) / 4, layout.SectionCount - 1)
            });
        }

        layout.Height = tileY + 7;
        layout.GeometryValid = layout.Validate();
        return layout;
    }

    private bool Validate()
    {
        for (int i = 1; i < Steps.Count; i++)
        {
            Step a = Steps[i - 1];
            Step b = Steps[i];
            float dy = b.SurfaceY - a.SurfaceY;
            if (dy <= 0f || dy > SafeJumpHeight || Mathf.Abs(b.CenterX - a.CenterX) > 3.6f ||
                b.Tiles.xMin < 2 || b.Tiles.xMax > Width - 2)
                return false;
        }
        return Steps.Count == SectionCount * 4 + 1;
    }
}
