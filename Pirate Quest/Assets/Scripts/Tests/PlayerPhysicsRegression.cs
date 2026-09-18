using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class PlayerPhysicsRegression
{
    public sealed class Result
    {
        public bool StaticSolid, StaticTrigger, NegativeControl, NaturalSwing;
        public bool DropInput, DropFalls, DropRestored, ResetRestores, DisableRestores, SolidProtected, DownAloneSafe;
        public int SolidContacts, TriggerContacts, LongestStall;
        public float FurthestX, HighestSwing, Travel;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && StaticSolid && StaticTrigger && NegativeControl &&
            NaturalSwing && DropInput && DropFalls && DropRestored && ResetRestores && DisableRestores &&
            SolidProtected && DownAloneSafe;
        public override string ToString() => $"success={Success}, staticSolid={StaticSolid}/{SolidContacts}, " +
            $"staticTrigger={StaticTrigger}/{TriggerContacts}, negativeControl={NegativeControl}, naturalSwing={NaturalSwing}, " +
            $"drop={DropInput}/{DropFalls}/{DropRestored}, reset={ResetRestores}, disable={DisableRestores}, " +
            $"solidProtected={SolidProtected}, downAlone={DownAloneSafe}, furthestX={FurthestX:F3}, " +
            $"swingHighest={HighestSwing:F3}, travel={Travel:F2}, stall={LongestStall}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        Collider2D capsule = player != null ? player.GetComponent<Collider2D>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        if (body == null || grapple == null || capsule == null || abilities == null)
        {
            result.Error = "Live production player components are required.";
            completed?.Invoke(result);
            yield break;
        }
        Vector2 savedPosition = body.position;
        Vector2 savedVelocity = body.linearVelocity;
        bool savedControls = player.ControlsEnabled;
        bool savedFacingRight = player.IsFacingRight;
        PirateUpgrade[] savedUpgrades = abilities.CaptureProgression();
        Vector2 origin = new Vector2(5000f, 5000f);
        var objects = new List<GameObject>();
        WaitForFixedUpdate step = new WaitForFixedUpdate();
        int groundLayer = 0;
        for (int i = 0; i < 32; i++) if ((player.GroundLayer.value & (1 << i)) != 0) { groundLayer = i; break; }
        PlayerPhysicsContactProbe probe = player.gameObject.AddComponent<PlayerPhysicsContactProbe>();
        DistanceJoint2D joint = player.GetComponent<DistanceJoint2D>();
        bool originalJointCollision = joint.enableCollision;

        BoxCollider2D Box(string name, Vector2 offset, Vector2 size, bool trigger = false)
        {
            var obj = new GameObject("Physics regression " + name);
            objects.Add(obj);
            obj.layer = groundLayer;
            obj.transform.position = origin + offset;
            BoxCollider2D collider = obj.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.isTrigger = trigger;
            return collider;
        }
        void Place(Vector2 offset, Vector2 input, bool hold = false)
        {
            abilities.ResetTransientState();
            player.ResetMotion();
            player.SetControlsEnabled(true);
            player.ClearAutomationInputOverride();
            player.SetAutomationInputOverride(input);
            grapple.ClearAutomationInputOverride();
            grapple.SetAutomationInputOverride(hold);
            body.position = origin + offset;
            player.transform.position = body.position;
            Physics2D.SyncTransforms();
        }
        IEnumerator StandOn(BoxCollider2D platform)
        {
            Place((Vector2)platform.bounds.center - origin + Vector2.up * (platform.bounds.extents.y + capsule.bounds.extents.y + 0.08f), Vector2.zero);
            for (int i = 0; i < 35; i++) yield return step;
        }
        IEnumerator PressDrop()
        {
            player.SetAutomationInputOverride(Vector2.down, jumpPressed: true);
            yield return null;
            yield return step;
        }

        try
        {
            abilities.Apply(PirateUpgrade.Hook1);
            abilities.Apply(PirateUpgrade.Hook2);
            while (PirateHUD.Instance != null && PirateHUD.Instance.IsUpgradeOpen) yield return null;
            BoxCollider2D anchorCollider = Box("anchor", Vector2.up * 4f, Vector2.one * 0.1f, true);
            HookAnchor anchor = anchorCollider.gameObject.AddComponent<HookAnchor>();
            anchor.Configure(true, 0f);
            BoxCollider2D wall = Box("static wall", new Vector2(2f, 0f), new Vector2(0.4f, 8f));
            BoxCollider2D trigger = Box("static trigger", new Vector2(0.9f, 0f), new Vector2(0.3f, 8f), true);
            probe.Solid = wall;
            probe.Trigger = trigger;
            Place(Vector2.zero, Vector2.right, true);
            bool attached = grapple.TryAttachToAnchor(anchor);
            body.linearVelocity = Vector2.right * 10f;
            result.FurthestX = body.position.x - origin.x;
            for (int i = 0; i < 35; i++)
            {
                yield return step;
                result.FurthestX = Mathf.Max(result.FurthestX, body.position.x - origin.x);
            }
            result.SolidContacts = probe.SolidContacts;
            result.TriggerContacts = probe.TriggerContacts;
            result.StaticSolid = attached && originalJointCollision && probe.SolidContacts > 0 &&
                result.FurthestX + capsule.bounds.extents.x <= wall.bounds.min.x - origin.x + 0.12f;
            result.StaticTrigger = probe.TriggerContacts > 0 && trigger.attachedRigidbody == null && wall.attachedRigidbody == null;

            wall.enabled = trigger.enabled = false;
            grapple.Detach();
            yield return step;
            trigger.transform.position = origin;
            trigger.size = new Vector2(3f, 3f);
            Place(Vector2.zero, Vector2.zero, true);
            probe.TriggerContacts = 0;
            joint.enableCollision = false;
            bool negativeAttached = grapple.TryAttachToAnchor(anchor);
            trigger.enabled = true;
            Physics2D.SyncTransforms();
            for (int i = 0; i < 4; i++) yield return step;
            bool suppressed = probe.TriggerContacts == 0;
            int disabledContacts = probe.TriggerContacts;
            trigger.enabled = false;
            grapple.Detach();
            joint.enableCollision = originalJointCollision;
            Place(Vector2.zero, Vector2.zero, true);
            bool restoredAttached = grapple.TryAttachToAnchor(anchor);
            probe.TriggerContacts = 0;
            trigger.enabled = true;
            body.WakeUp();
            Physics2D.SyncTransforms();
            for (int i = 0; i < 4; i++) yield return step;
            result.NegativeControl = negativeAttached && restoredAttached && suppressed && probe.TriggerContacts > 0;
            Debug.Log($"PIRATE_PHYSICS_NEGATIVE_CONTROL attached={negativeAttached}/{restoredAttached} contactsDisabled={disabledContacts} contactsEnabled={probe.TriggerContacts}");
            trigger.enabled = false;

            Place(Vector2.zero, Vector2.left, true);
            attached = grapple.TryAttachToAnchor(anchor);
            Vector2 previous = body.position;
            int stall = 0;
            result.HighestSwing = body.position.y - origin.y;
            for (int i = 0; i < 240; i++)
            {
                yield return step;
                result.Travel += Vector2.Distance(previous, body.position);
                previous = body.position;
                result.HighestSwing = Mathf.Max(result.HighestSwing, body.position.y - origin.y);
                stall = i > 20 && body.linearVelocity.magnitude < 0.15f ? stall + 1 : 0;
                result.LongestStall = Mathf.Max(result.LongestStall, stall);
            }
            result.NaturalSwing = attached && grapple.IsAttached && result.Travel > 8f &&
                result.HighestSwing < 3.98f && result.LongestStall < 30;
            grapple.Detach();
            anchorCollider.gameObject.SetActive(false);

            BoxCollider2D ledge = Box("one way ledge", Vector2.zero, new Vector2(8f, 0.25f));
            PlatformEffector2D effector = ledge.gameObject.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            ledge.usedByEffector = true;
            BoxCollider2D floor = Box("solid floor below", Vector2.down * 4f, new Vector2(12f, 0.5f));
            yield return StandOn(ledge);
            player.SetAutomationInputOverride(Vector2.down);
            for (int i = 0; i < 8; i++) yield return step;
            result.DownAloneSafe = player.IsGrounded && !player.IsDroppingThrough;
            yield return PressDrop();
            result.DropInput = player.IsDroppingThrough && !player.IsGrounded &&
                Physics2D.GetIgnoreCollision(capsule, ledge) && !Physics2D.GetIgnoreCollision(capsule, floor);
            for (int i = 0; i < 50; i++) yield return step;
            result.DropFalls = capsule.bounds.max.y < ledge.bounds.min.y && player.IsGrounded &&
                capsule.bounds.min.y >= floor.bounds.max.y - 0.1f;
            result.DropRestored = !player.IsDroppingThrough && !Physics2D.GetIgnoreCollision(capsule, ledge);

            yield return StandOn(ledge);
            yield return PressDrop();
            bool resetStarted = player.IsDroppingThrough;
            player.ResetMotion();
            result.ResetRestores = resetStarted && !player.IsDroppingThrough && !Physics2D.GetIgnoreCollision(capsule, ledge);
            yield return StandOn(ledge);
            yield return PressDrop();
            bool disableStarted = player.IsDroppingThrough;
            player.enabled = false;
            result.DisableRestores = disableStarted && !player.IsDroppingThrough && !Physics2D.GetIgnoreCollision(capsule, ledge);
            player.enabled = true;

            ledge.usedByEffector = false;
            effector.enabled = false;
            yield return StandOn(ledge);
            yield return PressDrop();
            for (int i = 0; i < 8; i++) yield return step;
            result.SolidProtected = player.IsGrounded && !player.IsDroppingThrough &&
                !Physics2D.GetIgnoreCollision(capsule, ledge) && capsule.bounds.min.y >= ledge.bounds.max.y - 0.1f;
            player.SetAutomationInputOverride(savedFacingRight ? Vector2.right : Vector2.left);
            yield return null;
        }
        finally
        {
            joint.enableCollision = originalJointCollision;
            player.enabled = true;
            player.ResetMotion();
            foreach (GameObject obj in objects) if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
            UnityEngine.Object.Destroy(probe);
            abilities.RestoreProgression(savedUpgrades);
            player.ClearAutomationInputOverride();
            grapple.ClearAutomationInputOverride();
            player.SetControlsEnabled(savedControls);
            body.position = savedPosition;
            player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity;
            Physics2D.SyncTransforms();
        }
        Debug.Log("PIRATE_PLAYER_PHYSICS_REGRESSION " + result + " fixtureRelocation=True fullRouteProof=False");
        completed?.Invoke(result);
    }
}

public sealed class PlayerPhysicsContactProbe : MonoBehaviour
{
    public Collider2D Solid, Trigger;
    public int SolidContacts, TriggerContacts;
    private void OnCollisionEnter2D(Collision2D collision) { if (collision.collider == Solid) SolidContacts++; }
    private void OnCollisionStay2D(Collision2D collision) { if (collision.collider == Solid) SolidContacts++; }
    private void OnTriggerEnter2D(Collider2D other) { if (other == Trigger) TriggerContacts++; }
    private void OnTriggerStay2D(Collider2D other) { if (other == Trigger) TriggerContacts++; }
}
