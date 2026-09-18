using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class CampaignMechanicsRegression
{
    public sealed class Result
    {
        public bool HookLocked, LocalChain, RangedLocked, RangedHook;
        public bool SpringLocked, SpringBounce, SpringOnce, SpringReset;
        public bool DoubleJumpLocked, DoubleJumpWorks, DoubleJumpOnce;
        public bool SaberLocked, RopeCut, SaberBlockedByWall, SaberOutOfReach, SpikeSlide, SlideAccelerates, SlideReleaseLethal;
        public bool NooseCatches, NooseCutEscape, NooseKills, PlantTelegraphs, PlantCut;
        public bool NooseCheckpointRelease, NooseDashRelease, DeathClearsGrapple, ScoutPauseResumesSafely, DeathClearsScout;
        public bool CannonTelegraphs, CannonFiresAtRange, ParrotLocked, ParrotScout, ScoutBounded, ScoutReturns, DarkReveal;
        public bool ResetLocksAll;
        public bool AudioGenerated;
        public bool PlayerPhysics;
        public bool CannonTrajectory;
        public bool DarkRenderTested, DarkPixels;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && HookLocked && LocalChain && RangedLocked && RangedHook &&
            SpringLocked && SpringBounce && SpringOnce && SpringReset && DoubleJumpLocked && DoubleJumpWorks && DoubleJumpOnce &&
            SaberLocked && RopeCut && SaberBlockedByWall && SaberOutOfReach && SpikeSlide && SlideAccelerates && SlideReleaseLethal &&
            NooseCatches && NooseCutEscape && NooseKills && NooseCheckpointRelease && NooseDashRelease &&
            DeathClearsGrapple && ScoutPauseResumesSafely && DeathClearsScout && PlantTelegraphs && PlantCut &&
            CannonTelegraphs && CannonFiresAtRange && ParrotLocked && ParrotScout && ScoutBounded && ScoutReturns && DarkReveal &&
            (!DarkRenderTested || DarkPixels) && ResetLocksAll && AudioGenerated && PlayerPhysics && CannonTrajectory;
        public override string ToString() =>
            $"success={Success}, hook={HookLocked}/{LocalChain}/{RangedLocked}/{RangedHook}, " +
            $"spring={SpringLocked}/{SpringBounce}/{SpringOnce}/{SpringReset}, double={DoubleJumpLocked}/{DoubleJumpWorks}/{DoubleJumpOnce}, " +
            $"saber={SaberLocked}/{RopeCut}, saberWall={SaberBlockedByWall}, saberRange={SaberOutOfReach}, slide={SpikeSlide}/{SlideAccelerates}/{SlideReleaseLethal}, " +
            $"noose={NooseCatches}/{NooseCutEscape}/{NooseKills}, nooseReset={NooseCheckpointRelease}, dashSnare={NooseDashRelease}, " +
            $"deathRope={DeathClearsGrapple}, pauseScout={ScoutPauseResumesSafely}, deathScout={DeathClearsScout}, plant={PlantTelegraphs}/{PlantCut}, " +
            $"cannon={CannonTelegraphs}/{CannonFiresAtRange}, parrot={ParrotLocked}/{ParrotScout}/{ScoutBounded}/{ScoutReturns}/{DarkReveal}, " +
            $"darkPixels={(DarkRenderTested ? DarkPixels.ToString() : "SKIPPED_NO_GRAPHICS")}, reset={ResetLocksAll}, audio={AudioGenerated}, physics={PlayerPhysics}, cannonTrajectory={CannonTrajectory}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        Result result = new Result();
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        if (abilities == null || grapple == null || life == null)
        {
            result.Error = "Campaign player components are missing.";
            completed?.Invoke(result);
            yield break;
        }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D playerCollider = player.GetComponent<Collider2D>();
        Vector2 savedPosition = body.position;
        Vector2 savedVelocity = body.linearVelocity;
        Vector3 savedCheckpoint = life.CheckpointPosition;
        PirateUpgrade[] savedUpgrades = abilities.CaptureProgression();
        bool savedControls = player.ControlsEnabled;
        int savedDeaths = life.DeathCount;
        float savedTimeScale = Time.timeScale;
        Vector2 origin = new Vector2(2000f, 2000f);
        var objects = new List<GameObject>();
        WaitForFixedUpdate step = new WaitForFixedUpdate();
        int groundLayer = 0;
        for (int i = 0; i < 32; i++) if ((player.GroundLayer.value & (1 << i)) != 0) { groundLayer = i; break; }

        GameObject Create(string name, Vector2 position, Vector2 size, bool trigger = true)
        {
            GameObject obj = new GameObject("Mechanics fixture - " + name);
            obj.transform.position = position;
            BoxCollider2D collider = obj.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = trigger;
            objects.Add(obj);
            return obj;
        }
        void Place(Vector2 position)
        {
            abilities.ResetTransientState();
            player.ResetMotion();
            player.SetControlsEnabled(true);
            player.SetAutomationInputOverride(Vector2.zero);
            grapple.SetAutomationInputOverride(false);
            body.position = position;
            player.transform.position = position;
            Physics2D.SyncTransforms();
        }

        try
        {
            PlayerPhysicsRegression.Result physics = null;
            yield return PlayerPhysicsRegression.Run(player, value => physics = value);
            result.PlayerPhysics = physics != null && physics.Success;
            abilities.ResetProgression();
            Place(origin);
            GameObject floor = Create("safe floor", origin - Vector2.up, new Vector2(80f, 1f), false);
            floor.layer = groundLayer;
            GameObject chainObject = Create("hanging chain", origin + Vector2.up * 7f, Vector2.one * 0.2f);
            HookAnchor chain = chainObject.AddComponent<HookAnchor>();
            chain.Configure(false, 6f);
            result.HookLocked = !grapple.TryAttachToAnchor(chain);
            abilities.Apply(PirateUpgrade.Hook1);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            grapple.SetAutomationInputOverride(true);
            result.LocalChain = grapple.TryAttachToAnchor(chain) && grapple.RopeLength > 6f;
            grapple.Detach();
            chain.Configure(true, 0f);
            result.RangedLocked = !grapple.TryAttachToAnchor(chain);
            abilities.Apply(PirateUpgrade.Hook2);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            result.RangedHook = grapple.TryAttachToAnchor(chain);
            grapple.Detach();
            chainObject.SetActive(false);

            GameObject spikesObject = Create("spikes", origin + Vector2.right * 10f, new Vector2(20f, 0.3f));
            InstantKillHazard spikes = spikesObject.AddComponent<InstantKillHazard>();
            spikes.Configure(HazardKind.Spikes);
            Collider2D spikeCollider = spikesObject.GetComponent<Collider2D>();
            spikesObject.SetActive(false);
            float halfHeight = playerCollider.bounds.extents.y;
            Vector2 spikeContact = new Vector2(origin.x + 5f, origin.y + 0.15f + halfHeight - 0.03f);
            Place(spikeContact);
            result.SpringLocked = spikes.IsLethalTo(life, spikeCollider);
            abilities.Apply(PirateUpgrade.SpringLeg);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            spikesObject.SetActive(true);
            Physics2D.SyncTransforms();
            Place(spikeContact + Vector2.up * 0.6f);
            body.linearVelocity = Vector2.down * 2f;
            int deaths = life.DeathCount;
            for (int frame = 0; frame < 20 && body.linearVelocity.y <= 0f; frame++) yield return step;
            result.SpringBounce = life.DeathCount == deaths && body.linearVelocity.y > 15f && !abilities.SpringAvailable;
            GameObject otherSpikesObject = Create("second spikes", origin + Vector2.right * 30f, new Vector2(3f, 0.3f));
            InstantKillHazard otherSpikes = otherSpikesObject.AddComponent<InstantKillHazard>();
            otherSpikes.Configure(HazardKind.Spikes);
            body.position = new Vector2(origin.x + 30f, origin.y + 0.15f + halfHeight - 0.03f);
            body.linearVelocity = Vector2.down;
            Physics2D.SyncTransforms();
            result.SpringOnce = otherSpikes.IsLethalTo(life, otherSpikes.GetComponent<Collider2D>());
            spikesObject.SetActive(false);
            otherSpikesObject.SetActive(false);
            body.position = origin + Vector2.up * 2f;
            body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
            for (int frame = 0; frame < 50; frame++) yield return step;
            result.SpringReset = player.IsGrounded && abilities.SpringAvailable;

            abilities.ResetProgression();
            Place(origin + Vector2.up * 8f);
            for (int frame = 0; frame < 8; frame++) yield return step;
            float lockedBefore = body.linearVelocity.y;
            bool groundedBeforeLockedJump = player.IsGrounded;
            player.SetAutomationInputOverride(Vector2.zero, jumpPressed: true);
            yield return null;
            yield return step;
            result.DoubleJumpLocked = !groundedBeforeLockedJump && !player.DoubleJumpUnlocked && body.linearVelocity.y <= lockedBefore + 0.05f;
            Debug.Log($"PIRATE_LOCKED_DOUBLE_JUMP beforeVy={lockedBefore:F3}, afterVy={body.linearVelocity.y:F3}, groundedBefore={groundedBeforeLockedJump}, unlocked={player.DoubleJumpUnlocked}, accepted={result.DoubleJumpLocked}");
            abilities.Apply(PirateUpgrade.DoubleJump);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            player.SetAutomationInputOverride(Vector2.zero, jumpPressed: true);
            yield return null;
            yield return step;
            result.DoubleJumpWorks = body.linearVelocity.y > 10f;
            body.linearVelocity = Vector2.down;
            player.SetAutomationInputOverride(Vector2.zero, jumpPressed: true);
            yield return null;
            yield return step;
            result.DoubleJumpOnce = body.linearVelocity.y <= 0f;

            abilities.ResetProgression();
            Place(origin);
            GameObject ropeObject = Create("cuttable rope", origin + Vector2.right * 0.8f, new Vector2(0.2f, 2f));
            SaberCuttable rope = ropeObject.AddComponent<SaberCuttable>();
            Physics2D.SyncTransforms();
            result.SaberLocked = !abilities.TryAttack() && !rope.IsCut;
            abilities.Apply(PirateUpgrade.Saber1);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            result.RopeCut = abilities.TryAttack() && rope.IsCut && !ropeObject.GetComponent<Collider2D>().enabled;

            GameObject farRopeObject = Create("out of saber range", origin + Vector2.right * 3.2f, new Vector2(0.2f, 1.2f));
            SaberCuttable farRope = farRopeObject.AddComponent<SaberCuttable>();
            abilities.ResetTransientState();
            Physics2D.SyncTransforms();
            result.SaberOutOfReach = abilities.TryAttack() && !farRope.IsCut;
            GameObject shieldedRopeObject = Create("rope behind solid wall", origin + Vector2.right * 1.5f, new Vector2(0.2f, 1.2f));
            SaberCuttable shieldedRope = shieldedRopeObject.AddComponent<SaberCuttable>();
            GameObject swordWall = Create("solid sword obstruction", origin + Vector2.right * 0.8f, new Vector2(0.25f, 2f), false);
            swordWall.layer = groundLayer;
            abilities.ResetTransientState();
            Physics2D.SyncTransforms();
            result.SaberBlockedByWall = abilities.TryAttack() && !shieldedRope.IsCut;
            swordWall.SetActive(false);
            farRopeObject.SetActive(false);
            shieldedRopeObject.SetActive(false);

            abilities.ResetProgression();
            abilities.Apply(PirateUpgrade.Saber2);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            Place(spikeContact + Vector2.up * 0.1f);
            spikesObject.SetActive(true);
            player.SetAutomationInputOverride(Vector2.right);
            abilities.SetAutomationSlide(true);
            body.linearVelocity = new Vector2(5f, -1f);
            Physics2D.SyncTransforms();
            deaths = life.DeathCount;
            float maximumSpeed = 0f;
            bool sawSlide = false;
            for (int frame = 0; frame < 30; frame++)
            {
                yield return step;
                sawSlide |= abilities.IsSpikeSliding;
                maximumSpeed = Mathf.Max(maximumSpeed, body.linearVelocity.x);
            }
            result.SpikeSlide = sawSlide && life.DeathCount == deaths;
            result.SlideAccelerates = maximumSpeed > player.MoveSpeed + 1f;
            abilities.SetAutomationSlide(false);
            result.SlideReleaseLethal = spikes.IsLethalTo(life, spikeCollider);
            spikesObject.SetActive(false);

            Place(origin);
            abilities.Apply(PirateUpgrade.Saber1);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            GameObject nooseObject = Create("noose", origin + Vector2.right * 1f, new Vector2(1f, 1.2f));
            SnareTrap noose = nooseObject.AddComponent<SnareTrap>();
            noose.Configure(0.5f);
            body.position = origin + Vector2.right;
            Physics2D.SyncTransforms();
            for (int frame = 0; frame < 3; frame++) yield return step;
            result.NooseCatches = noose.HasVictim;
            abilities.TryAttack();
            result.NooseCutEscape = noose.IsCut && !noose.HasVictim && body.gravityScale > 0f;

            GameObject resetNooseObject = Create("checkpoint reset noose", origin + Vector2.right * 5f, new Vector2(1f, 1.2f));
            SnareTrap resetNoose = resetNooseObject.AddComponent<SnareTrap>();
            Place(origin + Vector2.right * 5f);
            for (int frame = 0; frame < 3; frame++) yield return step;
            bool caughtBeforeReset = resetNoose.HasVictim && life.IsSnared;
            life.SetCheckpoint(origin);
            life.RespawnImmediately();
            for (int frame = 0; frame < 5; frame++) yield return step;
            result.NooseCheckpointRelease = caughtBeforeReset && !resetNoose.HasVictim && !life.IsSnared &&
                Mathf.Abs(body.position.x - origin.x) < 0.05f && body.gravityScale > 0f;
            resetNooseObject.SetActive(false);

            GameObject dashNooseObject = Create("dash capture noose", origin + new Vector2(3f, 5f), new Vector2(1f, 1.4f));
            SnareTrap dashNoose = dashNooseObject.AddComponent<SnareTrap>();
            Place(origin + Vector2.up * 5f);
            player.SetAutomationInputOverride(Vector2.right, dashPressed: true);
            yield return null;
            bool sawDashBeforeNoose = false;
            for (int frame = 0; frame < 10; frame++)
            {
                sawDashBeforeNoose |= player.IsDashing;
                yield return step;
                if (dashNoose.HasVictim) break;
            }
            bool caughtFromDash = dashNoose.HasVictim;
            abilities.TryAttack();
            player.SetAutomationInputOverride(Vector2.zero);
            for (int frame = 0; frame < 4; frame++) yield return step;
            result.NooseDashRelease = sawDashBeforeNoose && caughtFromDash && dashNoose.IsCut && !life.IsSnared &&
                !player.IsDashing && body.gravityScale > 0f && body.linearVelocity.y < -0.5f;
            dashNooseObject.SetActive(false);

            GameObject lethalNooseObject = Create("lethal noose", origin + Vector2.right * 3f, new Vector2(1f, 1.2f));
            SnareTrap lethalNoose = lethalNooseObject.AddComponent<SnareTrap>();
            lethalNoose.Configure(0.5f);
            Place(origin + Vector2.right * 3f);
            life.SetCheckpoint(origin);
            deaths = life.DeathCount;
            for (int frame = 0; frame < 35; frame++) yield return step;
            result.NooseKills = life.DeathCount == deaths + 1;
            lethalNooseObject.SetActive(false);
            life.RespawnImmediately();

            Place(origin);
            abilities.Apply(PirateUpgrade.Hook2, false);
            chainObject.SetActive(true);
            grapple.SetAutomationInputOverride(true);
            bool ropeBeforeDeath = grapple.TryAttachToAnchor(chain);
            life.Die();
            result.DeathClearsGrapple = ropeBeforeDeath && !grapple.IsAttached && !player.ControlsEnabled;
            life.RespawnImmediately();
            result.DeathClearsGrapple &= !grapple.IsAttached && !player.IsDashing && body.gravityScale > 0f && player.ControlsEnabled;
            chainObject.SetActive(false);

            Place(origin);
            GameObject plantObject = Create("plant", origin + new Vector2(2f, 3f), new Vector2(1.2f, 1.5f));
            HangingPlant plant = plantObject.AddComponent<HangingPlant>();
            plant.Initialize(player.transform);
            for (int frame = 0; frame < 65; frame++) { yield return step; result.PlantTelegraphs |= plant.IsWindingUp; }
            Place((Vector2)plant.transform.position - Vector2.right * 0.7f);
            abilities.TryAttack();
            result.PlantCut = plant.IsDead;
            plantObject.SetActive(false);

            Place(origin);
            GameObject cannonObject = Create("cannon", origin + Vector2.right * 12f, Vector2.one);
            PirateWorldArt.ApplyTo(cannonObject,PirateArtKind.Cannon,new Vector2(1.5f,1.3f));
            cannonObject.GetComponent<PirateWorldVisual>().SetGroundSurface(origin.y-.5f);
            Cannon cannon = cannonObject.AddComponent<Cannon>();
            cannon.Initialize(player.transform, PirateWorldArt.GetSprite(PirateArtKind.Cannonball));
            cannon.SetActivationRange(16f);
            Vector2 warnedOrigin=Vector2.zero, warnedDirection=Vector2.zero;
            bool warningCaptured=false;
            for (int frame = 0; frame < 90; frame++)
            {
                yield return step;
                result.CannonTelegraphs |= cannon.IsTelegraphing;
                if (cannon.IsTelegraphing)
                {
                    warnedOrigin=cannon.LockedShotOrigin; warnedDirection=cannon.LockedShotDirection;
                    warningCaptured=true;
                }
                if (cannon.ShotsFired==1 && warningCaptured && !result.CannonTrajectory)
                {
                    foreach (Cannonball ball in UnityEngine.Object.FindObjectsByType<Cannonball>(FindObjectsSortMode.None))
                    {
                        Rigidbody2D shot=ball.GetComponent<Rigidbody2D>();
                        Vector2 delta=(Vector2)ball.transform.position-warnedOrigin;
                        if (shot==null || delta.magnitude>2f) continue;
                        float offLine=Mathf.Abs(delta.x*warnedDirection.y-delta.y*warnedDirection.x);
                        result.CannonTrajectory=Vector2.Distance(cannon.LastShotOrigin,warnedOrigin)<.001f &&
                            Vector2.Distance(cannon.LastShotDirection,warnedDirection)<.001f && offLine<.005f &&
                            Vector2.Dot(shot.linearVelocity.normalized,warnedDirection)>.9999f;
                        if (result.CannonTrajectory) Debug.Log($"PIRATE_CANNON_TRAJECTORY_PASS sameOrigin=True sameDirection=True offLine={offLine:F5} physicalProjectile=True artMuzzle=True");
                    }
                }
            }
            result.CannonFiresAtRange = cannon.ShotsFired > 0;
            cannonObject.SetActive(false);

            abilities.ResetProgression();
            Place(origin);
            for (int frame = 0; frame < 10; frame++) yield return step;
            result.ParrotLocked = !abilities.Scout.Toggle();
            abilities.Apply(PirateUpgrade.Parrot);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            GameObject darkness = new GameObject("Mechanics fixture darkness");
            objects.Add(darkness);
            darkness.transform.position = origin + Vector2.right * 8f;
            DarkZone dark = darkness.AddComponent<DarkZone>();
            dark.Initialize(new Vector2(16f, 8f));
            yield return null;
            int revealedBefore = dark.RevealedCellCount;
            result.DarkRenderTested = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
            float darknessBefore = result.DarkRenderTested ? RenderDarknessSample(origin, origin + new Vector2(12f, 1f)) : -1f;
            result.ParrotScout = abilities.Scout.Toggle() && abilities.IsScouting && !player.ControlsEnabled;
            Vector2 stationaryPirate = body.position;
            abilities.Scout.SetAutomationInput(Vector2.right);
            for (int frame = 0; frame < 130; frame++) yield return step;
            result.ScoutBounded = Vector2.Distance(body.position, stationaryPirate) < 0.02f &&
                Vector2.Distance(abilities.ScoutTransform.position, body.position) <= abilities.Scout.MaximumRange + 0.02f;
            result.DarkReveal = dark.RevealedCellCount > revealedBefore;
            if (result.DarkRenderTested)
            {
                float nearBird = RenderDarknessSample(origin, origin + new Vector2(12f, 1f));
                float farFromBoth = RenderDarknessSample(origin, origin + new Vector2(6f, 1f));
                result.DarkPixels = darknessBefore < 0.2f && farFromBoth < 0.2f && nearBird > 0.85f;
                Debug.Log($"PIRATE_DARKNESS_RENDER before={darknessBefore:F3}, nearBird={nearBird:F3}, farBoth={farFromBoth:F3}, graphics={SystemInfo.graphicsDeviceType}, success={result.DarkPixels}");
            }
            else Debug.Log("PIRATE_DARKNESS_RENDER SKIPPED_NO_GRAPHICS — CPU reveal counters are not pixel proof.");
            PirateGameFlow flow = UnityEngine.Object.FindFirstObjectByType<PirateGameFlow>();
            if (flow != null)
            {
                Vector3 birdBeforePause = abilities.ScoutTransform.position;
                flow.SetMenuBlocked(true);
                yield return null;
                yield return null;
                bool remainedStill = Vector3.Distance(birdBeforePause, abilities.ScoutTransform.position) < 0.01f;
                flow.SetMenuBlocked(false);
                player.SetAutomationInputOverride(Vector2.right, jumpPressed: true, dashPressed: true);
                yield return null;
                for (int frame = 0; frame < 3; frame++) yield return step;
                result.ScoutPauseResumesSafely = remainedStill && abilities.IsScouting && !player.ControlsEnabled &&
                    !player.IsDashing && !grapple.IsAttached && Vector2.Distance(body.position, stationaryPirate) < 0.02f;
                player.ClearAutomationInputOverride();
                player.SetAutomationInputOverride(Vector2.zero);
            }
            abilities.Scout.ReturnToPirate();
            result.ScoutReturns = !abilities.IsScouting && player.ControlsEnabled && body.gravityScale > 0f;
            for (int frame = 0; frame < 3; frame++) yield return step;
            bool scoutingBeforeDeath = abilities.Scout.Toggle();
            life.Die();
            result.DeathClearsScout = scoutingBeforeDeath && !abilities.IsScouting && !player.ControlsEnabled;
            life.RespawnImmediately();
            result.DeathClearsScout &= !abilities.IsScouting && player.ControlsEnabled && body.gravityScale > 0f &&
                abilities.ScoutTransform.parent == player.transform;
            abilities.ResetProgression();
            result.ResetLocksAll = abilities.HookLevel == 0 && abilities.LegLevel == 1 && abilities.SaberLevel == 0 &&
                !abilities.HasParrot && !player.DoubleJumpUnlocked && !abilities.IsScouting;
            result.AudioGenerated = PirateAudio.VerifyGeneratedAudio(out string audioDetail);
            Debug.Log("PIRATE_AUDIO_REGRESSION " + audioDetail);
        }
        finally
        {
            Time.timeScale = savedTimeScale;
            foreach (GameObject obj in objects) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            life.SetCheckpoint(savedCheckpoint);
            if (life.IsRespawning) life.RespawnImmediately();
            life.SetDeathCount(savedDeaths);
            abilities.Scout.ClearAutomationInput();
            abilities.RestoreProgression(savedUpgrades);
            abilities.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride();
            grapple.ClearAutomationInputOverride();
            player.ResetMotion();
            player.SetControlsEnabled(savedControls);
            body.position = savedPosition;
            player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity;
            Physics2D.SyncTransforms();
        }
        Debug.Log("PIRATE_CAMPAIGN_MECHANICS " + result);
        completed?.Invoke(result);
    }

    private static float RenderDarknessSample(Vector2 origin, Vector2 sample)
    {
        GameObject cameraObject = new GameObject("Darkness pixel regression camera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.white;
        camera.transform.position = new Vector3(origin.x + 8f, origin.y, -10f);
        RenderTexture target = new RenderTexture(512, 256, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Texture2D pixels = new Texture2D(512, 256, TextureFormat.RGB24, false, true);
        RenderTexture previous = RenderTexture.active;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 512, 256), 0, 0);
            pixels.Apply();
            Vector3 viewport = camera.WorldToViewportPoint(sample);
            Color color = pixels.GetPixel(Mathf.Clamp(Mathf.RoundToInt(viewport.x * 512f), 0, 511),
                Mathf.Clamp(Mathf.RoundToInt(viewport.y * 256f), 0, 255));
            return Mathf.Max(color.r, color.g, color.b);
        }
        finally
        {
            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.Destroy(cameraObject);
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(pixels);
        }
    }
}
