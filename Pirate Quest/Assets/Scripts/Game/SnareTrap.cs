using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class SnareTrap : MonoBehaviour
{
    public bool IsCut { get; private set; }
    public bool HasVictim => victim != null;
    public float TightenProgress => victim == null ? 0f : Mathf.Clamp01((Time.time - caughtAt) / grace);
    private float grace = 1.1f;
    private float caughtAt;
    private PlayerLife victim;
    private Vector2 snaredPosition;
    private float victimGravity;
    private SpriteRenderer visual;
    private Color baseTint;
    private LineRenderer tether;
    private Material tetherMaterial;
    public bool HasOverheadAttachment => tether != null;
    public Vector2 AttachmentPoint { get; private set; }
    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
        visual = GetComponentInChildren<SpriteRenderer>();
        if (visual != null) baseTint = visual.color;
    }
    public void Configure(float grace = 1.1f) => this.grace = Mathf.Max(0.5f, grace);
    public void AttachToOverhead(Vector2 point)
    {
        AttachmentPoint = point;
        PirateWorldVisual artwork = GetComponent<PirateWorldVisual>();
        float loopTop = artwork != null ? artwork.OpaqueWorldBounds.max.y : GetComponent<Collider2D>().bounds.max.y;
        if (point.y < loopTop) return;
        GameObject rope = new GameObject("Snare tether to underside of real platform");
        rope.transform.SetParent(transform, false);
        tether = rope.AddComponent<LineRenderer>();
        tetherMaterial = new Material(Shader.Find("Sprites/Default"));
        tether.sharedMaterial = tetherMaterial;
        tether.useWorldSpace = true;
        tether.positionCount = 3;
        tether.startWidth = tether.endWidth = .045f;
        tether.startColor = new Color(.47f, .32f, .16f);
        tether.endColor = new Color(.65f, .46f, .24f);
        tether.sortingOrder = 3;
        tether.SetPosition(0, new Vector3(point.x, point.y + .025f));
        tether.SetPosition(1, new Vector3(point.x, (point.y + loopTop) * .5f));
        tether.SetPosition(2, new Vector3(transform.position.x, loopTop - .04f));
    }
    private void OnTriggerEnter2D(Collider2D other) => TryCatch(other);
    private void OnTriggerStay2D(Collider2D other) => TryCatch(other);
    private void TryCatch(Collider2D other)
    {
        if (IsCut || victim != null) return;
        PlayerLife player = other.GetComponentInParent<PlayerLife>();
        if (player == null || player.IsRespawning || player.IsSnared || player.IsExitProtected ||
            player.IsModalProtected) return;
        victim = player;
        victim.SetSnared(true);
        victim.Died += Release;
        victim.CheckpointRestoring += Release;
        caughtAt = Time.time;
        Rigidbody2D body = victim.GetComponent<Rigidbody2D>();
        snaredPosition = body.position;
        victim.GetComponent<PlayerMovement>().CancelBurstMotion();
        victimGravity = body.gravityScale;
        body.gravityScale = 0f;
        victim.GetComponent<PlayerGrapple>()?.Detach();
    }
    private void FixedUpdate()
    {
        if (victim == null) return;
        if (victim.IsRespawning) { Release(); return; }
        Rigidbody2D body = victim.GetComponent<Rigidbody2D>();
        body.position = snaredPosition;
        body.linearVelocity = Vector2.zero;
        if (visual != null) visual.color = Color.Lerp(baseTint, Color.red, TightenProgress);
        if (Time.time - caughtAt < grace) return;
        PlayerLife doomed = victim;
        Release();
        doomed.Die();
    }
    public void Cut()
    {
        if (IsCut) return;
        IsCut = true;
        Release();
        GetComponent<Collider2D>().enabled = false;
        if (visual != null) visual.enabled = false;
        if (tether != null) tether.enabled = false;
    }
    private void Release()
    {
        if (victim != null)
        {
            victim.Died -= Release;
            victim.CheckpointRestoring -= Release;
            victim.SetSnared(false);
            victim.GetComponent<Rigidbody2D>().gravityScale = victimGravity;
        }
        victim = null;
        if (visual != null) visual.color = baseTint;
    }
    private void OnDisable() => Release();
    private void OnDestroy() { if (tetherMaterial != null) Destroy(tetherMaterial); }
}
