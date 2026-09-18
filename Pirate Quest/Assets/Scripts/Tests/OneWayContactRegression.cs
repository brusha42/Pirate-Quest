using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class OneWayContactRegression
{
    public sealed class Result
    {
        public int LowArcConstantStall, LowArcTurningStall, SideEntryConstantStall, SideEntryTurningStall;
        public bool Completed;
        public override string ToString() => $"completed={Completed}, lowArcConstant={LowArcConstantStall}, " +
            $"lowArcTurning={LowArcTurningStall}, sideConstant={SideEntryConstantStall}, sideTurning={SideEntryTurningStall}";
    }

    public static IEnumerator Run(PlayerMovement player, Action<Result> completed)
    {
        var result = new Result();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        PlayerAbilities abilities = player.GetComponent<PlayerAbilities>();
        Vector2 savedPosition = body.position;
        Vector2 savedVelocity = body.linearVelocity;
        bool savedControls = player.ControlsEnabled;
        PirateUpgrade[] savedUpgrades = abilities != null ? abilities.CaptureProgression() : null;
        float savedScale = Time.timeScale;
        float savedCapture = Time.captureDeltaTime;
        int layer = 0;
        for (int i = 0; i < 32; i++) if ((player.GroundLayer.value & (1 << i)) != 0) { layer = i; break; }
        var fixtures = new List<GameObject>();
        Vector2 origin = new Vector2(3000f, 3000f);
        ContactPoint2D[] contacts = new ContactPoint2D[32];
        WaitForFixedUpdate step = new WaitForFixedUpdate();

        BoxCollider2D Platform(string name, Vector2 center, Vector2 size, bool oneWay)
        {
            GameObject item = new GameObject("Contact probe " + name);
            fixtures.Add(item);
            item.layer = layer;
            item.transform.position = center;
            BoxCollider2D box = item.AddComponent<BoxCollider2D>();
            box.size = size;
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

        void Place(Vector2 position)
        {
            abilities?.ResetTransientState();
            player.ResetMotion();
            player.SetControlsEnabled(true);
            player.ClearAutomationInputOverride();
            player.SetAutomationInputOverride(Vector2.zero);
            player.GetComponent<PlayerGrapple>()?.SetAutomationInputOverride(false);
            body.position = position;
            player.transform.position = position;
            Physics2D.SyncTransforms();
        }

        void Trace(string scenario, int frame, BoxCollider2D slab)
        {
            int count = capsule.GetContacts(contacts);
            var text = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                ContactPoint2D contact = contacts[i];
                string other = contact.collider != null ? contact.collider.name : "null";
                string second = contact.otherCollider != null ? contact.otherCollider.name : "null";
                text.Append($"[{other}/{second} enabled={contact.enabled} normal={contact.normal:F3} " +
                    $"point={contact.point:F3} separation={contact.separation:F4} normalImpulse={contact.normalImpulse:F3}]");
            }
            Debug.Log($"PIRATE_ONEWAY_CONTACT scenario={scenario} frame={frame} position={body.position:F3} " +
                $"feet={capsule.bounds.min.y:F3} head={capsule.bounds.max.y:F3} velocity={body.linearVelocity:F3} " +
                $"scaleX={player.transform.localScale.x:F1} grounded={player.IsGrounded} slab={slab.bounds} contacts={text}");
        }

        try
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = Time.fixedDeltaTime;
            abilities?.ResetProgression();
            BoxCollider2D floor = Platform("floor", origin - Vector2.up, new Vector2(30f, 1f), false);
            BoxCollider2D lowArcSlab = Platform("low arc slab", origin + Vector2.up * 1.26f, new Vector2(8f, 0.18f), true);
            BoxCollider2D leftLedge = Platform("left approach ledge", origin + new Vector2(-3f, -0.5f), new Vector2(4f, 1f), false);
            BoxCollider2D upperSlab = Platform("side entry slab", origin + new Vector2(0.3f, 0.41f), new Vector2(3.4f, 0.18f), true);
            leftLedge.enabled = upperSlab.enabled = false;
            for (int mode = 0; mode < 4; mode++)
            {
                bool sideEntry = mode >= 2;
                bool turning = mode % 2 == 1;
                string scenario = (sideEntry ? "side" : "lowArc") + (turning ? "Turning" : "Constant");
                lowArcSlab.enabled = !sideEntry;
                leftLedge.enabled = upperSlab.enabled = sideEntry;
                Place(sideEntry ? origin + new Vector2(-2.6f, 0.52f) : origin);
                for (int frame = 0; frame < 12; frame++) yield return step;
                if (!sideEntry)
                {
                    player.SetAutomationInputOverride(Vector2.zero, jumpPressed: true);
                    yield return null;
                    yield return step;
                    yield return step;
                    player.SetAutomationInputOverride(Vector2.zero, jumpReleased: true);
                    yield return null;
                }
                int longestStall = 0;
                int currentStall = 0;
                bool enteredSlab = false;
                BoxCollider2D slab = sideEntry ? upperSlab : lowArcSlab;
                for (int frame = 0; frame < 100; frame++)
                {
                    enteredSlab |= body.position.x >= origin.x - 0.25f;
                    float axis = sideEntry && !enteredSlab && frame < 35 ? 1f :
                        turning && frame < 72 ? (frame % 2 == 0 ? -1f : 1f) : 0f;
                    player.SetAutomationInputOverride(new Vector2(axis, 0f));
                    yield return null;
                    yield return step;
                    bool intersectsSlab = capsule.bounds.max.y > slab.bounds.min.y && capsule.bounds.min.y < slab.bounds.max.y - 0.15f &&
                        capsule.bounds.max.x > slab.bounds.min.x && capsule.bounds.min.x < slab.bounds.max.x;
                    bool stalled = intersectsSlab && Mathf.Abs(body.linearVelocity.y) < 0.5f && !player.IsGrounded;
                    currentStall = stalled ? currentStall + 1 : 0;
                    longestStall = Mathf.Max(longestStall, currentStall);
                    if (frame % 10 == 0 || currentStall == 5 || currentStall == 15) Trace(scenario, frame, slab);
                }
                if (mode == 0) result.LowArcConstantStall = longestStall;
                if (mode == 1) result.LowArcTurningStall = longestStall;
                if (mode == 2) result.SideEntryConstantStall = longestStall;
                if (mode == 3) result.SideEntryTurningStall = longestStall;
            }
            result.Completed = true;
        }
        finally
        {
            foreach (GameObject item in fixtures) { item.SetActive(false); UnityEngine.Object.Destroy(item); }
            abilities?.RestoreProgression(savedUpgrades);
            player.ClearAutomationInputOverride();
            player.GetComponent<PlayerGrapple>()?.ClearAutomationInputOverride();
            player.ResetMotion();
            player.SetControlsEnabled(savedControls);
            body.position = savedPosition;
            player.transform.position = savedPosition;
            body.linearVelocity = savedVelocity;
            Physics2D.SyncTransforms();
            Time.timeScale = savedScale;
            Time.captureDeltaTime = savedCapture;
        }
        Debug.Log("PIRATE_ONEWAY_CONTACT_RESULT " + result);
        completed?.Invoke(result);
    }
}

public class OneWayContactProbeRunner : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pirateQuestContactTest") < 0) return;
        new GameObject("One way contact diagnostic runner").AddComponent<OneWayContactProbeRunner>();
    }
    private IEnumerator Start()
    {
        Application.runInBackground = true;
        for (int frame = 0; frame < 4; frame++) yield return null;
        OneWayContactRegression.Result result = null;
        yield return OneWayContactRegression.Run(FindFirstObjectByType<PlayerMovement>(), value => result = value);
        Application.Quit(result != null && result.Completed ? 0 : 9);
    }
}
