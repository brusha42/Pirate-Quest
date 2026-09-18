using UnityEngine;
using System;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class BrineCrawler : MonoBehaviour
{
    private enum Phase { Patrol, Windup, Attack, Recovery, Defeated }
    public const float MinimumSupportWidth = BrineCrawlerArtLibrary.MaximumWorldWidth + 0.2f;
    public const float WindupDuration = 0.6f;
    public const float LungeDuration = 0.35f;
    public const float RecoveryDuration = 1.2f;
    public const float LungeSpeed = 6f;
    public const int MaximumHealth = 3;
    private const float PatrolSpeed = 0.85f;
    private const float DetectionRange = 3.4f;
    private Rigidbody2D body;
    private BoxCollider2D hitbox;
    private SpriteRenderer visual;
    private BrineCrawlerArtLibrary art;
    private PlayerLife target;
    private Rect support;
    private Phase phase;
    private float phaseRemaining;
    private float minX, maxX;
    private Vector2 restPosition;
    private int initialFacing;
    private bool configured;
    private float hitFlashUntil;
    private Action onDefeated;
    private readonly RaycastHit2D[] hits = new RaycastHit2D[24];

    public bool IsConfigured => configured;
    public int Health { get; private set; } = MaximumHealth;
    public bool IsDefeated => phase == Phase.Defeated;
    public bool IsWindingUp => configured && phase == Phase.Windup;
    public bool IsAttacking => configured && isActiveAndEnabled && phase == Phase.Attack;
    public int AttacksStarted { get; private set; }
    public int Facing { get; private set; } = 1;
    public BrineCrawlerFrame CurrentFrame { get; private set; }
    public Rect SupportBounds => support;
    public SpriteRenderer Renderer => visual;
    public Vector2 BodySize => hitbox != null ? hitbox.size : Vector2.zero;
    public Rect OpaqueWorldBounds
    {
        get
        {
            Rect bounds = art != null ? art.GetOpaqueBounds(CurrentFrame) : default;
            return new Rect((Vector2)transform.position + bounds.position, bounds.size);
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        hitbox = GetComponent<BoxCollider2D>();
        hitbox.size = new Vector2(1.85f, 1.08f);
        hitbox.offset = Vector2.up * (hitbox.size.y * 0.5f);
        hitbox.isTrigger = true;
        var artwork = new GameObject("Brine crawler illustrated animation");
        artwork.transform.SetParent(transform, false);
        visual = artwork.AddComponent<SpriteRenderer>();
        visual.sortingOrder = 19;
        art = Resources.Load<BrineCrawlerArtLibrary>("BrineCrawlerArtLibrary");
    }

    public static BrineCrawler Install(Transform parent, Vector2 position, Rect supportBounds, int facing = 1)
    {
        var obj = new GameObject("Brine crawler");
        obj.transform.SetParent(parent, false);
        obj.transform.position = position;
        BrineCrawler crawler = obj.AddComponent<BrineCrawler>();
        crawler.Configure(supportBounds, facing);
        return crawler;
    }

    public void Configure(Rect supportBounds, int facing = 1)
    {
        if (art == null) art = Resources.Load<BrineCrawlerArtLibrary>("BrineCrawlerArtLibrary");
        support = supportBounds;
        configured = art != null && art.IsComplete && support.width >= MinimumSupportWidth && support.height > 0f &&
            Mathf.Abs(transform.lossyScale.x - 1f) < 0.001f && Mathf.Abs(transform.lossyScale.y - 1f) < 0.001f;
        if (!configured)
        {
            hitbox.enabled = visual.enabled = body.simulated = false;
            Debug.LogError("BrineCrawler requires its imported four-frame art library, an unscaled transform, " +
                "and a support at least " + MinimumSupportWidth + " units wide.", this);
            return;
        }
        float margin = BrineCrawlerArtLibrary.MaximumWorldWidth * 0.5f + 0.08f;
        minX = support.xMin + margin;
        maxX = support.xMax - margin;
        initialFacing = facing < 0 ? -1 : 1;
        restPosition = new Vector2(Mathf.Clamp(transform.position.x, minX, maxX), support.yMax);
        target = FindFirstObjectByType<PlayerLife>();
        ResetEncounter();
    }

    public void SetTarget(PlayerLife player) => target = player;
    public void SetDefeatCallback(Action callback) => onDefeated = callback;

    public void ResetEncounter()
    {
        if (!configured) return;
        phase = Phase.Patrol;
        Health = MaximumHealth;
        hitFlashUntil = 0f;
        phaseRemaining = 0f;
        Facing = initialFacing;
        AttacksStarted = 0;
        hitbox.enabled = body.simulated = visual.enabled = true;
        body.linearVelocity = Vector2.zero;
        body.position = restPosition;
        transform.position = restPosition;
        visual.color = Color.white;
        SetFrame(BrineCrawlerFrame.Idle);
    }

    private void FixedUpdate()
    {
        if (!configured || IsDefeated) return;
        switch (phase)
        {
            case Phase.Windup:
                phaseRemaining -= Time.fixedDeltaTime;
                if (phaseRemaining <= 0f)
                {
                    phase = Phase.Attack;
                    phaseRemaining = LungeDuration;
                    AttacksStarted++;
                    SetFrame(BrineCrawlerFrame.Lunge);
                }
                return;
            case Phase.Attack:
                phaseRemaining -= Time.fixedDeltaTime;
                if (phaseRemaining <= 0f || MoveOnSupport(LungeSpeed)) BeginRecovery();
                return;
            case Phase.Recovery:
                phaseRemaining -= Time.fixedDeltaTime;
                if (phaseRemaining <= 0f) phase = Phase.Patrol;
                return;
            default:
                if (TargetInRange())
                {
                    Facing = target.transform.position.x < body.position.x ? -1 : 1;
                    phase = Phase.Windup;
                    phaseRemaining = WindupDuration;
                    body.linearVelocity = Vector2.zero;
                    SetFrame(BrineCrawlerFrame.Windup);
                    return;
                }
                if (MoveOnSupport(PatrolSpeed)) Facing = -Facing;
                return;
        }
    }

    private bool MoveOnSupport(float speed)
    {
        float requested = speed * Time.fixedDeltaTime;
        float nextX = Mathf.Clamp(body.position.x + Facing * requested, minX, maxX);
        float distance = Mathf.Abs(nextX - body.position.x);
        ContactFilter2D filter = new ContactFilter2D { useTriggers = false };
        int count = Physics2D.BoxCast(body.position + hitbox.offset, hitbox.size * 0.98f, 0f,
            Vector2.right * Facing, filter, hits, distance + 0.02f);
        for (int i = 0; i < count; i++)
        {
            Collider2D obstacle = hits[i].collider;
            if (obstacle == null || obstacle.attachedRigidbody == body ||
                obstacle.GetComponentInParent<PlayerLife>() != null ||
                obstacle.bounds.max.y <= support.yMax + 0.025f) continue;
            distance = Mathf.Min(distance, Mathf.Max(0f, hits[i].distance - 0.025f));
        }
        if (count == hits.Length) distance = 0f;
        body.MovePosition(new Vector2(body.position.x + Facing * distance, support.yMax));
        return distance < requested - 0.0005f;
    }

    private bool TargetInRange()
    {
        if (target == null || target.IsRespawning || target.IsExitProtected) return false;
        Vector2 centre = body.position + hitbox.offset;
        Vector2 offset = (Vector2)target.transform.position - centre;
        if (Mathf.Abs(offset.x) > DetectionRange || Mathf.Abs(offset.y) > 1.35f) return false;
        int count = Physics2D.Raycast(centre, offset.normalized,
            new ContactFilter2D { useTriggers = false }, hits, offset.magnitude);
        for (int i = 0; i < count; i++)
        {
            Collider2D obstacle = hits[i].collider;
            if (obstacle != null && obstacle.attachedRigidbody != body &&
                obstacle.GetComponentInParent<PlayerLife>() != target) return false;
        }
        return count < hits.Length;
    }

    private void BeginRecovery()
    {
        phase = Phase.Recovery;
        phaseRemaining = RecoveryDuration;
        body.linearVelocity = Vector2.zero;
        SetFrame(BrineCrawlerFrame.Idle);
    }

    private void LateUpdate()
    {
        if (!configured) return;
        if (IsDefeated)
        {
            Color tint = visual.color;
            tint.a = Mathf.MoveTowards(tint.a, 0f, Time.deltaTime / 0.35f);
            visual.color = tint;
            if (tint.a <= 0f) visual.enabled = false;
        }
        else if (phase == Phase.Patrol)
        {
            SetFrame(Mathf.FloorToInt(Time.time / 0.16f) % 2 == 0 ? BrineCrawlerFrame.Idle : BrineCrawlerFrame.Walk);
        }
        visual.flipX = Facing < 0;
        if (!IsDefeated) visual.color = Time.time < hitFlashUntil ? new Color(1f, .45f, .25f) : Color.white;
    }

    private void SetFrame(BrineCrawlerFrame frame)
    {
        CurrentFrame = frame;
        visual.sprite = art.Get(frame);
        visual.flipX = Facing < 0;
    }

    private void OnTriggerEnter2D(Collider2D other) => Touch(other);
    private void OnTriggerStay2D(Collider2D other) => Touch(other);
    private void Touch(Collider2D other)
    {
        if (!IsAttacking) return;
        PlayerLife player = other.GetComponentInParent<PlayerLife>();
        if (player != null && !player.IsRespawning && !player.IsExitProtected) player.Die();
    }

    public void Strike()
    {
        if (!configured || IsDefeated) return;
        Health--;
        hitFlashUntil = Time.time + .16f;
        if (Health > 0)
        {
            BeginRecovery();
            return;
        }
        phase = Phase.Defeated;
        phaseRemaining = 0f;
        hitbox.enabled = false;
        body.linearVelocity = Vector2.zero;
        body.simulated = false;
        SetFrame(BrineCrawlerFrame.Idle);
        visual.color = new Color(0.6f, 0.7f, 0.7f, 1f);
        onDefeated?.Invoke();
    }

    private void OnDisable()
    {
        if (!configured || IsDefeated) return;
        BeginRecovery();
    }
}
