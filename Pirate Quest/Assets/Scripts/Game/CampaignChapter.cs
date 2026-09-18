using System.Collections.Generic;
using UnityEngine;

public sealed class CampaignChapter : MonoBehaviour
{
    [Range(0, 3)] public int chapterIndex;
    public static readonly string[] SceneNames = { "Dock", "Arsenal", "OvergrownGarden", "Crown" };
    public static readonly string[] Titles = { "I. The Docks", "II. Powder Arsenal", "III. Hanging Gardens", "IV. Tower Crown" };
    public static readonly string[] Objectives = {
        "Reach the arsenal. Cut ropes with LMB.",
        "Dodge the cannons. Find new gear.",
        "Climb the gardens. Master your spring leg and saber.",
        "The black tide is rising. Scout with your parrot. Reach the king."
    };
}

public static class PirateCampaignSession
{
    public static int Seed { get; private set; } = 20260918;
    public static int Chapter { get; private set; }
    public static bool Active { get; private set; }
    public static int Deaths { get; set; }
    public static bool IntroSeen { get; set; }
    public static PirateTreasureLedger TreasureLedger { get; } = new PirateTreasureLedger();
    public static PirateRunStatistics Statistics { get; } = new PirateRunStatistics();
    public static PirateSaveData PendingRestore { get; private set; }
    private static readonly HashSet<PirateUpgrade> earned = new HashSet<PirateUpgrade>();
    public static IEnumerable<PirateUpgrade> Earned => earned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDomain() { Active = false; Chapter = 0; earned.Clear(); Seed = 20260918; Deaths = 0; PendingRestore = null; IntroSeen = false; TreasureLedger.Clear(); Statistics.Reset(false); }

    public static void Begin(int seed)
    {
        Active = true; Seed = seed; Chapter = 0; earned.Clear();
        Deaths = 0; PendingRestore = null;
        IntroSeen = false; TreasureLedger.Clear();
        Statistics.Reset(true);
        earned.Add(PirateUpgrade.Hook1);
        earned.Add(PirateUpgrade.Saber1);
    }

    public static void Remember(PirateUpgrade upgrade) => earned.Add(upgrade);
    public static void Load(PirateSaveData data)
    {
        PirateSaveStore.Validate(data);
        Active = true; Seed = data.seed; Chapter = data.chapter; Deaths = data.deaths;
        IntroSeen = data.introSeen; TreasureLedger.Restore(data.treasureReceipts);
        Statistics.Restore(data);
        earned.Clear();
        foreach (int upgrade in data.upgrades) earned.Add((PirateUpgrade)upgrade);
        PendingRestore = data;
    }
    public static PirateSaveData ConsumeRestore()
    {
        PirateSaveData data = PendingRestore; PendingRestore = null; return data;
    }
    public static void Advance(int chapter) => Chapter = Mathf.Clamp(chapter, 0, 3);
    public static void Restore(PlayerAbilities abilities)
    {
        abilities.ResetProgression();
        foreach (PirateUpgrade upgrade in earned) abilities.Apply(upgrade, false);
    }
}
