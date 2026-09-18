using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class SecretCacheTraversalRegression
{
    public sealed class Result
    {
        public int Cases, Drops, Returns, Revealed, OrdinaryJumps;
        public string Error;
        public bool Success => Error == null && Cases == 12 && Drops == 8 && Returns == 12 && Revealed == 12 && OrdinaryJumps == 8;
        public override string ToString() => $"success={Success} cases={Cases}/12 drops={Drops}/8 returns={Returns}/12 revealed={Revealed}/12 ordinaryJumps={OrdinaryJumps}/8 error={Error}";
    }

    public static IEnumerator Run(PirateGameFlow flow, PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        if (!PirateFrontEnd.IsAutomationRun || flow == null || !flow.IsInitialized || player == null || !player.ControlsEnabled || Time.timeScale != 1f)
        { result.Error = "Requires a live unpaused automation process."; completed?.Invoke(result); yield break; }
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        PlayerGrapple grapple = player.GetComponent<PlayerGrapple>();
        int savedChapter = flow.ChapterIndex;
        float savedMoveSpeed = player.MoveSpeed;
        if (Mathf.Abs(savedMoveSpeed - PirateMovementProfile.ForChapter(savedChapter)) > .001f)
        { result.Error = "Source scene is not using its real chapter movement profile."; completed?.Invoke(result); yield break; }
        Vector2 savedPosition = body.position, savedVelocity = body.linearVelocity;
        Vector3 checkpoint = life.CheckpointPosition;
        bool exitProtected = life.IsExitProtected;
        PirateUpgrade[] upgrades = abilities.CaptureProgression();
        int deaths = life.DeathCount, sessionDeaths = PirateCampaignSession.Deaths;
        var objects = new List<GameObject>();
        try
        {
            abilities.ResetProgression(); abilities.SetAutomationSlide(false);
            grapple.SetAutomationInputOverride(false);
            foreach (int seed in new[] { 20260918, 42 })
            foreach (CampaignLayout.SecretEntranceKind kind in new[] {
                CampaignLayout.SecretEntranceKind.UnderStairs, CampaignLayout.SecretEntranceKind.DropHatch, CampaignLayout.SecretEntranceKind.SideSlit })
            foreach (int direction in new[] { 1, -1 })
            {
                if (result.Error != null) continue;
                int chapter = -1;
                CampaignLayout map = null;
                CampaignLayout.SecretCache source = null;
                foreach (int candidate in new[] { 0, 1, 2, 3 })
                {
                    map = CampaignLayout.Create(seed, candidate, player.JumpLaunchSpeed, player.GravityStrength,
                        Time.fixedDeltaTime, PirateMovementProfile.ForChapter(candidate));
                    source = map.SecretCaches.FirstOrDefault(cache => cache.Kind == kind &&
                        (kind != CampaignLayout.SecretEntranceKind.SideSlit || cache.ReturnPath.All(p => Mathf.Abs(p.y - cache.ReturnPath[0].y) < .01f)));
                    if (source != null) { chapter = candidate; break; }
                }
                if (source == null) { result.Error = $"Missing representative {kind} at seed {seed}."; break; }
                player.ConfigureChapterSpeed(chapter);
                Vector2 offset = new Vector2(16000f + result.Cases * 100f, 12000f);
                Vector2 Point(Vector2 point) => offset + new Vector2(direction * (point.x - source.ChamberBounds.center.x), point.y - source.ChamberBounds.yMin);
                Rect Area(Rect area) => new Rect(Point(new Vector2(direction > 0 ? area.xMin : area.xMax, area.yMin)), area.size);
                Rect capture = Rect.MinMaxRect(
                    Mathf.Min(source.ChamberBounds.xMin, source.ReturnPath.Min(p => p.x)) - 5f,
                    source.ChamberBounds.yMin - 5f,
                    Mathf.Max(source.ChamberBounds.xMax, source.ReturnPath.Max(p => p.x)) + 5f,
                    source.ReturnPath.Max(p => p.y) + 6f);
                int layer = LayerMask.NameToLayer("Ground");
                foreach (CampaignLayout.Platform platform in map.Platforms)
                    if (platform.Bounds.Overlaps(capture)) AddSupport(objects, Area(platform.Bounds), platform.OneWay, layer);
                foreach (RectInt wall in map.Solids)
                {
                    Rect rect = new Rect(wall.x, wall.y, wall.width, wall.height);
                    if (rect.Overlaps(capture)) AddSupport(objects, Area(rect), false, layer);
                }
                foreach (CampaignLayout.Spawn spawn in map.Spawns.Where(spawn => spawn.Kind == CampaignLayout.SpawnKind.JumpPad &&
                    capture.Overlaps(new Rect(spawn.Position - spawn.Size * .5f, spawn.Size))))
                {
                    GameObject pad = PirateWorldArt.Create("Fixture recovery pad", PirateArtKind.JumpPad, Point(spawn.Position), spawn.Size);
                    objects.Add(pad); BoxCollider2D trigger = pad.AddComponent<BoxCollider2D>(); trigger.size = spawn.Size; trigger.isTrigger = true;
                    pad.AddComponent<CampaignJumpPad>().Configure(spawn.Value);
                }
                var description = new CampaignLayout.SecretCache { Id = source.Id, Bounds = Area(source.Bounds), Concealed = true,
                    Kind = kind, ChamberBounds = Area(source.ChamberBounds), RewardBounds = Area(source.RewardBounds),
                    EntranceBounds = Area(source.EntranceBounds), CoverBounds = Area(source.CoverBounds) };
                GameObject coverObject = new GameObject("Fixture hidden wall"); objects.Add(coverObject);
                SecretCacheCover cover = coverObject.AddComponent<SecretCacheCover>(); cover.Initialize(description, chapter, player.transform);
                Vector2[] path = source.ReturnPath.Select(Point).ToArray();
                player.ResetMotion(); player.SetAutomationInputOverride(Vector2.zero);
                body.position = path[0] + Vector2.up * (capsule.bounds.extents.y + .03f); player.transform.position = body.position;
                life.SetCheckpoint(body.position); life.SetExitProtected(false); Physics2D.SyncTransforms();
                for (int frame = 0; frame < 30; frame++) { yield return null; yield return new WaitForFixedUpdate(); }
                bool staged = player.IsGrounded && !cover.IsDiscovered;
                var outward = new LegResult();
                yield return Follow(player, body, capsule, path, outward);
                bool reachedReward = outward.Success && cover.IsDiscovered;
                var inward = new LegResult();
                yield return Follow(player, body, capsule, path.Reverse().ToArray(), inward);
                bool needsDrop = kind != CampaignLayout.SecretEntranceKind.SideSlit;
                bool passed = staged && reachedReward && inward.Success &&
                    (!needsDrop || outward.DropObserved && inward.OrdinaryJumpObserved) && life.DeathCount == deaths;
                Debug.Log($"PIRATE_SECRET_CACHE_CASE seed={seed} chapter={chapter} speed={player.MoveSpeed:F2} kind={kind} mirrored={direction < 0} staged={staged} actualDrop={outward.DropObserved} " +
                    $"rewardReached={reachedReward} actualReturn={inward.Success} normalJumpReturn={inward.OrdinaryJumpObserved} coverRevealed={cover.IsDiscovered} " +
                    $"deaths={life.DeathCount - deaths} failure={outward.Error}/{inward.Error} " +
                    "initialFixturePlacement=True teleportBetweenLegs=False launchVelocityAssigned=False progressionGranted=False fullRouteProof=False");
                if (!passed) { result.Error = $"Actual {kind} traversal failed at seed {seed}, mirror {direction}: {outward.Error}/{inward.Error}."; break; }
                result.Cases++; if (outward.DropObserved) result.Drops++;
                result.Returns++; result.Revealed++; if (inward.OrdinaryJumpObserved) result.OrdinaryJumps++;
                foreach (GameObject obj in objects) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); } objects.Clear();
            }
        }
        finally
        {
            foreach (GameObject obj in objects) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            life.SetCheckpoint(checkpoint); if (life.IsRespawning) life.RespawnImmediately();
            abilities.RestoreProgression(upgrades); abilities.ClearAutomationInputOverride(); grapple.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride(); player.ResetMotion(); player.SetControlsEnabled(true);
            player.ConfigureChapterSpeed(savedChapter);
            body.position = savedPosition; player.transform.position = savedPosition; body.linearVelocity = savedVelocity;
            life.SetDeathCount(deaths); life.SetExitProtected(exitProtected); PirateCampaignSession.Deaths = sessionDeaths;
            Physics2D.SyncTransforms();
            if (Mathf.Abs(player.MoveSpeed - savedMoveSpeed) > .001f) result.Error = "Fixture did not restore original chapter speed.";
        }
        Debug.Log("PIRATE_SECRET_CACHE_TRAVERSAL " + result + " realPhysics=True realInputOverrides=True nativeKeyboardProof=False diskAccess=False");
        completed?.Invoke(result);
    }

    private sealed class LegResult
    {
        public bool Success = true, DropObserved, OrdinaryJumpObserved;
        public string Error;
    }

    private static IEnumerator Follow(PlayerMovement player, Rigidbody2D body, Collider2D capsule, Vector2[] path, LegResult result)
    {
        for (int leg = 1; leg < path.Length; leg++)
        {
            Vector2 from = path[leg - 1], target = path[leg];
            if (Mathf.Abs(from.y - target.y) < .05f)
            {
                yield return Walk(player, body, target.x, 360);
                if (!player.IsGrounded || Mathf.Abs(body.position.x - target.x) > .2f || Mathf.Abs(capsule.bounds.min.y - target.y) > .12f)
                { result.Success = false; result.Error = $"walk {leg} target={target} actualFeet={capsule.bounds.min}"; yield break; }
                continue;
            }
            bool rising = target.y > from.y, grounded = player.IsGrounded;
            if (!rising) yield return Walk(player, body, target.x, 150);
            player.SetAutomationInputOverride(rising ? new Vector2(Mathf.Sign(target.x - body.position.x), 0f) : Vector2.down, jumpPressed: true);
            bool airborne = false, landed = false;
            float maxSpeed = 0f;
            for (int frame = 0; frame < 180; frame++)
            {
                yield return null; maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.y);
                result.DropObserved |= player.IsDroppingThrough;
                yield return new WaitForFixedUpdate();
                maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.y);
                result.DropObserved |= player.IsDroppingThrough;
                airborne |= !player.IsGrounded;
                player.SetAutomationInputOverride(new Vector2(Axis(player, body, target.x), 0f));
                if (airborne && player.IsGrounded && Mathf.Abs(capsule.bounds.min.y - target.y) < .12f && Mathf.Abs(body.position.x - target.x) < .25f)
                { landed = true; break; }
            }
            bool normal = grounded && maxSpeed > player.JumpLaunchSpeed - player.GravityStrength * Time.fixedDeltaTime - .15f &&
                maxSpeed <= player.JumpLaunchSpeed + .05f;
            if (rising) result.OrdinaryJumpObserved |= normal;
            if (!landed || rising && !normal)
            { result.Success = false; result.Error = $"vertical {leg} rising={rising} landed={landed} maxVy={maxSpeed:F3} target={target} feet={capsule.bounds.min}"; yield break; }
        }
        player.SetAutomationInputOverride(Vector2.zero);
    }

    private static IEnumerator Walk(PlayerMovement player, Rigidbody2D body, float x, int budget)
    {
        for (int frame = 0; frame < budget; frame++)
        {
            player.SetAutomationInputOverride(new Vector2(Axis(player, body, x), 0f));
            yield return null; yield return new WaitForFixedUpdate();
            if (Mathf.Abs(body.position.x - x) < .09f && Mathf.Abs(body.linearVelocity.x) < .2f) break;
        }
        player.SetAutomationInputOverride(Vector2.zero);
    }

    private static float Axis(PlayerMovement player, Rigidbody2D body, float x)
    {
        float difference = x - body.position.x;
        float stop = body.linearVelocity.x * body.linearVelocity.x / (2f * (player.IsGrounded ? player.Deceleration : player.AirDeceleration));
        return Mathf.Abs(difference) < .06f || difference * body.linearVelocity.x > 0f && stop >= Mathf.Abs(difference) - .04f ? 0f : Mathf.Sign(difference);
    }

    private static void AddSupport(List<GameObject> objects, Rect bounds, bool oneWay, int layer)
    {
        GameObject obj = PirateWorldArt.CreatePlatform("Secret cache fixture support", bounds, 0); objects.Add(obj); obj.layer = layer;
        BoxCollider2D collider = obj.AddComponent<BoxCollider2D>(); collider.size = bounds.size;
        if (!oneWay) return;
        PlatformEffector2D effector = obj.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true; effector.useOneWayGrouping = true; effector.surfaceArc = 160f;
        effector.useSideFriction = false; effector.useSideBounce = false; collider.usedByEffector = true;
    }
}
