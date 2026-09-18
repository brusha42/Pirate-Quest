using System;
using System.Collections;
using UnityEngine;

public static class GroundDashRegression
{
    public sealed class Result
    {
        public bool GroundDash, CooldownBlocks, CooldownRecharges, HeldInputSingleBurst;
        public bool AirDash, AirChargeOnce, GroundEdgeConsumesCharge;
        public bool PauseFreezes, PauseResumes, ResetRestores, DisableRestores;
        public float ObservedGroundSpeed;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && GroundDash && CooldownBlocks && CooldownRecharges &&
            HeldInputSingleBurst && AirDash && AirChargeOnce && GroundEdgeConsumesCharge &&
            PauseFreezes && PauseResumes && ResetRestores && DisableRestores;
        public override string ToString() => $"success={Success}, ground={GroundDash}/{ObservedGroundSpeed:F2}, " +
            $"cooldown={CooldownBlocks}/{CooldownRecharges}, heldOnce={HeldInputSingleBurst}, " +
            $"air={AirDash}/{AirChargeOnce}, groundEdge={GroundEdgeConsumesCharge}, " +
            $"pause={PauseFreezes}/{PauseResumes}, reset={ResetRestores}, disable={DisableRestores}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        Collider2D capsule = player != null ? player.GetComponent<Collider2D>() : null;
        if (body == null || capsule == null || !player.ControlsEnabled || !body.simulated)
        {
            result.Error = "An enabled, simulated production player is required.";
            completed?.Invoke(result);
            yield break;
        }
        Vector2 savedPosition = body.position;
        Vector2 savedVelocity = body.linearVelocity;
        float savedGravity = body.gravityScale;
        float savedScale = Time.timeScale;
        float savedCapture = Time.captureDeltaTime;
        bool savedControls = player.ControlsEnabled;
        bool savedEnabled = player.enabled;
        PlayerGrapple grapple = player.GetComponent<PlayerGrapple>();
        Vector2 origin = new Vector2(6000f, 6000f);
        var floorObject = new GameObject("Ground dash regression solid floor");
        for (int i = 0; i < 32; i++)
            if ((player.GroundLayer.value & (1 << i)) != 0) { floorObject.layer = i; break; }
        floorObject.transform.position = origin - Vector2.up;
        BoxCollider2D floor = floorObject.AddComponent<BoxCollider2D>();
        floor.size = new Vector2(200f, 1f);
        WaitForFixedUpdate step = new WaitForFixedUpdate();
        int burstFrames = Mathf.CeilToInt(player.DashDuration / Time.fixedDeltaTime) + 3;
        int cooldownFrames = Mathf.CeilToInt(player.DashCooldown / Time.fixedDeltaTime) + 3;

        IEnumerator Stand()
        {
            player.ResetMotion();
            player.ClearAutomationInputOverride();
            player.SetAutomationInputOverride(Vector2.zero);
            grapple?.ClearAutomationInputOverride();
            grapple?.SetAutomationInputOverride(false);
            body.position = new Vector2(origin.x, floor.bounds.max.y + capsule.bounds.extents.y + 0.08f);
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 35; i++) yield return step;
        }
        IEnumerator PressDash()
        {
            player.SetAutomationInputOverride(Vector2.right, dashPressed: true);
            yield return null;
            yield return step;
        }
        IEnumerator FinishBurst()
        {
            for (int i = 0; i < burstFrames && player.IsDashing; i++) yield return step;
        }

        try
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = Time.fixedDeltaTime;
            yield return Stand();
            bool groundedBefore = player.IsGrounded;
            yield return PressDash();
            result.ObservedGroundSpeed = body.linearVelocity.x;
            result.GroundDash = groundedBefore && player.IsDashing &&
                result.ObservedGroundSpeed >= player.DashSpeed * 0.9f && body.gravityScale == 0f;
            yield return FinishBurst();
            bool finished = !player.IsDashing && body.gravityScale > 0f;
            yield return PressDash();
            result.CooldownBlocks = finished && !player.IsDashing && body.gravityScale > 0f;
            result.HeldInputSingleBurst = true;
            for (int i = 0; i < cooldownFrames + burstFrames; i++)
            {
                yield return step;
                result.HeldInputSingleBurst &= !player.IsDashing;
            }
            yield return PressDash();
            result.CooldownRecharges = player.IsDashing && player.IsGrounded;
            yield return FinishBurst();

            yield return Stand();
            player.SetAutomationInputOverride(Vector2.zero, jumpPressed: true);
            yield return null;
            for (int i = 0; i < 3; i++) yield return step;
            bool airborne = !player.IsGrounded;
            yield return PressDash();
            result.AirDash = airborne && player.IsDashing && body.gravityScale == 0f;
            yield return FinishBurst();
            for (int i = 0; i < cooldownFrames; i++) yield return step;
            bool stillAirborne = !player.IsGrounded;
            yield return PressDash();
            result.AirChargeOnce = stillAirborne && !player.IsDashing && body.gravityScale > 0f;

            floor.size = new Vector2(3f, 1f);
            yield return Stand();
            yield return PressDash();
            yield return FinishBurst();
            for (int i = 0; i < cooldownFrames; i++) yield return step;
            bool beyondEdge = capsule.bounds.min.x > floor.bounds.max.x && !player.IsGrounded;
            yield return PressDash();
            result.GroundEdgeConsumesCharge = beyondEdge && !player.IsDashing && body.gravityScale > 0f;

            floor.size = new Vector2(200f, 1f);
            yield return Stand();
            yield return PressDash();
            Vector2 pausedPosition = body.position;
            bool activeBeforePause = player.IsDashing;
            Time.timeScale = 0f;
            for (int i = 0; i < 5; i++) yield return null;
            result.PauseFreezes = activeBeforePause && player.IsDashing &&
                Vector2.Distance(pausedPosition, body.position) < 0.002f;
            Time.timeScale = 1f;
            yield return FinishBurst();
            result.PauseResumes = !player.IsDashing && body.gravityScale > 0f;

            yield return Stand();
            yield return PressDash();
            bool activeBeforeReset = player.IsDashing;
            player.ResetMotion();
            result.ResetRestores = activeBeforeReset && !player.IsDashing && body.gravityScale > 0f &&
                body.linearVelocity.sqrMagnitude < 0.001f && !player.IsDroppingThrough;
            yield return Stand();
            yield return PressDash();
            bool activeBeforeDisable = player.IsDashing;
            player.enabled = false;
            result.DisableRestores = activeBeforeDisable && !player.IsDashing && body.gravityScale > 0f;
            player.enabled = true;
            yield return step;
            result.DisableRestores &= !player.IsDashing;
        }
        finally
        {
            Time.timeScale = 1f;
            player.enabled = savedEnabled;
            player.ResetMotion();
            player.ClearAutomationInputOverride();
            grapple?.ClearAutomationInputOverride();
            player.SetControlsEnabled(savedControls);
            floorObject.SetActive(false);
            UnityEngine.Object.Destroy(floorObject);
            body.position = savedPosition;
            player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity;
            body.gravityScale = savedGravity;
            Physics2D.SyncTransforms();
            Time.captureDeltaTime = savedCapture;
            Time.timeScale = savedScale;
        }
        Debug.Log("PIRATE_GROUND_DASH_REGRESSION " + result + " fixtureRelocation=True fullRouteProof=False");
        completed?.Invoke(result);
    }
}
