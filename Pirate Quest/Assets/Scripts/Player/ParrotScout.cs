using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class ParrotScout : MonoBehaviour
{
    public bool IsScouting { get; private set; }
    public float RevealRadius => IsScouting ? 4.5f : 1.8f;
    public float MaximumRange => 14f;
    private PlayerAbilities owner;
    private PlayerMovement movement;
    private Rigidbody2D body;
    private SpriteRenderer visual;
    private Vector2 piratePosition;
    private float savedGravity;
    private Vector2 automationInput;
    private bool automationEnabled;

    public void Initialize(PlayerAbilities player)
    {
        owner = player;
        movement = owner.GetComponent<PlayerMovement>();
        body = owner.GetComponent<Rigidbody2D>();
        transform.localPosition = new Vector3(0.65f, 0.8f, 0f);
        visual = gameObject.AddComponent<SpriteRenderer>();
        visual.sortingOrder = 35;
        visual.enabled = false;
    }

    public void InitializeVisual(Sprite sprite)
    {
        visual.sprite = sprite;
        if (sprite != null) transform.localScale = Vector3.one * (0.85f / sprite.bounds.size.y);
    }

    public bool Toggle()
    {
        if (IsScouting) { ReturnToPirate(); return true; }
        if (owner == null || !owner.HasParrot || !movement.ControlsEnabled || !movement.IsGrounded) return false;
        PlayerLife life = owner.GetComponent<PlayerLife>();
        if (life != null && (life.IsRespawning || life.IsSnared)) return false;
        IsScouting = true;
        PirateAudio.Play(PirateSound.Parrot);
        piratePosition = body.position;
        savedGravity = body.gravityScale;
        movement.SetControlsEnabled(false);
        body.linearVelocity = Vector2.zero;
        body.gravityScale = 0f;
        transform.SetParent(null, true);
        owner.NotifyScoutChanged(true);
        return true;
    }

    private void Update()
    {
        if (owner == null) { Destroy(gameObject); return; }
        PlayerLife life = owner.GetComponent<PlayerLife>();
        visual.enabled = owner.HasParrot && (life == null || !life.IsRespawning);
        Sprite flightFrame = PirateWorldArt.GetSprite((int)(Time.time * 9f) % 2 == 0 ? PirateArtKind.Parrot : PirateArtKind.ParrotFlap);
        if (flightFrame != null)
        {
            visual.sprite = flightFrame;
            transform.localScale = Vector3.one * (0.85f / flightFrame.bounds.size.y);
        }
        if (Time.timeScale <= 0f) return;
        if (IsScouting)
        {
            Vector2 input = automationInput;
            if (!automationEnabled)
            {
                Keyboard k = Keyboard.current;
                input = k == null ? Vector2.zero : new Vector2(
                    (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0),
                    (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0));
            }
            Vector2 next = (Vector2)transform.position + Vector2.ClampMagnitude(input, 1f) * 8f * Time.deltaTime;
            transform.position = piratePosition + Vector2.ClampMagnitude(next - piratePosition, MaximumRange);
            body.position = piratePosition;
            body.linearVelocity = Vector2.zero;
            if (Mathf.Abs(input.x) > 0.01f) visual.flipX = input.x < 0f;
        }
        else
        {
            transform.localPosition = new Vector3(0.65f, 0.85f + Mathf.Sin(Time.time * 5f) * 0.12f, 0f);
        }
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 9f) * 7f);
    }

    public void ReturnToPirate()
    {
        if (owner == null) return;
        bool wasScouting = IsScouting;
        IsScouting = false;
        transform.SetParent(owner.transform, true);
        transform.localPosition = new Vector3(0.65f, 0.85f, 0f);
        if (wasScouting)
        {
            body.gravityScale = savedGravity;
            body.linearVelocity = Vector2.zero;
            movement.SetControlsEnabled(true);
            owner.NotifyScoutChanged(false);
        }
    }

    public void SetAutomationInput(Vector2 input) { automationEnabled = true; automationInput = input; }
    public void ClearAutomationInput() { automationEnabled = false; automationInput = Vector2.zero; }
    private void OnDestroy() { if (IsScouting && owner != null) ReturnToPirate(); }
}
