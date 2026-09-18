using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

public static class PirateWorldPolishRegression
{
    public const string FrameName = "08a-crown-ceiling-window-clearance.png";

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player,
        string evidenceDirectory, Action<bool,string> completed)
    {
        Camera camera = Camera.main;
        CameraFollow follow = camera != null ? camera.GetComponent<CameraFollow>() : null;
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        DarkZone[] zones = UnityEngine.Object.FindObjectsByType<DarkZone>(FindObjectsSortMode.None);
        if (!Environment.GetCommandLineArgs().Contains("-pirateMenuTest") ||
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
            flow == null || !flow.IsInitialized || flow.ChapterIndex != 3 || flow.IsVictory ||
            player == null || !player.ControlsEnabled || body == null || life == null ||
            life.IsRespawning || life.IsSnared || abilities == null || abilities.IsScouting ||
            player.IsDashing || grapple != null && grapple.IsAttached ||
            camera == null || follow == null || !camera.orthographic || camera.targetTexture != null ||
            zones.Length == 0 || Time.timeScale != 1f)
        {
            completed(false, "Requires idle live Crown, actual darkness and menu-test graphics.");
            yield break;
        }

        CampaignLayout map = flow.Generator.Campaign;
        int ceilingIndex = map.Platforms.FindIndex(platform => !platform.OneWay &&
            platform.Bounds.height > 3f && platform.Bounds.width >= 5f &&
            map.Rooms[platform.RoomIndex].ScenarioId == "crown.slide-descending-terraces" &&
            map.Spawns.Any(spawn => spawn.Kind == CampaignLayout.SpawnKind.Spikes &&
                Mathf.Abs(spawn.Position.x - platform.CenterX) < .01f &&
                Mathf.Abs(platform.Bounds.yMin - spawn.Position.y - 1.45f) < .02f));
        if (ceilingIndex < 0)
        {
            completed(false, "Matching slide ceiling was not generated.");
            yield break;
        }
        Rect ceiling = map.Platforms[ceilingIndex].Bounds;
        float landingX = 0f;
        CampaignLayout.Platform landing = null;
        foreach (float candidate in new[] { ceiling.xMax + 1.2f, ceiling.xMin - 1.2f })
        {
            landing = map.Platforms.FirstOrDefault(platform =>
                candidate > platform.Bounds.xMin + .35f && candidate < platform.Bounds.xMax - .35f &&
                Mathf.Abs(platform.SurfaceY - (ceiling.yMin - 1.3f)) < .02f);
            if (landing == null) continue;
            landingX = candidate;
            break;
        }
        if (landing == null)
        {
            completed(false, "Real safe ledge beside the ceiling was not found.");
            yield break;
        }

        Vector3 position = player.transform.position;
        Vector2 bodyPosition = body.position;
        Vector2 velocity = body.linearVelocity;
        RigidbodyInterpolation2D interpolation = body.interpolation;
        Vector3 cameraPosition = camera.transform.position;
        Quaternion cameraRotation = camera.transform.rotation;
        float cameraSize = camera.orthographicSize;
        bool followEnabled = follow.enabled;
        bool controls = player.ControlsEnabled;
        float timeScale = Time.timeScale;
        int deaths = life.DeathCount;
        int[] lightCells = zones.Select(zone => zone.RevealedCellCount).ToArray();
        bool[] lightEnabled = zones.Select(zone => zone.enabled).ToArray();
        bool[] lightActive = zones.Select(zone => zone.gameObject.activeSelf).ToArray();
        Rect[] lightBounds = zones.Select(zone => zone.WorldBounds).ToArray();
        bool captured = false, windowsClear = false, lightingRestored = false;
        string captureDetail = "Frame unavailable";

        void RestorePlacement()
        {
            body.position = bodyPosition;
            player.transform.position = position;
            body.linearVelocity = velocity;
            body.interpolation = interpolation;
            camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
            camera.orthographicSize = cameraSize;
        }

        try
        {
            Time.timeScale = 0f;
            player.SetControlsEnabled(false);
            follow.enabled = false;
            body.interpolation = RigidbodyInterpolation2D.None;
            Vector2 fixture = new Vector2(landingX,
                landing.SurfaceY + player.GetComponent<Collider2D>().bounds.extents.y + .02f);
            body.position = fixture;
            player.transform.position = new Vector3(fixture.x, fixture.y, position.z);
            body.linearVelocity = Vector2.zero;
            float edgeX = landingX > ceiling.center.x ? ceiling.xMax + .4f : ceiling.xMin - .4f;
            camera.transform.position = new Vector3(edgeX, ceiling.yMin + .7f, cameraPosition.z);
            camera.orthographicSize = 3.5f;
            Debug.Log("PIRATE_WORLD_POLISH_FIXTURE fixturePlacement=True fullTraversalProof=False " +
                "actualLevelArt=True actualDarkZone=True physicsSteps=0 ceilingIndex=" + ceilingIndex +
                " ceiling=" + ceiling + " safeLanding=" + fixture);
            for (int i = 0; i < 3; i++) yield return new WaitForEndOfFrame();
            windowsClear = !UnityEngine.Object.FindObjectsByType<PirateWorldVisual>(FindObjectsSortMode.None)
                .Any(visual => visual.Kind == PirateArtKind.Window && visual.Renderer != null &&
                    visual.Renderer.enabled && visual.gameObject.activeInHierarchy &&
                    ceiling.Overlaps(new Rect(visual.OpaqueWorldBounds.min.x, visual.OpaqueWorldBounds.min.y,
                        visual.OpaqueWorldBounds.size.x, visual.OpaqueWorldBounds.size.y)));
            captured = Capture(evidenceDirectory, out captureDetail);
            RestorePlacement();
            yield return null;
            yield return new WaitForEndOfFrame();
            lightingRestored = zones.Select((zone, i) => zone != null &&
                zone.enabled == lightEnabled[i] && zone.gameObject.activeSelf == lightActive[i] &&
                zone.WorldBounds == lightBounds[i] && zone.RevealedCellCount == lightCells[i]).All(value => value);
        }
        finally
        {
            RestorePlacement();
            player.SetControlsEnabled(controls);
            follow.enabled = followEnabled;
            Time.timeScale = timeScale;
        }
        bool restored = player.transform.position == position && body.position == bodyPosition &&
            body.linearVelocity == velocity && body.interpolation == interpolation &&
            camera.transform.position == cameraPosition && camera.transform.rotation == cameraRotation &&
            camera.orthographicSize == cameraSize && follow.enabled == followEnabled &&
            player.ControlsEnabled == controls && Time.timeScale == timeScale && life.DeathCount == deaths;
        completed(captured && windowsClear && lightingRestored && restored,
            "ceilingIndex=" + ceilingIndex + " windowClear=" + windowsClear +
            " lightingRestored=" + lightingRestored + " stateRestored=" + restored + " " + captureDetail);
    }

    private static bool Capture(string directory, out string details)
    {
        Texture2D frame = null;
        try
        {
            frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null || frame.width != 1280 || frame.height != 720)
            { details = "Expected real 1280x720 frame."; return false; }
            Color32[] pixels = frame.GetPixels32();
            int bright = 0;
            for (int i = 0; i < pixels.Length; i += 127)
                if (Mathf.Max(pixels[i].r, Mathf.Max(pixels[i].g, pixels[i].b)) > 40) bright++;
            if (bright < 32) { details = "Rendered frame has insufficient visible content."; return false; }
            string path = Path.Combine(directory, FrameName);
            File.WriteAllBytes(path, frame.EncodeToPNG());
            details = "renderedFrame=" + path;
            Debug.Log("PIRATE_MENU_FRAME " + details);
            return true;
        }
        catch (Exception exception) { details = exception.GetType().Name + ": " + exception.Message; return false; }
        finally { if (frame != null) UnityEngine.Object.Destroy(frame); }
    }
}
