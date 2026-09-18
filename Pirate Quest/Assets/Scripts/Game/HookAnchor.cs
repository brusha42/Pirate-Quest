using UnityEngine;

[DisallowMultipleComponent]
public class HookAnchor : MonoBehaviour
{
    [SerializeField] private bool canGrab = true;

    private SpriteRenderer spriteRenderer;
    private Color baseColor;
    private Vector3 baseScale;
    public bool RequiresRangedHook { get; private set; }
    public float ChainLength { get; private set; }

    public bool CanGrab => canGrab && isActiveAndEnabled;

    private void Awake()
    {
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        baseColor = spriteRenderer != null ? spriteRenderer.color : Color.white;
        baseScale = transform.localScale;
    }

    public void Configure(bool requiresRangedHook = false, float chainLength = 0f)
    {
        RequiresRangedHook = requiresRangedHook;
        ChainLength = Mathf.Max(0f, chainLength);
        spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (spriteRenderer != null) baseColor = spriteRenderer.color;
    }

    public Vector2 NearestChainPoint(Vector2 playerPosition)
    {
        Vector2 pivot = transform.position;
        return new Vector2(pivot.x, Mathf.Clamp(playerPosition.y, pivot.y - ChainLength, pivot.y));
    }

    public void SetHighlighted(bool highlighted)
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = highlighted
                ? new Color(1f, 0.9f, 0.18f, 1f)
                : baseColor;
        }

    }
}
