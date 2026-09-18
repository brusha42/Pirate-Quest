using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

public sealed class PlayerContactDiagnostics : MonoBehaviour
{
    private static PlayerContactDiagnostics recorder;
    private readonly Queue<string> recentFrames = new Queue<string>();
    private PlayerMovement observed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        if (recorder != null || Array.IndexOf(Environment.GetCommandLineArgs(), "-pirateQuestRoutePlaytest") < 0) return;
        GameObject item = new GameObject("Read-only player contact diagnostic");
        DontDestroyOnLoad(item);
        recorder = item.AddComponent<PlayerContactDiagnostics>();
    }

    private void LateUpdate()
    {
        if (observed == null)
        {
            observed = FindFirstObjectByType<PlayerMovement>();
            recentFrames.Clear();
        }
        if (observed == null) return;
        Rigidbody2D body = observed.GetComponent<Rigidbody2D>();
        Collider2D capsule = observed.GetComponent<Collider2D>();
        recentFrames.Enqueue($"frame={Time.frameCount} pos={body.position:F3} feet={capsule.bounds.min.y:F3} " +
            $"velocity={body.linearVelocity:F3} grounded={observed.IsGrounded} axis={observed.ReadMovementInput().x:F0} " +
            $"rootScaleX={observed.transform.localScale.x:F1} gravity={body.gravityScale:F2}");
        while (recentFrames.Count > 90) recentFrames.Dequeue();
    }

    public static string Describe(PlayerMovement player)
    {
        if (player == null) return "PIRATE_PLAYER_CONTACTS no player";
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        Collider2D capsule = player.GetComponent<Collider2D>();
        var report = new StringBuilder("PIRATE_PLAYER_CONTACTS ");
        report.AppendLine($"frame={Time.frameCount} position={body.position:F4} velocity={body.linearVelocity:F4} " +
            $"gravity={body.gravityScale:F4} damping={body.linearDamping:F4} mass={body.mass:F3} " +
            $"rootScale={player.transform.localScale:F3} bounds={capsule.bounds} facingRight={player.IsFacingRight} " +
            $"grounded={player.IsGrounded} controls={player.ControlsEnabled} dash={player.IsDashing}");
        foreach (string name in new[] { "jumpIsActive", "coyoteTimer", "jumpBufferTimer", "dashTimer", "extraJumpsRemaining" })
        {
            FieldInfo field = typeof(PlayerMovement).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            report.Append($"{name}={field?.GetValue(player)} ");
        }
        report.AppendLine();
        var contacts = new ContactPoint2D[32];
        int count = capsule.GetContacts(contacts);
        report.AppendLine($"contactCount={count}");
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contacts[i];
            report.AppendLine($"contact={contact.collider?.name}/{contact.otherCollider?.name} enabled={contact.enabled} " +
                $"normal={contact.normal:F4} point={contact.point:F4} separation={contact.separation:F5} " +
                $"normalImpulse={contact.normalImpulse:F5} tangentImpulse={contact.tangentImpulse:F5}");
        }
        foreach (Collider2D other in Physics2D.OverlapBoxAll(capsule.bounds.center,
                     (Vector2)capsule.bounds.size + Vector2.one * .1f, 0f))
        {
            if (other == capsule) continue;
            report.AppendLine($"nearCollider={other.name} type={other.GetType().Name} trigger={other.isTrigger} " +
                $"bounds={other.bounds} usedByEffector={other.usedByEffector} " +
                $"pad={other.GetComponent<CampaignJumpPad>() != null} hazard={other.GetComponent<InstantKillHazard>() != null}");
        }
        if (recorder != null && recorder.observed == player)
        {
            report.AppendLine("PIRATE_PLAYER_RECENT_FRAMES oldest first:");
            foreach (string frame in recorder.recentFrames) report.AppendLine(frame);
        }
        return report.ToString();
    }
}
