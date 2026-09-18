using UnityEngine;
using UnityEngine.Tilemaps;

public enum PirateArtKind
{
    Checkpoint, CheckpointLit, Cannon, CannonFire, Cannonball, Chain, Anchor, HookUpgrade,
    SpringLeg, DoubleJumpLeg, Saber, SaberUpgrade, Parrot, ParrotFlap, Snare, Spikes,
    Plant, PlantAttack, Barrel, Crates, Lantern, LanternLit, Vines, Banner,
    Door, PirateKing, JumpPad, Rope, Skull, Window, Beam, CannonballPile,
    Platform = Beam
}

[CreateAssetMenu(menuName = "Pirate Quest/World Art Library")]
public class PirateWorldArtLibrary : ScriptableObject
{
    [SerializeField] private Sprite[] objects = new Sprite[32];
    [SerializeField] private TileBase[] materials = new TileBase[16];
    [SerializeField] private Rect[] opaqueBounds = new Rect[0];
    [SerializeField] private Vector2 cannonMuzzleNormalized = new Vector2(.06f, .73f);
    public bool IsComplete
    {
        get
        {
            if (objects == null || objects.Length != 32 || materials == null || materials.Length != 16) return false;
            foreach (var item in objects) if (item == null) return false;
            foreach (var item in materials) if (item == null) return false;
            return true;
        }
    }
    public Sprite Get(PirateArtKind kind) => objects != null && (int)kind < objects.Length ? objects[(int)kind] : null;
    public TileBase GetTile(int chapter, int slot) => materials[Mathf.Clamp(chapter, 0, 3) * 4 + Mathf.Clamp(slot, 0, 3)];
    public Rect GetOpaqueBounds(PirateArtKind kind)
    {
        int index = (int)kind;
        if (opaqueBounds != null && index >= 0 && index < opaqueBounds.Length && opaqueBounds[index].width > 0f)
            return opaqueBounds[index];
        Sprite sprite = Get(kind);
        return sprite != null ? new Rect(sprite.bounds.min, sprite.bounds.size) : new Rect(-.5f,-.5f,1f,1f);
    }
    public Rect GetLayoutBounds(PirateArtKind kind)
    {
        Rect bounds = GetOpaqueBounds(kind);
        if (kind == PirateArtKind.CannonFire)
        {
            Rect body = GetOpaqueBounds(PirateArtKind.Cannon);
            return new Rect(bounds.xMax-body.width,bounds.yMin,body.width,body.height);
        }
        if (kind == PirateArtKind.CheckpointLit)
        {
            Rect body = GetOpaqueBounds(PirateArtKind.Checkpoint);
            return new Rect(bounds.xMin,bounds.yMin,body.width,body.height);
        }
        return bounds;
    }
    public Vector2 CannonMuzzleNormalized => cannonMuzzleNormalized;
    public bool HasOpaqueGeometry => opaqueBounds != null && opaqueBounds.Length == 32;
    public void Configure(Sprite[] sprites, TileBase[] tiles, Rect[] bounds, Vector2 muzzle)
    { objects = sprites; materials = tiles; opaqueBounds = bounds; cannonMuzzleNormalized = muzzle; }
}
