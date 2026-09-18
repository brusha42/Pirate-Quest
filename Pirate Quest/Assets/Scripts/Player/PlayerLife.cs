using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(PlayerMovement))]
public class PlayerLife : MonoBehaviour
{
    [SerializeField, Min(0f)] private float respawnDelay = 0.35f;
    [SerializeField] private float killHeight = -15f;

    private Rigidbody2D body;
    private Collider2D playerCollider;
    private PlayerMovement movement;
    private SpriteRenderer[] renderers;
    private Vector3 checkpointPosition;

    public event Action Died;
    public event Action Respawned;
    public event Action CheckpointRestoring;

    public bool IsRespawning { get; private set; }
    public bool IsSnared { get; private set; }
    public bool IsExitProtected { get; private set; }
    public bool IsModalProtected => movement != null && movement.IsModalInputBlocked;
    public int DeathCount { get; private set; }
    public Vector3 CheckpointPosition => checkpointPosition;
    public void SetDeathCount(int value) => DeathCount = Mathf.Max(0, value);
    public void SetExitProtected(bool value) => IsExitProtected = value;
    internal void SetSnared(bool value) => IsSnared = value;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        movement = GetComponent<PlayerMovement>();
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        checkpointPosition = transform.position;
    }

    private void Update()
    {
        if (!IsRespawning && transform.position.y < killHeight)
        {
            Die();
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        CheckHazard(other);
    }

    private void OnTriggerStay2D(Collider2D other) => CheckHazard(other);

    private void CheckHazard(Collider2D other)
    {
        if (IsExitProtected || IsModalProtected) return;
        InstantKillHazard hazard = other.GetComponentInParent<InstantKillHazard>();
        if (!IsRespawning && hazard != null && hazard.IsLethalTo(this, other))
        {
            if (hazard.Kind == HazardKind.Spikes && PirateFrontEnd.IsAutomationRun)
            {
                PlayerAbilities abilities = GetComponent<PlayerAbilities>();
                Debug.Log($"PIRATE_SPIKE_FATAL_CONTACT hazard={hazard.name} position={body.position} velocity={body.linearVelocity} " +
                    $"capsule={playerCollider.bounds} hazardBounds={other.bounds} feet={playerCollider.bounds.min.y:F4} " +
                    $"spikeTop={other.bounds.max.y:F4} springAvailable={abilities != null && abilities.SpringAvailable} " +
                    $"legLevel={(abilities != null ? abilities.LegLevel : 0)} grounded={movement.IsGrounded} " +
                    $"sliding={abilities != null && abilities.IsSpikeSliding} slideRequested={abilities != null && abilities.SlideRequested} " +
                    $"controls={movement.ControlsEnabled} timeScale={Time.timeScale:F2} beforeActualDie=True");
            }
            Die();
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        CheckHazard(collision.collider);
    }

    private void OnCollisionStay2D(Collision2D collision) => CheckHazard(collision.collider);

    public void SetCheckpoint(Vector3 position)
    {
        checkpointPosition = position;
    }

    public void SetKillHeight(float value)
    {
        killHeight = value;
    }

    public void Die()
    {
        if (IsExitProtected || IsModalProtected) return;
        if (!IsRespawning)
        {
            StartCoroutine(RespawnRoutine());
        }
    }

    public void RespawnImmediately()
    {
        if (IsExitProtected) return;
        if (IsRespawning)
        {
            StopAllCoroutines();
        }

        IsRespawning = false;
        RestoreAtCheckpoint();
    }

    private IEnumerator RespawnRoutine()
    {
        IsRespawning = true;
        DeathCount++;
        PirateAudio.Play(PirateSound.Death);
        GetComponent<PlayerAbilities>()?.ResetTransientState();
        Died?.Invoke();

        movement.SetControlsEnabled(false);
        GetComponent<PlayerGrapple>()?.Detach();
        body.simulated = false;
        playerCollider.enabled = false;
        SetRenderersEnabled(false);

        yield return new WaitForSecondsRealtime(respawnDelay);

        RestoreAtCheckpoint();
        IsRespawning = false;
        Respawned?.Invoke();
    }

    private void RestoreAtCheckpoint()
    {
        CheckpointRestoring?.Invoke();
        IsSnared = false;
        GetComponent<PlayerAbilities>()?.ResetTransientState();
        transform.position = checkpointPosition;
        body.position = checkpointPosition;
        body.simulated = true;
        playerCollider.enabled = true;
        movement.ResetMotion();
        movement.SetControlsEnabled(true);
        SetRenderersEnabled(true);
        Physics2D.SyncTransforms();
    }

    private void SetRenderersEnabled(bool value)
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer spriteRenderer in renderers)
        {
            bool isHiddenAnimatorSource = spriteRenderer.transform == transform &&
                                          GetComponent<PlayerVisualAnimator>() != null;
            spriteRenderer.enabled = value && !isHiddenAnimatorSource;
        }
    }
}
