using System;
using System.Collections;
using UnityEngine;

public static class BlackTideRegression
{
    public sealed class Result
    {
        public bool ClockRate, ClockPartition, Calm, ClockScoutPause, CheckpointReset, ThreatTiming;
        public bool PauseFreezes, PausedDamageBlocked, RealScoutPausesOnlyTide, ActualDeath, ActualRespawn;
        public bool DisabledColliderReset, ExitProtection, VictoryStops, VictoryUnscaledFade;
        public bool ViewportGeometryTested, InitialBandInViewport;
        public float StandingDangerSeconds, NextFloorDangerSeconds, InitialVisibleBandHeight, InitialSurfaceViewportY, CameraHalfHeight;
        public float PauseRequestDelta, PausedCalmDelta, PausedSurfaceDelta;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && ClockRate && ClockPartition && Calm && ClockScoutPause &&
            CheckpointReset && ThreatTiming && PauseFreezes && PausedDamageBlocked && RealScoutPausesOnlyTide && ActualDeath && ActualRespawn &&
            DisabledColliderReset && ExitProtection && VictoryStops && VictoryUnscaledFade &&
            (!ViewportGeometryTested || InitialBandInViewport);
        public override string ToString() =>
            $"success={Success}, clock={ClockRate}/{ClockPartition}, noCheckpointCalm={Calm}, modelScoutPause={ClockScoutPause}, " +
            $"checkpoint={CheckpointReset}, timePause={PauseFreezes}, pausedDamageBlocked={PausedDamageBlocked}, " +
            $"threatTiming={ThreatTiming}/{StandingDangerSeconds:F3}/{NextFloorDangerSeconds:F3}, " +
            $"viewportGeometry={(ViewportGeometryTested ? InitialBandInViewport.ToString() : "SKIPPED_NO_CAMERA_SETUP")}, " +
            $"bandHeight={InitialVisibleBandHeight:F3}, surfaceViewportY={InitialSurfaceViewportY:F4}, halfHeight={CameraHalfHeight:F2}, " +
            $"pauseRequestDelta={PauseRequestDelta:F6}, pausedDeltas={PausedCalmDelta:F6}/{PausedSurfaceDelta:F6}, " +
            $"realScoutPause={RealScoutPausesOnlyTide}, " +
            $"actualDeath={ActualDeath}, actualRespawn={ActualRespawn}, disabledColliderReset={DisabledColliderReset}, " +
            $"exitProtected={ExitProtection}, victoryStops={VictoryStops}, unscaledFade={VictoryUnscaledFade}, " +
            $"fixtureTeleports=True, viewportPixelProof=False, humanBalanceProof=False, campaignCompletionProof=False, error={Error}";
    }

    public static void CheckClock(Result result)
    {
        var clock = new BlackTideClock();
        clock.Reset(new Vector2(4f, 100f));
        result.StandingDangerSeconds = clock.SecondsUntilHeight(100f);
        result.NextFloorDangerSeconds = clock.SecondsUntilHeight(116f);
        result.ThreatTiming = Near(result.StandingDangerSeconds, 35f / 3f) &&
            Near(result.NextFloorDangerSeconds, 65f);
        var threshold = new BlackTideClock();
        threshold.Reset(new Vector2(4f, 100f));
        threshold.Advance(35f / 3f - .1f, false);
        result.ThreatTiming &= threshold.SurfaceY < 100f && Near(threshold.SecondsUntilHeight(100f), .1f);
        threshold.Advance(.1f, false);
        result.ThreatTiming &= Near(threshold.SurfaceY, 100f) && Near(threshold.SecondsUntilHeight(100f), 0f);
        clock.Advance(11f, false);
        result.Calm = BlackTideClock.CalmSeconds == 0f && Near(clock.SurfaceY, 99.8f) && Near(clock.CalmRemaining, 0f);
        clock.Advance(17f, false);
        result.ClockRate = Near(clock.SurfaceY, 104.9f) && Near(clock.CalmRemaining, 0f);
        float before = clock.SurfaceY;
        clock.Advance(60f, true);
        clock.Advance(0f, false);
        result.ClockScoutPause = Near(clock.SurfaceY, before);
        var partitioned = new BlackTideClock();
        partitioned.Reset(new Vector2(4f, 100f));
        for (int i = 0; i < 280; i++) partitioned.Advance(.1f, false);
        result.ClockPartition = Near(clock.SurfaceY, partitioned.SurfaceY);
        clock.Reset(new Vector2(8f, 120f));
        result.CheckpointReset = Near(clock.SurfaceY, 116.5f) && Near(clock.CalmRemaining, 0f);
        clock.Advance(.02f, false);
        result.CheckpointReset &= Near(clock.SurfaceY, 116.506f);
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        CheckClock(result);
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        if (!PirateFrontEnd.IsAutomationRun || life == null || abilities == null ||
            life.IsRespawning || life.IsSnared || abilities.IsScouting)
        {
            result.Error = "An explicit automation process and an idle, live production pirate are required; real saves must not be touched.";
            completed?.Invoke(result);
            yield break;
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        Vector2 originalPosition = body.position, originalVelocity = body.linearVelocity;
        Vector3 originalCheckpoint = life.CheckpointPosition;
        PirateUpgrade[] originalUpgrades = abilities.CaptureProgression();
        bool originalControls = player.ControlsEnabled, originalExitProtection = life.IsExitProtected;
        int originalDeaths = life.DeathCount;
        float originalTimeScale = Time.timeScale;
        var origin = new Vector2(3200f, 3200f);
        var ground = new GameObject("Black tide fixture - isolated safe pier");
        var water = new GameObject("Black tide fixture - opt-in prototype");
        BlackTide tide = null;
        CameraFollow viewportFollow = null;
        Action onDeath = null;
        int observedDeaths = 0;

        void Place(Vector2 centre)
        {
            player.ResetMotion();
            player.SetControlsEnabled(true);
            player.SetAutomationInputOverride(Vector2.zero);
            body.position = centre;
            player.transform.position = centre;
            Physics2D.SyncTransforms();
        }

        void CheckCheckpointViewport()
        {
            viewportFollow = UnityEngine.Object.FindFirstObjectByType<CameraFollow>();
            Camera camera = viewportFollow != null ? viewportFollow.GetComponent<Camera>() : null;
            PirateGameFlow flow = UnityEngine.Object.FindFirstObjectByType<PirateGameFlow>();
            CampaignLayout layout = flow != null && flow.Generator != null ? flow.Generator.Campaign : null;
            if (camera == null || !camera.orthographic || layout == null)
            {
                Debug.Log("PIRATE_BLACK_TIDE_VIEWPORT_GEOMETRY SKIPPED_NO_CAMERA_SETUP pixelProof=False humanBalanceProof=False");
                return;
            }
            Place(originalCheckpoint);
            viewportFollow.SnapToTarget();
            var checkpointClock = new BlackTideClock();
            checkpointClock.Reset(new Vector2(body.position.x, capsule.bounds.min.y));
            Vector3 surface = camera.WorldToViewportPoint(new Vector3(body.position.x, checkpointClock.SurfaceY, 0f));
            float bottom = camera.ViewportToWorldPoint(new Vector3(.5f, 0f, surface.z)).y;
            float top = camera.ViewportToWorldPoint(new Vector3(.5f, 1f, surface.z)).y;
            result.ViewportGeometryTested = true;
            result.CameraHalfHeight = camera.orthographicSize;
            result.InitialSurfaceViewportY = surface.y;
            result.InitialVisibleBandHeight = Mathf.Max(0f,
                Mathf.Min(checkpointClock.SurfaceY, top) - Mathf.Max(layout.Bounds.yMin, bottom));
            result.InitialBandInViewport = surface.z > 0f && surface.x >= 0f && surface.x <= 1f &&
                surface.y > 0f && surface.y < .5f && result.InitialVisibleBandHeight > .1f;
            Debug.Log($"PIRATE_BLACK_TIDE_VIEWPORT_GEOMETRY tested=True visibleBand={result.InitialBandInViewport} " +
                $"bandHeight={result.InitialVisibleBandHeight:F3} surfaceViewportY={surface.y:F4} " +
                $"cameraHalfHeight={camera.orthographicSize:F2} surfaceY={checkpointClock.SurfaceY:F3} " +
                $"viewportBottom={bottom:F3} worldBottom={layout.Bounds.yMin} checkpointPose=True pixelProof=False humanBalanceProof=False");
            Require(result.InitialBandInViewport, "The initial water band does not fit the real checkpoint camera viewport geometry.");
        }

        try
        {
            Time.timeScale = 1f;
            abilities.ResetTransientState();
            life.SetExitProtected(false);
            CheckCheckpointViewport();
            for (int layer = 0; layer < 32; layer++)
                if ((player.GroundLayer.value & (1 << layer)) != 0) { ground.layer = layer; break; }
            ground.transform.position = origin + Vector2.down * .5f;
            BoxCollider2D floor = ground.AddComponent<BoxCollider2D>();
            floor.size = new Vector2(12f, 1f);
            Place(origin + Vector2.up * (capsule.bounds.extents.y + .02f));
            for (int i = 0; i < 12; i++) yield return new WaitForFixedUpdate();
            Vector2 checkpoint = body.position;
            float feetY = capsule.bounds.min.y;
            life.SetCheckpoint(checkpoint);
            tide = water.AddComponent<BlackTide>();
            tide.Initialize(new Rect(origin.x - 10f, origin.y - 20f, 20f, 60f), life, abilities);
            Require(Near(tide.SurfaceY, feetY - 3.5f), "Initialization did not use the actual saved foot position.");

            float calm = tide.CalmRemainingSeconds, surface = tide.SurfaceY;
            result.PauseRequestDelta = Time.deltaTime;
            Time.timeScale = 0f;
            for (int i = 0; i < 6; i++) yield return null;
            result.PausedCalmDelta = tide.CalmRemainingSeconds - calm;
            result.PausedSurfaceDelta = tide.SurfaceY - surface;
            result.PauseFreezes = Time.timeScale == 0f && Near(tide.CalmRemainingSeconds, calm) && Near(tide.SurfaceY, surface);
            Time.timeScale = 1f;

            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            int deathsBeforePause = life.DeathCount;
            float submergedRequestDelta = Time.deltaTime;
            Time.timeScale = 0f;
            Place(new Vector2(origin.x, tide.SurfaceY - .2f + capsule.bounds.extents.y));
            for (int i = 0; i < 4; i++) yield return null;
            result.PausedDamageBlocked = Time.timeScale == 0f && life.DeathCount == deathsBeforePause && !life.IsRespawning;
            Debug.Log($"PIRATE_BLACK_TIDE_PAUSE_BOUNDARY clockRequestDelta={result.PauseRequestDelta:F6}, " +
                $"calmDelta={result.PausedCalmDelta:F6}, surfaceDelta={result.PausedSurfaceDelta:F6}, " +
                $"submergedRequestDelta={submergedRequestDelta:F6}, deathsBefore={deathsBeforePause}, " +
                $"deathsAfter={life.DeathCount}, noPausedDamage={result.PausedDamageBlocked}");
            if (life.IsRespawning) life.RespawnImmediately();
            Place(checkpoint);
            Time.timeScale = 1f;
            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();

            abilities.Apply(PirateUpgrade.Parrot, false);
            Require(abilities.Scout.Toggle(), "Real parrot scouting did not start from the pier.");
            calm = tide.CalmRemainingSeconds;
            surface = tide.SurfaceY;
            float worldTime = Time.time;
            for (int i = 0; i < 12; i++) yield return null;
            result.RealScoutPausesOnlyTide = tide.IsScoutingPaused && Near(tide.CalmRemainingSeconds, calm) &&
                Near(tide.SurfaceY, surface) && Time.time > worldTime;
            abilities.Scout.ReturnToPirate();

            onDeath = () => observedDeaths++;
            life.Died += onDeath;
            life.SetExitProtected(true);
            Place(new Vector2(origin.x, tide.SurfaceY - .2f + capsule.bounds.extents.y));
            for (int i = 0; i < 4; i++) yield return null;
            result.ExitProtection = observedDeaths == 0 && !life.IsRespawning;
            life.SetExitProtected(false);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (observedDeaths == 0 && Time.realtimeSinceStartup < deadline) yield return null;
            result.ActualDeath = observedDeaths == 1 && life.DeathCount == originalDeaths + 1;
            float respawnSurface = tide.SurfaceY;
            deadline = Time.realtimeSinceStartup + 2f;
            while (life.IsRespawning && Time.realtimeSinceStartup < deadline) yield return null;
            result.ActualRespawn = !life.IsRespawning && Vector2.Distance(body.position, checkpoint) < .15f;
            float resumeRise = tide.SurfaceY - respawnSurface;
            float maximumResumeRise = BlackTideClock.RiseSpeed * Mathf.Max(Time.deltaTime, Time.fixedDeltaTime) * 2f + .002f;
            result.DisabledColliderReset = Near(respawnSurface, feetY - BlackTideClock.InitialDepth) &&
                resumeRise >= -.002f && resumeRise <= maximumResumeRise && Near(tide.CalmRemainingSeconds, 0f);

            Time.timeScale = 0f;
            tide.StopAndDrain();
            float stoppedSurface = tide.SurfaceY;
            Place(new Vector2(origin.x, stoppedSurface - .2f + capsule.bounds.extents.y));
            deadline = Time.realtimeSinceStartup + 2f;
            while (tide.VisualOpacity > .001f && Time.realtimeSinceStartup < deadline) yield return null;
            result.VictoryUnscaledFade = tide.VisualOpacity <= .001f && Time.timeScale == 0f;
            Time.timeScale = 1f;
            for (int i = 0; i < 4; i++) yield return null;
            result.VictoryStops = tide.IsStopped && !tide.IsActive && Near(tide.SurfaceY, stoppedSurface) &&
                float.IsPositiveInfinity(tide.SecondsToDanger) && observedDeaths == 1;
        }
        finally
        {
            if (onDeath != null) life.Died -= onDeath;
            if (tide != null) { tide.StopAndDrain(); tide.enabled = false; }
            UnityEngine.Object.Destroy(water);
            UnityEngine.Object.Destroy(ground);
            Time.timeScale = 1f;
            life.SetExitProtected(false);
            life.SetCheckpoint(originalCheckpoint);
            if (life.IsRespawning) life.RespawnImmediately();
            abilities.RestoreProgression(originalUpgrades);
            Place(originalPosition);
            body.linearVelocity = originalVelocity;
            life.SetDeathCount(originalDeaths);
            life.SetExitProtected(originalExitProtection);
            player.SetControlsEnabled(originalControls);
            player.ClearAutomationInputOverride();
            viewportFollow?.SnapToTarget();
            Time.timeScale = originalTimeScale;
        }
        Debug.Log("PIRATE_BLACK_TIDE_REGRESSION " + result);
        completed?.Invoke(result);
    }

    private static bool Near(float a, float b) => Mathf.Abs(a - b) < .002f;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Black tide regression: " + message);
    }
}
