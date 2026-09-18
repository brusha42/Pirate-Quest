using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public sealed class TreasurePickup : MonoBehaviour
{
    private PirateGameFlow flow;
    private PirateTreasureLayout.Item item;
    private Transform artwork;
    private bool collected;
    public string Key => item?.Key;
    public PirateTreasureKind Kind => item != null ? item.Kind : PirateTreasureKind.Doubloon;
    public bool IsCollected => collected;

    public void Initialize(PirateGameFlow owner, PirateTreasureLayout.Item description, Sprite sprite)
    {
        flow = owner; item = description;
        transform.position = item.Position;
        CircleCollider2D trigger = GetComponent<CircleCollider2D>();
        trigger.isTrigger = true; trigger.radius = item.Kind == PirateTreasureKind.Relic ? .34f : .26f;
        artwork = new GameObject("Treasure artwork").transform;
        artwork.SetParent(transform,false);
        SpriteRenderer renderer = artwork.gameObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite; renderer.sortingOrder = 15;
        float size = item.Kind == PirateTreasureKind.Doubloon ? .42f : item.Kind == PirateTreasureKind.Gem ? .55f : .67f;
        artwork.localScale = Vector3.one * (size / Mathf.Max(sprite.bounds.size.x,sprite.bounds.size.y));
    }
    private void Update()
    {
        if (artwork == null || collected) return;
        artwork.localPosition = Vector3.up * (Mathf.Sin(Time.time * 3.2f + transform.position.x) * .045f);
    }
    private void OnTriggerEnter2D(Collider2D other) => TryCollect(other);
    private void OnTriggerStay2D(Collider2D other) => TryCollect(other);
    private void TryCollect(Collider2D other)
    {
        if (collected || flow == null || !flow.IsInitialized || flow.IsVictory || flow.IsTransitioning || Time.timeScale == 0f) return;
        PlayerLife life = other.GetComponent<PlayerLife>();
        if (life == null || life.IsRespawning || life.IsExitProtected || life.IsModalProtected) return;
        if (!flow.CollectTreasure(item.Key)) return;
        collected = true;
        gameObject.SetActive(false);
    }
}
