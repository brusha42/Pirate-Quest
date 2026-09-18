using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement), typeof(SpriteRenderer))]
public class PlayerVisualAnimator : MonoBehaviour
{
    public enum AnimationState { Idle, Run, Jump, Fall, Dash, JumpApex, Grapple }

    [SerializeField] private PirateAnimationLibrary animationLibrary;
    [SerializeField, Min(0.1f)] private float referenceRunSpeed = 10f;
    [SerializeField, Min(0f)] private float apexVelocityThreshold = 1.4f;

    private PlayerMovement movement;
    private PlayerGrapple grapple;
    private SpriteRenderer sourceRenderer;
    private SpriteRenderer visualRenderer;
    private float elapsedInState;

    public AnimationState CurrentState { get; private set; }
    public int CurrentFrameIndex { get; private set; }
    public Sprite CurrentSprite => visualRenderer != null ? visualRenderer.sprite : null;
    public bool HasFrameAnimation => animationLibrary != null && animationLibrary.IsComplete;
    public int RunFrameCount => animationLibrary != null ? animationLibrary.RunFrameCount : 0;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        sourceRenderer = GetComponent<SpriteRenderer>();
        if (animationLibrary == null)
        {
            animationLibrary = Resources.Load<PirateAnimationLibrary>(PirateAnimationLibrary.ResourceName);
        }

        GameObject visualObject = new GameObject("Pirate Visual");
        visualObject.transform.SetParent(transform, false);
        visualRenderer = visualObject.AddComponent<SpriteRenderer>();
        visualRenderer.sprite = sourceRenderer.sprite;
        visualRenderer.color = sourceRenderer.color;
        visualRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
        visualRenderer.flipX = sourceRenderer.flipX;
        visualRenderer.flipY = sourceRenderer.flipY;
        visualRenderer.maskInteraction = sourceRenderer.maskInteraction;
        visualRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        visualRenderer.sortingOrder = Mathf.Max(sourceRenderer.sortingOrder, 10);

        if (HasFrameAnimation)
        {
            visualObject.transform.localPosition = GetLocalFootPosition();
            ApplyCurrentFrame();
        }
        else
        {
            Debug.LogError("Pirate animation frames are missing. Run Coursework/Import Pirate Animation Sheet before building.", this);
        }

        sourceRenderer.enabled = false;
    }

    private void LateUpdate()
    {
        sourceRenderer.enabled = false;
        if (!HasFrameAnimation)
        {
            return;
        }

        if (grapple == null)
        {
            TryGetComponent(out grapple);
        }

        AnimationState nextState = SelectState();
        if (nextState != CurrentState)
        {
            CurrentState = nextState;
            elapsedInState = 0f;
        }
        else
        {
            float playbackRate = CurrentState == AnimationState.Run
                ? Mathf.Clamp(Mathf.Abs(movement.Velocity.x) / referenceRunSpeed, 0.55f, 1.4f)
                : 1f;
            elapsedInState += Time.deltaTime * playbackRate;
        }

        ApplyCurrentFrame();
    }

    private AnimationState SelectState()
    {
        if (movement.IsDashing) return AnimationState.Dash;
        if (grapple != null && grapple.IsAttached && !movement.IsGrounded) return AnimationState.Grapple;

        Vector2 velocity = movement.Velocity;
        if (!movement.IsGrounded)
        {
            if (Mathf.Abs(velocity.y) <= apexVelocityThreshold) return AnimationState.JumpApex;
            return velocity.y > 0f ? AnimationState.Jump : AnimationState.Fall;
        }

        return Mathf.Abs(velocity.x) > 0.2f ? AnimationState.Run : AnimationState.Idle;
    }

    private void ApplyCurrentFrame()
    {
        PirateAnimationClip clip = animationLibrary.GetClip(CurrentState);
        CurrentFrameIndex = clip.GetFrameIndex(elapsedInState);
        visualRenderer.sprite = clip.GetFrame(CurrentFrameIndex);
    }

    private Vector3 GetLocalFootPosition()
    {
        CapsuleCollider2D capsule = GetComponent<CapsuleCollider2D>();
        if (capsule != null)
        {
            return new Vector3(capsule.offset.x, capsule.offset.y - capsule.size.y * 0.5f, 0f);
        }

        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null)
        {
            return new Vector3(box.offset.x, box.offset.y - box.size.y * 0.5f, 0f);
        }

        Collider2D bodyCollider = GetComponent<Collider2D>();
        return bodyCollider != null
            ? transform.InverseTransformPoint(new Vector3(bodyCollider.bounds.center.x, bodyCollider.bounds.min.y, transform.position.z))
            : new Vector3(0f, -0.5f, 0f);
    }
}
