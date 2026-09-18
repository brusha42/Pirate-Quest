using System;
using UnityEngine;

public static class WorldVisualRegression
{
    public static bool Verify(out string detail)
    {
        int anchored = 0;
        if (PirateWorldArt.Library == null || !PirateWorldArt.Library.HasOpaqueGeometry)
        { detail = "Missing imported opaque geometry"; return false; }
        foreach (PirateWorldVisual visual in UnityEngine.Object.FindObjectsByType<PirateWorldVisual>(FindObjectsSortMode.None))
        {
            if (visual.name.IndexOf("Structural crossbeam", StringComparison.OrdinalIgnoreCase) >= 0)
            { detail = "False decorative platform remains"; return false; }
            if (!visual.IsGroundAnchored) continue;
            visual.RefreshPlacement();
            float error = Mathf.Abs(visual.OpaqueWorldBounds.min.y - visual.GroundSurfaceY);
            if (error > .025f)
            { detail = $"Floating grounded art {visual.Kind}: delta={error:F4}"; return false; }
            anchored++;
        }
        detail = $"anchored={anchored}, opaqueBottomTolerance=.025, falseBeams=0, nativeVisualReview=False";
        return anchored > 0;
    }
}
