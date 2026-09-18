using UnityEngine;

public static class ParrotPresentationRegression
{
    public static bool VerifyRestoredScene(PirateGameFlow flow, PlayerMovement player, out string details)
    {
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        if (flow == null || !flow.IsInitialized || flow.ChapterIndex != 3 || abilities == null || !abilities.HasParrot)
        {
            details = "Expected an initialized Crown scene with a restored, already-owned parrot.";
            return false;
        }
        Vector3 before = player.transform.position;
        ParrotScout[] scouts = Object.FindObjectsByType<ParrotScout>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int ownedPickupCount = 0;
        int parrotPickupCount = 0;
        foreach (AbilityPickup pickup in Object.FindObjectsByType<AbilityPickup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (abilities.Has(pickup.Upgrade)) ownedPickupCount++;
            if (pickup.Upgrade == PirateUpgrade.Parrot) parrotPickupCount++;
        }
        ParrotScout companion = abilities.Scout;
        SpriteRenderer[] companionRenderers = companion != null ? companion.GetComponents<SpriteRenderer>() : new SpriteRenderer[0];
        bool oneCompanion = scouts.Length == 1 && scouts[0] == companion && companion != null &&
            companion.transform.parent == player.transform && !companion.IsScouting;
        bool oneSprite = companionRenderers.Length == 1 && companionRenderers[0].sprite != null;
        bool untouched = before == player.transform.position;
        bool success = oneCompanion && oneSprite && ownedPickupCount == 0 && parrotPickupCount == 0 && untouched;
        details = $"success={success}, scouts={scouts.Length}, companionSprites={companionRenderers.Length}, " +
            $"ownedPickups={ownedPickupCount}, parrotPickups={parrotPickupCount}, attachedCompanion={oneCompanion}, " +
            $"playerUntouched={untouched}, immediateReadOnly=True, firstPhysicsStepRequired=False, nativeRenderProof=False";
        return success;
    }
}
