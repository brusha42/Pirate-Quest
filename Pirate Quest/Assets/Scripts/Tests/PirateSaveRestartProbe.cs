using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-200)]
public sealed class PirateSaveRestartProbe : MonoBehaviour
{
    [Serializable]
    private sealed class Manifest
    {
        public int version = 1;
        public string runId;
        public int writerPid;
        public string writerStartedUtc;
        public string assemblyHash;
        public string finalSaveHash;
        public PirateSaveData expected;
    }

    [Serializable]
    private sealed class ReaderResult
    {
        public int writerPid;
        public int readerPid;
        public string runId;
        public string inputHash;
        public string outputHash;
        public string assemblyHash;
        public bool actualContinueHandler;
        public bool checkpointFallback;
        public bool gamePrefsUnchanged;
        public bool quitPrefsFlushSuppressed;
        public bool completedRestored;
        public bool completedClearedByConfirmedNewRun;
        public bool statisticsRestoredBeforeGameplay;
        public bool completedClockFrozen;
        public bool newRunStatisticsReset;
    }

    public static bool IsRequested => Environment.GetCommandLineArgs().Contains("-pirateSaveRestartProbe");
    private static PirateSaveRestartProbe instance;
    private static string phase;
    private static string runId;
    private static string directory;
    private static string expectedInputHash;
    private static Manifest manifest;
    private static bool installed;
    private static bool fallbackSeen;
    private static string initialPrefs;
    private static string assemblyHash;
    private static bool completedFixture;
    private bool successReady;
    private bool failed;
    private int checks;
    private bool completedRestored;
    private bool newRunResetChecked;
    private PirateGameFlow flow;
    private PlayerMovement player;
    private PlayerLife life;
    private PlayerAbilities abilities;
    private PirateGameFlow readerPreviousFlow;
    private bool holdNextContinue;
    private bool statisticsHeldBeforeGameplay;
    private bool completedClockFrozen;
    private bool newRunStatisticsReset;
    private PirateSaveData firstRestoredStatistics;

    private void Update()
    {
        if (!holdNextContinue || phase != "read") return;
        PirateGameFlow restored = FindFirstObjectByType<PirateGameFlow>();
        if (restored == null || restored == readerPreviousFlow || !restored.IsInitialized ||
            restored.ChapterIndex != manifest.expected.chapter) return;
        PirateFrontEnd menu = PirateFrontEnd.Instance;
        if (menu == null || menu.RegressionPage != "Hidden") return;
        if (!restored.IsVictory) menu.PerformRegressionAction("pause");
        firstRestoredStatistics = new PirateSaveData();
        PirateCampaignSession.Statistics.CaptureInto(firstRestoredStatistics);
        statisticsHeldBeforeGameplay = Time.timeScale == 0f;
        holdNextContinue = false;
    }

    public static void InstallRequested()
    {
        if (installed) return;
        installed = true;
        try
        {
            string sandbox = PirateSaveStore.BeginMenuTestSandbox();
            initialPrefs = GamePrefsSnapshot();
            Application.logMessageReceived += ObserveLog;
            var runner = new GameObject("Isolated two-process save and Continue probe");
            DontDestroyOnLoad(runner);
            instance = runner.AddComponent<PirateSaveRestartProbe>();
            Debug.Log("PIRATE_SAVE_RESTART_SCOPE phase="+phase+" pid="+CurrentPid+" runId="+runId+
                " sandbox="+sandbox+" fixtureRelocations=True sceneSetupBypass=True fullTraversalProof=False nativeClicks=False prefsScope=PirateQuest.*");
        }
        catch (Exception exception)
        {
            Debug.LogError("PIRATE_SAVE_RESTART_FAILED startup "+exception);
            Application.Quit(2);
        }
    }

    public static string PrepareSandbox()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (args.Count(arg => arg == "-pirateMenuTest") != 1 || args.Count(arg => arg == "-pirateAudioMute") != 1 ||
            args.Any(arg => arg.StartsWith("-pirateQuest",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Probe requires exactly -pirateMenuTest and -pirateAudioMute, with no -pirateQuest automation flags.");
        phase = Argument(args,"-pirateSaveRestartProbe");
        if (phase != "write" && phase != "read") throw new ArgumentException("Probe phase must be write or read.");
        int completedFlags = args.Count(arg => arg == "-pirateSaveRestartCompleted");
        if (completedFlags > 1 || (phase == "read" && completedFlags != 0))
            throw new ArgumentException("-pirateSaveRestartCompleted is a writer-only fixture flag; reader follows the manifest.");
        completedFixture = completedFlags == 1;
        string idText = Argument(args,"-pirateSaveRestartId");
        if (!Guid.TryParseExact(idText,"N",out Guid id)) throw new ArgumentException("Probe ID must be a 32-digit GUID in N format.");
        runId = id.ToString("N");
        string root = Path.GetFullPath(Application.temporaryCachePath);
        directory = Path.GetFullPath(Path.Combine(root,"PirateQuest-save-restart-"+runId));
        if (!directory.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Probe directory is outside the temporary cache.");
        assemblyHash = HashFile(typeof(PirateGameFlow).Assembly.Location);
        if (phase == "write")
        {
            if (args.Contains("-pirateSaveRestartExpectedHash")) throw new ArgumentException("Only read phase accepts an expected input hash.");
            if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Writer requires a fresh unique directory.");
            Directory.CreateDirectory(directory);
            WriteNew(Path.Combine(directory,"writer.claim"),CurrentPid.ToString());
        }
        else
        {
            expectedInputHash = Argument(args,"-pirateSaveRestartExpectedHash").ToUpperInvariant();
            if (expectedInputHash.Length != 64 || expectedInputHash.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("Reader requires a SHA-256 obtained after writer process exit.");
            if (!Directory.Exists(directory) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Reader directory is missing or a reparse point.");
            string manifestPath = Path.Combine(directory,"writer.complete.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536)
                throw new InvalidDataException("A completed writer manifest is required.");
            manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            if (manifest == null || manifest.version != 1 || manifest.runId != runId || manifest.writerPid == CurrentPid ||
                manifest.assemblyHash != assemblyHash || manifest.finalSaveHash != expectedInputHash)
                throw new InvalidDataException("Writer identity, assembly or final hash does not match this reader.");
            PirateSaveStore.Validate(manifest.expected);
            try
            {
                using (var writer = System.Diagnostics.Process.GetProcessById(manifest.writerPid))
                    if (!writer.HasExited) throw new InvalidOperationException("Writer must fully exit before reader begins.");
            }
            catch (ArgumentException) { }
            if (HashFile(Path.Combine(directory,"campaign-v1.json")) != expectedInputHash)
                throw new InvalidDataException("Save bytes differ from the orchestrator's post-exit hash.");
            WriteNew(Path.Combine(directory,"reader.claim"),CurrentPid.ToString());
        }
        return directory;
    }

    private IEnumerator Start()
    {
        PirateTestModalAcknowledger.Install();
        Application.runInBackground = true;
        Time.captureDeltaTime = 0f;
        IEnumerator test = Run();
        while (true)
        {
            object next = null;
            bool moved = false;
            try { moved = test.MoveNext(); if (moved) next = test.Current; }
            catch (Exception exception)
            {
                failed = true;
                Debug.LogError("PIRATE_SAVE_RESTART_FAILED phase="+phase+" "+exception);
                Application.Quit(2);
            }
            if (failed || !moved) yield break;
            yield return next;
        }
    }

    private IEnumerator Run()
    {
        yield return WaitForFlow(0);
        Require(flow != null && PirateFrontEnd.Instance != null,"Fresh Dock and front end initialized");
        PirateFrontEnd menu = PirateFrontEnd.Instance;
        Require(menu.RegressionPage == "Main" && !menu.HasPlayableRun && Time.timeScale == 0f,"Fresh process opens menu without a playable restored session");
        Require(Path.GetDirectoryName(Path.GetFullPath(PirateSaveStore.SavePath)) == directory,"All production save calls use the shared isolated directory");
        Require(GamePrefsSnapshot() == initialPrefs,"Startup preserves game PlayerPrefs");

        if (phase == "write")
        {
            Require(!File.Exists(PirateSaveStore.SavePath),"Opening writer menu did not create a save");
            menu.PerformRegressionAction("new");
            menu.SetRegressionSeed(7);
            menu.PerformRegressionAction("confirm-new");
            yield return new WaitForSecondsRealtime(.2f);
            Require(menu.IsBriefingOpen && Time.timeScale == 0f && !player.ControlsEnabled && !PirateCampaignSession.IntroSeen,
                "Writer starts through the real blocking new-expedition story");
            menu.PerformRegressionAction("acknowledge-briefing");
            Require(menu.RegressionPage == "Hidden" && player.ControlsEnabled && PirateCampaignSession.IntroSeen &&
                PirateSaveStore.TryLoad(out PirateSaveData introSave) && introSave.introSeen,
                "Writer acknowledges and saves the story through its ordinary handler");
            int fixtureChapter = completedFixture ? 3 : 1;
            SceneManager.LoadScene(CampaignChapter.SceneNames[fixtureChapter]);
            yield return WaitForFlow(fixtureChapter);
            Require(flow != null && flow.ChapterIndex == fixtureChapter && flow.Generator.Campaign.Seed == 7,"Writer fixture loaded its real non-default scene/seed");
            if (completedFixture)
            {
                foreach (PirateUpgrade upgrade in Enum.GetValues(typeof(PirateUpgrade))) abilities.Apply(upgrade);
                Debug.Log("PIRATE_SAVE_RESTART_COMPLETED_FIXTURE allAbilitiesGranted=True exitFixture=True fullTraversalProof=False");
            }
            else
                foreach (AbilityPickup pickup in FindObjectsByType<AbilityPickup>(FindObjectsSortMode.None))
                {
                    if (pickup == null) continue;
                    MoveFixture(pickup.transform.position);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                }
            Require(abilities.Has(PirateUpgrade.Hook2) && abilities.Has(PirateUpgrade.SpringLeg),
                completedFixture ? "Explicit completed fixture supplied every earned ability" : "Actual pickup triggers supplied non-starting upgrades");
            Checkpoint checkpoint = FindObjectsByType<Checkpoint>(FindObjectsSortMode.None)
                .Where(candidate => !candidate.IsActivated && Vector2.Distance(candidate.transform.position,flow.Generator.Campaign.Start)>8f)
                .OrderBy(candidate => Vector2.Distance(candidate.transform.position,flow.Generator.Campaign.Start)).FirstOrDefault();
            Require(checkpoint != null,"A non-entrance checkpoint exists for the fixture");
            MoveFixture(checkpoint.transform.position);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Require(checkpoint.IsActivated,"Production checkpoint trigger activated");
            life.RespawnImmediately();
            yield return new WaitForFixedUpdate();
            menu.PerformRegressionAction("pause");
            life.SetDeathCount(6);
            ConfigureStatisticsFixture();
            if (completedFixture)
            {
                typeof(PirateGameFlow).GetMethod("CompleteRun",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(flow,null);
                Require(flow.IsVictory && life.IsExitProtected,"Writer completion fixture entered production terminal state");
            }
            menu.PerformRegressionAction("main");
            Require(PirateSaveStore.TryLoad(out PirateSaveData saved),"Production SaveCampaign wrote readable JSON");
            Require(saved.seed == 7 && saved.chapter == fixtureChapter && saved.deaths == 6 && saved.hasCheckpoint && saved.completed == completedFixture &&
                Vector2.Distance(new Vector2(saved.checkpointX,saved.checkpointY),flow.Generator.Campaign.Start)>8f,
                "Saved fixture has non-default seed/chapter/deaths/checkpoint");
            Require(SameProgress(saved,Snapshot()),"Saved progression equals real runtime state");
            Require(SameStatistics(saved,Snapshot()) && saved.activePlaySeconds == 123.5d &&
                saved.defeatedEnemyReceipts.Length == 2, "SaveCampaign captured the nonzero model statistics fixture");
            manifest = new Manifest {
                runId = runId,writerPid = CurrentPid,
                writerStartedUtc = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O"),
                assemblyHash = assemblyHash,expected = saved
            };
            Debug.Log("PIRATE_SAVE_RESTART_WRITER_READY pid="+CurrentPid+" savePath="+PirateSaveStore.SavePath+
                " preQuitHash="+HashFile(PirateSaveStore.SavePath)+" actualSaveCampaign=True fixtureRelocations=True fullTraversalProof=False");
        }
        else
        {
            Require(HashFile(PirateSaveStore.SavePath) == expectedInputHash,"Menu did not modify the writer's post-exit bytes");
            Require(PirateCampaignSession.Seed == 20260918 && flow.ChapterIndex == 0 && life.DeathCount == 0 &&
                !abilities.Has(PirateUpgrade.Hook2) && !abilities.Has(PirateUpgrade.SpringLeg),
                "Reader has fresh memory, not the writer's static session");
            PirateGameFlow beforeContinue = flow;
            readerPreviousFlow = beforeContinue;
            holdNextContinue = true;
            menu.PerformRegressionAction("continue");
            yield return WaitForFlow(manifest.expected.chapter,beforeContinue);
            Require(statisticsHeldBeforeGameplay && !holdNextContinue && firstRestoredStatistics != null &&
                SameStatistics(manifest.expected,firstRestoredStatistics) && SameStatistics(manifest.expected,Snapshot()),
                "Continue restored bit-exact nonzero statistics before the first gameplay Update");
            if (menu.RegressionPage == "Pause") menu.PerformRegressionAction("resume");
            Require(flow != null && flow != beforeContinue && menu.RegressionPage == "Hidden","Actual Continue handler loaded the saved scene");
            Require(!menu.IsBriefingOpen && PirateCampaignSession.IntroSeen == manifest.expected.introSeen,
                "Process-restart Continue restores story state without opening the briefing");
            if (manifest.expected.chapter == 3 && abilities.HasParrot)
            {
                bool oneParrot = ParrotPresentationRegression.VerifyRestoredScene(flow,player,out string parrotDetails);
                Debug.Log("PIRATE_SAVE_RESTART_PARROT_CONTINUE "+parrotDetails);
                Require(oneParrot,"Restored Crown has one parrot before movement or pickup overlap processing");
            }
            Require(!fallbackSeen && SameProgress(manifest.expected,Snapshot()),"Continue restored exact progression and validated checkpoint without fallback");
            Vector2 point = new Vector2(manifest.expected.checkpointX,manifest.expected.checkpointY);
            Require(Vector2.Distance(player.transform.position,point)<.2f,"Reader restored the saved non-entrance checkpoint");
            Require(PirateSaveStore.TryLoad(out PirateSaveData afterStart) && SameProgress(manifest.expected,afterStart),
                "Real Continue Start-autosave preserves all loaded state including completed");
            Debug.Log("PIRATE_SAVE_RESTART_READER_RESTORED writerPid="+manifest.writerPid+" readerPid="+CurrentPid+
                " inputHash="+expectedInputHash+" differentPids=True actualContinueHandler=True checkpointFallback=False"+
                " seed="+manifest.expected.seed+" chapter="+manifest.expected.chapter+" deaths="+manifest.expected.deaths);
            if (manifest.expected.completed)
            {
                Require(flow.IsVictory && life.IsExitProtected && Time.timeScale == 0f && !player.ControlsEnabled,
                    "Completed Continue remains frozen and exit-protected");
                Goal king = FindFirstObjectByType<Goal>();
                CameraFollow camera = FindFirstObjectByType<CameraFollow>();
                Transform cameraTarget = (Transform)typeof(CameraFollow).GetField("target",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(camera);
                Require(king != null && cameraTarget == king.transform,"Completed Continue camera targets the king");
                life.Die();
                life.RespawnImmediately();
                yield return new WaitForSecondsRealtime(.5f);
                completedClockFrozen = SameStatistics(manifest.expected,Snapshot());
                Require(completedClockFrozen, "Completed Continue keeps active time and defeat history frozen");
                Require(!life.IsRespawning && SameProgress(manifest.expected,Snapshot()) && Vector2.Distance(player.transform.position,point)<.01f,
                    "Restored completion rejects death/respawn without losing progression");
                menu.PerformRegressionAction("main");
                Require(PirateSaveStore.TryLoad(out PirateSaveData afterMain) && afterMain.completed && SameProgress(manifest.expected,afterMain),
                    "Save-and-main retains completed true");
                completedRestored = true;
                PirateGameFlow completedFlow = flow;
                menu.PerformRegressionAction("new");
                Require(menu.RegressionPage == "ConfirmNew" && PirateSaveStore.TryLoad(out PirateSaveData beforeConfirm) && beforeConfirm.completed,
                    "New-game confirmation does not erase completed save before acceptance");
                menu.PerformRegressionAction("confirm-new");
                yield return WaitForFlow(0,completedFlow);
                Require(flow != null && menu.IsBriefingOpen && Time.timeScale == 0f && !player.ControlsEnabled && !PirateCampaignSession.IntroSeen,
                    "Confirmed replacement from Crown opens an unseen briefing in the new Dock scene");
                newRunStatisticsReset = PirateCampaignSession.Statistics.ActiveSeconds == 0d &&
                    PirateCampaignSession.Statistics.UniqueDefeats == 0 && PirateCampaignSession.Statistics.HasFullHistory;
                Require(newRunStatisticsReset && PirateCampaignSession.TreasureLedger.Count == 0,
                    "Confirmed new run clears time, defeats and treasure while retaining eligibility for full history");
                yield return new WaitForSecondsRealtime(.2f);
                Require(PirateCampaignSession.Statistics.ActiveSeconds == 0d,
                    "The new-run briefing and scene loading do not advance the active run clock");
                menu.PerformRegressionAction("acknowledge-briefing");
                Require(menu.RegressionPage == "Hidden" && PirateCampaignSession.IntroSeen,
                    "Reader explicitly acknowledges the new expedition story");
                Require(flow != null && !flow.IsVictory && !life.IsExitProtected && player.ControlsEnabled && Time.timeScale == 1f &&
                    life.DeathCount == 0 && abilities.CaptureProgression().Length == 2,
                    "Confirmed new run clears terminal state and progression normally");
                Require(PirateSaveStore.TryLoad(out PirateSaveData newSave) && !newSave.completed && newSave.chapter == 0 && newSave.deaths == 0,
                    "Confirmed new campaign persists completed false");
                PirateSaveData archived = JsonUtility.FromJson<PirateSaveData>(File.ReadAllText(Path.Combine(directory,"previous-expedition.json")));
                PirateSaveStore.Validate(archived);
                Require(archived.completed && SameProgress(manifest.expected,archived),"Previous expedition preserves the completed campaign after confirmed replacement");
                newRunResetChecked = true;
                point = life.CheckpointPosition;
                Debug.Log("PIRATE_SAVE_RESTART_COMPLETED_RESTORED completedAfterContinue=True completedAfterSaveMain=True " +
                    "confirmedNewRunCleared=True previousArchiveCompleted=True readerWillExitInNewExpedition=True");
            }
            else
            {
                Require(player.ControlsEnabled && Time.timeScale == 1f,"Ordinary Continue restores playable controls");
                menu.PerformRegressionAction("pause");
                menu.PerformRegressionAction("resume");
            }
            int deaths = life.DeathCount;
            life.Die();
            Require(life.IsRespawning && life.DeathCount == deaths+1,"Normal death works after process-restart Continue");
            float respawnDeadline = Time.realtimeSinceStartup+3f;
            while (life.IsRespawning && Time.realtimeSinceStartup < respawnDeadline) yield return null;
            Require(!life.IsRespawning && life.DeathCount == deaths+1 && player.ControlsEnabled &&
                Vector2.Distance(player.transform.position,point)<.2f,"Normal respawn returns to restored checkpoint");
            menu.PerformRegressionAction("pause");
            menu.PerformRegressionAction("main");
        }
        Require(GamePrefsSnapshot() == initialPrefs,"Probe left every known game preference value/existence unchanged");
        Require(FindObjectsByType<AudioSource>(FindObjectsSortMode.None).All(source => source.volume == 0f),"All production audio sources are muted by the explicit flag");
        successReady = true;
        Debug.Log("PIRATE_SAVE_RESTART_PHASE_READY phase="+phase+" checks="+checks+" awaitingRealQuitAutosave=True");
        Application.Quit(0);
    }

    private IEnumerator WaitForFlow(int chapter,PirateGameFlow previous = null)
    {
        flow = null;
        float deadline = Time.realtimeSinceStartup+30f;
        while (Time.realtimeSinceStartup < deadline)
        {
            PirateGameFlow candidate = FindFirstObjectByType<PirateGameFlow>();
            if (candidate != null && candidate != previous && candidate.IsInitialized && candidate.ChapterIndex == chapter)
            {
                flow = candidate;
                player = FindFirstObjectByType<PlayerMovement>();
                life = player.GetComponent<PlayerLife>();
                abilities = player.GetComponent<PlayerAbilities>();
                yield break;
            }
            yield return null;
        }
    }

    private void MoveFixture(Vector2 point)
    {
        if (life.IsRespawning) life.RespawnImmediately();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        body.position = point;
        player.transform.position = point;
        player.ResetMotion();
        Physics2D.SyncTransforms();
    }

    private PirateSaveData Snapshot()
    {
        Vector3 checkpoint = life.CheckpointPosition;
        var data = new PirateSaveData {
            seed = flow.Generator.Campaign.Seed,chapter = flow.ChapterIndex,deaths = life.DeathCount,
            upgrades = abilities.CaptureProgression().Select(upgrade => (int)upgrade).ToArray(),hasCheckpoint = true,
            checkpointX = checkpoint.x,checkpointY = checkpoint.y,layoutSignature = flow.Generator.Campaign.Signature(),completed = flow.IsVictory,
            introSeen = PirateCampaignSession.IntroSeen,treasureReceipts = PirateCampaignSession.TreasureLedger.Capture()
        };
        PirateCampaignSession.Statistics.CaptureInto(data);
        return data;
    }

    private void ConfigureStatisticsFixture()
    {
        Require(Time.timeScale == 0f, "Statistics fixture is initialized while the real menu is paused");
        var data = new PirateSaveData();
        PirateCampaignSession.Statistics.CaptureInto(data);
        data.activePlaySeconds = 123.5d;
        data.defeatedEnemyReceipts = Array.Empty<string>();
        PirateCampaignSession.Statistics.Restore(data);
        CampaignLayout layout = flow.Generator.Campaign;
        int credited = 0;
        for (int index = 0; index < layout.Spawns.Count && credited < 2; index++)
        {
            CampaignLayout.Spawn spawn = layout.Spawns[index];
            if (spawn.Kind != CampaignLayout.SpawnKind.Crawler && spawn.Kind != CampaignLayout.SpawnKind.Plant) continue;
            string receipt = PirateRunStatistics.EnemyReceipt(flow.ChapterIndex,layout.Signature(),index,
                spawn.Kind == CampaignLayout.SpawnKind.Plant);
            if (PirateCampaignSession.Statistics.RecordDefeat(receipt)) credited++;
        }
        Require(credited == 2 && PirateCampaignSession.Statistics.UniqueDefeats == 2 &&
            PirateCampaignSession.Statistics.ActiveSeconds == 123.5d,
            "Two actual-layout enemy identities and exact nonzero time form the isolated serialization fixture");
        Debug.Log("PIRATE_SAVE_RESTART_STATISTICS_FIXTURE modelStatisticsSetup=True actualLayoutIdentities=True " +
            "activeSeconds=123.5 uniqueDefeats=2 physicalKillProof=False actualSaveCampaign=True");
    }

    private static bool SameProgress(PirateSaveData expected,PirateSaveData actual) =>
        expected.version == actual.version && expected.seed == actual.seed && expected.chapter == actual.chapter &&
        expected.deaths == actual.deaths && expected.hasCheckpoint == actual.hasCheckpoint && expected.completed == actual.completed &&
        expected.introSeen == actual.introSeen && (expected.treasureReceipts ?? Array.Empty<string>()).OrderBy(value => value,StringComparer.Ordinal)
            .SequenceEqual((actual.treasureReceipts ?? Array.Empty<string>()).OrderBy(value => value,StringComparer.Ordinal)) &&
        expected.layoutSignature == actual.layoutSignature && Mathf.Abs(expected.checkpointX-actual.checkpointX)<.01f &&
        Mathf.Abs(expected.checkpointY-actual.checkpointY)<.01f && expected.upgrades.OrderBy(value => value).SequenceEqual(actual.upgrades.OrderBy(value => value)) &&
        SameStatistics(expected,actual);

    private static bool SameStatistics(PirateSaveData expected,PirateSaveData actual) =>
        expected.statisticsVersion == actual.statisticsVersion && expected.statisticsComplete == actual.statisticsComplete &&
        BitConverter.DoubleToInt64Bits(expected.activePlaySeconds) == BitConverter.DoubleToInt64Bits(actual.activePlaySeconds) &&
        (expected.defeatedEnemyReceipts ?? Array.Empty<string>()).SequenceEqual(actual.defeatedEnemyReceipts ?? Array.Empty<string>()) &&
        (expected.chapterTreasureValues ?? Array.Empty<int>()).SequenceEqual(actual.chapterTreasureValues ?? Array.Empty<int>()) &&
        (expected.chapterEnemyCounts ?? Array.Empty<int>()).SequenceEqual(actual.chapterEnemyCounts ?? Array.Empty<int>()) &&
        (expected.chapterScoreSignatures ?? Array.Empty<string>()).SequenceEqual(actual.chapterScoreSignatures ?? Array.Empty<string>());

    public static void AfterRealQuitAutosave()
    {
        try
        {
            if (instance == null || !instance.successReady || instance.failed) throw new InvalidOperationException("Probe did not complete before quitting.");
            instance.Require(PirateTestModalAcknowledger.InvariantFailures == 0,
                "Modal driver preserved freeze/input invariants before real quit");
            bool prefsUnchanged = GamePrefsSnapshot() == initialPrefs;
            if (!prefsUnchanged) throw new InvalidOperationException("Game PlayerPrefs changed before quit.");
            if (!PirateSaveStore.TryLoad(out PirateSaveData finalSave)) throw new InvalidDataException("Real quit autosave is unreadable.");
            string finalHash = HashFile(PirateSaveStore.SavePath);
            if (phase == "write")
            {
                if (!SameProgress(manifest.expected,finalSave)) throw new InvalidDataException("Quit autosave changed writer progression.");
                manifest.finalSaveHash = finalHash;
                WriteNew(Path.Combine(directory,"writer.complete.json"),JsonUtility.ToJson(manifest,true));
            }
            else
            {
                if (manifest.expected.completed && (!instance.completedRestored || !instance.newRunResetChecked || finalSave.completed || finalSave.chapter != 0))
                    throw new InvalidDataException("Completed reader must finish in its explicitly confirmed new expedition.");
                WriteNew(Path.Combine(directory,"reader.complete.json"),JsonUtility.ToJson(new ReaderResult {
                    writerPid = manifest.writerPid,readerPid = CurrentPid,runId = runId,inputHash = expectedInputHash,
                    outputHash = finalHash,assemblyHash = assemblyHash,actualContinueHandler = true,checkpointFallback = fallbackSeen,
                    gamePrefsUnchanged = prefsUnchanged,quitPrefsFlushSuppressed = true,
                    completedRestored = instance.completedRestored,completedClearedByConfirmedNewRun = instance.newRunResetChecked,
                    statisticsRestoredBeforeGameplay = instance.statisticsHeldBeforeGameplay,
                    completedClockFrozen = instance.completedClockFrozen,newRunStatisticsReset = instance.newRunStatisticsReset
                },true));
            }
            Debug.Log("PIRATE_SAVE_RESTART_"+(phase == "write" ? "WRITER" : "READER")+"_SUCCESS pid="+CurrentPid+
                " checks="+instance.checks+" savePath="+PirateSaveStore.SavePath+" finalSaveHash="+finalHash+
                " assemblyHash="+assemblyHash+" realQuitAutosave=True prefsWritten="+(!prefsUnchanged)+
                " quitPrefsFlushSuppressed=True prefsScope=PirateQuest.* userSaveTouched=False fixtureRelocations=True fullTraversalProof=False nativeClicks=False");
        }
        catch (Exception exception)
        {
            Debug.LogError("PIRATE_SAVE_RESTART_FAILED quit "+exception);
            Application.Quit(2);
        }
    }

    private void Require(bool condition,string label)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(label);
    }

    private static string Argument(string[] args,string name)
    {
        if (args.Count(value => value == name) != 1) throw new ArgumentException("Exactly one "+name+" is required.");
        int index = Array.IndexOf(args,name);
        if (index >= args.Length-1 || args[index+1].StartsWith("-",StringComparison.Ordinal)) throw new ArgumentException("Missing value for "+name);
        return args[index+1];
    }
    private static int CurrentPid => System.Diagnostics.Process.GetCurrentProcess().Id;
    private static string HashFile(string path)
    {
        using (var algorithm = SHA256.Create())
        using (var input = File.OpenRead(path))
            return BitConverter.ToString(algorithm.ComputeHash(input)).Replace("-",string.Empty);
    }
    private static void WriteNew(string path,string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        using (var output = new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        {
            output.Write(bytes,0,bytes.Length);
            output.Flush(true);
        }
    }
    private static string GamePrefsSnapshot() =>
        PlayerPrefs.HasKey("PirateQuest.MusicVolume")+":"+PlayerPrefs.GetFloat("PirateQuest.MusicVolume",.22f).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+
        PlayerPrefs.HasKey("PirateQuest.SfxVolume")+":"+PlayerPrefs.GetFloat("PirateQuest.SfxVolume",.65f).ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"|"+
        PlayerPrefs.HasKey("PirateQuest.FullScreen")+":"+PlayerPrefs.GetInt("PirateQuest.FullScreen",-1);
    private static void ObserveLog(string condition,string stackTrace,LogType type)
    {
        if (condition.StartsWith("PIRATE_SAVE_CHECKPOINT_FALLBACK",StringComparison.Ordinal)) fallbackSeen = true;
    }
}
