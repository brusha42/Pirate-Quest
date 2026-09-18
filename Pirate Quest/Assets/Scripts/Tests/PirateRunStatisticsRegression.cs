using System;
using System.IO;
using System.Linq;

public static class PirateRunStatisticsRegression
{
    public sealed class Result
    {
        public int Checks, Failures;
        public string Error;
        public bool Success => Checks > 0 && Failures == 0;
        public override string ToString() => $"success={Success}, checks={Checks}, failures={Failures}, " +
            $"diskWrites=False, runtimeTraversalProof=False, error={Error}";
    }

    private static readonly string[] Signatures = {
        "0123456789ABCDEF", "1111111111111111", "2222222222222222", "3333333333333333"
    };

    public static Result Run()
    {
        var result = new Result();
        void Check(bool condition, string description)
        {
            result.Checks++;
            if (condition) return;
            result.Failures++;
            result.Error = string.IsNullOrEmpty(result.Error) ? description : result.Error + "; " + description;
        }
        void Reject(Action action, string description)
        {
            bool rejected = false;
            try { action(); }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException)
            { rejected = true; }
            Check(rejected, description);
        }

        PirateTreasureLedger treasure = FullTreasure();
        PirateRunStatistics perfect = FullStatistics();
        var best = perfect.Evaluate(treasure, 0, true);
        Check(best.Total == 100000 && best.Treasure == 60000 && best.Speed == 20000 &&
            best.Combat == 15000 && best.Survival == 5000 && best.HasFullHistory,
            "A complete clean run at par did not reach exactly 100000.");
        Check(best.TreasureValue == 400 && best.PossibleTreasureValue == 400 &&
            best.Defeats == 8 && best.PossibleDefeats == 8, "Score breakdown lost source counts.");

        var emptyTreasure = new PirateTreasureLedger();
        var poor = perfect.Evaluate(emptyTreasure, 0, true);
        Check(poor.Total == 40000 && poor.Treasure == 0 && poor.Total < best.Total,
            "Less treasure did not reduce only its score component.");
        PirateRunStatistics slow = FullStatistics();
        slow.Advance(900d);
        var slowScore = slow.Evaluate(treasure, 0, true);
        Check(slowScore.Speed == 10000 && slowScore.Total == 90000 && slowScore.Treasure == best.Treasure,
            "A run twice as long did not halve the speed component.");
        var oneDeath = perfect.Evaluate(treasure, 1, true);
        var manyDeaths = perfect.Evaluate(treasure, 9, true);
        Check(oneDeath.Survival == 2500 && oneDeath.Total < best.Total && manyDeaths.Survival == 500 &&
            manyDeaths.Total < oneDeath.Total, "Deaths did not monotonically reduce survival points.");
        var incomplete = perfect.Evaluate(treasure, 0, false);
        Check(incomplete.Speed == 0 && incomplete.Survival == 0 && incomplete.Total == 75000,
            "An unfinished run earned completion-only speed or survival points.");
        Check(perfect.Evaluate(treasure, int.MaxValue, true).Total <= PirateRunStatistics.MaximumScore,
            "Extreme deaths overflowed the score cap.");

        var fresh = new PirateRunStatistics();
        fresh.Reset(true);
        fresh.RegisterChapter(0, Signatures[0], 100, 2);
        string firstEnemy = PirateRunStatistics.EnemyReceipt(0, Signatures[0], 0, false);
        Check(fresh.RecordDefeat(firstEnemy) && !fresh.RecordDefeat(firstEnemy) && fresh.UniqueDefeats == 1,
            "The same enemy could be credited twice before a reload.");
        var restored = new PirateRunStatistics();
        restored.Restore(Snapshot(fresh));
        Check(!restored.RecordDefeat(firstEnemy) && restored.UniqueDefeats == 1,
            "An enemy respawn or Continue could credit the same defeat twice.");
        Check(restored.RecordDefeat(PirateRunStatistics.EnemyReceipt(0, Signatures[0], 1, true)) &&
            restored.UniqueDefeats == 2, "A distinct plant defeat did not receive its own receipt.");
        Check(!restored.RecordDefeat(PirateRunStatistics.EnemyReceipt(1, Signatures[1], 0, false)) &&
            !restored.RecordDefeat(PirateRunStatistics.EnemyReceipt(0, "FFFFFFFFFFFFFFFF", 0, false)),
            "An unknown chapter or stale layout defeat was accepted.");

        PirateSaveData saved = Snapshot(perfect);
        restored.Restore(saved);
        Check(Same(saved, Snapshot(restored)) &&
            BitConverter.DoubleToInt64Bits(restored.ActiveSeconds) == BitConverter.DoubleToInt64Bits(perfect.ActiveSeconds),
            "Capture/Restore changed a statistic or the exact active-time value.");
        Check(restored.Evaluate(treasure, 0, true).Total == best.Total,
            "Capture/Restore changed the final score.");
        saved.chapterTreasureValues[0] = 0;
        saved.chapterEnemyCounts[0] = 0;
        saved.chapterScoreSignatures[0] = string.Empty;
        saved.defeatedEnemyReceipts[0] = "changed";
        Check(restored.AvailableTreasureValue == 400 && restored.AvailableEnemies == 8 && restored.UniqueDefeats == 8,
            "Restored statistics retained aliases to save arrays.");
        PirateSaveData detached = Snapshot(restored);
        detached.chapterTreasureValues[0] = 0;
        Check(restored.AvailableTreasureValue == 400, "Capture exposed the live inventory array.");

        var legacy = new PirateRunStatistics();
        legacy.Restore(new PirateSaveData());
        Check(!legacy.HasFullHistory && legacy.ActiveSeconds == 0d && legacy.UniqueDefeats == 0 &&
            legacy.AvailableEnemies == 0 && !legacy.AllChaptersKnown, "Legacy save acquired invented run history.");
        for (int chapter = 0; chapter < 4; chapter++) legacy.RegisterChapter(chapter, Signatures[chapter], 100, 2);
        Check(legacy.Evaluate(treasure, 0, true).Speed == 0 && !legacy.Evaluate(treasure, 0, true).HasFullHistory,
            "A legacy save received a speed bonus for unknown past time.");
        restored.RegisterChapter(0, "FFFFFFFFFFFFFFFF", 100, 2);
        Check(!restored.HasFullHistory && restored.UniqueDefeats == 6 &&
            restored.Evaluate(treasure, 0, true).TreasureValue == 300 && restored.Evaluate(treasure, 0, true).Speed == 0,
            "A changed layout retained ranked speed or counted obsolete chapter receipts.");

        var clock = new PirateRunStatistics();
        clock.Reset(true);
        clock.Advance(.125d);
        clock.Advance(.375d);
        Check(clock.ActiveSeconds == .5d, "Active-time accumulation changed an exact binary fraction.");
        foreach (double invalidTime in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Reject(() => clock.Advance(invalidTime), "Invalid elapsed time was accepted: " + invalidTime);
            Check(clock.ActiveSeconds == .5d, "Rejected elapsed time mutated the clock.");
        }
        clock.Advance(PirateRunStatistics.MaximumPlaySeconds * 2d);
        Check(clock.ActiveSeconds == PirateRunStatistics.MaximumPlaySeconds, "The live clock exceeded its seven-day cap.");

        foreach (string key in new[] { null, string.Empty, "k1:4:0123456789ABCDEF:0:0",
            "k1:0:0123456789abcdef:0:0", "k1:0:0123456789ABCDEF:-1:0", "k1:0:0123456789ABCDEF:10000:0",
            "k1:0:0123456789ABCDEF:0:2", firstEnemy + "\n", " " + firstEnemy })
            Check(!PirateRunStatistics.IsValidEnemyReceipt(key) && !perfect.RecordDefeat(key),
                "Malformed enemy receipt was accepted: " + (key ?? "null"));
        Reject(() => PirateRunStatistics.EnemyReceipt(0, Signatures[0], -1, false), "Negative spawn index was accepted.");
        Reject(() => PirateRunStatistics.EnemyReceipt(4, Signatures[0], 0, false), "Invalid receipt chapter was accepted.");
        Reject(() => fresh.RegisterChapter(-1, Signatures[0], 100, 1), "Negative inventory chapter was accepted.");
        Reject(() => fresh.RegisterChapter(4, Signatures[0], 100, 1), "Inventory chapter beyond Crown was accepted.");
        Reject(() => fresh.RegisterChapter(0, null, 100, 1), "Null inventory signature was accepted.");
        Reject(() => fresh.RegisterChapter(0, "0123456789abcdef", 100, 1), "Malformed inventory signature was accepted.");
        Reject(() => fresh.RegisterChapter(0, Signatures[0], -1, 1), "Negative treasure inventory was accepted.");
        Reject(() => fresh.RegisterChapter(0, Signatures[0], 150001, 1), "Oversized treasure inventory was accepted.");
        Reject(() => fresh.RegisterChapter(0, Signatures[0], 100, -1), "Negative enemy inventory was accepted.");
        Reject(() => fresh.RegisterChapter(0, Signatures[0], 100, 257), "Oversized enemy inventory was accepted.");

        PirateSaveData baseline = Snapshot(perfect);
        var atomicTarget = new PirateRunStatistics();
        atomicTarget.Restore(baseline);
        void RejectSave(Action<PirateSaveData> corrupt, string description)
        {
            PirateSaveData bad = Snapshot(perfect);
            corrupt(bad);
            Reject(() => atomicTarget.Restore(bad), description);
            Check(Same(baseline, Snapshot(atomicTarget)), "Rejected Restore mutated live state: " + description);
        }
        RejectSave(data => data.statisticsVersion = 2, "Unknown statistics version was accepted.");
        RejectSave(data => data.statisticsVersion = 0, "Versionless nonempty statistics were accepted.");
        foreach (double invalidTime in new[] { -1d, double.NaN, double.PositiveInfinity,
            double.NegativeInfinity, PirateRunStatistics.MaximumPlaySeconds + 1d })
            RejectSave(data => data.activePlaySeconds = invalidTime, "Invalid saved time was accepted: " + invalidTime);
        RejectSave(data => data.defeatedEnemyReceipts = null, "Null defeat list was accepted.");
        RejectSave(data => data.defeatedEnemyReceipts = new[] { firstEnemy, firstEnemy }, "Duplicate saved defeats were accepted.");
        RejectSave(data => data.defeatedEnemyReceipts = new[] { firstEnemy + "\n" }, "Malformed saved defeat was accepted.");
        RejectSave(data => data.defeatedEnemyReceipts = Enumerable.Range(0, 257)
            .Select(index => PirateRunStatistics.EnemyReceipt(0, Signatures[0], index, false)).ToArray(),
            "More than 256 saved defeats were accepted.");
        RejectSave(data => data.chapterTreasureValues = null, "Null treasure inventory was accepted.");
        RejectSave(data => data.chapterTreasureValues = new int[3], "Wrong treasure inventory length was accepted.");
        RejectSave(data => data.chapterTreasureValues[0] = -1, "Negative saved treasure inventory was accepted.");
        RejectSave(data => data.chapterTreasureValues[0] = 150001, "Oversized saved treasure inventory was accepted.");
        RejectSave(data => data.chapterEnemyCounts = null, "Null enemy inventory was accepted.");
        RejectSave(data => data.chapterEnemyCounts = new int[5], "Wrong enemy inventory length was accepted.");
        RejectSave(data => data.chapterEnemyCounts[0] = -1, "Negative saved enemy inventory was accepted.");
        RejectSave(data => data.chapterEnemyCounts[0] = 257, "Oversized saved enemy inventory was accepted.");
        RejectSave(data => data.chapterEnemyCounts = new[] { 100, 100, 100, 100 }, "Aggregate enemy inventory exceeded 256.");
        RejectSave(data => data.chapterScoreSignatures = null, "Null signature inventory was accepted.");
        RejectSave(data => data.chapterScoreSignatures = new string[3], "Wrong signature inventory length was accepted.");
        RejectSave(data => data.chapterScoreSignatures[0] = null, "Null saved signature was accepted.");
        RejectSave(data => data.chapterScoreSignatures[0] = "0123456789abcdef", "Malformed saved signature was accepted.");
        return result;
    }

    private static PirateRunStatistics FullStatistics()
    {
        var statistics = new PirateRunStatistics();
        statistics.Reset(true);
        for (int chapter = 0; chapter < 4; chapter++)
        {
            statistics.RegisterChapter(chapter, Signatures[chapter], 100, 2);
            for (int index = 0; index < 2; index++)
                statistics.RecordDefeat(PirateRunStatistics.EnemyReceipt(chapter, Signatures[chapter], index, index == 1));
        }
        statistics.Advance(PirateRunStatistics.ParSeconds);
        return statistics;
    }

    private static PirateTreasureLedger FullTreasure()
    {
        var ledger = new PirateTreasureLedger();
        for (int chapter = 0; chapter < 4; chapter++)
        for (int index = 0; index < 10; index++) ledger.Collect($"t1:{chapter}:{Signatures[chapter]}:{index}:0");
        return ledger;
    }

    private static PirateSaveData Snapshot(PirateRunStatistics statistics)
    {
        var data = new PirateSaveData();
        statistics.CaptureInto(data);
        return data;
    }

    private static bool Same(PirateSaveData first, PirateSaveData second) =>
        first.statisticsVersion == second.statisticsVersion && first.statisticsComplete == second.statisticsComplete &&
        BitConverter.DoubleToInt64Bits(first.activePlaySeconds) == BitConverter.DoubleToInt64Bits(second.activePlaySeconds) &&
        first.defeatedEnemyReceipts.SequenceEqual(second.defeatedEnemyReceipts) &&
        first.chapterTreasureValues.SequenceEqual(second.chapterTreasureValues) &&
        first.chapterEnemyCounts.SequenceEqual(second.chapterEnemyCounts) &&
        first.chapterScoreSignatures.SequenceEqual(second.chapterScoreSignatures);
}
