using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public sealed class PirateRunStatistics
{
    public const int FormatVersion = 1;
    public const int MaximumEnemyReceipts = 256;
    public const double MaximumPlaySeconds = 604800d;
    public const double ParSeconds = 900d;
    public const int MaximumScore = 100000;
    private static readonly Regex EnemyKey = new Regex(@"\Ak1:[0-3]:[0-9A-F]{16}:[0-9]{1,4}:[01]\z",
        RegexOptions.CultureInvariant);
    private static readonly Regex SignatureKey = new Regex(@"\A[0-9A-F]{16}\z", RegexOptions.CultureInvariant);
    private readonly HashSet<string> defeats = new HashSet<string>(StringComparer.Ordinal);
    private readonly int[] treasureValues = new int[4];
    private readonly int[] enemyCounts = new int[4];
    private readonly string[] signatures = new string[4];

    public double ActiveSeconds { get; private set; }
    public bool HasFullHistory { get; private set; }
    public int UniqueDefeats => defeats.Count(IsCurrentReceipt);
    public int AvailableEnemies => enemyCounts.Sum();
    public int AvailableTreasureValue => treasureValues.Sum();
    public bool AllChaptersKnown => signatures.All(value => !string.IsNullOrEmpty(value));

    public void Reset(bool fullHistory)
    {
        ActiveSeconds = 0d;
        HasFullHistory = fullHistory;
        defeats.Clear();
        Array.Clear(treasureValues, 0, treasureValues.Length);
        Array.Clear(enemyCounts, 0, enemyCounts.Length);
        Array.Clear(signatures, 0, signatures.Length);
    }

    public void Advance(double seconds)
    {
        if (!Finite(seconds) || seconds < 0d) throw new ArgumentOutOfRangeException(nameof(seconds));
        ActiveSeconds = Math.Min(MaximumPlaySeconds, ActiveSeconds + seconds);
    }

    public void RegisterChapter(int chapter, string signature, int treasureValue, int enemyCount)
    {
        if (chapter < 0 || chapter >= 4 || !ValidSignature(signature) || treasureValue < 0 ||
            treasureValue > 150000 || enemyCount < 0 || enemyCount > MaximumEnemyReceipts ||
            enemyCounts.Sum() - enemyCounts[chapter] + enemyCount > MaximumEnemyReceipts)
            throw new ArgumentException("Invalid chapter score inventory.");
        if (!string.IsNullOrEmpty(signatures[chapter]) && (signatures[chapter] != signature ||
            treasureValues[chapter] != treasureValue || enemyCounts[chapter] != enemyCount))
        {
            HasFullHistory = false;
            defeats.RemoveWhere(key => key[3] - '0' == chapter);
        }
        signatures[chapter] = signature;
        treasureValues[chapter] = treasureValue;
        enemyCounts[chapter] = enemyCount;
    }

    public static string EnemyReceipt(int chapter, string signature, int spawnIndex, bool isPlant)
    {
        string key = $"k1:{chapter}:{signature}:{spawnIndex}:{(isPlant ? 1 : 0)}";
        if (!IsValidEnemyReceipt(key)) throw new ArgumentException("Invalid enemy identity.");
        return key;
    }

    public bool RecordDefeat(string key)
    {
        if (!IsValidEnemyReceipt(key) || !IsCurrentReceipt(key) || defeats.Count >= MaximumEnemyReceipts ||
            defeats.Count(value => value[3] == key[3] && IsCurrentReceipt(value)) >= enemyCounts[key[3] - '0'])
            return false;
        return defeats.Add(key);
    }

    public ScoreBreakdown Evaluate(PirateTreasureLedger treasure, int deaths, bool completed)
    {
        int collectedValue = treasure.Capture().Where(IsCurrentReceipt).Sum(PirateTreasureLedger.ValueOf);
        int loot = Scale(60000, collectedValue, AvailableTreasureValue);
        int hunt = Scale(15000, UniqueDefeats, AvailableEnemies);
        bool ranked = HasFullHistory && AllChaptersKnown;
        int speed = completed && ranked ? (int)Math.Round(20000d * Math.Min(1d, ParSeconds / Math.Max(1d, ActiveSeconds))) : 0;
        int survival = completed ? (int)Math.Round(5000d / (1d + Math.Max(0, deaths))) : 0;
        return new ScoreBreakdown(loot, speed, hunt, survival, ranked, collectedValue, AvailableTreasureValue,
            UniqueDefeats, AvailableEnemies);
    }

    public void CaptureInto(PirateSaveData data)
    {
        data.statisticsVersion = FormatVersion;
        data.activePlaySeconds = ActiveSeconds;
        data.statisticsComplete = HasFullHistory;
        data.defeatedEnemyReceipts = defeats.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        data.chapterTreasureValues = (int[])treasureValues.Clone();
        data.chapterEnemyCounts = (int[])enemyCounts.Clone();
        data.chapterScoreSignatures = signatures.Select(value => value ?? string.Empty).ToArray();
    }

    public void Restore(PirateSaveData data)
    {
        Validate(data);
        Reset(data.statisticsVersion == FormatVersion && data.statisticsComplete);
        if (data.statisticsVersion == 0) return;
        ActiveSeconds = data.activePlaySeconds;
        Array.Copy(data.chapterTreasureValues, treasureValues, 4);
        Array.Copy(data.chapterEnemyCounts, enemyCounts, 4);
        Array.Copy(data.chapterScoreSignatures, signatures, 4);
        foreach (string key in data.defeatedEnemyReceipts) defeats.Add(key);
    }

    public static void Validate(PirateSaveData data)
    {
        if (data.statisticsVersion == 0)
        {
            if (data.activePlaySeconds != 0d || data.statisticsComplete ||
                (data.defeatedEnemyReceipts != null && data.defeatedEnemyReceipts.Length != 0) ||
                (data.chapterTreasureValues != null && data.chapterTreasureValues.Length != 0) ||
                (data.chapterEnemyCounts != null && data.chapterEnemyCounts.Length != 0) ||
                (data.chapterScoreSignatures != null && data.chapterScoreSignatures.Length != 0))
                throw new System.IO.InvalidDataException("Run statistics have no supported format version.");
            return;
        }
        if (data.statisticsVersion != FormatVersion || !Finite(data.activePlaySeconds) ||
            data.activePlaySeconds < 0d || data.activePlaySeconds > MaximumPlaySeconds ||
            data.defeatedEnemyReceipts == null || data.defeatedEnemyReceipts.Length > MaximumEnemyReceipts ||
            data.defeatedEnemyReceipts.Any(value => !IsValidEnemyReceipt(value)) ||
            data.defeatedEnemyReceipts.Distinct(StringComparer.Ordinal).Count() != data.defeatedEnemyReceipts.Length ||
            data.chapterTreasureValues == null || data.chapterTreasureValues.Length != 4 ||
            data.chapterTreasureValues.Any(value => value < 0 || value > 150000) ||
            data.chapterEnemyCounts == null || data.chapterEnemyCounts.Length != 4 ||
            data.chapterEnemyCounts.Any(value => value < 0 || value > MaximumEnemyReceipts) ||
            data.chapterEnemyCounts.Sum() > MaximumEnemyReceipts ||
            data.chapterScoreSignatures == null || data.chapterScoreSignatures.Length != 4 ||
            data.chapterScoreSignatures.Any(value => value != string.Empty && !ValidSignature(value)))
            throw new System.IO.InvalidDataException("Invalid saved run statistics.");
        for (int chapter = 0; chapter < 4; chapter++)
        {
            string signature = data.chapterScoreSignatures[chapter];
            string[] chapterDefeats = data.defeatedEnemyReceipts.Where(key => key[3] - '0' == chapter).ToArray();
            if ((signature == string.Empty && (data.chapterTreasureValues[chapter] != 0 ||
                 data.chapterEnemyCounts[chapter] != 0 || chapterDefeats.Length != 0)) ||
                chapterDefeats.Length > data.chapterEnemyCounts[chapter] ||
                chapterDefeats.Any(key => string.CompareOrdinal(key, 5, signature, 0, 16) != 0))
                throw new System.IO.InvalidDataException("Run statistics do not match the chapter inventory.");
        }
    }

    public static bool IsValidEnemyReceipt(string key) => key != null && key.Length <= 40 && EnemyKey.IsMatch(key);
    private static bool ValidSignature(string value) => value != null && SignatureKey.IsMatch(value);
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private bool IsCurrentReceipt(string key)
    {
        if (key == null || key.Length < 22 || key[3] < '0' || key[3] > '3') return false;
        return !string.IsNullOrEmpty(signatures[key[3] - '0']) &&
            string.CompareOrdinal(key, 5, signatures[key[3] - '0'], 0, 16) == 0;
    }
    private static int Scale(int maximum, int amount, int available) => available <= 0 ? 0 :
        (int)Math.Round(maximum * Math.Min(1d, Math.Max(0d, amount / (double)available)));

    public readonly struct ScoreBreakdown
    {
        public readonly int Treasure, Speed, Combat, Survival;
        public readonly int TreasureValue, PossibleTreasureValue, Defeats, PossibleDefeats;
        public readonly bool HasFullHistory;
        public int Total => Math.Min(MaximumScore, Treasure + Speed + Combat + Survival);
        public ScoreBreakdown(int treasure, int speed, int combat, int survival, bool complete,
            int value, int possibleValue, int defeats, int possibleDefeats)
        {
            Treasure = treasure; Speed = speed; Combat = combat; Survival = survival;
            HasFullHistory = complete; TreasureValue = value; PossibleTreasureValue = possibleValue;
            Defeats = defeats; PossibleDefeats = possibleDefeats;
        }
    }
}
