using System;
using System.Collections;
using UnityEngine;

public static class GrappleRegression
{
    public sealed class Result
    {
        public bool MovesFromRest;
        public bool DoesNotStall;
        public bool NeutralInputPreservesSwing;
        public bool ReleasePreservesVelocity;
        public bool JumpDetaches;
        public bool DashDetaches;
        public bool ResetDetaches;
        public bool WallBlocksAttachment;
        public bool WallBreaksRope;
        public bool OneWayAllowsRope;
        public bool OneWayUndersideNotGrounded;
        public bool LandingHasSupport;
        public float DistanceTravelled;
        public float NeutralHorizontalSpeed;
        public int LongestStationaryFrames;
        public string Error;

        public bool Success => string.IsNullOrEmpty(Error) && MovesFromRest && DoesNotStall &&
                               NeutralInputPreservesSwing && ReleasePreservesVelocity &&
                               JumpDetaches && DashDetaches && ResetDetaches &&
                               WallBlocksAttachment && WallBreaksRope && OneWayAllowsRope &&
                               OneWayUndersideNotGrounded && LandingHasSupport;

        public override string ToString() =>
            $"success={Success}, rest={MovesFromRest}, noStall={DoesNotStall}, " +
            $"neutral={NeutralInputPreservesSwing} ({NeutralHorizontalSpeed:F2}), " +
            $"release={ReleasePreservesVelocity}, jump={JumpDetaches}, dash={DashDetaches}, " +
            $"reset={ResetDetaches}, wallAttach={WallBlocksAttachment}, wallRope={WallBreaksRope}, " +
            $"oneWayRope={OneWayAllowsRope}, oneWay={OneWayUndersideNotGrounded}, landing={LandingHasSupport}, " +
            $"travel={DistanceTravelled:F2}, stationaryFrames={LongestStationaryFrames}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        Result result = new Result();
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        if (player == null || grapple == null || body == null || !body.simulated)
        {
            result.Error = "A live player, Rigidbody2D and PlayerGrapple are required.";
            completed?.Invoke(result);
            yield break;
        }

        Vector2 savedPosition = body.position;
        Vector2 savedVelocity = body.linearVelocity;
        bool savedControls = player.ControlsEnabled;
        Vector2 testOrigin = new Vector2(1000f, 1000f);
        GameObject anchorObject = new GameObject("Grapple regression anchor");
        anchorObject.transform.position = testOrigin + Vector2.up * 4f;
        HookAnchor anchor = anchorObject.AddComponent<HookAnchor>();
        GameObject wall = new GameObject("Grapple regression wall");
        wall.transform.position = testOrigin + Vector2.up * 2f;
        BoxCollider2D wallCollider = wall.AddComponent<BoxCollider2D>();
        wallCollider.size = new Vector2(2f, 0.5f);
        wallCollider.enabled = false;
        GameObject platform = new GameObject("Ground support regression platform");
        platform.transform.position = testOrigin + Vector2.up * 2f;
        for (int layer = 0; layer < 32; layer++)
        {
            if ((player.GroundLayer.value & (1 << layer)) != 0)
            {
                platform.layer = layer;
                break;
            }
        }
        BoxCollider2D platformCollider = platform.AddComponent<BoxCollider2D>();
        platformCollider.size = new Vector2(4f, 0.25f);
        platformCollider.usedByEffector = true;
        PlatformEffector2D effector = platform.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true;
        effector.useOneWayGrouping = true;
        platformCollider.enabled = false;
        WaitForFixedUpdate physicsStep = new WaitForFixedUpdate();

        try
        {
            player.SetControlsEnabled(true);
            ResetPlayer(player, grapple, body, testOrigin, Vector2.left);
            if (!grapple.TryAttachToAnchor(anchor))
            {
                result.Error = "Failed to attach to unobstructed test anchor.";
            }
            else
            {
                Vector2 previousPosition = body.position;
                int stationaryFrames = 0;
                float mostLeft = body.position.x;
                for (int frame = 0; frame < 180; frame++)
                {
                    yield return physicsStep;
                    result.DistanceTravelled += Vector2.Distance(previousPosition, body.position);
                    previousPosition = body.position;
                    mostLeft = Mathf.Min(mostLeft, body.position.x);
                    stationaryFrames = frame > 20 && body.linearVelocity.magnitude < 0.15f
                        ? stationaryFrames + 1
                        : 0;
                    result.LongestStationaryFrames = Mathf.Max(result.LongestStationaryFrames, stationaryFrames);
                }

                result.MovesFromRest = mostLeft < testOrigin.x - 0.75f;
                result.DoesNotStall = grapple.IsAttached && result.LongestStationaryFrames < 20 &&
                                      result.DistanceTravelled > 8f;

                ResetPlayer(player, grapple, body, testOrigin, Vector2.zero);
                grapple.TryAttachToAnchor(anchor);
                body.linearVelocity = new Vector2(6f, 0f);
                for (int frame = 0; frame < 8; frame++)
                {
                    yield return physicsStep;
                }

                result.NeutralHorizontalSpeed = Mathf.Abs(body.linearVelocity.x);
                result.NeutralInputPreservesSwing = grapple.IsAttached && result.NeutralHorizontalSpeed > 3f;
                Vector2 releaseVelocity = body.linearVelocity;
                grapple.Detach();
                result.ReleasePreservesVelocity = !grapple.IsAttached &&
                                                  Vector2.Distance(releaseVelocity, body.linearVelocity) < 0.001f;

                ResetPlayer(player, grapple, body, testOrigin, Vector2.zero);
                grapple.TryAttachToAnchor(anchor);
                player.SetAutomationInputOverride(Vector2.zero, jumpPressed: true);
                yield return null;
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return physicsStep;
                }
                result.JumpDetaches = !grapple.IsAttached && body.linearVelocity.y > player.JumpLaunchSpeed * 0.5f;

                ResetPlayer(player, grapple, body, testOrigin, Vector2.left);
                grapple.TryAttachToAnchor(anchor);
                player.SetAutomationInputOverride(Vector2.left, dashPressed: true);
                yield return null;
                yield return physicsStep;
                result.DashDetaches = !grapple.IsAttached && player.IsDashing && body.linearVelocity.x < -15f;

                ResetPlayer(player, grapple, body, testOrigin, Vector2.zero);
                grapple.TryAttachToAnchor(anchor);
                player.ResetMotion();
                result.ResetDetaches = !grapple.IsAttached && body.linearVelocity.sqrMagnitude < 0.001f &&
                                      body.gravityScale > 0f;

                wallCollider.enabled = true;
                Physics2D.SyncTransforms();
                result.WallBlocksAttachment = !grapple.TryAttachToAnchor(anchor);
                wallCollider.enabled = false;
                Physics2D.SyncTransforms();
                bool attachedBeforeWall = grapple.TryAttachToAnchor(anchor);
                wallCollider.enabled = true;
                Physics2D.SyncTransforms();
                yield return physicsStep;
                result.WallBreaksRope = attachedBeforeWall && !grapple.IsAttached;

                wallCollider.enabled = false;
                platformCollider.enabled = true;
                ResetPlayer(player, grapple, body, testOrigin, Vector2.zero);
                bool attachedThroughPlatform = grapple.TryAttachToAnchor(anchor);
                yield return physicsStep;
                result.OneWayAllowsRope = attachedThroughPlatform && grapple.IsAttached;

                ResetPlayer(player, grapple, body, testOrigin + Vector2.up * 1.5f, Vector2.zero);
                grapple.SetAutomationInputOverride(false);
                body.linearVelocity = Vector2.up * 4f;
                result.OneWayUndersideNotGrounded = true;
                for (int frame = 0; frame < 10; frame++)
                {
                    yield return physicsStep;
                    result.OneWayUndersideNotGrounded &= !player.IsGrounded;
                }

                ResetPlayer(player, grapple, body, testOrigin + Vector2.up * 4.2f, Vector2.zero);
                grapple.SetAutomationInputOverride(false);
                for (int frame = 0; frame < 45; frame++)
                {
                    yield return physicsStep;
                }
                result.LandingHasSupport = player.IsGrounded &&
                                           body.position.y > testOrigin.y + 2f &&
                                           Mathf.Abs(body.linearVelocity.y) < 0.2f;
            }
        }
        finally
        {
            grapple.Detach();
            grapple.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride();
            player.ResetMotion();
            player.SetControlsEnabled(savedControls);
            body.position = savedPosition;
            body.linearVelocity = savedVelocity;
            Physics2D.SyncTransforms();
            UnityEngine.Object.Destroy(anchorObject);
            UnityEngine.Object.Destroy(wall);
            UnityEngine.Object.Destroy(platform);
        }

        completed?.Invoke(result);
    }

    private static void ResetPlayer(PlayerMovement player, PlayerGrapple grapple, Rigidbody2D body,
        Vector2 position, Vector2 input)
    {
        player.ResetMotion();
        body.position = position;
        player.ClearAutomationInputOverride();
        player.SetAutomationInputOverride(input);
        grapple.ClearAutomationInputOverride();
        grapple.SetAutomationInputOverride(true);
        Physics2D.SyncTransforms();
    }
}
