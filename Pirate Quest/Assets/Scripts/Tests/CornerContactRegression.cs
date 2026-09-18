using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class CornerContactRegression
{
    public sealed class Result
    {
        public bool ProductionMaterial, NegativeRight, NegativeLeft, ReleasedRight, ReleasedLeft;
        public bool FloorRight, FloorLeft, TopLanding, SolidWall, MotorBraking;
        public int DefaultRightStall, DefaultLeftStall, ProductionRightStall, ProductionLeftStall;
        public float BrakingDistance;
        public string Error;
        public bool Success => string.IsNullOrEmpty(Error) && ProductionMaterial && NegativeRight && NegativeLeft &&
            ReleasedRight && ReleasedLeft && FloorRight && FloorLeft && TopLanding && SolidWall && MotorBraking;
        public override string ToString() => $"success={Success}, productionMaterial={ProductionMaterial}, " +
            $"negativeRight={NegativeRight}/{DefaultRightStall}, negativeLeft={NegativeLeft}/{DefaultLeftStall}, " +
            $"releasedRight={ReleasedRight}/{ProductionRightStall}, releasedLeft={ReleasedLeft}/{ProductionLeftStall}, " +
            $"floorRight={FloorRight}, floorLeft={FloorLeft}, topLanding={TopLanding}, solidWall={SolidWall}, " +
            $"motorBraking={MotorBraking}/{BrakingDistance:F3}, error={Error}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        Rigidbody2D body = player != null ? player.GetComponent<Rigidbody2D>() : null;
        Collider2D capsule = player != null ? player.GetComponent<Collider2D>() : null;
        PlayerGrapple grapple = player != null ? player.GetComponent<PlayerGrapple>() : null;
        PlayerAbilities abilities = player != null ? player.GetComponent<PlayerAbilities>() : null;
        PlayerLife life = player != null ? player.GetComponent<PlayerLife>() : null;
        if (body == null || capsule == null || !body.simulated || !capsule.enabled || !player.ControlsEnabled ||
            Time.timeScale <= 0f || life != null && life.IsRespawning || grapple != null && grapple.IsAttached)
        {
            result.Error = "An alive, active, unpaused production player without an attached rope is required.";
            completed?.Invoke(result);
            yield break;
        }

        Vector2 savedPosition = body.position;
        Vector2 savedVelocity = body.linearVelocity;
        bool savedControls = player.ControlsEnabled;
        bool savedFacing = player.IsFacingRight;
        PirateUpgrade[] savedProgression = abilities != null ? abilities.CaptureProgression() : null;
        PhysicsMaterial2D productionMaterial = capsule.sharedMaterial;
        var defaultEquivalent = new PhysicsMaterial2D("Corner regression old default friction")
        { friction = .4f, bounciness = 0f };
        result.ProductionMaterial = productionMaterial != null && Mathf.Abs(productionMaterial.friction) < .0001f &&
            Mathf.Abs(productionMaterial.bounciness) < .0001f;
        var objects = new List<GameObject>();
        Vector2 origin = new Vector2(1000f, 1000f);
        var contacts = new ContactPoint2D[24];
        var step = new WaitForFixedUpdate();
        int groundLayer = 0;
        for (int i = 0; i < 32; i++)
            if ((player.GroundLayer.value & (1 << i)) != 0) { groundLayer = i; break; }

        BoxCollider2D Box(string name, Vector2 center, Vector2 size, bool oneWay)
        {
            var item = new GameObject("Corner regression " + name);
            objects.Add(item);
            item.layer = groundLayer;
            item.transform.position = origin + center;
            BoxCollider2D box = item.AddComponent<BoxCollider2D>();
            box.size = size;
            box.sharedMaterial = defaultEquivalent;
            if (oneWay)
            {
                PlatformEffector2D effector = item.AddComponent<PlatformEffector2D>();
                effector.useOneWay = true;
                effector.useOneWayGrouping = true;
                effector.useSideFriction = false;
                effector.useSideBounce = false;
                effector.surfaceArc = 160f;
                box.usedByEffector = true;
            }
            return box;
        }

        void Place(Vector2 offset, Vector2 input)
        {
            abilities?.ResetTransientState();
            abilities?.SetAutomationSlide(false);
            player.ResetMotion();
            player.ClearAutomationInputOverride();
            player.SetAutomationInputOverride(input);
            grapple?.ClearAutomationInputOverride();
            grapple?.SetAutomationInputOverride(false);
            body.position = origin + offset;
            player.transform.position = body.position;
            body.WakeUp();
            Physics2D.SyncTransforms();
        }

        bool HasContact(Collider2D expected, out Vector2 normal, out Vector2 point)
        {
            normal = point = Vector2.zero;
            int count = capsule.GetContacts(contacts);
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = contacts[i];
                if (!contact.enabled || contact.collider != expected && contact.otherCollider != expected) continue;
                normal = contact.normal;
                if (Vector2.Dot(normal, body.worldCenterOfMass - contact.point) < 0f) normal = -normal;
                point = contact.point;
                return true;
            }
            return false;
        }

        try
        {
            BoxCollider2D ledge = Box("recorded one way ledge", new Vector2(0f, -.09f), new Vector2(4.8f, .18f), true);
            BoxCollider2D floor = Box("solid floor", new Vector2(0f, -3.25f), new Vector2(30f, .5f), false);
            BoxCollider2D wall = Box("solid wall", new Vector2(6f, -1f), new Vector2(.4f, 4f), false);
            wall.enabled = false;

            for (int mode = 0; mode < 4; mode++)
            {
                bool oldDefaultMaterial = mode < 2;
                bool rightCorner = mode % 2 == 0;
                float side = rightCorner ? 1f : -1f;
                capsule.sharedMaterial = oldDefaultMaterial ? defaultEquivalent : productionMaterial;
                Place(new Vector2(side * (2.4f + .2132f), .3974f), Vector2.left * side);
                int currentStall = 0;
                int longestStall = 0;
                int observedCornerContacts = 0;
                bool fellClear = false;
                bool reachedFloor = false;
                Vector2 lastNormal = Vector2.zero;
                for (int frame = 0; frame < 100; frame++)
                {
                    player.SetAutomationInputOverride(fellClear ? Vector2.zero : Vector2.left * side);
                    yield return null;
                    yield return step;
                    bool cornerContact = HasContact(ledge, out Vector2 normal, out Vector2 point) &&
                        normal.y > .1f && normal.y < .65f && normal.x * side > .5f &&
                        Mathf.Abs(point.y - ledge.bounds.max.y) < .15f;
                    if (cornerContact) { observedCornerContacts++; lastNormal = normal; }
                    bool stalled = cornerContact && !player.IsGrounded && body.linearVelocity.magnitude < .15f;
                    currentStall = stalled ? currentStall + 1 : 0;
                    longestStall = Mathf.Max(longestStall, currentStall);
                    fellClear |= body.position.y < origin.y - .5f;
                    reachedFloor |= fellClear && player.IsGrounded && HasContact(floor, out Vector2 floorNormal, out _) &&
                        floorNormal.y >= .65f && Mathf.Abs(capsule.bounds.min.y - floor.bounds.max.y) < .08f;
                }
                bool oldPinObserved = observedCornerContacts >= 20 && longestStall >= 20 && !fellClear;
                bool released = fellClear && longestStall < 20;
                if (mode == 0) { result.NegativeRight = oldPinObserved; result.DefaultRightStall = longestStall; }
                if (mode == 1) { result.NegativeLeft = oldPinObserved; result.DefaultLeftStall = longestStall; }
                if (mode == 2) { result.ReleasedRight = released; result.FloorRight = reachedFloor; result.ProductionRightStall = longestStall; }
                if (mode == 3) { result.ReleasedLeft = released; result.FloorLeft = reachedFloor; result.ProductionLeftStall = longestStall; }
                Debug.Log($"PIRATE_CORNER_CONTACT_CASE material={(oldDefaultMaterial ? "negative-default-equivalent-.4" : "production")} " +
                    $"side={(rightCorner ? "right" : "left")} cornerContacts={observedCornerContacts} longestStall={longestStall} " +
                    $"lastNormal={lastNormal:F4} fellClear={fellClear} reachedFloor={reachedFloor} " +
                    $"finalOffset={body.position - origin:F4} velocity={body.linearVelocity:F4} " +
                    "ordinaryHorizontalInput=True jumpInputs=0 collisionsDisabled=0 fixtureRelocation=True fullRouteProof=False");
            }

            capsule.sharedMaterial = productionMaterial;
            Place(new Vector2(0f, .6f), Vector2.zero);
            for (int i = 0; i < 40; i++) { yield return null; yield return step; }
            result.TopLanding = player.IsGrounded && HasContact(ledge, out Vector2 topNormal, out _) &&
                topNormal.y >= .65f && Mathf.Abs(capsule.bounds.min.y - ledge.bounds.max.y) < .08f &&
                !Physics2D.GetIgnoreCollision(capsule, ledge);

            wall.enabled = true;
            Place(new Vector2(4f, -2.45f), Vector2.right);
            bool wallContact = false;
            float maximumRight = capsule.bounds.max.x;
            for (int i = 0; i < 80; i++)
            {
                yield return null;
                yield return step;
                maximumRight = Mathf.Max(maximumRight, capsule.bounds.max.x);
                wallContact |= HasContact(wall, out Vector2 wallNormal, out _) && wallNormal.x < -.65f;
            }
            result.SolidWall = wallContact && body.position.x > origin.x + 5f &&
                maximumRight <= wall.bounds.min.x + .04f && player.IsGrounded &&
                !Physics2D.GetIgnoreCollision(capsule, wall) && !Physics2D.GetIgnoreCollision(capsule, floor);

            Place(new Vector2(-9f, -2.45f), Vector2.zero);
            for (int i = 0; i < 20; i++) { yield return null; yield return step; }
            player.SetAutomationInputOverride(Vector2.right);
            for (int i = 0; i < 20; i++) { yield return null; yield return step; }
            float brakingStart = body.position.x;
            float brakingSpeed = body.linearVelocity.x;
            float maximumStoppingDistance = brakingSpeed * brakingSpeed / (2f * Mathf.Max(1f, player.Deceleration)) +
                player.MoveSpeed * Time.fixedDeltaTime * 2f + .1f;
            player.SetAutomationInputOverride(Vector2.zero);
            for (int i = 0; i < 30; i++) { yield return null; yield return step; }
            result.BrakingDistance = body.position.x - brakingStart;
            result.MotorBraking = brakingSpeed >= player.MoveSpeed * .85f && player.IsGrounded &&
                Mathf.Abs(body.linearVelocity.x) < .05f && result.BrakingDistance > .02f &&
                result.BrakingDistance <= maximumStoppingDistance && !Physics2D.GetIgnoreCollision(capsule, floor);
            player.SetAutomationInputOverride(savedFacing ? Vector2.right : Vector2.left);
            yield return null;
        }
        finally
        {
            capsule.sharedMaterial = productionMaterial;
            player.ResetMotion();
            foreach (GameObject item in objects)
                if (item != null) { item.SetActive(false); UnityEngine.Object.Destroy(item); }
            UnityEngine.Object.Destroy(defaultEquivalent);
            abilities?.RestoreProgression(savedProgression);
            abilities?.ClearAutomationInputOverride();
            player.ClearAutomationInputOverride();
            grapple?.ClearAutomationInputOverride();
            player.SetControlsEnabled(savedControls);
            body.position = savedPosition;
            player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity;
            Physics2D.SyncTransforms();
        }
        Debug.Log("PIRATE_CORNER_CONTACT_REGRESSION " + result + " fixtureRelocation=True fullRouteProof=False");
        completed?.Invoke(result);
    }
}
