using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class Checkpoint : MonoBehaviour
{
    public event Action<Checkpoint> Activated;

    public bool IsActivated { get; private set; }
    private Vector3? configuredRespawn;

    public void Configure(Vector3 standingPosition) => configuredRespawn = standingPosition;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerLife playerLife = other.GetComponent<PlayerLife>();

        if (playerLife == null || IsActivated)
        {
            return;
        }

        IsActivated = true;
        playerLife.SetCheckpoint(configuredRespawn ?? transform.position + Vector3.up * 0.75f);

        PirateWorldVisual visual = GetComponent<PirateWorldVisual>();
        if (visual != null)
        {
            BoxCollider2D bounds = GetComponent<BoxCollider2D>();
            PirateWorldArt.ApplyTo(gameObject, PirateArtKind.CheckpointLit, bounds != null ? bounds.size : new Vector2(.8f,1.5f));
        }

        SpriteRenderer spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            spriteRenderer.sprite = PirateWorldArt.GetSprite(PirateArtKind.CheckpointLit);
            spriteRenderer.color = Color.white;
        }

        Activated?.Invoke(this);
    }
}
