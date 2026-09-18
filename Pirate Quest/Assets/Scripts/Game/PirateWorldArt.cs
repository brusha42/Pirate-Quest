using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class PirateWorldArt
{
    private static PirateWorldArtLibrary library;
    private static Sprite lightHalo;
    private static Sprite chainLinks;
    public static PirateWorldArtLibrary Library => library != null ? library : library = Resources.Load<PirateWorldArtLibrary>("PirateWorldArtLibrary");
    public static Sprite GetSprite(PirateArtKind kind) => Library != null ? Library.Get(kind) : null;
    public static TileBase GetTile(int chapter, int slot) => Library != null ? Library.GetTile(chapter, slot) : null;
    private static readonly Color[] WallTints = {
        new Color(.62f,.65f,.69f), new Color(.74f,.55f,.41f),
        new Color(.44f,.64f,.46f), new Color(.69f,.65f,.83f)
    };
    public static Color WallTint(int chapter) => WallTints[Mathf.Clamp(chapter, 0, WallTints.Length - 1)];
    public static Sprite GetChainLinks()
    {
        if (chainLinks != null) return chainLinks;
        Sprite chain = GetSprite(PirateArtKind.Chain);
        if (chain == null) return null;
        Rect region = chain.rect;
        region.yMin += region.height * .36f;
        chainLinks = Sprite.Create(chain.texture, region, new Vector2(.5f, .5f), chain.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        chainLinks.name = "Repeated iron chain links";
        return chainLinks;
    }

    public static GameObject Create(string name, PirateArtKind kind, Vector3 position, Vector2 size, Transform parent = null)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        ApplyTo(root, kind, size);
        return root;
    }

    public static GameObject CreateHoistChains(Vector3 position, Vector2 size, Transform parent = null)
    {
        var root = new GameObject("Background hoist chain links");
        root.transform.SetParent(parent, false);
        root.transform.position = position;
        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = GetChainLinks();
        renderer.sortingOrder = -9;
        renderer.color = new Color(.48f, .54f, .59f);
        if (renderer.sprite != null)
        {
            float scale = Mathf.Min(.25f, size.x) / renderer.sprite.bounds.size.x;
            root.transform.localScale = Vector3.one * scale;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = new Vector2(renderer.sprite.bounds.size.x, size.y / scale);
        }
        return root;
    }

    public static void ApplyTo(GameObject root, PirateArtKind kind, Vector2 size)
    {
        var visual = root.GetComponent<PirateWorldVisual>();
        if (visual == null) visual = root.AddComponent<PirateWorldVisual>();
        visual.Configure(kind, size);
        if ((kind == PirateArtKind.LanternLit || kind == PirateArtKind.CheckpointLit) && root.transform.Find("Lantern glow") == null)
        {
            var glow = new GameObject("Lantern glow");
            glow.transform.SetParent(root.transform, false);
            var renderer = glow.AddComponent<SpriteRenderer>();
            renderer.sprite = GetLightHalo();
            renderer.sortingOrder = -10;
            renderer.color = kind == PirateArtKind.CheckpointLit ? new Color(0.13f, 0.85f, 1f, 0.24f) : new Color(1f, 0.58f, 0.14f, 0.19f);
            glow.transform.localScale = Vector3.one * 2.7f;
        }
    }

    public static GameObject CreatePlatform(string name, Rect bounds, int chapter, Transform parent = null)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.position = bounds.center;
        var child = new GameObject("Platform material");
        child.transform.SetParent(root.transform, false);
        var renderer = child.AddComponent<SpriteRenderer>();
        var tile = GetTile(chapter, bounds.height > .45f ? 1 : 2) as Tile;
        renderer.sprite = tile != null ? tile.sprite : GetSprite(PirateArtKind.Beam);
        renderer.sortingOrder = 5;
        renderer.drawMode = SpriteDrawMode.Tiled;
        renderer.size = bounds.size;
        if (bounds.height > .45f) renderer.color = WallTint(chapter);
        if (bounds.height > .45f)
        {
            var lip = new GameObject("Timber landing edge");
            lip.transform.SetParent(root.transform, false);
            lip.transform.localPosition = new Vector3(0f, bounds.height * .5f - .09f, 0f);
            var lipRenderer = lip.AddComponent<SpriteRenderer>();
            var timber = GetTile(chapter, 2) as Tile;
            lipRenderer.sprite = timber != null ? timber.sprite : GetSprite(PirateArtKind.Beam);
            lipRenderer.sortingOrder = 6;
            lipRenderer.drawMode = SpriteDrawMode.Tiled;
            lipRenderer.size = new Vector2(bounds.width, .18f);
        }
        return root;
    }

    public static void Decorate(Rect bounds, IEnumerable<Rect> rooms, int chapter, Transform parent,
        System.Func<Vector2,float?> supportHeight = null, System.Func<Bounds,bool> windowOccluded = null)
    {
        var cameraFollow = Object.FindFirstObjectByType<CameraFollow>();
        if (cameraFollow != null) cameraFollow.SetWorldBounds(bounds);
        int index = 0;
        foreach (Rect room in rooms)
        {
            Color windowTint = chapter == 2 ? new Color(.55f, .78f, .65f) : new Color(.78f, .85f, 1f);
            AddWindow(new Vector2(room.xMin + 5f, room.yMin + 7.5f));
            if (room.width > 17f)
                AddWindow(new Vector2(room.xMax - 5f, room.yMin + 7.5f));
            void AddWindow(Vector2 position)
            {
                GameObject window = AddDecoration("Arched window", PirateArtKind.Window, position,
                    new Vector2(2.4f, 3.7f), parent, -12, windowTint);
                PirateWorldVisual visual = window.GetComponent<PirateWorldVisual>();
                if (windowOccluded == null || !windowOccluded(visual.OpaqueWorldBounds)) return;
                visual.Renderer.enabled = false;
                Object.Destroy(window);
            }
            AddDecoration("Wall lantern", PirateArtKind.LanternLit,
                new Vector2(room.xMin + 2.3f, room.yMin + 6f), new Vector2(0.65f, 1.2f), parent, -5, Color.white);
            AddDecoration("Wall lantern", PirateArtKind.LanternLit,
                new Vector2(room.xMax - 2.3f, room.yMin + 6f), new Vector2(0.65f, 1.2f), parent, -5, Color.white);
            var clutter = chapter == 1 ? PirateArtKind.CannonballPile : index % 2 == 0 ? PirateArtKind.Barrel : PirateArtKind.Crates;
            Vector2 storagePosition = new Vector2(room.xMin + 1.3f, room.yMin + 1.3f);
            float? surface = supportHeight != null ? supportHeight(storagePosition) : room.yMin;
            float? leftSurface = supportHeight != null ? supportHeight(storagePosition + Vector2.left * .58f) : surface;
            float? rightSurface = supportHeight != null ? supportHeight(storagePosition + Vector2.right * .58f) : surface;
            if (supportHeight != null && (!surface.HasValue || !leftSurface.HasValue || !rightSurface.HasValue ||
                Mathf.Abs(leftSurface.Value-surface.Value)>.01f || Mathf.Abs(rightSurface.Value-surface.Value)>.01f))
            {
                foreach (float shift in new[] { 1.3f, -1.3f })
                {
                    Vector2 candidate=storagePosition+Vector2.right*shift;
                    if (candidate.x<room.xMin+.6f || candidate.x>room.xMax-.6f) continue;
                    float? middle=supportHeight(candidate);
                    float? left=supportHeight(candidate+Vector2.left*.58f);
                    float? right=supportHeight(candidate+Vector2.right*.58f);
                    if (!middle.HasValue || !left.HasValue || !right.HasValue || middle.Value<room.yMin-.01f ||
                        Mathf.Abs(left.Value-middle.Value)>.01f || Mathf.Abs(right.Value-middle.Value)>.01f) continue;
                    storagePosition=candidate; surface=middle; leftSurface=left; rightSurface=right;
                    break;
                }
            }
            if (surface.HasValue && surface.Value >= room.yMin - .01f && leftSurface.HasValue && rightSurface.HasValue &&
                Mathf.Abs(leftSurface.Value - surface.Value) < .01f && Mathf.Abs(rightSurface.Value - surface.Value) < .01f)
            {
                GameObject storage = AddDecoration("Storage decoration", clutter, storagePosition,
                    new Vector2(1.15f, 1.3f), parent, -6, new Color(0.72f, 0.72f, 0.72f));
                storage.GetComponent<PirateWorldVisual>().SetGroundSurface(surface.Value);
            }
            if (chapter == 2)
            {
                AddDecoration("Ceiling vines", PirateArtKind.Vines, new Vector2(room.xMin + 2f, room.yMin + 9f),
                    new Vector2(2.7f, 2.6f), parent, -7, Color.white);
                AddDecoration("Ceiling vines", PirateArtKind.Vines, new Vector2(room.xMax - 2f, room.yMin + 9f),
                    new Vector2(2.7f, 2.6f), parent, -7, new Color(0.66f, 0.85f, 0.75f));
            }
            else
                AddDecoration("Pirate banner", PirateArtKind.Banner, new Vector2(room.center.x, room.yMin + 8f),
                    new Vector2(1.15f, 2.5f), parent, -8, chapter == 3 ? new Color(0.65f, 0.45f, 0.9f) : Color.white);
            index++;
        }
        if (Camera.main != null) Camera.main.backgroundColor = new[]
        {
            new Color(0.035f, 0.08f, 0.12f), new Color(0.12f, 0.055f, 0.045f),
            new Color(0.025f, 0.09f, 0.065f), new Color(0.055f, 0.045f, 0.12f)
        }[Mathf.Clamp(chapter, 0, 3)];
    }

    private static GameObject AddDecoration(string name, PirateArtKind kind, Vector2 position, Vector2 size, Transform parent, int order, Color tint)
    {
        GameObject item = Create(name, kind, position, size, parent);
        SpriteRenderer renderer = item.GetComponentInChildren<SpriteRenderer>();
        renderer.sortingOrder = order;
        renderer.color = tint;
        return item;
    }

    private static Sprite GetLightHalo()
    {
        if (lightHalo != null) return lightHalo;
        const int resolution = 64;
        var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        for (int x = 0; x < resolution; x++)
        {
            float radius = new Vector2((x + 0.5f) / resolution * 2f - 1f, (y + 0.5f) / resolution * 2f - 1f).magnitude;
            pixels[y * resolution + x] = new Color(1f, 1f, 1f, Mathf.Pow(Mathf.Clamp01(1f - radius), 2f));
        }
        texture.SetPixels(pixels);
        texture.Apply(false, true);
        lightHalo = Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
        lightHalo.name = "Soft radial light effect";
        return lightHalo;
    }
}

public class PirateWorldVisual : MonoBehaviour
{
    public PirateArtKind Kind { get; private set; }
    public SpriteRenderer Renderer { get; private set; }
    private Vector2 requestedSize;
    private PirateArtKind currentFrame;
    private float? groundLocalY;
    private bool previousFlip;
    public bool IsGroundAnchored => groundLocalY.HasValue;
    public float GroundSurfaceY => transform.TransformPoint(new Vector3(0f,groundLocalY ?? 0f,0f)).y;

    public void SetGroundSurface(float worldY)
    {
        groundLocalY = transform.InverseTransformPoint(new Vector3(transform.position.x,worldY,transform.position.z)).y;
        RefreshPlacement();
    }

    public Bounds OpaqueWorldBounds
    {
        get
        {
            if (Renderer == null || Renderer.sprite == null) return new Bounds(transform.position,Vector3.zero);
            if (Renderer.drawMode != SpriteDrawMode.Simple) return Renderer.bounds;
            Rect rect = PirateWorldArt.Library.GetOpaqueBounds(currentFrame);
            Vector3 first = SpritePointToWorld(rect.min);
            var result = new Bounds(first,Vector3.zero);
            result.Encapsulate(SpritePointToWorld(rect.max));
            result.Encapsulate(SpritePointToWorld(new Vector2(rect.xMin,rect.yMax)));
            result.Encapsulate(SpritePointToWorld(new Vector2(rect.xMax,rect.yMin)));
            return result;
        }
    }

    private Vector3 SpritePointToWorld(Vector2 point)
    {
        if (Renderer.flipX) point.x = -point.x;
        if (Renderer.flipY) point.y = -point.y;
        return Renderer.transform.TransformPoint(point);
    }

    public bool TryGetCannonMuzzle(out Vector2 worldPoint)
    {
        worldPoint = transform.position;
        if (Kind != PirateArtKind.Cannon || Renderer == null || PirateWorldArt.Library == null) return false;
        RefreshPlacement();
        Rect body = PirateWorldArt.Library.GetLayoutBounds(currentFrame);
        Vector2 anchor = PirateWorldArt.Library.CannonMuzzleNormalized;
        worldPoint = SpritePointToWorld(new Vector2(body.xMin+anchor.x*body.width,body.yMin+anchor.y*body.height));
        return true;
    }

    public void RefreshPlacement()
    {
        if (Renderer == null || Renderer.sprite == null || Renderer.drawMode != SpriteDrawMode.Simple || PirateWorldArt.Library == null) return;
        Rect layout = PirateWorldArt.Library.GetLayoutBounds(currentFrame);
        Rect opaque = PirateWorldArt.Library.GetOpaqueBounds(currentFrame);
        float scale = Mathf.Min(requestedSize.x/layout.width,requestedSize.y/layout.height);
        Renderer.transform.localScale = Vector3.one*scale;
        float y = groundLocalY.HasValue ? groundLocalY.Value-opaque.yMin*scale : -layout.center.y*scale;
        Renderer.transform.localPosition = new Vector3(-layout.center.x*scale*(Renderer.flipX ? -1f : 1f),y,0f);
        previousFlip = Renderer.flipX;
    }
    public void Configure(PirateArtKind kind, Vector2 size)
    {
        Kind = kind;
        requestedSize = size;
        if (Renderer == null)
        {
            var child = new GameObject("Sprite art");
            child.transform.SetParent(transform, false);
            Renderer = child.AddComponent<SpriteRenderer>();
            Renderer.sortingOrder = 20;
        }
        Renderer.sortingOrder = kind == PirateArtKind.Checkpoint || kind == PirateArtKind.CheckpointLit || kind == PirateArtKind.Door ? 9 : 20;
        SetFrame(kind);
    }
    public void SetFrame(PirateArtKind kind)
    {
        if (Renderer == null) return;
        currentFrame = kind;
        Sprite sprite = PirateWorldArt.GetSprite(kind);
        if (sprite == null) { Debug.LogError($"Missing production sprite: {kind}", this); return; }
        Renderer.sprite = sprite;
        Vector2 dimensions = sprite.bounds.size;
        float scale = Mathf.Min(requestedSize.x / dimensions.x, requestedSize.y / dimensions.y);
        Renderer.drawMode = SpriteDrawMode.Simple;
        Renderer.transform.localPosition = Vector3.zero;
        Renderer.transform.localScale = Vector3.one * scale;
        if (kind == PirateArtKind.Spikes)
        {
            float spikeScale = requestedSize.y / dimensions.y;
            Renderer.transform.localScale = Vector3.one * spikeScale;
            Renderer.drawMode = SpriteDrawMode.Tiled;
            Renderer.size = new Vector2(requestedSize.x / spikeScale, dimensions.y);
        }
        if (kind == PirateArtKind.Chain && requestedSize.y > dimensions.y * scale + .01f)
        {
            float terminalHeight = dimensions.y * scale;
            Renderer.transform.localPosition = Vector3.down * (requestedSize.y - terminalHeight) * .5f;
            Transform oldLinks = transform.Find("Repeated chain artwork");
            var links = oldLinks != null ? oldLinks.gameObject : new GameObject("Repeated chain artwork");
            links.transform.SetParent(transform, false);
            links.transform.localPosition = Vector3.up * terminalHeight * .5f;
            links.transform.localScale = Vector3.one * scale;
            var linkRenderer = links.GetComponent<SpriteRenderer>();
            if (linkRenderer == null) linkRenderer = links.AddComponent<SpriteRenderer>();
            linkRenderer.sprite = PirateWorldArt.GetChainLinks();
            linkRenderer.sortingOrder = Renderer.sortingOrder;
            linkRenderer.drawMode = SpriteDrawMode.Tiled;
            linkRenderer.size = new Vector2(dimensions.x, (requestedSize.y - terminalHeight) / scale);
        }
        if (kind == PirateArtKind.Rope || kind == PirateArtKind.Beam)
            Renderer.transform.localScale = new Vector3(requestedSize.x / dimensions.x, requestedSize.y / dimensions.y, 1f);
        else if (kind != PirateArtKind.Chain && kind != PirateArtKind.Spikes)
            RefreshPlacement();
    }
    private void Update()
    {
        PirateArtKind next = Kind;
        if (Kind == PirateArtKind.Cannon)
        {
            Cannon cannon = GetComponent<Cannon>();
            if (cannon != null)
            {
                next = cannon.IsFiring ? PirateArtKind.CannonFire : PirateArtKind.Cannon;
                Renderer.color = cannon.IsTelegraphing ? Color.Lerp(Color.white, new Color(1f, 0.4f, 0.2f), (Mathf.Sin(Time.time * 24f) + 1f) * 0.5f) : Color.white;
            }
        }
        if (Kind == PirateArtKind.Plant)
        {
            HangingPlant plant = GetComponent<HangingPlant>();
            if (plant != null)
            {
                next = plant.IsAttacking ? PirateArtKind.PlantAttack : PirateArtKind.Plant;
                Renderer.color = plant.IsWindingUp ? new Color(1f, 0.64f, 0.5f) : Color.white;
            }
        }
        if (Kind == PirateArtKind.Parrot || Kind == PirateArtKind.ParrotFlap)
            next = (int)(Time.time * 9f) % 2 == 0 ? PirateArtKind.Parrot : PirateArtKind.ParrotFlap;
        if (next != currentFrame) SetFrame(next);
        else if (Renderer != null && Renderer.flipX != previousFlip) RefreshPlacement();
        if (Renderer != null && (Kind == PirateArtKind.LanternLit || Kind == PirateArtKind.CheckpointLit))
            Renderer.color = Color.Lerp(new Color(1f, 0.85f, 0.7f), Color.white, 0.55f + Mathf.Sin(Time.time * 7f + transform.position.x) * 0.2f);
    }
}
