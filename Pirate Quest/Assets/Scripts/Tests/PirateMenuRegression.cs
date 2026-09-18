using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PirateMenuRegression : MonoBehaviour
{
    private static bool installed;
    private string evidence;
    private bool failed;
    private int checks;
    private int renderedFrames;
    private int unavailableFrames;
    private float originalMusic;
    private float originalSfx;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDomain() => installed = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (PirateSaveRestartProbe.IsRequested)
        {
            PirateSaveRestartProbe.InstallRequested();
            return;
        }
        if (installed || !Environment.GetCommandLineArgs().Contains("-pirateMenuTest")) return;
        installed = true;
        string sandbox = PirateSaveStore.BeginMenuTestSandbox();
        if (Environment.GetCommandLineArgs().Contains("-pirateVisualReview"))
        {
            Application.runInBackground = true;
            Debug.Log("PIRATE_VISUAL_REVIEW_SCOPE automaticActions=False saveSandbox=" + sandbox);
            return;
        }
        Debug.Log("PIRATE_MENU_TEST_SCOPE engineHandlers=True renderedOnGUI=True nativeClicks=False saveSandbox="+sandbox);
        var runner = new GameObject("Menu action and rendered-frame regression");
        DontDestroyOnLoad(runner);
        runner.AddComponent<PirateMenuRegression>();
    }

    private IEnumerator Start()
    {
        PirateTestModalAcknowledger.Install();
        Application.runInBackground = true;
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 60;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        evidence = Argument("-pirateMenuEvidence");
        if (string.IsNullOrWhiteSpace(evidence)) evidence = Path.Combine(Application.temporaryCachePath,"PirateQuest-menu-evidence");
        Directory.CreateDirectory(evidence);
        originalMusic = PirateAudio.MusicVolume;
        originalSfx = PirateAudio.SfxVolume;
        float deadline = Time.realtimeSinceStartup+30f;
        while ((PirateFrontEnd.Instance == null || FindFirstObjectByType<PirateGameFlow>()?.IsInitialized != true) &&
            Time.realtimeSinceStartup < deadline) yield return null;
        PirateFrontEnd menu = PirateFrontEnd.Instance;
        PirateGameFlow flow = FindFirstObjectByType<PirateGameFlow>();
        if (!Check(menu != null && flow != null && menu.RegressionPage == "Main","Main menu on launch")) yield break;
        if (!Check(Time.timeScale == 0f && !FindFirstObjectByType<PlayerMovement>().ControlsEnabled,"Menu blocks world and controls")) yield break;
        if (!Check(!File.Exists(PirateSaveStore.SavePath),"Opening menu does not overwrite or create campaign slot")) yield break;
        yield return Capture("01-main-menu.png");
        if (failed) yield break;

        menu.PerformRegressionAction("settings");
        if (!Check(menu.RegressionPage == "Settings","Settings button opens settings")) yield break;
        yield return Capture("02-settings.png");
        if (failed) yield break;
        menu.PerformRegressionAction("back");
        if (!Check(menu.RegressionPage == "Main","Settings back returns to main")) yield break;
        menu.PerformRegressionAction("new");
        if (!Check(menu.RegressionPage == "ConfirmNew","New expedition has confirmation")) yield break;
        yield return Capture("03-new-expedition-confirmation.png");
        if (failed) yield break;
        menu.PerformRegressionAction("confirm-new");
        yield return new WaitForSecondsRealtime(.25f);
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        PlayerLife life = player.GetComponent<PlayerLife>();
        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (!Check(menu.IsBriefingOpen && Time.timeScale == 0f && !player.ControlsEnabled && !PirateCampaignSession.IntroSeen,
            "Confirmed same-scene new game opens unseen story with world blocked")) yield break;
        Vector2 introPosition = body.position;
        float introClock = Time.time;
        player.SetAutomationInputOverride(Vector2.right,jumpPressed:true,dashPressed:true);
        yield return new WaitForSecondsRealtime(.2f);
        player.ClearAutomationInputOverride();
        menu.PerformRegressionAction("resume");
        if (!Check(menu.IsBriefingOpen && Vector2.Distance(body.position,introPosition)<.001f &&
            Mathf.Abs(Time.time-introClock)<.001f && PirateSaveStore.TryLoad(out PirateSaveData unseenSave) && !unseenSave.introSeen,
            "Briefing rejects gameplay and pause-resume bypass; unseen state is saved")) yield break;
        yield return Capture("03b-story-briefing.png");
        menu.PerformRegressionAction("acknowledge-briefing");
        if (!Check(PirateCampaignSession.IntroSeen && PirateSaveStore.TryLoad(out PirateSaveData seenSave) && seenSave.introSeen,
            "Actual story confirmation persists IntroSeen")) yield break;
        if (!Check(menu.RegressionPage == "Hidden" && Time.timeScale == 1f && player.ControlsEnabled,"New-game handler starts playable campaign")) yield break;
        if (!Check(PirateSaveStore.TryLoad(out PirateSaveData firstSave) && firstSave.seed == 20260918 && firstSave.chapter == 0 &&
            firstSave.upgrades.Contains((int)PirateUpgrade.Hook1) && firstSave.upgrades.Contains((int)PirateUpgrade.Saber1),
            "New campaign persists seed, chapter and starting kit")) yield break;

        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            bool layoutOk = false;
            string layoutDetails = null;
            yield return PirateHudLayoutRegression.Run(flow.HUD,(ok,details) => { layoutOk = ok; layoutDetails = details; });
            if (!Check(layoutOk,"All seven ability cards fit real GUI font measurements: "+layoutDetails)) yield break;
        }
        else Debug.LogWarning("PIRATE_HUD_LAYOUT_NOT_MEASURED graphicsDeviceNull=True");
        bool oldAutomaticAcknowledgement = PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement;
        try
        {
            PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = true;
            PirateUpgrade[] originalEquipment = player.GetComponent<PlayerAbilities>().CaptureProgression();
            flow.HUD.ShowUpgrade(PirateUpgrade.DoubleJump);
            if (!Check(flow.HUD.IsUpgradeOpen && Time.timeScale == 0f && !player.ControlsEnabled,
                "Double-jump description uses the real blocking modal")) yield break;
            yield return new WaitForSecondsRealtime(.2f);
            yield return Capture("03c-double-jump-card.png");
            flow.HUD.DismissUpgrade();
            if (!Check(!flow.HUD.IsUpgradeOpen && Time.timeScale == 1f && player.ControlsEnabled &&
                player.GetComponent<PlayerAbilities>().CaptureProgression().SequenceEqual(originalEquipment),
                "Description acknowledgement restores gameplay without granting equipment")) yield break;
            PirateUpgrade[] otherCards = { PirateUpgrade.Hook1, PirateUpgrade.Hook2, PirateUpgrade.SpringLeg,
                PirateUpgrade.Saber1, PirateUpgrade.Saber2, PirateUpgrade.Parrot };
            string[] cardFrames = { "03d-hook1-card.png", "03e-hook2-card.png", "03f-spring-card.png",
                "03g-saber1-card.png", "03h-saber2-card.png", "03i-parrot-card.png" };
            for (int i = 0; i < otherCards.Length; i++)
            {
                flow.HUD.ShowUpgrade(otherCards[i]);
                if (!Check(flow.HUD.IsUpgradeOpen && flow.HUD.DisplayedUpgrade == otherCards[i] &&
                    Time.timeScale == 0f && !player.ControlsEnabled,
                    otherCards[i]+" description uses the real blocking modal")) yield break;
                yield return new WaitForSecondsRealtime(.2f);
                yield return Capture(cardFrames[i]);
                flow.HUD.DismissUpgrade();
                if (!Check(!flow.HUD.IsUpgradeOpen && Time.timeScale == 1f && player.ControlsEnabled &&
                    player.GetComponent<PlayerAbilities>().CaptureProgression().SequenceEqual(originalEquipment),
                    otherCards[i]+" acknowledgement restores gameplay without changing equipment")) yield break;
            }
        }
        finally { PirateTestModalAcknowledger.SuspendAutomaticAcknowledgement = oldAutomaticAcknowledgement; }

        player.SetAutomationInputOverride(Vector2.right);
        yield return new WaitForSecondsRealtime(.16f);
        player.ClearAutomationInputOverride();
        menu.PerformRegressionAction("pause");
        Vector2 frozenPosition = body.position;
        float frozenClock = Time.time;
        player.SetAutomationInputOverride(Vector2.right,jumpPressed:true,dashPressed:true);
        yield return new WaitForSecondsRealtime(.25f);
        player.ClearAutomationInputOverride();
        if (!Check(menu.RegressionPage == "Pause" && Time.timeScale == 0f && !player.ControlsEnabled &&
            Vector2.Distance(body.position,frozenPosition)<.001f && Mathf.Abs(Time.time-frozenClock)<.001f,
            "Pause freezes physics and rejects movement/jump/dash input")) yield break;
        yield return Capture("04-pause.png");
        if (failed) yield break;
        menu.PerformRegressionAction("resume");
        if (!Check(menu.RegressionPage == "Hidden" && Time.timeScale == 1f && player.ControlsEnabled,"Resume restores controls")) yield break;
        yield return new WaitForSecondsRealtime(.1f);
        menu.PerformRegressionAction("checkpoint");
        yield return new WaitForFixedUpdate();
        if (!Check(Vector2.Distance(body.position,life.CheckpointPosition)<.12f,"Backspace/shared checkpoint handler restores checkpoint")) yield break;

        Vector3 checkpoint = life.CheckpointPosition;
        menu.PerformRegressionAction("pause");
        menu.PerformRegressionAction("main");
        if (!Check(menu.RegressionPage == "Main" && Time.timeScale == 0f && PirateSaveStore.TryLoad(out PirateSaveData saved) &&
            saved.hasCheckpoint && Vector2.Distance(new Vector2(saved.checkpointX,saved.checkpointY),checkpoint)<.01f,
            "Save-and-main writes the actual checkpoint")) yield break;
        yield return Capture("05-main-with-continue.png");
        if (failed) yield break;
        PirateGameFlow previousFlow = flow;
        menu.PerformRegressionAction("continue");
        deadline = Time.realtimeSinceStartup+30f;
        while (Time.realtimeSinceStartup<deadline)
        {
            flow = FindFirstObjectByType<PirateGameFlow>();
            if (flow != null && flow != previousFlow && flow.IsInitialized) break;
            yield return null;
        }
        player = FindFirstObjectByType<PlayerMovement>();
        if (!Check(flow != null && flow != previousFlow && flow.IsInitialized && menu.RegressionPage == "Hidden" &&
            Vector2.Distance(player.transform.position,checkpoint)<.2f && player.GetComponent<PlayerAbilities>().Has(PirateUpgrade.Hook1),
            "Continue reopens saved scene and restores checkpoint/inventory from JSON")) yield break;
        yield return Capture("06-continued-game.png");
        if (failed) yield break;

        menu.PerformRegressionAction("pause");
        menu.PerformRegressionAction("main");
        menu.PerformRegressionAction("new");
        if (!Check(menu.RegressionPage == "ConfirmNew","Existing-save new game requires confirmation")) yield break;
        menu.PerformRegressionAction("back");
        if (!Check(menu.RegressionPage == "Main" && PirateSaveStore.TryLoad(out _),"Cancelling new game retains saved expedition")) yield break;
        if (!Check(Mathf.Approximately(PirateAudio.MusicVolume,originalMusic) && Mathf.Approximately(PirateAudio.SfxVolume,originalSfx),
            "Menu test leaves real volume preferences unchanged")) yield break;
        yield return Capture("07-final-menu.png");
        if (failed) yield break;

        player.GetComponent<PlayerAbilities>().Apply(PirateUpgrade.Parrot,false);
        PirateCampaignSession.Remember(PirateUpgrade.Parrot);
        Debug.Log("PIRATE_MENU_CROWN_FIXTURE sceneSetupBypass=True fixtureProgression=True fullTraversalProof=False");
        previousFlow = flow;
        SceneManager.LoadScene(CampaignChapter.SceneNames[3]);
        yield return WaitForScene(3,previousFlow);
        flow = FindFirstObjectByType<PirateGameFlow>();
        player = FindFirstObjectByType<PlayerMovement>();
        if (!Check(flow != null && flow != previousFlow && flow.IsInitialized && flow.ChapterIndex == 3,
            "Explicit Crown presentation fixture initialized")) yield break;
        menu.PerformRegressionAction("pause");
        menu.PerformRegressionAction("main");
        if (!Check(PirateSaveStore.TryLoad(out PirateSaveData crownSave) && crownSave.chapter == 3 && crownSave.introSeen &&
            crownSave.upgrades.Contains((int)PirateUpgrade.Parrot),"Crown Save-and-main stores the already-owned parrot")) yield break;
        previousFlow = flow;
        menu.PerformRegressionAction("continue");
        yield return WaitForScene(3,previousFlow);
        flow = FindFirstObjectByType<PirateGameFlow>();
        player = FindFirstObjectByType<PlayerMovement>();
        bool parrotOk = ParrotPresentationRegression.VerifyRestoredScene(flow,player,out string parrotDetails);
        Debug.Log("PIRATE_MENU_PARROT_CONTINUE "+parrotDetails);
        if (!Check(flow != previousFlow && parrotOk,"Crown Continue has one companion and no already-owned pickup before movement")) yield break;
        if (!Check(menu.RegressionPage == "Hidden" && !menu.IsBriefingOpen && PirateCampaignSession.IntroSeen,
            "Continue does not repeat the story")) yield break;
        yield return Capture("08-crown-continued-parrot.png");

        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            bool worldPolishOk = false;
            string worldPolishDetails = null;
            yield return PirateWorldPolishRegression.Run(flow,player,evidence,
                (ok,details) => { worldPolishOk = ok; worldPolishDetails = details; });
            if (!Check(worldPolishOk,"Crown wall/window close-up and full fixture restoration: "+worldPolishDetails)) yield break;
            renderedFrames++;
        }
        else Debug.LogWarning("PIRATE_WORLD_POLISH_NOT_RENDERED graphicsDeviceNull=True");

        menu.PerformRegressionAction("pause");
        menu.PerformRegressionAction("main");
        if (!Check(PirateSaveStore.TryLoad(out PirateSaveData scoreSave) && menu.RegressionPage == "Main",
            "Score presentation starts from the real sandbox Crown save")) yield break;
        bool scoreFixtureReady = TryBuildScoreFixture(flow,player,scoreSave,
            out PirateRunStatistics.ScoreBreakdown expectedScore,out string scoreFixtureDetail);
        if (!Check(scoreFixtureReady && PirateSaveStore.Write(scoreSave),
            "Validated nonzero four-chapter score fixture: "+scoreFixtureDetail+" "+PirateSaveStore.LastError)) yield break;
        Debug.Log("PIRATE_MENU_SCORE_FIXTURE modelStatisticsSetup=True actualContinue=True physicalCollectionProof=False " +
            "physicalKillProof=False fullTraversalProof=False "+scoreFixtureDetail);
        previousFlow = flow;
        menu.PerformRegressionAction("continue");
        yield return WaitForScene(3,previousFlow);
        flow = FindFirstObjectByType<PirateGameFlow>();
        player = FindFirstObjectByType<PlayerMovement>();
        if (!Check(flow != null && flow != previousFlow && flow.IsInitialized && flow.IsVictory &&
            menu.RegressionPage == "Hidden" && Time.timeScale == 0f && !player.ControlsEnabled,
            "Completed fixture restores the actual frozen victory screen through Continue")) yield break;
        PirateRunStatistics.ScoreBreakdown actualScore = PirateCampaignSession.Statistics.Evaluate(
            PirateCampaignSession.TreasureLedger,player.GetComponent<PlayerLife>().DeathCount,true);
        if (!Check(SameScore(actualScore,expectedScore) && actualScore.HasFullHistory &&
            PirateCampaignSession.Statistics.AllChaptersKnown && PirateCampaignSession.Statistics.ActiveSeconds == 1234.5d &&
            player.GetComponent<PlayerLife>().DeathCount == 3 &&
            PirateCampaignSession.TreasureLedger.Capture().SequenceEqual(scoreSave.treasureReceipts),
            "Actual scoreboard uses restored loot, active time, kills and deaths without losing history")) yield break;
        yield return Capture("08b-completed-score.png");
        if (!Check(PirateCampaignSession.Statistics.ActiveSeconds == 1234.5d && PirateSaveStore.TryLoad(out PirateSaveData scoredSave) &&
            scoredSave.completed && scoredSave.activePlaySeconds == 1234.5d,
            "Completed scoreboard rendering leaves the run clock and saved completion frozen")) yield break;

        menu.PerformRegressionAction("pause");
        menu.PerformRegressionAction("main");
        if (!Check(PirateSaveStore.TryLoad(out PirateSaveData legacyScoreSave) && menu.RegressionPage == "Main" && legacyScoreSave.completed,
            "Legacy scoreboard fixture starts from the completed sandbox slot")) yield break;
        legacyScoreSave.statisticsVersion = 0;
        legacyScoreSave.activePlaySeconds = 0d;
        legacyScoreSave.statisticsComplete = false;
        legacyScoreSave.defeatedEnemyReceipts = Array.Empty<string>();
        legacyScoreSave.chapterScoreSignatures = Array.Empty<string>();
        legacyScoreSave.chapterTreasureValues = Array.Empty<int>();
        legacyScoreSave.chapterEnemyCounts = Array.Empty<int>();
        if (!Check(PirateSaveStore.Write(legacyScoreSave),
            "Legacy completed fixture validates and writes only to the isolated menu slot")) yield break;
        Debug.Log("PIRATE_MENU_SCORE_FIXTURE legacyStatistics=True modelStatisticsSetup=True actualContinue=True " +
            "physicalCollectionProof=False physicalKillProof=False fullTraversalProof=False");
        previousFlow = flow;
        menu.PerformRegressionAction("continue");
        yield return WaitForScene(3,previousFlow);
        flow = FindFirstObjectByType<PirateGameFlow>();
        player = FindFirstObjectByType<PlayerMovement>();
        if (!Check(flow != null && flow != previousFlow && flow.IsInitialized && flow.IsVictory &&
            menu.RegressionPage == "Hidden" && Time.timeScale == 0f && !player.ControlsEnabled,
            "Legacy completed Continue reaches the real frozen scoreboard")) yield break;
        PirateRunStatistics.ScoreBreakdown legacyScore = PirateCampaignSession.Statistics.Evaluate(
            PirateCampaignSession.TreasureLedger,player.GetComponent<PlayerLife>().DeathCount,true);
        if (!Check(!legacyScore.HasFullHistory && legacyScore.Speed == 0 && legacyScore.Combat == 0 &&
            legacyScore.Defeats == 0 && legacyScore.Treasure > 0 && legacyScore.Survival == expectedScore.Survival &&
            PirateCampaignSession.Statistics.ActiveSeconds == 0d &&
            PirateCampaignSession.TreasureLedger.Capture().SequenceEqual(legacyScoreSave.treasureReceipts),
            "Imported-save scoreboard keeps loot but labels partial history and invents no time or kills")) yield break;
        yield return Capture("08c-legacy-score.png");
        if (!Check(PirateCampaignSession.Statistics.ActiveSeconds == 0d && PirateSaveStore.TryLoad(out PirateSaveData legacyRenderedSave) &&
            legacyRenderedSave.completed && !legacyRenderedSave.statisticsComplete && legacyRenderedSave.activePlaySeconds == 0d,
            "Legacy scoreboard rendering preserves frozen completion and partial-history state")) yield break;

        menu.PerformRegressionAction("pause");
        menu.PerformRegressionAction("main");
        menu.PerformRegressionAction("new");
        previousFlow = flow;
        menu.PerformRegressionAction("confirm-new");
        yield return WaitForScene(0,previousFlow);
        flow = FindFirstObjectByType<PirateGameFlow>();
        player = FindFirstObjectByType<PlayerMovement>();
        if (!Check(flow != null && flow != previousFlow && flow.ChapterIndex == 0 && menu.IsBriefingOpen &&
            Time.timeScale == 0f && !player.ControlsEnabled && !PirateCampaignSession.IntroSeen,
            "Cross-scene new run keeps the briefing blocked through Dock rebind")) yield break;
        yield return new WaitForSecondsRealtime(.2f);
        yield return Capture("09-new-run-briefing.png");
        menu.PerformRegressionAction("back");
        if (!Check(menu.RegressionPage == "Hidden" && Time.timeScale == 1f && player.ControlsEnabled && PirateCampaignSession.IntroSeen,
            "Escape acknowledges cross-scene story through the common handler")) yield break;
        if (!Check(PirateTestModalAcknowledger.InvariantFailures == 0,"Tutorial acknowledgements preserve modal invariants")) yield break;
        Debug.Log($"PIRATE_MENU_REGRESSION_SUCCESS checks={checks} handlersPASS=True renderedFrames={unavailableFrames == 0 && renderedFrames == 20} " +
            $"screenshots={renderedFrames} unavailableFrames={unavailableFrames} engineHandlers=True nativeClicks=False " +
            $"visualReviewRequired=True " +
            $"savePath={PirateSaveStore.SavePath} preferencesChanged=False");
        Application.Quit(0);
    }

    private static bool TryBuildScoreFixture(PirateGameFlow flow,PlayerMovement player,PirateSaveData data,
        out PirateRunStatistics.ScoreBreakdown score,out string detail)
    {
        score = default;
        try
        {
            var statistics = new PirateRunStatistics();
            var treasure = new PirateTreasureLedger();
            statistics.Reset(true);
            for (int chapter = 0; chapter < CampaignChapter.SceneNames.Length; chapter++)
            {
                CampaignLayout layout = CampaignLayout.Create(data.seed,chapter,player.JumpLaunchSpeed,
                    player.GravityStrength,Time.fixedDeltaTime,PirateMovementProfile.ForChapter(chapter));
                if (!layout.GeometryValid) throw new InvalidDataException("Invalid model layout for chapter "+chapter);
                string signature = layout.Signature();
                if (chapter == flow.ChapterIndex && signature != flow.Generator.Campaign.Signature())
                    throw new InvalidDataException("Score model differs from the actual Crown layout.");
                var items = PirateTreasureLayout.Create(layout);
                statistics.RegisterChapter(chapter,signature,items.Sum(item => PirateTreasureLedger.ValueOf(item.Key)),
                    layout.Spawns.Count(spawn => spawn.Kind == CampaignLayout.SpawnKind.Crawler || spawn.Kind == CampaignLayout.SpawnKind.Plant));
                foreach (PirateTreasureLayout.Item item in items)
                    if (!treasure.Collect(item.Key)) throw new InvalidDataException("Duplicate or rejected model treasure receipt.");
                for (int spawnIndex = 0; spawnIndex < layout.Spawns.Count; spawnIndex++)
                {
                    CampaignLayout.SpawnKind kind = layout.Spawns[spawnIndex].Kind;
                    if (kind != CampaignLayout.SpawnKind.Crawler && kind != CampaignLayout.SpawnKind.Plant) continue;
                    if (!statistics.RecordDefeat(PirateRunStatistics.EnemyReceipt(chapter,signature,spawnIndex,
                        kind == CampaignLayout.SpawnKind.Plant)))
                        throw new InvalidDataException("Rejected model enemy receipt.");
                }
            }
            statistics.Advance(1234.5d);
            data.completed = true;
            data.deaths = 3;
            data.upgrades = Enum.GetValues(typeof(PirateUpgrade)).Cast<PirateUpgrade>().Select(upgrade => (int)upgrade).ToArray();
            data.treasureReceipts = treasure.Capture();
            statistics.CaptureInto(data);
            PirateSaveStore.Validate(data);
            score = statistics.Evaluate(treasure,data.deaths,true);
            if (!score.HasFullHistory || score.Treasure != 60000 || score.Combat != 15000 || score.Speed <= 0 ||
                score.Survival <= 0 || score.Total >= PirateRunStatistics.MaximumScore)
                throw new InvalidDataException("Score fixture must have four nonzero components and a non-perfect total.");
            detail = $"score={score.Total}/100000 treasure={score.TreasureValue}/{score.PossibleTreasureValue} " +
                $"kills={score.Defeats}/{score.PossibleDefeats} activeSeconds=1234.5 deaths=3";
            return true;
        }
        catch (Exception exception)
        {
            detail = exception.GetType().Name+": "+exception.Message;
            return false;
        }
    }

    private static bool SameScore(PirateRunStatistics.ScoreBreakdown actual,PirateRunStatistics.ScoreBreakdown expected) =>
        actual.Total == expected.Total && actual.Treasure == expected.Treasure && actual.Speed == expected.Speed &&
        actual.Combat == expected.Combat && actual.Survival == expected.Survival && actual.HasFullHistory == expected.HasFullHistory &&
        actual.TreasureValue == expected.TreasureValue && actual.PossibleTreasureValue == expected.PossibleTreasureValue &&
        actual.Defeats == expected.Defeats && actual.PossibleDefeats == expected.PossibleDefeats;

    private static IEnumerator WaitForScene(int chapter,PirateGameFlow previous)
    {
        float deadline = Time.realtimeSinceStartup+30f;
        while (Time.realtimeSinceStartup < deadline)
        {
            PirateGameFlow candidate = FindFirstObjectByType<PirateGameFlow>();
            if (candidate != null && candidate != previous && candidate.IsInitialized && candidate.ChapterIndex == chapter) yield break;
            yield return null;
        }
    }

    private IEnumerator Capture(string name)
    {
        string reason = "No graphics device";
        if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (frame != null && frame.width == 1280 && frame.height == 720 && HasRenderedContent(frame,out reason))
                {
                    File.WriteAllBytes(Path.Combine(evidence,name),frame.EncodeToPNG());
                    Destroy(frame);
                    renderedFrames++;
                    Debug.Log("PIRATE_MENU_SCREENSHOT verifiedNonempty=True "+Path.Combine(evidence,name));
                    yield break;
                }
                if (frame == null) reason = "ScreenCapture returned null";
                else if (frame.width != 1280 || frame.height != 720) reason = $"Unexpected frame size {frame.width}x{frame.height}";
                if (frame != null) Destroy(frame);
            }
        }
        reason += "; OnGUI repaintCount="+(PirateFrontEnd.Instance != null ? PirateFrontEnd.Instance.RegressionRepaints : 0);
        unavailableFrames++;
        string detail = "UI handler checks continue, but this is NOT visual QA. "+reason;
        File.WriteAllText(Path.Combine(evidence,name.Replace(".png",".unavailable.txt")),detail);
        Debug.LogWarning("PIRATE_MENU_RENDER_UNAVAILABLE name="+name+" "+detail);
    }

    private static bool HasRenderedContent(Texture2D frame,out string reason)
    {
        Color32[] pixels = frame.GetPixels32();
        int brightest = 0;
        int darkest = 255;
        int visible = 0;
        int samples = 0;
        for (int i = 0; i < pixels.Length; i += 16)
        {
            Color32 pixel = pixels[i];
            int brightness = Mathf.Max(pixel.r,Mathf.Max(pixel.g,pixel.b));
            brightest = Mathf.Max(brightest,brightness);
            darkest = Mathf.Min(darkest,brightness);
            if (brightness > 32) visible++;
            samples++;
        }
        bool nonempty = brightest > 48 && brightest-darkest > 24 && visible > samples*.005f;
        reason = $"pixelRange={darkest}..{brightest}; visibleSamples={visible}/{samples}; blankOrUniform={!nonempty}";
        return nonempty;
    }

    private bool Check(bool value,string message)
    {
        checks++;
        if (value) return true;
        failed = true;
        Debug.LogError("PIRATE_MENU_REGRESSION_FAILED "+message);
        Application.Quit(9);
        return false;
    }

    private static string Argument(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i=0;i<args.Length-1;i++) if (args[i] == flag) return args[i+1];
        return null;
    }
}
