using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class PlayerGrapple : MonoBehaviour
{
    [SerializeField, Min(0.5f)] private float grabRange = 9f;
    [SerializeField, Min(0f)] private float swingAcceleration = 48f;
    [SerializeField, Min(1f)] private float maximumAssistedSwingSpeed = 15f;
    [SerializeField, Min(0.1f)] private float minimumRopeLength = 0.8f;
    [SerializeField] private LayerMask ropeObstacleLayers = ~0;
    [SerializeField, Range(0.01f, 0.2f)] private float ropeWidth = 0.055f;

    private Rigidbody2D body;
    private PlayerMovement movement;
    private DistanceJoint2D ropeJoint;
    private LineRenderer ropeLine;
    private HookAnchor currentAnchor;
    private HookAnchor highlightedAnchor;
    private Material ropeMaterial;
    private readonly RaycastHit2D[] ropeHits = new RaycastHit2D[32];
    private bool automationInputEnabled;
    private bool automationHold;
    private bool automationAttachPressed;
    private readonly HashSet<HookAnchor> spentJumpAnchors = new HashSet<HookAnchor>();

    public const float AnchorJumpLaunchSpeed = 18f;
    public int SpentAnchorCount => spentJumpAnchors.Count;
    public bool IsAnchorJumpAvailable(HookAnchor anchor) => anchor != null && !spentJumpAnchors.Contains(anchor);

    public bool IsAttached => currentAnchor != null && ropeJoint != null && ropeJoint.enabled;
    public float GrabRange => Abilities == null || Abilities.HookLevel >= 2 ? grabRange : 2.7f;
    public HookAnchor CurrentAnchor => currentAnchor;
    public float RopeLength => IsAttached ? ropeJoint.distance : 0f;
    private PlayerAbilities Abilities => GetComponent<PlayerAbilities>();

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        movement = GetComponent<PlayerMovement>();

        ropeJoint = gameObject.AddComponent<DistanceJoint2D>();
        ropeJoint.autoConfigureDistance = false;
        ropeJoint.autoConfigureConnectedAnchor = false;
        ropeJoint.enableCollision = true;
        ropeJoint.maxDistanceOnly = true;
        ropeJoint.enabled = false;

        ropeLine = gameObject.AddComponent<LineRenderer>();
        ropeLine.positionCount = 2;
        ropeLine.useWorldSpace = true;
        ropeLine.startWidth = ropeWidth;
        ropeLine.endWidth = ropeWidth;
        ropeLine.startColor = new Color(0.95f, 0.82f, 0.48f, 1f);
        ropeLine.endColor = new Color(0.72f, 0.48f, 0.18f, 1f);
        ropeLine.sortingOrder = 25;
        ropeLine.enabled = false;

        Shader spriteShader = Shader.Find("Sprites/Default");
        if (spriteShader != null)
        {
            ropeMaterial = new Material(spriteShader)
            {
                name = "Runtime Rope Material",
                hideFlags = HideFlags.HideAndDontSave
            };
            ropeLine.material = ropeMaterial;
        }
    }

    private void Update()
    {
        if (movement != null && movement.IsModalInputBlocked && Time.timeScale <= 0f) return;
        if (!CanUseGrapple())
        {
            Detach();
            SetHighlightedAnchor(null);
            automationAttachPressed = false;
            return;
        }

        if (ropeJoint.enabled && (currentAnchor == null || !currentAnchor.CanGrab))
        {
            Detach();
        }

        if (!IsAttached)
        {
            UpdateHighlightedAnchor();
        }

        bool attachPressed = automationInputEnabled
            ? automationAttachPressed
            : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame &&
                !PirateHUD.ConsumedInputThisFrame && !PirateHUD.PointerBlocksWorld;
        automationAttachPressed = false;
        if (attachPressed && !IsAttached)
        {
            TryAttachToNearestAnchor();
        }

        bool holdRequested = automationInputEnabled
            ? automationHold
            : Mouse.current != null && Mouse.current.rightButton.isPressed;
        if (IsAttached && !holdRequested)
        {
            Detach();
        }

    }

    private void LateUpdate()
    {
        if (IsAttached)
        {
            ropeLine.SetPosition(0, transform.position);
            ropeLine.SetPosition(1, currentAnchor.transform.position);
        }
    }

    private void FixedUpdate()
    {
        if (movement != null && movement.IsGrounded) ResetJumpChain();
        if (!IsAttached || !CanUseGrapple())
        {
            return;
        }

        if (!HasClearLineTo(currentAnchor))
        {
            Detach();
            return;
        }

        Vector2 anchorPosition = currentAnchor.transform.position;
        ropeJoint.connectedAnchor = anchorPosition;
        Vector2 radialOffset = body.position - anchorPosition;
        float distance = radialOffset.magnitude;
        float horizontalInput = movement != null ? movement.ReadMovementInput().x : 0f;

        if (distance < 0.001f || Mathf.Abs(horizontalInput) < 0.01f)
        {
            return;
        }

        if (distance < ropeJoint.distance - 0.12f)
        {
            float direction = Mathf.Sign(horizontalInput);
            if (body.linearVelocity.x * direction < maximumAssistedSwingSpeed)
            {
                body.AddForce(Vector2.right * horizontalInput * swingAcceleration * body.mass, ForceMode2D.Force);
            }
            return;
        }

        Vector2 radialDirection = radialOffset / distance;
        Vector2 tangent = new Vector2(-radialDirection.y, radialDirection.x);
        float tangentialSpeed = Vector2.Dot(body.linearVelocity, tangent);
        float desiredDirection = Mathf.Sign(horizontalInput);
        float lowerArc = Mathf.Clamp01((-radialDirection.y - 0.35f) / 0.65f);
        bool canStartFromRest = lowerArc > 0.85f && Mathf.Abs(tangentialSpeed) < 0.5f;
        if (lowerArc <= 0f || (!canStartFromRest && tangentialSpeed * desiredDirection < 0.5f))
        {
            return;
        }

        float gravity = Mathf.Abs(Physics2D.gravity.y * body.gravityScale);
        float heightAboveBottom = Mathf.Max(0f, radialOffset.y + ropeJoint.distance);
        float remainingHeight = Mathf.Max(0f, ropeJoint.distance * 0.92f - heightAboveBottom);
        float assistedSpeedLimit = Mathf.Min(maximumAssistedSwingSpeed, Mathf.Sqrt(2f * gravity * remainingHeight));
        float speedBudget = assistedSpeedLimit - tangentialSpeed * desiredDirection;
        if (speedBudget <= 0f) return;
        float assistedAcceleration = Mathf.Min(swingAcceleration * Mathf.Abs(horizontalInput) * lowerArc,
            speedBudget / Time.fixedDeltaTime);
        body.AddForce(tangent * desiredDirection * assistedAcceleration * body.mass, ForceMode2D.Force);
    }

    private void OnDisable()
    {
        Detach();
        SetHighlightedAnchor(null);
    }

    private void OnDestroy()
    {
        if (ropeMaterial != null)
        {
            Destroy(ropeMaterial);
        }
    }

    public bool TryAttachToNearestAnchor()
    {
        HookAnchor nearest = FindNearestAnchor(out _);
        return TryAttachToAnchor(nearest);
    }

    public bool TryAttachToAnchor(HookAnchor anchor)
    {
        if (!CanUseGrapple() || anchor == null || !anchor.CanGrab)
        {
            return false;
        }

        float distance = Vector2.Distance(body.position, anchor.transform.position);
        if (distance < minimumRopeLength || !CanReachAnchor(anchor) || !HasClearLineTo(anchor))
        {
            return false;
        }

        Detach();
        SetHighlightedAnchor(anchor);
        currentAnchor = anchor;
        ropeJoint.connectedBody = null;
        ropeJoint.anchor = Vector2.zero;
        ropeJoint.connectedAnchor = anchor.transform.position;
        ropeJoint.distance = distance;
        ropeJoint.enabled = true;
        ropeLine.enabled = true;
        PirateAudio.Play(PirateSound.Grapple);
        return true;
    }

    public void Detach()
    {
        if (currentAnchor != null)
        {
            currentAnchor.SetHighlighted(false);

            if (highlightedAnchor == currentAnchor)
            {
                highlightedAnchor = null;
            }
        }

        currentAnchor = null;

        if (ropeJoint != null)
        {
            ropeJoint.enabled = false;
        }

        if (ropeLine != null)
        {
            ropeLine.enabled = false;
        }
    }

    public float ReleaseForJump(float defaultJumpSpeed)
    {
        float launch = body.linearVelocity.y;
        if (IsAttached && spentJumpAnchors.Add(currentAnchor))
            launch = Mathf.Max(launch, Mathf.Max(defaultJumpSpeed, AnchorJumpLaunchSpeed));
        Detach();
        return launch;
    }

    public void ResetJumpChain() => spentJumpAnchors.Clear();

    private void UpdateHighlightedAnchor()
    {
        SetHighlightedAnchor(FindNearestAnchor(out _));
    }

    private HookAnchor FindNearestAnchor(out float nearestSqrDistance)
    {
        HookAnchor[] anchors = FindObjectsByType<HookAnchor>(FindObjectsSortMode.None);
        HookAnchor nearest = null;
        nearestSqrDistance = float.PositiveInfinity;
        bool foundFreshAbove = false;

        foreach (HookAnchor anchor in anchors)
        {
            if (!anchor.CanGrab || !CanReachAnchor(anchor))
            {
                continue;
            }

            float sqrDistance = ((Vector2)anchor.transform.position - (Vector2)transform.position).sqrMagnitude;
            bool freshAbove = spentJumpAnchors.Count > 0 && IsAnchorJumpAvailable(anchor) &&
                anchor.transform.position.y > body.position.y + .2f;
            if (foundFreshAbove && !freshAbove) continue;
            if (sqrDistance >= minimumRopeLength * minimumRopeLength &&
                (freshAbove && !foundFreshAbove || sqrDistance <= nearestSqrDistance) && HasClearLineTo(anchor))
            {
                nearest = anchor;
                nearestSqrDistance = sqrDistance;
                foundFreshAbove = freshAbove;
            }
        }

        return nearest;
    }

    private bool CanUseGrapple()
    {
        return isActiveAndEnabled && body != null && body.simulated &&
               (Abilities == null || Abilities.HookLevel > 0) &&
               (GetComponent<PlayerLife>() == null || !GetComponent<PlayerLife>().IsSnared) &&
               (movement == null || (movement.ControlsEnabled && !movement.IsDashing));
    }

    private bool CanReachAnchor(HookAnchor anchor)
    {
        if (Abilities == null) return Vector2.Distance(body.position, anchor.transform.position) <= grabRange;
        if (Abilities.HookLevel >= 2)
            return Vector2.Distance(body.position, anchor.transform.position) <= grabRange;
        return Abilities.HookLevel == 1 && !anchor.RequiresRangedHook &&
               Vector2.Distance(body.position, anchor.NearestChainPoint(body.position)) <= 2.7f &&
               Vector2.Distance(body.position, anchor.transform.position) <= 12f;
    }

    private bool HasClearLineTo(HookAnchor anchor)
    {
        Vector2 offset = (Vector2)anchor.transform.position - body.position;
        ContactFilter2D filter = new ContactFilter2D
        {
            useTriggers = false,
            useLayerMask = true,
            layerMask = ropeObstacleLayers
        };
        int count = Physics2D.Raycast(body.position, offset.normalized, filter, ropeHits, offset.magnitude);
        for (int i = 0; i < count; i++)
        {
            Collider2D hit = ropeHits[i].collider;
            if (hit.attachedRigidbody == body || hit.transform.IsChildOf(transform) ||
                hit.GetComponentInParent<HookAnchor>() == anchor)
            {
                continue;
            }

            PlatformEffector2D platform = hit.usedByEffector ? hit.GetComponent<PlatformEffector2D>() : null;
            if (platform != null && platform.isActiveAndEnabled && platform.useOneWay)
            {
                continue;
            }

            return false;
        }

        return count < ropeHits.Length;
    }

    public void SetAutomationInputOverride(bool hold, bool attachPressed = false)
    {
        automationInputEnabled = true;
        automationHold = hold;
        automationAttachPressed |= attachPressed;
    }

    public void ClearAutomationInputOverride()
    {
        automationInputEnabled = false;
        automationHold = false;
        automationAttachPressed = false;
    }

    private void SetHighlightedAnchor(HookAnchor anchor)
    {
        if (highlightedAnchor == anchor)
        {
            return;
        }

        if (highlightedAnchor != null && highlightedAnchor != currentAnchor)
        {
            highlightedAnchor.SetHighlighted(false);
        }

        highlightedAnchor = anchor;

        if (highlightedAnchor != null)
        {
            highlightedAnchor.SetHighlighted(true);
        }
    }
}
