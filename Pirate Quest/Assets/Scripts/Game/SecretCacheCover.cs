using UnityEngine;
using UnityEngine.Tilemaps;

public sealed class SecretCacheCover : MonoBehaviour
{
    private Rect revealBounds;
    private Rect entranceBounds;
    private SpriteRenderer cover;
    private Transform player;
    private bool discovered;
    public bool IsDiscovered => discovered;
    public Rect RevealBounds => revealBounds;

    public void Initialize(CampaignLayout.SecretCache cache, int chapter, Transform pirate)
    {
        player = pirate;
        revealBounds = cache.ChamberBounds;
        entranceBounds = cache.EntranceBounds;
        Rect facade = cache.CoverBounds;
        transform.position = facade.center;
        cover = gameObject.AddComponent<SpriteRenderer>();
        cover.sprite = (PirateWorldArt.GetTile(chapter, 1) as Tile)?.sprite;
        cover.drawMode = SpriteDrawMode.Tiled;
        cover.size = facade.size;
        cover.color = PirateWorldArt.WallTint(chapter);
        cover.sortingOrder = 40;
    }

    private void Update()
    {
        if (cover == null || player == null) return;
        if (revealBounds.Contains(player.position) || entranceBounds.Contains(player.position)) discovered = true;
        float alpha = Mathf.MoveTowards(cover.color.a, discovered ? 0f : 1f, Time.deltaTime * 5f);
        Color tint = cover.color; tint.a = alpha; cover.color = tint;
    }
}
