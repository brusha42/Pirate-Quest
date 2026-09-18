using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class AbilityPickup : MonoBehaviour
{
    public event Action Collected;
    public PirateUpgrade Upgrade { get; private set; } = PirateUpgrade.DoubleJump;
    private bool collected;
    public void Configure(PirateUpgrade upgrade) => Upgrade = upgrade;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerMovement movement = other.GetComponent<PlayerMovement>();

        if (movement == null || collected)
        {
            return;
        }

        collected = true;
        PlayerAbilities abilities = movement.GetComponent<PlayerAbilities>();
        if (abilities != null) abilities.Apply(Upgrade);
        else if (Upgrade == PirateUpgrade.DoubleJump) movement.UnlockDoubleJump();
        Collected?.Invoke();
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
