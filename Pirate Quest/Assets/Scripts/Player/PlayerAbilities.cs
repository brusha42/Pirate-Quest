using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public enum PirateUpgrade { Hook1, Hook2, SpringLeg, DoubleJump, Saber1, Saber2, Parrot }
public enum HazardKind { Lethal, Spikes }

[DefaultExecutionOrder(-10)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(Rigidbody2D))]
public class PlayerAbilities : MonoBehaviour
{
    public int HookLevel { get; private set; }
    public int LegLevel { get; private set; } = 1;
    public int SaberLevel { get; private set; }
    public bool HasParrot { get; private set; }
    public bool SpringAvailable { get; private set; }
    public bool IsSpikeSliding { get; private set; }
    public bool IsAttacking => Time.time < attackUntil;
    public bool IsScouting => Scout != null && Scout.IsScouting;
    public Transform ScoutTransform => Scout != null ? Scout.transform : null;
    public ParrotScout Scout { get; private set; }
    public event Action<PirateUpgrade> Upgraded;
    public event Action<bool> ScoutChanged;
    public event Action SaberSwung;
    public event Action SpringBounced;

    private PlayerMovement movement;
    private Rigidbody2D body;
    private Collider2D playerCollider;
    private float attackUntil;
    private float nextAttack;
    private float spikeGraceUntil;
    private InstantKillHazard bouncedHazard;
    private float slidingUntil;
    private float slideSpeed;
    private bool automationEnabled;
    private bool automationSlide;
    private bool automationAttackPressed;
    private bool automationScoutPressed;
    private readonly Collider2D[] nearby = new Collider2D[48];
    private readonly RaycastHit2D[] swordHits = new RaycastHit2D[16];
    private readonly HashSet<BrineCrawler> struckCrawlers = new HashSet<BrineCrawler>();

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        body = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        GameObject bird = new GameObject("Pirate companion - parrot");
        bird.transform.SetParent(transform, false);
        Scout = bird.AddComponent<ParrotScout>();
        Scout.Initialize(this);
        if (GetComponent<PirateEquipmentVisual>() == null) gameObject.AddComponent<PirateEquipmentVisual>();
    }

    public bool Has(PirateUpgrade upgrade)
    {
        switch (upgrade)
        {
            case PirateUpgrade.Hook1: return HookLevel >= 1;
            case PirateUpgrade.Hook2: return HookLevel >= 2;
            case PirateUpgrade.SpringLeg: return LegLevel >= 2;
            case PirateUpgrade.DoubleJump: return LegLevel >= 3;
            case PirateUpgrade.Saber1: return SaberLevel >= 1;
            case PirateUpgrade.Saber2: return SaberLevel >= 2;
            case PirateUpgrade.Parrot: return HasParrot;
            default: return false;
        }
    }

    public void Apply(PirateUpgrade upgrade, bool notify = true)
    {
        bool alreadyOwned = Has(upgrade);
        switch (upgrade)
        {
            case PirateUpgrade.Hook1: HookLevel = Mathf.Max(HookLevel, 1); break;
            case PirateUpgrade.Hook2: HookLevel = 2; break;
            case PirateUpgrade.SpringLeg: LegLevel = Mathf.Max(LegLevel, 2); SpringAvailable = true; break;
            case PirateUpgrade.DoubleJump: LegLevel = 3; SpringAvailable = true; movement.SetDoubleJumpUnlocked(true); break;
            case PirateUpgrade.Saber1: SaberLevel = Mathf.Max(SaberLevel, 1); break;
            case PirateUpgrade.Saber2: SaberLevel = 2; break;
            case PirateUpgrade.Parrot: HasParrot = true; break;
        }
        if (!alreadyOwned && notify)
        {
            PirateAudio.Play(PirateSound.Pickup);
            Upgraded?.Invoke(upgrade);
        }
    }

    public PirateUpgrade[] CaptureProgression()
    {
        var earned = new System.Collections.Generic.List<PirateUpgrade>();
        foreach (PirateUpgrade upgrade in Enum.GetValues(typeof(PirateUpgrade)))
            if (Has(upgrade)) earned.Add(upgrade);
        return earned.ToArray();
    }

    public void RestoreProgression(PirateUpgrade[] earned)
    {
        ResetProgression();
        if (earned != null) foreach (PirateUpgrade upgrade in earned) Apply(upgrade, false);
    }

    public void ResetProgression()
    {
        HookLevel = 0;
        LegLevel = 1;
        SaberLevel = 0;
        HasParrot = false;
        movement.SetDoubleJumpUnlocked(false);
        ResetTransientState();
    }

    public void ResetTransientState()
    {
        Scout?.ReturnToPirate();
        IsSpikeSliding = false;
        slideSpeed = 0f;
        slidingUntil = 0f;
        attackUntil = nextAttack = 0f;
        struckCrawlers.Clear();
        spikeGraceUntil = 0f;
        bouncedHazard = null;
        SpringAvailable = LegLevel >= 2;
    }

    private void Update()
    {
        if (Time.timeScale <= 0f || PirateHUD.ConsumedInputThisFrame ||
            (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking))
        {
            automationAttackPressed = automationScoutPressed = false;
            return;
        }
        Keyboard keyboard = Keyboard.current;
        bool scoutPressed = automationEnabled ? automationScoutPressed : keyboard != null && keyboard.qKey.wasPressedThisFrame;
        automationScoutPressed = false;
        if (scoutPressed && HasParrot)
            Scout.Toggle();
        if (!movement.ControlsEnabled || IsScouting) { automationAttackPressed = false; return; }
        bool attackPressed = automationEnabled ? automationAttackPressed : Mouse.current != null &&
            Mouse.current.leftButton.wasPressedThisFrame && !PirateHUD.PointerBlocksWorld;
        automationAttackPressed = false;
        if (attackPressed) TryAttack();
        if (IsAttacking) StrikeNearby();
    }

    private void FixedUpdate()
    {
        if (movement.IsGrounded && !HasSpikeAtFeet()) SpringAvailable = LegLevel >= 2;
        IsSpikeSliding = Time.time < slidingUntil && SlideRequested && movement.ControlsEnabled;
        if (!IsSpikeSliding) slideSpeed = 0f;
    }

    public bool SlideRequested => SaberLevel >= 2 &&
        (automationEnabled ? automationSlide : Keyboard.current != null && Keyboard.current.sKey.isPressed) &&
        Mathf.Abs(movement.ReadMovementInput().x) > 0.1f && !IsScouting && !movement.IsDashing;

    public bool TryAttack()
    {
        if (SaberLevel < 1 || !movement.ControlsEnabled || IsScouting || Time.time < nextAttack ||
            Time.timeScale <= 0f || PirateHUD.ConsumedInputThisFrame ||
            (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking)) return false;
        struckCrawlers.Clear();
        attackUntil = Time.time + 0.18f;
        nextAttack = Time.time + 0.32f;
        SaberSwung?.Invoke();
        PirateAudio.Play(PirateSound.Saber);
        StrikeNearby();
        return true;
    }

    private void StrikeNearby()
    {
        Vector2 facing = movement.IsFacingRight ? Vector2.right : Vector2.left;
        Vector2 center = body.position + facing * 0.85f;
        int count = Physics2D.OverlapCircle(center, 1.05f, new ContactFilter2D { useTriggers = true }, nearby);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = nearby[i];
            if (hit == null || hit.attachedRigidbody == body || !HasClearSwordArc(hit)) continue;
            hit.GetComponentInParent<SaberCuttable>()?.Cut();
            hit.GetComponentInParent<HangingPlant>()?.HitBySaber();
            BrineCrawler crawler = hit.GetComponentInParent<BrineCrawler>();
            if (crawler != null && struckCrawlers.Add(crawler)) crawler.Strike();
            hit.GetComponentInParent<SnareTrap>()?.Cut();
        }
    }

    private bool HasClearSwordArc(Collider2D target)
    {
        Vector2 destination = target.ClosestPoint(body.position);
        Vector2 offset = destination - body.position;
        if (offset.sqrMagnitude < 0.001f) return true;
        int count = Physics2D.Raycast(body.position, offset.normalized,
            new ContactFilter2D { useTriggers = false, useLayerMask = true, layerMask = movement.GroundLayer },
            swordHits, offset.magnitude);
        for (int i = 0; i < count; i++)
        {
            Collider2D obstacle = swordHits[i].collider;
            if (obstacle == target || obstacle.attachedRigidbody == body || obstacle.transform.IsChildOf(target.transform)) continue;
            PlatformEffector2D effector = obstacle.usedByEffector ? obstacle.GetComponent<PlatformEffector2D>() : null;
            if (effector != null && effector.useOneWay) continue;
            return false;
        }
        return count < swordHits.Length;
    }

    public bool TrySurviveSpike(InstantKillHazard hazard, Collider2D hazardCollider)
    {
        if (!movement.ControlsEnabled || hazardCollider == null) return false;
        if (hazard == bouncedHazard && Time.time < spikeGraceUntil) return true;
        float feet = playerCollider.bounds.min.y;
        float top = hazardCollider.bounds.max.y;
        bool topContact = feet >= top - 0.48f && body.linearVelocity.y <= 1f;
        if (topContact && SlideRequested)
        {
            IsSpikeSliding = true;
            slidingUntil = Time.time + Time.fixedDeltaTime * 2.5f;
            slideSpeed = Mathf.MoveTowards(Mathf.Max(slideSpeed, movement.MoveSpeed), 19f, 11f * Time.fixedDeltaTime);
            body.position = new Vector2(body.position.x, top + playerCollider.bounds.extents.y + 0.015f);
            body.linearVelocity = new Vector2(Mathf.Sign(movement.ReadMovementInput().x) * slideSpeed, 0f);
            return true;
        }
        if (topContact && LegLevel >= 2 && SpringAvailable)
        {
            SpringAvailable = false;
            bouncedHazard = hazard;
            spikeGraceUntil = Time.time + 0.2f;
            body.position = new Vector2(body.position.x, top + playerCollider.bounds.extents.y + 0.02f);
            movement.LaunchSpringBounce(18f);
            SpringBounced?.Invoke();
            PirateAudio.Play(PirateSound.Spring);
            return true;
        }
        return false;
    }

    private bool HasSpikeAtFeet()
    {
        Vector2 point = new Vector2(playerCollider.bounds.center.x, playerCollider.bounds.min.y);
        int count = Physics2D.OverlapBox(point, new Vector2(playerCollider.bounds.size.x, 0.35f), 0f,
            new ContactFilter2D { useTriggers = true }, nearby);
        for (int i = 0; i < count; i++)
        {
            InstantKillHazard hazard = nearby[i].GetComponentInParent<InstantKillHazard>();
            if (hazard != null && hazard.Kind == HazardKind.Spikes) return true;
        }
        return false;
    }

    public void SetAutomationSlide(bool slide) { automationEnabled = true; automationSlide = slide; }
    public void SetAutomationAttackPressed() { automationEnabled = true; automationAttackPressed = true; }
    public void SetAutomationScoutPressed() { automationEnabled = true; automationScoutPressed = true; }
    public void ClearAutomationInputOverride() { automationEnabled = false; automationSlide = false; automationAttackPressed = false; automationScoutPressed = false; }
    public void NotifyScoutChanged(bool value) => ScoutChanged?.Invoke(value);
    private void OnDestroy()
    {
        if (Scout != null && Scout.transform.parent == null) Destroy(Scout.gameObject);
    }
}
