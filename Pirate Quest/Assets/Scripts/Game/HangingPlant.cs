using UnityEngine;
using System;

[DisallowMultipleComponent]
public class HangingPlant : MonoBehaviour
{
    public bool IsDead { get; private set; }
    public bool IsWindingUp { get; private set; }
    public bool IsAttacking => Time.time < attackUntil;
    public int AttacksStarted { get; private set; }
    private Transform target;
    private Vector2 lockedTarget;
    private float phaseUntil;
    private float attackUntil;
    private Vector3 restPosition;
    private SpriteRenderer visual;
    private Color baseTint;
    private PolygonCollider2D bodyHitbox;
    private Action onDefeated;
    public void SetDefeatCallback(Action callback) => onDefeated = callback;
    private readonly PirateSpriteBodyGeometry.Placement bodyPlacement = new PirateSpriteBodyGeometry.Placement();
    public Collider2D BodyHitbox => bodyHitbox != null ? bodyHitbox : GetComponent<Collider2D>();
    public void Initialize(Transform playerTarget)
    {
        target = playerTarget;
        restPosition = transform.position;
        phaseUntil = Time.time + 1f;
        visual = GetComponentInChildren<SpriteRenderer>();
        if (visual != null) baseTint = visual.color;
        bodyHitbox = PirateSpriteBodyGeometry.Install(gameObject, visual);
        RefreshBodyHitbox();
    }
    private void LateUpdate() => RefreshBodyHitbox();
    public void RefreshBodyHitbox()
    {
        if (IsDead || bodyHitbox == null || visual == null || !visual.enabled) return;
        PirateWorldVisual art = GetComponent<PirateWorldVisual>();
        if (art != null) art.RefreshPlacement();
        if (!bodyPlacement.HasChanged(visual)) return;
        PirateArtKind frame = visual.sprite == PirateWorldArt.GetSprite(PirateArtKind.PlantAttack)
            ? PirateArtKind.PlantAttack : PirateArtKind.Plant;
        PirateSpriteBodyGeometry.Fit(bodyHitbox, visual,
            PirateWorldArt.Library.GetOpaqueBounds(frame), PirateSpriteBodyGeometry.PlantBody);
    }
    private void Update()
    {
        if (IsDead || target == null || Time.timeScale <= 0f) return;
        if (IsAttacking)
        {
            transform.position = Vector3.MoveTowards(transform.position, lockedTarget, 10f * Time.deltaTime);
            return;
        }
        transform.position = Vector3.MoveTowards(transform.position, restPosition, 5f * Time.deltaTime);
        if (Time.time < phaseUntil) return;
        if (IsWindingUp)
        {
            IsWindingUp = false;
            attackUntil = Time.time + 0.45f;
            phaseUntil = attackUntil + 1.4f;
            AttacksStarted++;
            if (visual != null) visual.color = baseTint;
            return;
        }
        if (Vector2.Distance(target.position, restPosition) > 5f || target.position.y > restPosition.y + 1f) return;
        lockedTarget = target.position;
        IsWindingUp = true;
        phaseUntil = Time.time + 0.8f;
        if (visual != null) visual.color = new Color(1f, 0.45f, 0.35f);
    }
    private void OnTriggerEnter2D(Collider2D other) => Touch(other);
    private void OnTriggerStay2D(Collider2D other) => Touch(other);
    private void Touch(Collider2D other)
    {
        if (IsDead || Time.timeScale <= 0f) return;
        PlayerLife player = other.GetComponentInParent<PlayerLife>();
        if (player == null || player.IsModalProtected) return;
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        if (abilities != null && abilities.IsAttacking) HitBySaber();
        else
        {
            if (PirateFrontEnd.IsAutomationRun && !player.IsRespawning && !player.IsExitProtected)
            {
                Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
                Collider2D capsule = player.GetComponent<Collider2D>();
                Collider2D hitbox = BodyHitbox;
                Debug.Log($"PIRATE_BODY_HAZARD_CONTACT kind={nameof(HangingPlant)} hazardPosition={transform.position} " +
                    $"playerPosition={(playerBody != null ? playerBody.position : (Vector2)player.transform.position)} " +
                    $"velocity={(playerBody != null ? playerBody.linearVelocity : Vector2.zero)} capsule={capsule?.bounds} " +
                    $"hitboxType={hitbox?.GetType().Name} hitbox={hitbox?.bounds} enabled={hitbox != null && hitbox.enabled} " +
                    $"trigger={hitbox != null && hitbox.isTrigger} windingUp={IsWindingUp} attacking={IsAttacking} dead={IsDead} " +
                    $"modal={player.IsModalProtected} timeScale={Time.timeScale} beforeActualDie=True");
            }
            player.Die();
        }
    }
    public void HitBySaber()
    {
        if (IsDead) return;
        IsDead = true;
        foreach (Collider2D collider in GetComponentsInChildren<Collider2D>()) collider.enabled = false;
        if (visual != null) visual.enabled = false;
        onDefeated?.Invoke();
    }
}
