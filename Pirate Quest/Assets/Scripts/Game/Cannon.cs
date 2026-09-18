using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class Cannon : MonoBehaviour
{
    [SerializeField, Min(0.25f)] private float fireInterval = 2.4f;
    [SerializeField, Min(1f)] private float activationRange = 16f;
    [SerializeField, Min(1f)] private float projectileSpeed = 8f;

    private readonly List<GameObject> projectiles = new List<GameObject>();
    private Transform target;
    private Sprite projectileSprite;
    private float nextFireTime;
    private SpriteRenderer visual;
    private Color baseTint;
    private Vector2 aimDirection;
    private Vector2 shotOrigin;
    private LineRenderer aimLine;
    private Material aimMaterial;
    private float fireFlashUntil;
    private PolygonCollider2D bodyHitbox;
    private readonly PirateSpriteBodyGeometry.Placement bodyPlacement = new PirateSpriteBodyGeometry.Placement();
    public Collider2D BodyHitbox => bodyHitbox != null ? bodyHitbox : GetComponent<Collider2D>();
    public bool IsTelegraphing { get; private set; }
    public bool IsFiring => Time.time < fireFlashUntil;
    public int ShotsFired { get; private set; }
    public float ActivationRange => activationRange;
    public float TelegraphDuration => 0.75f;
    public float TelegraphTimeRemaining => IsTelegraphing ? Mathf.Max(0f,nextFireTime-Time.time) : 0f;
    public const float ProjectileRadius = .21f;
    public Vector2 LockedShotOrigin => shotOrigin;
    public Vector2 LockedShotDirection => aimDirection;
    public Vector2 TelegraphEnd { get; private set; }
    public Vector2 LastShotOrigin { get; private set; }
    public Vector2 LastShotDirection { get; private set; }
    public void SetActivationRange(float range) => activationRange = Mathf.Max(1f, range);

    public void Initialize(Transform playerTarget, Sprite runtimeSprite)
    {
        target = playerTarget;
        projectileSprite = runtimeSprite;
        visual = GetComponentInChildren<SpriteRenderer>();
        if (visual != null)
        {
            baseTint = visual.color;
            visual.flipX = target != null && target.position.x > transform.position.x;
        }
        bodyHitbox = PirateSpriteBodyGeometry.Install(gameObject, visual);
        RefreshBodyHitbox();
        nextFireTime = Time.time + 0.9f;
        aimLine = gameObject.AddComponent<LineRenderer>();
        aimLine.positionCount = 2;
        aimLine.useWorldSpace = true;
        aimLine.startWidth = aimLine.endWidth = 0.035f;
        aimLine.startColor = new Color(1f, 0.28f, 0.15f, 0.8f);
        aimLine.endColor = new Color(1f, 0.4f, 0.15f, 0.15f);
        aimLine.sortingOrder = 23;
        aimLine.enabled = false;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null) { aimMaterial = new Material(shader); aimLine.material = aimMaterial; }
    }

    private void LateUpdate() => RefreshBodyHitbox();

    public void RefreshBodyHitbox()
    {
        if (bodyHitbox == null || visual == null || !visual.enabled) return;
        PirateWorldVisual art = GetComponent<PirateWorldVisual>();
        if (art != null) art.RefreshPlacement();
        if (!bodyPlacement.HasChanged(visual)) return;
        PirateArtKind frame = visual.sprite == PirateWorldArt.GetSprite(PirateArtKind.CannonFire)
            ? PirateArtKind.CannonFire : PirateArtKind.Cannon;
        PirateSpriteBodyGeometry.Fit(bodyHitbox, visual,
            PirateWorldArt.Library.GetLayoutBounds(frame), PirateSpriteBodyGeometry.CannonBody);
    }

    private void OnTriggerEnter2D(Collider2D other) => TouchBody(other);
    private void OnTriggerStay2D(Collider2D other) => TouchBody(other);
    private void TouchBody(Collider2D other)
    {
        if (Time.timeScale <= 0f) return;
        PlayerLife player = other.GetComponentInParent<PlayerLife>();
        if (player != null && !player.IsModalProtected)
        {
            if (PirateFrontEnd.IsAutomationRun && !player.IsRespawning && !player.IsExitProtected)
            {
                Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
                Collider2D capsule = player.GetComponent<Collider2D>();
                Collider2D hitbox = BodyHitbox;
                Debug.Log($"PIRATE_BODY_HAZARD_CONTACT kind={nameof(Cannon)} hazardPosition={transform.position} " +
                    $"playerPosition={(playerBody != null ? playerBody.position : (Vector2)player.transform.position)} " +
                    $"velocity={(playerBody != null ? playerBody.linearVelocity : Vector2.zero)} capsule={capsule?.bounds} " +
                    $"hitboxType={hitbox?.GetType().Name} hitbox={hitbox?.bounds} enabled={hitbox != null && hitbox.enabled} " +
                    $"trigger={hitbox != null && hitbox.isTrigger} telegraphing={IsTelegraphing} firing={IsFiring} " +
                    $"modal={player.IsModalProtected} timeScale={Time.timeScale} beforeActualDie=True");
            }
            player.Die();
        }
    }

    private void Update()
    {
        if (target == null || Time.timeScale <= 0f) return;
        if (IsTelegraphing)
        {
            if (Time.time < nextFireTime) return;
            Fire(shotOrigin,aimDirection);
            IsTelegraphing = false;
            aimLine.enabled = false;
            if (visual != null) visual.color = baseTint;
            nextFireTime = Time.time + fireInterval;
            return;
        }
        if (Time.time < nextFireTime) return;

        Vector2 offset = target.position - transform.position;
        if (offset.sqrMagnitude > activationRange * activationRange)
        {
            return;
        }

        if (visual != null) visual.flipX = offset.x > 0f;
        PirateWorldVisual artwork = GetComponent<PirateWorldVisual>();
        shotOrigin = transform.position;
        if (artwork != null && artwork.TryGetCannonMuzzle(out Vector2 muzzle)) shotOrigin = muzzle;
        Vector2 muzzleToTarget = (Vector2)target.position-shotOrigin;
        aimDirection = muzzleToTarget.sqrMagnitude > .0001f ? muzzleToTarget.normalized :
            (offset.x >= 0f ? Vector2.right : Vector2.left);
        IsTelegraphing = true;
        nextFireTime = Time.time + TelegraphDuration;
        if (visual != null) visual.color = new Color(1f, 0.55f, 0.3f);
        float travel = projectileSpeed*Cannonball.DefaultLifetime;
        foreach (RaycastHit2D hit in Physics2D.CircleCastAll(shotOrigin,ProjectileRadius,aimDirection,travel))
        {
            Collider2D obstacle = hit.collider;
            if (obstacle == null || obstacle.isTrigger || obstacle.transform == transform ||
                obstacle.transform.IsChildOf(transform) || obstacle.GetComponentInParent<PlayerLife>() != null) continue;
            travel = Mathf.Min(travel,hit.distance);
        }
        TelegraphEnd = shotOrigin+aimDirection*travel;
        aimLine.SetPosition(0, shotOrigin);
        aimLine.SetPosition(1, TelegraphEnd);
        aimLine.enabled = true;
    }

    private void OnDisable()
    {
        foreach (GameObject projectile in projectiles)
        {
            if (projectile != null)
            {
                Destroy(projectile);
            }
        }
        projectiles.Clear();
        if (aimLine != null) aimLine.enabled = false;
    }
    private void OnDestroy() { if (aimMaterial != null) Destroy(aimMaterial); }

    private void Fire(Vector2 origin, Vector2 direction)
    {
        ShotsFired++;
        PirateAudio.Play(PirateSound.Cannon);
        fireFlashUntil = Time.time + 0.15f;
        if (direction.sqrMagnitude < 0.01f)
        {
            direction = Vector2.right;
        }

        LastShotOrigin = origin;
        LastShotDirection = direction;
        GameObject projectile = new GameObject("Cannonball");
        projectile.transform.position = origin;

        GameObject art = new GameObject("Centred cannonball artwork");
        art.transform.SetParent(projectile.transform,false);
        SpriteRenderer renderer = art.AddComponent<SpriteRenderer>();
        renderer.sprite = projectileSprite;
        if (projectileSprite != null)
        {
            Rect opaque = projectileSprite == PirateWorldArt.GetSprite(PirateArtKind.Cannonball) && PirateWorldArt.Library != null
                ? PirateWorldArt.Library.GetOpaqueBounds(PirateArtKind.Cannonball)
                : new Rect(projectileSprite.bounds.min,projectileSprite.bounds.size);
            float scale = ProjectileRadius*2f/Mathf.Max(opaque.width,opaque.height);
            art.transform.localScale = Vector3.one*scale;
            art.transform.localPosition = -opaque.center*scale;
        }
        renderer.sortingOrder = 21;

        CircleCollider2D collider = projectile.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = ProjectileRadius;

        Rigidbody2D projectileBody = projectile.AddComponent<Rigidbody2D>();
        projectileBody.gravityScale = 0f;
        projectileBody.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        projectileBody.linearVelocity = direction * projectileSpeed;

        projectile.AddComponent<InstantKillHazard>();
        Cannonball cannonball = projectile.AddComponent<Cannonball>();
        cannonball.Initialize(gameObject);
        projectiles.RemoveAll(item => item == null);
        projectiles.Add(projectile);
    }
}

public static class PirateSpriteBodyGeometry
{
    public sealed class Placement
    {
        private Sprite sprite;
        private bool flipX, flipY;
        private Vector3 position, scale;
        public bool HasChanged(SpriteRenderer renderer)
        {
            bool changed = sprite != renderer.sprite || flipX != renderer.flipX || flipY != renderer.flipY ||
                position != renderer.transform.localPosition || scale != renderer.transform.localScale;
            sprite = renderer.sprite; flipX = renderer.flipX; flipY = renderer.flipY;
            position = renderer.transform.localPosition; scale = renderer.transform.localScale;
            return changed;
        }
    }
    public static readonly Vector2[] CannonBody =
    {
        new Vector2(.04f,.60f), new Vector2(.04f,.91f), new Vector2(.16f,.93f),
        new Vector2(.24f,.87f), new Vector2(.80f,.87f), new Vector2(.90f,.72f),
        new Vector2(.87f,.51f), new Vector2(.96f,.30f), new Vector2(.97f,.16f),
        new Vector2(.90f,.04f), new Vector2(.48f,.04f), new Vector2(.35f,.15f),
        new Vector2(.26f,.16f), new Vector2(.23f,.26f), new Vector2(.29f,.48f),
        new Vector2(.29f,.58f)
    };
    public static readonly Vector2[] PlantBody =
    {
        new Vector2(.14f,.77f), new Vector2(.27f,.90f), new Vector2(.50f,.95f),
        new Vector2(.74f,.90f), new Vector2(.86f,.79f), new Vector2(.62f,.68f),
        new Vector2(.62f,.47f), new Vector2(.78f,.34f), new Vector2(.83f,.17f),
        new Vector2(.71f,.06f), new Vector2(.44f,.03f), new Vector2(.21f,.10f),
        new Vector2(.15f,.21f), new Vector2(.23f,.35f), new Vector2(.43f,.47f),
        new Vector2(.43f,.68f)
    };
    public static PolygonCollider2D Install(GameObject owner, SpriteRenderer renderer)
    {
        if (renderer == null || renderer.sprite == null || PirateWorldArt.Library == null) return null;
        foreach (Collider2D old in owner.GetComponents<Collider2D>()) old.enabled = false;
        var collider = owner.AddComponent<PolygonCollider2D>();
        collider.isTrigger = true;
        collider.pathCount = 1;
        return collider;
    }
    public static void Fit(PolygonCollider2D collider, SpriteRenderer renderer, Rect opaque, Vector2[] profile)
    {
        var points = new Vector2[profile.Length];
        for (int i = 0; i < profile.Length; i++)
        {
            Vector2 local = opaque.min + Vector2.Scale(profile[i], opaque.size);
            if (renderer.flipX) local.x = -local.x;
            if (renderer.flipY) local.y = -local.y;
            points[i] = collider.transform.InverseTransformPoint(renderer.transform.TransformPoint(local));
        }
        collider.SetPath(0, points);
    }
}
