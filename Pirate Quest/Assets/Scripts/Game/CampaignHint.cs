using UnityEngine;

public sealed class CampaignHint : MonoBehaviour
{
    private PirateGameFlow flow;
    private string hint;
    public void Initialize(PirateGameFlow owner, string text) { flow = owner; hint = text; }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerMovement>() != null) flow.SetContextHint(hint);
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<PlayerMovement>() != null) flow.SetContextHint(null);
    }
}
