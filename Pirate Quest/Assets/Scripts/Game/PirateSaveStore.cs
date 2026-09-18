using System;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class PirateSaveData
{
    public int version = 1;
    public int seed;
    public int chapter;
    public int[] upgrades = Array.Empty<int>();
    public bool hasCheckpoint;
    public float checkpointX;
    public float checkpointY;
    public int deaths;
    public string layoutSignature;
    public string savedAtUtc;
    public bool completed;
    public string[] treasureReceipts = Array.Empty<string>();
    public bool introSeen;
    public int statisticsVersion;
    public double activePlaySeconds;
    public bool statisticsComplete;
    public string[] defeatedEnemyReceipts = Array.Empty<string>();
    public int[] chapterTreasureValues = Array.Empty<int>();
    public int[] chapterEnemyCounts = Array.Empty<int>();
    public string[] chapterScoreSignatures = Array.Empty<string>();
}

public static class PirateSaveStore
{
    public const int Version = 1;
    private static string isolatedTestDirectory;
    private static string RootDirectory
    {
        get
        {
            if (PirateSaveRestartProbe.IsRequested && isolatedTestDirectory == null)
                throw new InvalidOperationException("Restart probe cannot access any save before its sandbox is configured.");
            return isolatedTestDirectory ?? Application.persistentDataPath;
        }
    }
    public static string SavePath => Path.Combine(RootDirectory,"campaign-v1.json");
    public static string LastError { get; private set; }

    public static string BeginMenuTestSandbox()
    {
        if (!Environment.GetCommandLineArgs().Contains("-pirateMenuTest"))
            throw new InvalidOperationException("Save sandbox is only available to the explicit menu test process.");
        if (PirateSaveRestartProbe.IsRequested)
        {
            isolatedTestDirectory = PirateSaveRestartProbe.PrepareSandbox();
            return isolatedTestDirectory;
        }
        isolatedTestDirectory = Path.Combine(Application.temporaryCachePath,"PirateQuest-menu-regression-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedTestDirectory);
        return isolatedTestDirectory;
    }

    public static bool TryLoad(out PirateSaveData data)
    {
        LastError = null;
        if (Read(SavePath,out data)) return true;
        if (Read(SavePath+".bak",out data))
        {
            LastError = null;
            Debug.LogWarning("PIRATE_SAVE_RECOVERED: primary slot unavailable; loaded atomic backup.");
            return true;
        }
        return false;
    }

    private static bool Read(string path,out PirateSaveData data)
    {
        data = null;
        if (!File.Exists(path)) return false;
        try
        {
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Save file is too large.");
            PirateSaveData candidate = JsonUtility.FromJson<PirateSaveData>(File.ReadAllText(path));
            Validate(candidate);
            data = candidate;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                          exception is ArgumentException || exception is InvalidDataException)
        {
            LastError = exception.Message;
            Debug.LogWarning("PIRATE_SAVE_READ_FAILED: "+Path.GetFileName(path)+" — "+exception.Message);
            return false;
        }
    }

    public static bool Write(PirateSaveData data)
    {
        try
        {
            Validate(data);
            data.savedAtUtc = DateTime.UtcNow.ToString("O");
            Directory.CreateDirectory(RootDirectory);
            string temporary = SavePath+".tmp";
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(data,true));
            if (bytes.Length > 65536) throw new InvalidDataException("Save file is too large.");
            using (var stream = new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
            {
                stream.Write(bytes,0,bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(SavePath)) File.Replace(temporary,SavePath,SavePath+".bak",true);
            else File.Move(temporary,SavePath);
            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                          exception is ArgumentException || exception is InvalidDataException)
        {
            LastError = exception.Message;
            Debug.LogError("PIRATE_SAVE_WRITE_FAILED: previous slot retained. "+exception.Message);
            return false;
        }
    }

    public static bool ArchiveBeforeNewRun()
    {
        try
        {
            string validSource = Read(SavePath,out _) ? SavePath :
                Read(SavePath+".bak",out _) ? SavePath+".bak" : null;
            if (validSource != null)
                File.Copy(validSource,Path.Combine(RootDirectory,"previous-expedition.json"),true);
            else
            {
                string archiveId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N");
                if (File.Exists(SavePath))
                    File.Copy(SavePath,Path.Combine(RootDirectory,"invalid-expedition-"+archiveId+"-primary.json"),false);
                if (File.Exists(SavePath+".bak"))
                    File.Copy(SavePath+".bak",Path.Combine(RootDirectory,"invalid-expedition-"+archiveId+"-backup.json"),false);
            }
            LastError = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            LastError = exception.Message;
            Debug.LogError("PIRATE_SAVE_ARCHIVE_FAILED: "+exception.Message);
            return false;
        }
    }

    public static void Validate(PirateSaveData data)
    {
        if (data == null || data.version != Version || data.chapter < 0 || data.chapter > 3 || data.deaths < 0 ||
            data.deaths > 10000000 || data.upgrades == null || data.upgrades.Length > 7 ||
            data.upgrades.Any(value => !Enum.IsDefined(typeof(PirateUpgrade),value)))
            throw new InvalidDataException("Unsupported or corrupt campaign save.");
        if (float.IsNaN(data.checkpointX) || float.IsInfinity(data.checkpointX) ||
            float.IsNaN(data.checkpointY) || float.IsInfinity(data.checkpointY))
            throw new InvalidDataException("Invalid saved checkpoint coordinates.");
        if (data.treasureReceipts != null && (data.treasureReceipts.Length > PirateTreasureLedger.MaximumReceipts ||
            data.treasureReceipts.Any(key => !PirateTreasureLedger.IsValidKey(key)) ||
            data.treasureReceipts.Distinct(StringComparer.Ordinal).Count() != data.treasureReceipts.Length))
            throw new InvalidDataException("Invalid saved treasure receipts.");
        PirateRunStatistics.Validate(data);
    }

    public static bool RunIsolatedDiskRegression(out string detail)
    {
        string previousRoot = isolatedTestDirectory;
        isolatedTestDirectory = Path.Combine(Application.temporaryCachePath,"PirateQuest-save-regression-"+Guid.NewGuid().ToString("N"));
        string testDirectory = isolatedTestDirectory;
        try
        {
            PirateSaveData first = new PirateSaveData { seed = 42,chapter = 1,upgrades = new[] {0,1,2,4},
                deaths = 7,hasCheckpoint = true,checkpointX = 12.5f,checkpointY = 16.52f,layoutSignature = "fixture-A",
                introSeen = true,treasureReceipts = new[] { "t1:0:0123456789ABCDEF:12:0", "t1:1:0123456789ABCDEF:21:2" } };
            if (!Write(first) || !TryLoad(out PirateSaveData loaded) || loaded.seed != 42 || loaded.chapter != 1 ||
                loaded.deaths != 7 || loaded.upgrades.Length != 4 || Mathf.Abs(loaded.checkpointY-16.52f) > .0001f ||
                !loaded.introSeen || !loaded.treasureReceipts.SequenceEqual(first.treasureReceipts))
                throw new InvalidDataException("Save round trip did not preserve progression.");
            string previousArchive = Path.Combine(testDirectory,"previous-expedition.json");
            if (!ArchiveBeforeNewRun() || !Read(previousArchive,out PirateSaveData initialArchive) || initialArchive.seed != first.seed)
                throw new InvalidDataException("Valid primary was not archived.");
            PirateSaveData second = new PirateSaveData { seed = 2026,chapter = 2,upgrades = new[] {0,1,2,3,4,6},deaths = 11 };
            if (!Write(second) || !File.Exists(SavePath+".bak")) throw new IOException("Atomic replacement backup not created.");
            const string corruptPrimary = "{ broken primary JSON";
            File.WriteAllText(SavePath,corruptPrimary);
            if (!TryLoad(out PirateSaveData recovered) || recovered.seed != first.seed || recovered.deaths != first.deaths ||
                !recovered.treasureReceipts.SequenceEqual(first.treasureReceipts) || !recovered.introSeen || LastError != null)
                throw new InvalidDataException("Corrupt primary did not recover the previous atomic backup.");
            if (!ArchiveBeforeNewRun() || !Read(previousArchive,out PirateSaveData recoveredArchive) ||
                recoveredArchive.seed != first.seed || recoveredArchive.deaths != first.deaths ||
                File.ReadAllText(SavePath) != corruptPrimary)
                throw new InvalidDataException("Recovered backup was not archived without mutating the primary.");
            if (!Write(second) || !TryLoad(out PirateSaveData newRun) || newRun.seed != second.seed ||
                !Read(previousArchive,out PirateSaveData archiveAfterWrite) || archiveAfterWrite.seed != first.seed ||
                archiveAfterWrite.deaths != first.deaths)
                throw new InvalidDataException("New-run autosave lost the valid recovered previous expedition.");
            const string corruptBackup = "{ broken backup JSON";
            File.WriteAllText(SavePath,corruptPrimary);
            File.WriteAllText(SavePath+".bak",corruptBackup);
            if (!ArchiveBeforeNewRun() || !Read(previousArchive,out PirateSaveData archiveAfterCorruption) ||
                archiveAfterCorruption.seed != first.seed || archiveAfterCorruption.deaths != first.deaths)
                throw new InvalidDataException("Invalid slots overwrote a valid previous expedition archive.");
            string[] invalidArchives = Directory.GetFiles(testDirectory,"invalid-expedition-*.json");
            if (invalidArchives.Length != 2 ||
                !invalidArchives.Any(path => path.EndsWith("-primary.json",StringComparison.Ordinal) && File.ReadAllText(path) == corruptPrimary) ||
                !invalidArchives.Any(path => path.EndsWith("-backup.json",StringComparison.Ordinal) && File.ReadAllText(path) == corruptBackup))
                throw new InvalidDataException("Invalid primary and backup were not preserved separately byte-for-byte.");
            bool rejectedVersion = false;
            try { Validate(new PirateSaveData { version = 99 }); } catch (InvalidDataException) { rejectedVersion = true; }
            if (!rejectedVersion) throw new InvalidDataException("Unknown future save version was accepted.");
            PirateSaveData legacy = JsonUtility.FromJson<PirateSaveData>("{\"version\":1,\"seed\":42,\"chapter\":1,\"upgrades\":[0,1,4],\"deaths\":3}");
            Validate(legacy);
            if (legacy.seed != 42 || (legacy.treasureReceipts != null && legacy.treasureReceipts.Length != 0))
                throw new InvalidDataException("Legacy v1 progression was not preserved.");

            if (!Write(first)) throw new IOException("Could not initialize the isolated locked-write fixture.");
            byte[] primaryBeforeFailure = File.ReadAllBytes(SavePath);
            byte[] backupBeforeFailure = File.ReadAllBytes(SavePath+".bak");
            string lockedTemporary = SavePath+".tmp";
            Debug.Log("PIRATE_SAVE_EXPECTED_WRITE_FAILURE fixture=lockedTemporary userSaveTouched=False isolated="+testDirectory);
            using (var locked = new FileStream(lockedTemporary,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
            {
                if (Write(second) || string.IsNullOrEmpty(LastError))
                    throw new InvalidDataException("A locked temporary file did not reject the save with an error.");
                if (!File.ReadAllBytes(SavePath).SequenceEqual(primaryBeforeFailure) ||
                    !File.ReadAllBytes(SavePath+".bak").SequenceEqual(backupBeforeFailure))
                    throw new InvalidDataException("A rejected write changed the primary or backup bytes.");
            }
            if (!Write(second) || LastError != null || !TryLoad(out PirateSaveData afterRetry) ||
                afterRetry.seed != second.seed || !File.ReadAllBytes(SavePath+".bak").SequenceEqual(primaryBeforeFailure))
                throw new InvalidDataException("Save retry after releasing the file lock did not preserve a valid atomic backup.");
            detail = "roundtrip=True treasureRoundtrip=True legacyV1=True atomicBackup=True corruptPrimaryRecovery=True recoveredLastErrorCleared=True versionRejected=True archive=True recoveredArchiveSurvivesNewRun=True invalidArchivesPreserved=True lockedTemporaryRejected=True failedWritePreservesSlots=True retryAfterUnlock=True isolated="+testDirectory;
            Debug.Log("PIRATE_SAVE_DISK_REGRESSION_SUCCESS "+detail);
            return true;
        }
        catch (Exception exception)
        {
            detail = exception.Message;
            Debug.LogError("PIRATE_SAVE_DISK_REGRESSION_FAILED "+detail);
            return false;
        }
        finally { isolatedTestDirectory = previousRoot; }
    }
}
