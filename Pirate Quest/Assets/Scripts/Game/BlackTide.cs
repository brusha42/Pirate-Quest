using System;
using UnityEngine;

public sealed class BlackTideClock
{
    public const float CalmSeconds = 0f;
    public const float InitialDepth = 3.5f;
    public const float RiseSpeed = .3f;
    private double activeSeconds;
    public Vector2 CheckpointFeet { get; private set; }
    public bool IsStopped { get; private set; }
    public float CalmRemaining => Mathf.Max(0f, CalmSeconds - (float)activeSeconds);
    public float SurfaceY => CheckpointFeet.y - InitialDepth +
        (float)Math.Max(0d, activeSeconds - CalmSeconds) * RiseSpeed;

    public void Reset(Vector2 checkpointFeet)
    {
        if (!Finite(checkpointFeet.x) || !Finite(checkpointFeet.y))
            throw new ArgumentOutOfRangeException(nameof(checkpointFeet));
        CheckpointFeet = checkpointFeet;
        activeSeconds = 0d;
        IsStopped = false;
    }

    public void Advance(float scaledDeltaTime, bool pausedForScouting)
    {
        if (!Finite(scaledDeltaTime) || scaledDeltaTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(scaledDeltaTime));
        if (!IsStopped && !pausedForScouting) activeSeconds += scaledDeltaTime;
    }

    public float SecondsUntilHeight(float feetY)
    {
        if (IsStopped) return float.PositiveInfinity;
        if (feetY <= SurfaceY) return 0f;
        return CalmRemaining + (feetY - SurfaceY) / RiseSpeed;
    }

    public void Stop() => IsStopped = true;
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}

[DisallowMultipleComponent]
public sealed class BlackTide : MonoBehaviour
{
    private readonly BlackTideClock clock = new BlackTideClock();
    private PlayerLife life;
    private PlayerAbilities abilities;
    private Collider2D pirateCollider;
    private float checkpointFootOffset;
    private Rect worldBounds;
    private bool initialized;
    private bool subscribed;
    private float animationSeconds;
    private float visualOpacity = 1f;
    private float drainedDistance;
    private GameObject waterObject;
    private Mesh mesh;
    private Material waterMaterial;
    private Material foamMaterial;
    private MeshRenderer waterRenderer;
    private LineRenderer foam;
    private Vector3[] vertices;
    private Vector3[] foamPoints;
    private Color[] vertexColors;
    private int segments;
    private bool customShader;

    public bool IsActive => initialized && !clock.IsStopped;
    public bool IsStopped => initialized && clock.IsStopped;
    public float SurfaceY => initialized ? clock.SurfaceY : float.NegativeInfinity;
    public bool IsScoutingPaused => IsActive && abilities != null && abilities.IsScouting;
    public float SecondsToDanger => !IsActive || pirateCollider == null
        ? float.PositiveInfinity : clock.SecondsUntilHeight(pirateCollider.bounds.min.y);
    public float CalmRemainingSeconds => initialized ? clock.CalmRemaining : 0f;
    public float VisualOpacity => visualOpacity;

    public void Initialize(Rect bounds, PlayerLife playerLife, PlayerAbilities playerAbilities)
    {
        if (playerLife == null || playerAbilities == null ||
            playerLife.gameObject != playerAbilities.gameObject)
            throw new ArgumentException("Black tide requires the same real pirate's life and abilities.");
        if (bounds.width <= 0f || bounds.height <= 0f || float.IsNaN(bounds.xMin) ||
            float.IsNaN(bounds.yMin) || float.IsInfinity(bounds.xMax) || float.IsInfinity(bounds.yMax))
            throw new ArgumentOutOfRangeException(nameof(bounds));
        Unsubscribe();
        DisposeVisuals();
        life = playerLife;
        abilities = playerAbilities;
        pirateCollider = life.GetComponent<Collider2D>();
        if (pirateCollider == null) throw new ArgumentException("Black tide needs the pirate's actual collider.");
        checkpointFootOffset = pirateCollider.bounds.min.y - life.transform.position.y;
        worldBounds = bounds;
        clock.Reset(CheckpointFeet());
        initialized = true;
        animationSeconds = drainedDistance = 0f;
        visualOpacity = 1f;
        Subscribe();
        CreateVisuals();
        RefreshVisuals();
    }

    public void SetSafeCheckpoint(Vector2 checkpointFeet)
    {
        if (!IsActive) return;
        clock.Reset(checkpointFeet);
        animationSeconds = 0f;
        RefreshVisuals();
    }

    public void StopAndDrain()
    {
        if (!initialized || clock.IsStopped) return;
        clock.Stop();
    }

    private void Update()
    {
        if (!initialized) return;
        if (IsStopped)
        {
            visualOpacity = Mathf.MoveTowards(visualOpacity, 0f, Time.unscaledDeltaTime / 1.5f);
            drainedDistance += Time.unscaledDeltaTime * 1.4f;
        }
        else if (Time.timeScale > 0f && life != null && !life.IsRespawning && !life.IsExitProtected)
        {
            bool scouting = IsScoutingPaused;
            clock.Advance(Time.deltaTime, scouting);
            if (!scouting) animationSeconds += Time.deltaTime;
            if (Time.deltaTime > 0f && !scouting && pirateCollider != null && pirateCollider.enabled)
            {
                Bounds body = pirateCollider.bounds;
                if (body.max.x >= worldBounds.xMin && body.min.x <= worldBounds.xMax &&
                    body.min.y <= SurfaceY && body.max.y >= worldBounds.yMin)
                {
                    if (PirateFrontEnd.IsAutomationRun)
                        Debug.Log($"PIRATE_BLACK_TIDE_CONTACT feet={body.min.y:F3} surface={SurfaceY:F3} checkpoint={clock.CheckpointFeet}");
                    life.Die();
                }
            }
        }
        RefreshVisuals();
    }

    private Vector2 CheckpointFeet()
    {
        Vector3 saved = life.CheckpointPosition;
        return new Vector2(saved.x, saved.y + checkpointFootOffset);
    }

    private void ResetForLifeCheckpoint()
    {
        if (!IsActive || life == null) return;
        SetSafeCheckpoint(CheckpointFeet());
    }

    private void Subscribe()
    {
        if (subscribed || life == null) return;
        life.Died += ResetForLifeCheckpoint;
        life.CheckpointRestoring += ResetForLifeCheckpoint;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (subscribed && life != null)
        {
            life.Died -= ResetForLifeCheckpoint;
            life.CheckpointRestoring -= ResetForLifeCheckpoint;
        }
        subscribed = false;
    }

    private void CreateVisuals()
    {
        segments = Mathf.Clamp(Mathf.CeilToInt(worldBounds.width * 2f), 16, 128);
        vertices = new Vector3[(segments + 1) * 4];
        vertexColors = new Color[vertices.Length];
        foamPoints = new Vector3[segments + 1];
        int[] triangles = new int[segments * 3 * 6];
        Vector2[] uv = new Vector2[vertices.Length];
        for (int x = 0; x <= segments; x++)
        for (int row = 0; row < 4; row++)
            uv[row * (segments + 1) + x] = new Vector2(x / (float)segments, row / 3f);
        int cursor = 0;
        for (int row = 0; row < 3; row++)
        for (int x = 0; x < segments; x++)
        {
            int a = row * (segments + 1) + x, b = a + segments + 1;
            triangles[cursor++] = a; triangles[cursor++] = a + 1; triangles[cursor++] = b + 1;
            triangles[cursor++] = a; triangles[cursor++] = b + 1; triangles[cursor++] = b;
        }
        mesh = new Mesh { name = "Black tide layered water volume" };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        waterObject = new GameObject("Black tide - deep water and luminous foam");
        waterObject.transform.SetParent(transform, false);
        waterObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        waterRenderer = waterObject.AddComponent<MeshRenderer>();
        Shader shader = Resources.Load<Shader>("PirateBlackTide");
        customShader = shader != null && shader.isSupported;
        if (!customShader) shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            waterMaterial = new Material(shader) { name = "Animated dark turquoise water" };
            waterRenderer.sharedMaterial = waterMaterial;
        }
        waterRenderer.sortingOrder = 72;
        foam = waterObject.AddComponent<LineRenderer>();
        foam.useWorldSpace = true;
        foam.positionCount = foamPoints.Length;
        foam.widthMultiplier = .045f;
        foam.numCornerVertices = 2;
        foam.sortingOrder = 74;
        Shader foamShader = Shader.Find("Sprites/Default");
        if (foamShader != null)
        {
            foamMaterial = new Material(foamShader) { name = "Black tide seafoam edge" };
            foam.sharedMaterial = foamMaterial;
        }
    }

    private void RefreshVisuals()
    {
        if (mesh == null || waterRenderer == null) return;
        float top = Mathf.Min(SurfaceY - drainedDistance, worldBounds.yMax + .1f);
        bool visible = top > worldBounds.yMin && visualOpacity > .001f;
        waterRenderer.enabled = visible;
        foam.enabled = visible;
        if (!visible) return;
        for (int column = 0; column <= segments; column++)
        {
            float x = Mathf.Lerp(worldBounds.xMin, worldBounds.xMax, column / (float)segments);
            float wave = Mathf.Sin(x * 2.1f + animationSeconds * 1.3f) * .025f +
                Mathf.Sin(x * 4.7f - animationSeconds * 1.9f) * .012f;
            float edge = Mathf.Max(worldBounds.yMin, top + wave);
            float depth = edge - worldBounds.yMin;
            float middle = worldBounds.yMin + depth * .62f;
            float nearSurface = Mathf.Max(middle, edge - .22f);
            for (int row = 0; row < 4; row++)
            {
                int index = row * (segments + 1) + column;
                float y = row == 0 ? worldBounds.yMin : row == 1 ? middle : row == 2 ? nearSurface : edge;
                vertices[index] = waterObject.transform.InverseTransformPoint(new Vector3(x, y, 0f));
                Color tint = row == 0 ? new Color(.007f, .03f, .044f, .97f) :
                    row == 1 ? new Color(.013f, .09f, .105f, .94f) :
                    row == 2 ? new Color(.035f, .22f, .23f, .91f) : new Color(.16f, .45f, .42f, .94f);
                tint.a *= visualOpacity;
                vertexColors[index] = tint;
            }
            foamPoints[column] = new Vector3(x, edge, 0f);
        }
        mesh.vertices = vertices;
        mesh.colors = vertexColors;
        mesh.RecalculateBounds();
        foam.SetPositions(foamPoints);
        float shimmer = .88f + .06f * Mathf.Sin(animationSeconds * 1.6f);
        foam.startColor = new Color(.38f, .85f, .73f, visualOpacity * shimmer);
        foam.endColor = new Color(.24f, .69f, .65f, visualOpacity * shimmer);
        if (customShader && waterMaterial != null)
        {
            waterMaterial.SetFloat("_AnimTime", animationSeconds);
            waterMaterial.SetFloat("_SurfaceY", top);
        }
    }

    private void DisposeVisuals()
    {
        if (waterObject != null) { waterObject.SetActive(false); Destroy(waterObject); }
        if (mesh != null) Destroy(mesh);
        if (waterMaterial != null) Destroy(waterMaterial);
        if (foamMaterial != null) Destroy(foamMaterial);
    }

    private void OnEnable() { if (initialized) Subscribe(); }
    private void OnDisable() => Unsubscribe();
    private void OnDestroy() { Unsubscribe(); DisposeVisuals(); }
}
