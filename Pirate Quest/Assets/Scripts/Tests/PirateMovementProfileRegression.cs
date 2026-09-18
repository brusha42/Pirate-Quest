using System;
using UnityEngine;

public static class PirateMovementProfileRegression
{
    public sealed class Result
    {
        public int Checks, Failures;
        public string Error;
        public bool Success => Checks > 0 && Failures == 0;
        public override string ToString() => $"success={Success}, checks={Checks}, failures={Failures}, " +
            $"physicsSteps=0, traversalProof=False, error={Error}";
    }

    public static Result Run(PlayerMovement livePlayer, int liveChapter)
    {
        var result = new Result();
        void Check(bool condition, string description)
        {
            result.Checks++;
            if (condition) return;
            result.Failures++;
            result.Error = string.IsNullOrEmpty(result.Error) ? description : result.Error + "; " + description;
        }

        float[] expected = { 6f, 6.6f, 7.3f, 8f };
        for (int chapter = 0; chapter < expected.Length; chapter++)
        {
            Check(PirateMovementProfile.ForChapter(chapter) == expected[chapter], "Wrong chapter speed: " + chapter);
            if (chapter > 0)
                Check(PirateMovementProfile.ForChapter(chapter) > PirateMovementProfile.ForChapter(chapter - 1),
                    "Chapter speed did not increase: " + chapter);
        }
        Check(PirateMovementProfile.ForChapter(-1) == 6f && PirateMovementProfile.ForChapter(int.MinValue) == 6f,
            "Negative chapters did not clamp to the first profile.");
        Check(PirateMovementProfile.ForChapter(4) == 8f && PirateMovementProfile.ForChapter(int.MaxValue) == 8f,
            "Out-of-range chapters did not clamp to the final profile.");
        Check(livePlayer != null && livePlayer.MoveSpeed == PirateMovementProfile.ForChapter(liveChapter),
            "Live chapter was generated with the wrong movement profile.");

        GameObject fixtureObject = null;
        try
        {
            fixtureObject = new GameObject("Inactive chapter-speed contract fixture");
            fixtureObject.SetActive(false);
            PlayerMovement fixture = fixtureObject.AddComponent<PlayerMovement>();
            JsonUtility.FromJsonOverwrite("{\"moveSpeed\":5.25}", fixture);
            Check(fixture.MoveSpeed == 5.25f, "A standalone serialized speed was overwritten before chapter configuration.");
            float jump = fixture.JumpLaunchSpeed, gravity = fixture.GravityStrength;
            float dash = fixture.DashSpeed, duration = fixture.DashDuration, cooldown = fixture.DashCooldown;
            float acceleration = fixture.GroundAcceleration, deceleration = fixture.Deceleration;
            float airAcceleration = fixture.AirAcceleration, airDeceleration = fixture.AirDeceleration;
            foreach (int chapter in new[] { 0, 1, 2, 3, 3, 0, int.MinValue, int.MaxValue })
            {
                fixture.ConfigureChapterSpeed(chapter);
                Check(fixture.MoveSpeed == PirateMovementProfile.ForChapter(chapter),
                    "Controller did not deterministically apply/reapply chapter " + chapter);
                Check(fixture.JumpLaunchSpeed == jump && fixture.GravityStrength == gravity &&
                    fixture.DashSpeed == dash && fixture.DashDuration == duration && fixture.DashCooldown == cooldown &&
                    fixture.GroundAcceleration == acceleration && fixture.Deceleration == deceleration &&
                    fixture.AirAcceleration == airAcceleration && fixture.AirDeceleration == airDeceleration,
                    "Chapter speed configuration changed a jump, dash or acceleration contract.");
            }
            Check(!fixtureObject.activeInHierarchy, "The profile fixture unexpectedly ran gameplay callbacks.");
        }
        catch (Exception exception)
        {
            Check(false, exception.GetType().Name + ": " + exception.Message);
        }
        finally
        {
            if (fixtureObject != null) UnityEngine.Object.Destroy(fixtureObject);
        }
        return result;
    }
}
