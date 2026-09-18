using UnityEngine;

public sealed class CampaignJumpPad : MonoBehaviour
{
    private float launchSpeed = 18f;
    private float nextLaunch;
    public void Configure(float speed) => launchSpeed = speed;
    private void OnTriggerStay2D(Collider2D other)
    {
        PlayerMovement movement = other.GetComponent<PlayerMovement>();
        Rigidbody2D body = other.attachedRigidbody;
        if (movement == null || body == null || Time.time < nextLaunch || body.linearVelocity.y > 1f) return;
        if (other.bounds.min.y < transform.position.y - .4f) return;
        movement.LaunchSpringBounce(launchSpeed);
        nextLaunch = Time.time + .35f;
    }
}
