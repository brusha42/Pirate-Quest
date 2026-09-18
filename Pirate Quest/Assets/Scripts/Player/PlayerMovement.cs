using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-20)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Горизонтальное движение")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [SerializeField, Min(0f)] private float acceleration = 50f;
    [SerializeField, Min(0f)] private float deceleration = 60f;
    [SerializeField, Range(0.1f, 1f)] private float airControl = 0.75f;

    [Header("Прыжок")]
    [SerializeField, Min(0f)] private float jumpForce = 14f;
    [SerializeField, Min(0f)] private float coyoteTime = 0.12f;
    [SerializeField, Min(0f)] private float jumpBufferTime = 0.12f;
    [SerializeField, Range(0.1f, 1f)] private float jumpCutMultiplier = 0.5f;
    [SerializeField] private bool doubleJumpUnlocked;

    [Header("Рывок на земле и в воздухе")]
    [SerializeField, Min(0f)] private float dashSpeed = 18f;
    [SerializeField, Min(0.01f)] private float dashDuration = 0.16f;
    [SerializeField, Min(0f)] private float dashCooldown = 0.1f;

    [Header("Физика и проверка земли")]
    [SerializeField] private Transform groundCheck;
    [SerializeField, Min(0.01f)] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField, Min(0f)] private float gravityScale = 3.5f;

    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private PhysicsMaterial2D movementMaterial;
    private PhysicsMaterial2D originalMaterial;
    private readonly ContactPoint2D[] groundContacts = new ContactPoint2D[16];
    private readonly List<Collider2D> dropCandidates = new List<Collider2D>(4);
    private readonly List<Collider2D> droppedPlatforms = new List<Collider2D>(4);
    private float dropElapsed;
    private bool dropRequested;
    private PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;

    private Vector2 moveInput;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float dashTimer;
    private float dashCooldownTimer;
    private int extraJumpsRemaining;
    private bool dashRequested;
    private bool dashAvailable;
    private bool isDashing;
    private bool isFacingRight = true;
    private bool controlsEnabled = true;
    private bool modalInputBlocked;
    private bool jumpIsActive;
    private PlayerGrapple grapple;
    private bool automationInputEnabled;
    private Vector2 automationMoveInput;
    private bool automationJumpPressed;
    private bool automationJumpReleased;
    private bool automationDashPressed;

    public bool IsGrounded { get; private set; }
    public bool IsDashing => isDashing;
    public bool IsDroppingThrough => droppedPlatforms.Count > 0;
    public bool DoubleJumpUnlocked => doubleJumpUnlocked;
    public Vector2 Velocity => body != null ? body.linearVelocity : Vector2.zero;
    public bool ControlsEnabled => controlsEnabled && !modalInputBlocked && isActiveAndEnabled;
    public bool IsModalInputBlocked => modalInputBlocked;
    public bool IsFacingRight => isFacingRight;
    public float MoveSpeed => moveSpeed;
    public float DashSpeed => dashSpeed;
    public float DashDuration => dashDuration;
    public float DashCooldown => dashCooldown;
    public float GroundAcceleration => acceleration;
    public float AirAcceleration => acceleration * airControl;
    public float Deceleration => deceleration;
    public float AirDeceleration => deceleration * airControl;
    public float JumpLaunchSpeed => jumpForce / (body != null ? body.mass : GetComponent<Rigidbody2D>().mass);
    public float GravityStrength => Mathf.Abs(Physics2D.gravity.y * gravityScale);
    public LayerMask GroundLayer => groundLayer;

    public void ConfigureChapterSpeed(int chapter) => moveSpeed = PirateMovementProfile.ForChapter(chapter);

    private PlayerGrapple Grapple
    {
        get
        {
            if (grapple == null)
            {
                grapple = GetComponent<PlayerGrapple>();
            }

            return grapple;
        }
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        playerInput = GetComponent<PlayerInput>();

        if (bodyCollider != null)
        {
            originalMaterial = bodyCollider.sharedMaterial;
            movementMaterial = new PhysicsMaterial2D("Pirate movement - frictionless")
            {
                hideFlags = HideFlags.DontSave,
                friction = 0f,
                bounciness = 0f
            };
            bodyCollider.sharedMaterial = movementMaterial;
        }

        body.gravityScale = gravityScale;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;

        if (groundCheck == null)
        {
            Debug.LogError("PlayerMovement: не назначен Ground Check.", this);
            enabled = false;
            return;
        }

        moveAction = playerInput.actions.FindAction("Move", true);
        jumpAction = playerInput.actions.FindAction("Jump", true);
        dashAction = playerInput.actions.FindAction("Sprint", true);
    }

    private void OnEnable()
    {
        if (playerInput != null && playerInput.actions != null)
        {
            playerInput.actions.Enable();
        }
    }

    private void OnDestroy()
    {
        if (bodyCollider != null && bodyCollider.sharedMaterial == movementMaterial)
            bodyCollider.sharedMaterial = originalMaterial;
        if (movementMaterial != null) Destroy(movementMaterial);
    }

    private void OnDisable()
    {
        RestoreDroppedPlatforms();
        Grapple?.Detach();
        if (isDashing)
        {
            isDashing = false;
            dashTimer = 0f;
            dashCooldownTimer = Mathf.Max(dashCooldownTimer, dashCooldown);
        }
        dashRequested = false;
        if (body != null)
        {
            body.gravityScale = gravityScale;
        }
    }

    private void Update()
    {
        if (!controlsEnabled || Time.timeScale <= 0f)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = automationInputEnabled ? automationMoveInput : moveAction.ReadValue<Vector2>();
        bool jumpPressed = automationInputEnabled ? automationJumpPressed : jumpAction.WasPressedThisFrame();
        bool jumpReleased = automationInputEnabled ? automationJumpReleased : jumpAction.WasReleasedThisFrame();
        bool dashPressed = automationInputEnabled ? automationDashPressed : dashAction.WasPressedThisFrame();
        automationJumpPressed = false;
        automationJumpReleased = false;
        automationDashPressed = false;

        if (jumpPressed && moveInput.y < -0.5f && IsGrounded)
        {
            dropRequested = true;
            jumpBufferTimer = 0f;
        }
        else if (jumpPressed)
        {
            jumpBufferTimer = jumpBufferTime;
        }
        else
        {
            jumpBufferTimer -= Time.deltaTime;
        }

        if (jumpReleased && jumpIsActive && body.linearVelocity.y > 0f && !isDashing &&
            (Grapple == null || !Grapple.IsAttached))
        {
            body.linearVelocity = new Vector2(body.linearVelocity.x, body.linearVelocity.y * jumpCutMultiplier);
        }

        if (dashPressed && !isDashing && dashCooldownTimer <= 0f)
        {
            dashRequested = true;
        }

        FlipIfNeeded();
    }

    private void FixedUpdate()
    {
        UpdateDroppedPlatforms();
        bool wasGrounded = IsGrounded;
        IsGrounded = HasGroundSupport();
        if (!wasGrounded && IsGrounded && controlsEnabled) PirateAudio.Play(PirateSound.Land);

        if (!controlsEnabled)
        {
            if (isDashing)
            {
                UpdateDash();
            }
            else
            {
                ApplyHorizontalMovement();
            }
            return;
        }

        if (dropRequested)
        {
            TryDropThrough();
            dropRequested = false;
        }

        if (IsGrounded)
        {
            coyoteTimer = coyoteTime;
            if (!isDashing) dashAvailable = true;
            extraJumpsRemaining = doubleJumpUnlocked ? 1 : 0;
        }
        else
        {
            coyoteTimer -= Time.fixedDeltaTime;
        }

        if (body.linearVelocity.y <= 0f || (Grapple != null && Grapple.IsAttached))
        {
            jumpIsActive = false;
        }

        dashCooldownTimer -= Time.fixedDeltaTime;

        if (isDashing)
        {
            UpdateDash();
            return;
        }

        if (dashRequested)
        {
            TryStartDash();
            dashRequested = false;

            if (isDashing)
            {
                return;
            }
        }

        TryJump();
        if (Grapple == null || !Grapple.IsAttached)
        {
            ApplyHorizontalMovement();
        }
    }

    private void ApplyHorizontalMovement()
    {
        PlayerAbilities abilities = GetComponent<PlayerAbilities>();
        if (abilities != null && abilities.IsSpikeSliding) return;
        float targetSpeed = moveInput.x * moveSpeed;
        bool hasInput = Mathf.Abs(targetSpeed) > 0.01f;
        float rate = hasInput ? acceleration : deceleration;

        if (!IsGrounded)
        {
            rate *= airControl;
        }

        float nextSpeed = Mathf.MoveTowards(body.linearVelocity.x, targetSpeed, rate * Time.fixedDeltaTime);
        body.linearVelocity = new Vector2(nextSpeed, body.linearVelocity.y);
    }

    private bool HasGroundSupport()
    {
        if (!body.simulated || bodyCollider == null || !bodyCollider.enabled || body.linearVelocity.y > 0.15f)
        {
            return false;
        }

        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = groundLayer,
            useTriggers = false
        };
        int count = bodyCollider.GetContacts(filter, groundContacts);
        float maximumSupportHeight = bodyCollider.bounds.min.y + Mathf.Min(groundCheckRadius, 0.15f);
        float minimumSupportHeight = bodyCollider.bounds.min.y - Mathf.Min(groundCheckRadius, 0.15f);

        for (int index = 0; index < count; index++)
        {
            ContactPoint2D contact = groundContacts[index];
            if (!contact.enabled || contact.point.y > maximumSupportHeight || contact.point.y < minimumSupportHeight)
            {
                continue;
            }

            Vector2 normal = contact.normal;
            if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f)
            {
                normal = -normal;
            }

            Collider2D support = OtherCollider(contact);
            if (normal.y >= 0.65f && support != null && !droppedPlatforms.Contains(support))
            {
                return true;
            }
        }

        return false;
    }

    private Collider2D OtherCollider(ContactPoint2D contact)
    {
        return contact.collider != null && contact.collider.attachedRigidbody != body
            ? contact.collider : contact.otherCollider;
    }

    private bool TryDropThrough()
    {
        if (!IsGrounded || isDashing || IsDroppingThrough || bodyCollider == null ||
            (Grapple != null && Grapple.IsAttached)) return false;
        PlayerAbilities abilities = GetComponent<PlayerAbilities>();
        if (abilities != null && abilities.IsSpikeSliding) return false;
        ContactFilter2D filter = new ContactFilter2D
        {
            useLayerMask = true, layerMask = groundLayer, useTriggers = false
        };
        int count = bodyCollider.GetContacts(filter, groundContacts);
        dropCandidates.Clear();
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = groundContacts[i];
            Vector2 normal = contact.normal;
            if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f) normal = -normal;
            if (!contact.enabled || normal.y < 0.65f ||
                Mathf.Abs(contact.point.y - bodyCollider.bounds.min.y) > Mathf.Min(groundCheckRadius, 0.15f)) continue;
            Collider2D support = OtherCollider(contact);
            PlatformEffector2D effector = support != null && support.usedByEffector
                ? support.GetComponent<PlatformEffector2D>() : null;
            if (effector == null || !effector.isActiveAndEnabled || !effector.useOneWay) return false;
            if (!dropCandidates.Contains(support) && !Physics2D.GetIgnoreCollision(bodyCollider, support))
                dropCandidates.Add(support);
        }
        if (dropCandidates.Count == 0) return false;
        foreach (Collider2D platform in dropCandidates)
        {
            Physics2D.IgnoreCollision(bodyCollider, platform, true);
            droppedPlatforms.Add(platform);
        }
        dropElapsed = 0f;
        IsGrounded = false;
        coyoteTimer = jumpBufferTimer = 0f;
        jumpIsActive = false;
        body.linearVelocity = new Vector2(body.linearVelocity.x, Mathf.Min(body.linearVelocity.y, -2f));
        return true;
    }

    private void UpdateDroppedPlatforms()
    {
        if (!IsDroppingThrough) return;
        dropElapsed += Time.fixedDeltaTime;
        for (int i = droppedPlatforms.Count - 1; i >= 0; i--)
        {
            Collider2D platform = droppedPlatforms[i];
            bool separated = bodyCollider == null || platform == null || !platform.enabled ||
                !bodyCollider.bounds.Intersects(platform.bounds);
            if ((dropElapsed < 0.16f || !separated) && dropElapsed < 1.2f) continue;
            if (platform != null && bodyCollider != null) Physics2D.IgnoreCollision(bodyCollider, platform, false);
            droppedPlatforms.RemoveAt(i);
        }
    }

    private void RestoreDroppedPlatforms()
    {
        foreach (Collider2D platform in droppedPlatforms)
            if (platform != null && bodyCollider != null) Physics2D.IgnoreCollision(bodyCollider, platform, false);
        droppedPlatforms.Clear();
        dropCandidates.Clear();
        dropRequested = false;
        dropElapsed = 0f;
    }

    private void TryJump()
    {
        if (jumpBufferTimer <= 0f)
        {
            return;
        }

        bool canUseRopeJump = Grapple != null && Grapple.IsAttached;
        bool canUseGroundJump = coyoteTimer > 0f;
        bool canUseExtraJump = !canUseGroundJump && extraJumpsRemaining > 0;

        if (!canUseRopeJump && !canUseGroundJump && !canUseExtraJump)
        {
            return;
        }

        if (canUseExtraJump && !canUseRopeJump)
        {
            extraJumpsRemaining--;
        }

        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        IsGrounded = false;
        jumpIsActive = !canUseRopeJump;
        float launchSpeed = canUseRopeJump ? Grapple.ReleaseForJump(JumpLaunchSpeed) : JumpLaunchSpeed;
        Grapple?.Detach();
        body.linearVelocity = new Vector2(body.linearVelocity.x, launchSpeed);
        PirateAudio.Play(PirateSound.Jump);
    }

    private void TryStartDash()
    {
        if (!dashAvailable || dashCooldownTimer > 0f || isDashing)
        {
            return;
        }

        float direction = Mathf.Abs(moveInput.x) > 0.01f
            ? Mathf.Sign(moveInput.x)
            : (isFacingRight ? 1f : -1f);

        dashAvailable = false;
        Grapple?.Detach();
        jumpIsActive = false;
        isDashing = true;
        dashTimer = dashDuration;
        body.gravityScale = 0f;
        body.linearVelocity = Vector2.right * direction * dashSpeed;
        PirateAudio.Play(PirateSound.Dash);
    }

    private void UpdateDash()
    {
        dashTimer -= Time.fixedDeltaTime;

        if (dashTimer > 0f)
        {
            return;
        }

        isDashing = false;
        dashCooldownTimer = dashCooldown;
        body.gravityScale = gravityScale;
        body.linearVelocity = new Vector2(body.linearVelocity.x, 0f);
    }

    private void FlipIfNeeded()
    {
        if ((isFacingRight && moveInput.x >= 0f) || (!isFacingRight && moveInput.x <= 0f))
        {
            return;
        }

        isFacingRight = !isFacingRight;
        Vector3 localScale = transform.localScale;
        localScale.x = -localScale.x;
        transform.localScale = localScale;
    }

    public void UnlockDoubleJump()
    {
        SetDoubleJumpUnlocked(true);
    }

    public void SetDoubleJumpUnlocked(bool unlocked)
    {
        doubleJumpUnlocked = unlocked;
        extraJumpsRemaining = unlocked ? 1 : 0;
    }

    public void LaunchSpringBounce(float launchSpeed)
    {
        Grapple?.Detach();
        isDashing = false;
        IsGrounded = false;
        coyoteTimer = 0f;
        jumpBufferTimer = 0f;
        jumpIsActive = false;
        body.gravityScale = gravityScale;
        body.linearVelocity = new Vector2(body.linearVelocity.x, launchSpeed);
    }

    public void SetControlsEnabled(bool value)
    {
        controlsEnabled = value;

        if (!value)
        {
            Grapple?.Detach();
            moveInput = Vector2.zero;
            dashRequested = false;
            jumpBufferTimer = 0f;
            dropRequested = false;
        }
    }

    public void SetModalInputBlocked(bool value) => modalInputBlocked = value;

    public void CancelBurstMotion()
    {
        Grapple?.Detach();
        isDashing = false;
        jumpIsActive = false;
        dashRequested = false;
        dashTimer = 0f;
        jumpBufferTimer = 0f;
        body.gravityScale = gravityScale;
        body.linearVelocity = Vector2.zero;
    }

    public void ResetMotion()
    {
        RestoreDroppedPlatforms();
        Grapple?.Detach();
        Grapple?.ResetJumpChain();
        IsGrounded = false;
        isDashing = false;
        jumpIsActive = false;
        dashRequested = false;
        dashTimer = 0f;
        dashCooldownTimer = 0f;
        jumpBufferTimer = 0f;
        coyoteTimer = 0f;
        dashAvailable = true;
        extraJumpsRemaining = doubleJumpUnlocked ? 1 : 0;

        if (body != null)
        {
            body.gravityScale = gravityScale;
            body.linearVelocity = Vector2.zero;
        }
    }

    public void SetAutomationInputOverride(Vector2 movement, bool jumpPressed = false,
        bool jumpReleased = false, bool dashPressed = false)
    {
        automationInputEnabled = true;
        automationMoveInput = Vector2.ClampMagnitude(movement, 1f);
        automationJumpPressed |= jumpPressed;
        automationJumpReleased |= jumpReleased;
        automationDashPressed |= dashPressed;
    }

    public void ClearAutomationInputOverride()
    {
        automationInputEnabled = false;
        automationMoveInput = Vector2.zero;
        automationJumpPressed = false;
        automationJumpReleased = false;
        automationDashPressed = false;
        moveInput = Vector2.zero;
    }

    public Vector2 ReadMovementInput() => controlsEnabled
        ? (automationInputEnabled ? automationMoveInput : moveInput)
        : Vector2.zero;

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
        {
            return;
        }

        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
