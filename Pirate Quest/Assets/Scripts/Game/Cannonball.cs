using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class Cannonball : MonoBehaviour
{
    public const float DefaultLifetime = 5f;
    [SerializeField, Min(0.1f)] private float lifetime = DefaultLifetime;
    private GameObject owner;

    public void Initialize(GameObject projectileOwner)
    {
        owner = projectileOwner;
        StartCoroutine(DestroyAfterLifetime());
    }

    private void OnTriggerEnter2D(Collider2D other) => Touch(other);
    private void OnTriggerStay2D(Collider2D other) => Touch(other);
    private void Touch(Collider2D other)
    {
        if (Time.timeScale <= 0f) return;
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform)))
        {
            return;
        }

        PlayerLife player = other.GetComponentInParent<PlayerLife>();
        if (player != null)
        {
            if (player.IsModalProtected) return;
            if (PirateFrontEnd.IsAutomationRun && !player.IsRespawning)
                Debug.Log($"PIRATE_CANNONBALL_CONTACT player={player.transform.position} ball={transform.position} velocity={GetComponent<Rigidbody2D>().linearVelocity}");
            player.Die();
        }
        else if (other.isTrigger) return;
        Destroy(gameObject);
    }

    private IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);
        Destroy(gameObject);
    }
}
