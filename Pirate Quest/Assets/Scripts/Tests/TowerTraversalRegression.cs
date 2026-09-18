using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class TowerTraversalRegression : MonoBehaviour
{
    private PlayerMovement player;
    private PlayerLife life;
    private PlayerGrapple grapple;
    private PirateGameFlow flow;
    private LevelGenerator generator;
    private Rigidbody2D body;
    private Collider2D capsule;
    private string evidenceDirectory;

    private IEnumerator Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;
        Time.captureDeltaTime = Time.fixedDeltaTime;
        player = FindFirstObjectByType<PlayerMovement>();
        life = player.GetComponent<PlayerLife>();
        grapple = player.GetComponent<PlayerGrapple>();
        body = player.GetComponent<Rigidbody2D>();
        capsule = player.GetComponent<Collider2D>();
        flow = GetComponent<PirateGameFlow>();
        generator = FindFirstObjectByType<LevelGenerator>();
        string[] arguments = Environment.GetCommandLineArgs();
        for (int i = 0; i < arguments.Length - 1; i++)
            if (arguments[i] == "-pirateQuestEvidence") evidenceDirectory = arguments[i + 1];

        yield return null;
        GrappleRegression.Result ropeResult = null;
        yield return GrappleRegression.Run(player, result => ropeResult = result);
        Debug.Log("PIRATE_GRAPPLE_REGRESSION: " + ropeResult);
        if (ropeResult == null || !ropeResult.Success)
        {
            Fail("grapple regression failed");
            yield break;
        }

        flow.RestartWithSeed(20260918);
        player.SetAutomationInputOverride(Vector2.zero);
        for (int i = 0; i < 20; i++) yield return null;
        var animator = player.GetComponent<PlayerVisualAnimator>();
        var runningFrames = new HashSet<Sprite>();
        for (int i = 0; i < 80; i++)
        {
            player.SetAutomationInputOverride(i < 40 ? Vector2.left : Vector2.right);
            yield return null;
            if (animator.CurrentState == PlayerVisualAnimator.AnimationState.Run)
                runningFrames.Add(animator.CurrentSprite);
        }
        Debug.Log($"PIRATE_ANIMATION_RUNTIME: uniqueRunFrames={runningFrames.Count}, complete={animator.HasFrameAnimation}");
        if (!animator.HasFrameAnimation || runningFrames.Count < 6)
        {
            Fail("fewer than six actual run frames seen while moving");
            yield break;
        }

        int[] seeds = { 20260918, 7, 42, 2026, 18092026, -13579, 0, int.MaxValue, int.MinValue, 918 };
        int totalJumps = 0;
        foreach (int seed in seeds)
        {
            flow.RestartWithSeed(seed);
            player.SetAutomationInputOverride(Vector2.zero);
            grapple.SetAutomationInputOverride(false);
            for (int i = 0; i < 12; i++) yield return null;
            int deathsAtStart = life.DeathCount;
            int reachedIndex = 0;
            int targetIndex = 1;
            int jumps = 0;
            int sinceProgress = 0;
            int stableFrames = 0;
            bool jumping = false;
            bool leftSupport = false;
            float previousY = body.position.y;
            TowerRouteLayout route = generator.TowerRoute;

            for (int frame = 0; frame < 12000 && !flow.IsVictory; frame++)
            {
                float feetY = capsule.bounds.min.y;
                bool supported = player.IsGrounded && Mathf.Abs(body.linearVelocity.y) < 0.15f;
                int supportIndex = -1;
                if (supported)
                {
                    for (int i = 0; i < route.Steps.Count; i++)
                    {
                        var step = route.Steps[i];
                        if (Mathf.Abs(feetY - step.SurfaceY) < 0.12f &&
                            body.position.x > step.Tiles.xMin + capsule.bounds.extents.x + 0.2f &&
                            body.position.x < step.Tiles.xMax - capsule.bounds.extents.x - 0.2f)
                            supportIndex = i;
                    }
                }
                stableFrames = supportIndex >= reachedIndex ? stableFrames + 1 : 0;
                if (!supported) leftSupport = true;
                if (supportIndex > reachedIndex && stableFrames >= 3 && leftSupport)
                {
                    reachedIndex = supportIndex;
                    targetIndex = Mathf.Min(reachedIndex + 1, route.Steps.Count - 1);
                    jumping = false;
                    leftSupport = false;
                    sinceProgress = 0;
                    if (seed == 20260918 && reachedIndex <= 6) SaveFrame($"landing-{reachedIndex:00}.png");
                }

                var target = route.Steps[targetIndex];
                float distance = target.CenterX - body.position.x;
                float braking = player.IsGrounded ? player.Deceleration : player.AirDeceleration;
                float stoppingDistance = body.linearVelocity.x * body.linearVelocity.x / (2f * braking);
                bool shouldBrake = distance * body.linearVelocity.x > 0f &&
                                   stoppingDistance >= Mathf.Abs(distance) - 0.12f;
                float horizontalKey = Mathf.Abs(distance) < 0.12f || shouldBrake ? 0f : Mathf.Sign(distance);

                bool jump = !jumping && supportIndex == reachedIndex && stableFrames >= 3;
                if (jump)
                {
                    jumping = true;
                    leftSupport = false;
                    jumps++;
                }
                player.SetAutomationInputOverride(new Vector2(horizontalKey, 0f), jumpPressed: jump);
                yield return null;
                sinceProgress++;
                if (life.DeathCount != deathsAtStart || sinceProgress > 200 || body.position.y < previousY - 8f)
                {
                    SaveFrame($"failed-{seed}.png");
                    Fail($"seed={seed} reached={reachedIndex} target={targetIndex} pos={body.position} " +
                        $"velocity={body.linearVelocity} feet={capsule.bounds.min.y:F3} grounded={player.IsGrounded} " +
                        $"targetSurface={target.SurfaceY} deaths={life.DeathCount - deathsAtStart}");
                    yield break;
                }
                previousY = body.position.y;
            }

            player.SetAutomationInputOverride(Vector2.zero);
            for (int settle = 0; settle < 50; settle++) yield return null;
            var top = route.Steps[route.Steps.Count - 1];
            bool standingAtTop = player.IsGrounded && Mathf.Abs(capsule.bounds.min.y - top.SurfaceY) < 0.12f;
            if (!flow.IsVictory || life.DeathCount != deathsAtStart)
            {
                Fail($"seed={seed}: full run did not reach victory without death");
                yield break;
            }
            if (!standingAtTop || life.CheckpointPosition.y <= route.Steps[8].SurfaceY || !player.DoubleJumpUnlocked)
            {
                Fail($"seed={seed}: topLanding={standingAtTop}, checkpoint={life.CheckpointPosition}, " +
                    $"upgrade={player.DoubleJumpUnlocked}, feet={capsule.bounds.min.y:F2}");
                yield break;
            }
            totalJumps += jumps;
            Debug.Log($"PIRATE_TRAVERSAL_SEED_SUCCESS: seed={seed}, jumps={jumps}, " +
                $"lastLanding={route.Steps.Count - 1}, victory=True, deaths=0, teleportsDuringRun=0, keyboardOnly=True, dash=0, grapple=0, extraJump=0");
            if (seed == 20260918) SaveFrame("victory.png");
        }

        flow.RestartWithSeed(20260918);
        Vector2 checkpoint = generator.TowerRoute.Steps[8].StandingPosition(capsule.bounds.extents.y);
        life.SetCheckpoint(checkpoint);
        int beforeDeath = life.DeathCount;
        life.Die();
        float respawnDeadline = Time.realtimeSinceStartup + 2f;
        while (life.IsRespawning && Time.realtimeSinceStartup < respawnDeadline) yield return null;
        bool respawn = !life.IsRespawning && life.DeathCount == beforeDeath + 1 &&
                       Vector2.Distance(body.position, checkpoint) < 0.2f &&
                       !player.GetComponent<SpriteRenderer>().enabled && animator.CurrentSprite != null;
        Debug.Log($"PIRATE_RESPAWN_REGRESSION: success={respawn}, position={body.position}, checkpoint={checkpoint}");
        if (!respawn)
        {
            Fail("checkpoint respawn failed");
            yield break;
        }
        Debug.Log($"PIRATE_TRAVERSAL_SUCCESS: seeds={seeds.Length}, totalJumps={totalJumps}, grapple=True, animation=True, respawn=True");
        Application.Quit(0);
    }

    private void Fail(string message)
    {
        Debug.LogError("PIRATE_TRAVERSAL_FAILED: " + message);
        Application.Quit(4);
    }

    private void SaveFrame(string filename)
    {
        if (string.IsNullOrEmpty(evidenceDirectory) || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            return;
        Directory.CreateDirectory(evidenceDirectory);
        Camera camera = Camera.main;
        var render = new RenderTexture(1280, 720, 24);
        var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = render;
            camera.Render();
            RenderTexture.active = render;
            texture.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(evidenceDirectory, filename), texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Destroy(render);
            Destroy(texture);
        }
    }
}
