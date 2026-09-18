using UnityEngine;

[DisallowMultipleComponent]
public class InstantKillHazard : MonoBehaviour
{
    public HazardKind Kind { get; private set; }
    public void Configure(HazardKind kind) => Kind = kind;

    public bool IsLethalTo(PlayerLife player, Collider2D contactCollider)
    {
        if (Kind != HazardKind.Spikes) return true;
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        return abilities == null || !abilities.TrySurviveSpike(this, contactCollider);
    }
}
