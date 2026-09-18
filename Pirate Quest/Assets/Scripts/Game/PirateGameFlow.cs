using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(1000)]
[DisallowMultipleComponent]
public class PirateGameFlow : MonoBehaviour
{
    private readonly List<GameObject> runtimeObjects = new List<GameObject>();
    private List<PirateTreasureLayout.Item> treasureLayout = new List<PirateTreasureLayout.Item>();
    public int TotalTreasures => treasureLayout.Count;
    public int CollectedTreasures => treasureLayout.Count(item => PirateCampaignSession.TreasureLedger.Contains(item.Key));

    private PlayerMovement playerMovement;
    private PlayerLife playerLife;
    private LevelGenerator generator;
    private CameraFollow cameraFollow;
    private Goal goal;
    private GUIStyle hudStyle;
    private GUIStyle titleStyle;
    private GUIStyle centeredStyle;
    private Sprite markerSprite;
    private float startHeight;
    private float finishHeight;
    private bool initialized;
    private bool isPaused;
    private bool isVictory;
    private string statusMessage = string.Empty;
    private string saveWarning;
    private float statusMessageUntil;
    private CampaignChapter chapter;
    private PlayerAbilities abilities;
    private bool transitioning;
    private float chapterTitleUntil;
    private string contextualHint;
    private PirateHUD hud;
    private bool upgradeBlocked;
    private BlackTide blackTide;
    private float autosaveElapsed;
    private readonly HashSet<string> chapterEnemyReceipts = new HashSet<string>(StringComparer.Ordinal);
    public PirateHUD HUD => hud;
    public BlackTide Tide => blackTide;
    public int ChapterIndex => chapter != null ? chapter.chapterIndex : 0;
    public bool IsInitialized => initialized;
    public LevelGenerator Generator => generator;
    public bool IsVictory => isVictory;
    public bool IsTransitioning => transitioning;

    private void Awake()
    {
        playerMovement = FindFirstObjectByType<PlayerMovement>();
        playerLife = playerMovement != null ? playerMovement.GetComponent<PlayerLife>() : null;
        generator = FindFirstObjectByType<LevelGenerator>();
        cameraFollow = FindFirstObjectByType<CameraFollow>();
        chapter = FindFirstObjectByType<CampaignChapter>();
        abilities = playerMovement != null ? playerMovement.GetComponent<PlayerAbilities>() : null;

        if (chapter != null && abilities != null)
        {
            if (!PirateCampaignSession.Active) PirateCampaignSession.Begin(20260918);
            PirateCampaignSession.Advance(chapter.chapterIndex);
            PirateCampaignSession.Restore(abilities);
            abilities.Upgraded += HandleUpgrade;
            abilities.ScoutChanged += HandleScout;
            abilities.Scout.InitializeVisual(PirateWorldArt.GetSprite(PirateArtKind.Parrot));
        }

        if (playerLife != null)
        {
            playerLife.Died += HandleDeath;
            playerLife.Respawned += HandleRespawn;
        }
    }

    private void Start()
    {
        if (playerMovement == null || playerLife == null)
        {
            Debug.LogError("PirateGameFlow: required player components are missing.", this);
            enabled = false;
            return;
        }

        if (generator != null && chapter != null)
        {
            generator.GenerateWithSeed(PirateCampaignSession.Seed);
        }
        else if (generator != null && generator.RoomCount == 0)
        {
            generator.GenerateLevel();
        }

        if (chapter != null && abilities != null)
        {
            hud = gameObject.AddComponent<PirateHUD>();
            hud.Initialize(playerMovement, abilities, playerLife, ChapterIndex,
                blocked => { upgradeBlocked = blocked; ApplyControlState(); });
        }
        SetupRunObjects();
        initialized = true;
        if (chapter != null)
        {
            playerLife.SetDeathCount(PirateCampaignSession.Deaths);
            RestoreSavedCheckpoint();
            if (blackTide != null && blackTide.IsActive)
                blackTide.SetSafeCheckpoint(playerLife.CheckpointPosition + Vector3.down * halfHeightForCheckpoint());
            PirateAudio.SetChapter(ChapterIndex);
            PirateFrontEnd.Bind(this);
            SaveCampaign();
        }
        chapterTitleUntil = Time.unscaledTime + 3.5f;

        if (Environment.GetCommandLineArgs().Contains("-pirateQuestCampaignTest") &&
            FindFirstObjectByType<CampaignTraversalRegression>() == null)
        {
            GameObject regression = new GameObject("Campaign Integration Regression");
            DontDestroyOnLoad(regression);
            regression.AddComponent<CampaignTraversalRegression>();
        }

        if (Environment.GetCommandLineArgs().Contains("-pirateQuestTraversalTest"))
        {
            gameObject.AddComponent<TowerTraversalRegression>();
        }

        if (Environment.GetCommandLineArgs().Contains("-pirateQuestSmokeTest"))
        {
            StartCoroutine(RunAutomatedSmokeTest());
        }

        string capturePath = GetCommandLineValue("-pirateQuestCapture");
        if (!string.IsNullOrWhiteSpace(capturePath))
        {
            Application.runInBackground = true;
            StartCoroutine(CaptureVerificationScreenshot(capturePath));
        }
    }

    private void Update()
    {
        if (initialized && !isPaused && !isVictory && !transitioning && !upgradeBlocked &&
            Time.timeScale > 0f && playerLife != null && !playerLife.IsRespawning &&
            (PirateFrontEnd.Instance == null || !PirateFrontEnd.Instance.IsBlocking))
        {
            PirateCampaignSession.Statistics.Advance(Time.unscaledDeltaTime);
            autosaveElapsed += Time.unscaledDeltaTime;
            if (autosaveElapsed >= 15f)
            {
                autosaveElapsed = 0f;
                SaveCampaign();
            }
        }
        if (hud != null)
        {
            hud.SetPresentationVisible(!transitioning && !isVictory);
            hud.SetMessage(saveWarning ?? (Time.unscaledTime < statusMessageUntil ? statusMessage : contextualHint));
            hud.SetTreasureStatus(PirateCampaignSession.TreasureLedger.Score, 0, CollectedTreasures, TotalTreasures);
            hud.SetRunTime(PirateCampaignSession.Statistics.ActiveSeconds);
            if (blackTide != null && blackTide.IsActive)
            {
                string pressure = blackTide.IsScoutingPaused ? "BLACK TIDE · scouting holds the water" :
                    $"BLACK TIDE · {Mathf.Max(0f, playerMovement.GetComponent<Collider2D>().bounds.min.y-blackTide.SurfaceY):0.0} m below";
                hud.SetPressureStatus(pressure, Mathf.Clamp01(1f-blackTide.SecondsToDanger/30f));
            }
            else hud.SetPressureStatus(null);
        }
        if (!initialized || transitioning || Keyboard.current == null || PirateFrontEnd.Instance != null)
        {
            return;
        }

        if (PirateHUD.ConsumeUpgradeInput()) return;
        if (Keyboard.current.escapeKey.wasPressedThisFrame && !isVictory)
        {
            SetPaused(!isPaused);
        }

        if (Keyboard.current.rKey.wasPressedThisFrame)
        {
            RestartWithSeed(generator != null ? generator.CurrentSeed : 20260918);
        }

        if (Keyboard.current.nKey.wasPressedThisFrame)
        {
            int newSeed = unchecked(Environment.TickCount * 397 ^ DateTime.UtcNow.Millisecond);
            RestartWithSeed(newSeed);
        }
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;

        if (playerLife != null)
        {
            playerLife.Died -= HandleDeath;
            playerLife.Respawned -= HandleRespawn;
        }
        if (abilities != null)
        {
            abilities.Upgraded -= HandleUpgrade;
            abilities.ScoutChanged -= HandleScout;
        }
    }

    private void SetupRunObjects()
    {
        ClearRuntimeObjects();

        if (generator != null && generator.Campaign != null)
        {
            SetupCampaignRun();
            return;
        }

        if (generator != null && generator.TowerRoute != null)
        {
            SetupTowerRun();
            return;
        }

        List<Room> rooms = generator != null
            ? generator.GetAllRooms().OrderBy(room => room.bounds.center.y).ToList()
            : new List<Room>();

        if (rooms.Count == 0)
        {
            SetupFallbackRun();
            return;
        }

        Room startRoom = rooms[0];
        Room finishRoom = rooms[rooms.Count - 1];
        Vector3 startPosition = ToWorldPosition(startRoom.GetFloorCenter(), 0.1f);
        Vector3 finishPosition = ToWorldPosition(finishRoom.GetFloorCenter(), 1.25f);

        startHeight = startPosition.y;
        finishHeight = Mathf.Max(startHeight + 1f, finishPosition.y);
        MovePlayerToStart(startPosition);
        SpawnGoal(finishPosition);

        if (rooms.Count >= 4)
        {
            SpawnAbilityPickup(ToWorldPosition(rooms[Mathf.Clamp(rooms.Count / 3, 1, rooms.Count - 2)].GetFloorCenter(), 1.5f));
            SpawnCheckpoint(ToWorldPosition(rooms[Mathf.Clamp(rooms.Count * 2 / 3, 1, rooms.Count - 2)].GetFloorCenter(), 1f));
        }

        SpawnRoomHazards(rooms);
        SpawnHookAnchors(rooms);
        SpawnCannons(rooms);
        ShowStatus($"Tower ready. Seed: {generator.CurrentSeed}", 3f);

        Debug.Log(
            $"Pirate Quest: seed={generator.CurrentSeed}, rooms={generator.RoomCount}, " +
            $"validated={generator.ValidatedRoomCount}/{generator.RoomCount}, size={generator.LevelSize.x}x{generator.LevelSize.y}");
    }

    private IEnumerator RunAutomatedSmokeTest()
    {
        yield return null;
        yield return new WaitForFixedUpdate();
        yield return new WaitForSecondsRealtime(0.5f);

        bool hasGeneratedWorld = generator == null || generator.RoomCount > 0;
        bool generationWasValidated = generator == null || generator.AllRoomsValidated;
        bool guaranteedRouteExists = generator == null ||
                                     (generator.GuaranteedRoutesValid &&
                                      generator.GuaranteedRouteRoomCount == generator.RoomCount);
        PlayerGrapple grapple = playerMovement != null ? playerMovement.GetComponent<PlayerGrapple>() : null;
        bool grappleWorksAtStart = grapple != null && grapple.TryAttachToNearestAnchor();
        grapple?.Detach();
        bool hasVisualAnimation = playerMovement != null &&
                                  playerMovement.GetComponent<PlayerVisualAnimator>() != null;
        bool coreLoopExists = playerMovement != null && playerLife != null && goal != null;
        bool success = hasGeneratedWorld && generationWasValidated && guaranteedRouteExists &&
                       grappleWorksAtStart && hasVisualAnimation && coreLoopExists;

        if (success)
        {
            Debug.Log(
                $"PIRATE_RUNTIME_SMOKE_SUCCESS: seed={generator?.CurrentSeed}, " +
                $"rooms={generator?.RoomCount}, route={guaranteedRouteExists}, " +
                $"grapple={grappleWorksAtStart}, animation={hasVisualAnimation}, " +
                $"player={playerMovement.transform.position}");
        }
        else
        {
            Debug.LogError(
                $"PIRATE_RUNTIME_SMOKE_FAILED: world={hasGeneratedWorld}, " +
                $"validated={generationWasValidated}, route={guaranteedRouteExists}, " +
                $"grapple={grappleWorksAtStart}, animation={hasVisualAnimation}, coreLoop={coreLoopExists}");
        }

        Application.Quit(success ? 0 : 2);
    }

    private IEnumerator CaptureVerificationScreenshot(string outputPath)
    {
        yield return new WaitForSecondsRealtime(1.5f);

        Camera captureCamera = Camera.main;
        if (captureCamera == null)
        {
            Debug.LogError("PIRATE_SCREENSHOT_FAILED: Main Camera not found.");
            Application.Quit(3);
            yield break;
        }

        const int captureWidth = 1280;
        const int captureHeight = 720;
        RenderTexture renderTexture = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        Texture2D screenshot = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = captureCamera.targetTexture;

        captureCamera.targetTexture = renderTexture;
        RenderTexture.active = renderTexture;
        captureCamera.Render();
        screenshot.ReadPixels(new Rect(0f, 0f, captureWidth, captureHeight), 0, 0);
        screenshot.Apply();

        captureCamera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;

        string directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
        Destroy(renderTexture);
        Destroy(screenshot);
        Debug.Log("PIRATE_SCREENSHOT_SAVED: " + outputPath);
        Application.Quit(0);
    }

    private static string GetCommandLineValue(string argumentName)
    {
        string[] arguments = Environment.GetCommandLineArgs();

        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (string.Equals(arguments[index], argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return string.Empty;
    }

    private void SetupFallbackRun()
    {
        Vector3 startPosition = playerMovement.transform.position;
        Vector3 finishPosition = startPosition + Vector3.up * 20f;
        startHeight = startPosition.y;
        finishHeight = finishPosition.y;
        MovePlayerToStart(startPosition);
        SpawnGoal(finishPosition);
        ShowStatus("No generated rooms. Fallback route enabled.", 4f);
    }

    private void SetupCampaignRun()
    {
        contextualHint = null;
        CampaignLayout layout = generator.Campaign;
        float halfHeight = playerMovement.GetComponent<Collider2D>().bounds.extents.y;
        Vector3 start = layout.Start + Vector2.up * (halfHeight + .02f);
        startHeight = start.y;
        finishHeight = layout.Exit.y + halfHeight;
        MovePlayerToStart(start);
        SpawnGoal(layout.Exit + Vector2.up * 1.05f);
        GameObject decor = new GameObject("Chapter scenery");
        runtimeObjects.Add(decor);
        PirateWorldArt.Decorate(new Rect(layout.Bounds.x, layout.Bounds.y, layout.Bounds.width, layout.Bounds.height),
            layout.Rooms.Where(r => r.IsLeaf && !r.IsShaft).Select(r => new Rect(r.Bounds.x, r.Bounds.y, r.Bounds.width, r.Bounds.height)),
            ChapterIndex, decor.transform, point => PresentationSurfaceBelow(layout,point), opaque =>
            {
                var footprint = new Rect(opaque.min.x, opaque.min.y, opaque.size.x, opaque.size.y);
                return layout.Platforms.Any(platform => !platform.OneWay && platform.Bounds.Overlaps(footprint)) ||
                    layout.Solids.Any(solid => new Rect(solid.x, solid.y, solid.width, solid.height).Overlaps(footprint));
            });

        treasureLayout = PirateTreasureLayout.Create(layout);
        string scoreSignature = layout.Signature();
        PirateCampaignSession.Statistics.RegisterChapter(ChapterIndex, scoreSignature,
            treasureLayout.Sum(item => PirateTreasureLedger.ValueOf(item.Key)),
            layout.Spawns.Count(spawn => spawn.Kind == CampaignLayout.SpawnKind.Crawler || spawn.Kind == CampaignLayout.SpawnKind.Plant));
        for (int spawnIndex = 0; spawnIndex < layout.Spawns.Count; spawnIndex++)
        {
            CampaignLayout.Spawn spawn = layout.Spawns[spawnIndex];
            if (spawn.Kind == CampaignLayout.SpawnKind.Exit) continue;
            if (spawn.Kind == CampaignLayout.SpawnKind.Upgrade && abilities.Has(spawn.Ability)) continue;
            if (spawn.Kind == CampaignLayout.SpawnKind.Crawler)
            {
                CampaignLayout.Platform support = layout.Platforms[(int)spawn.Value];
                BrineCrawler crawler = BrineCrawler.Install(null, spawn.Position, support.Bounds, spawn.Facing);
                string receipt = RegisterEnemy(scoreSignature, spawnIndex, false);
                crawler.SetDefeatCallback(() => RecordEnemyDefeat(receipt));
                runtimeObjects.Add(crawler.gameObject);
                continue;
            }
            if (spawn.Kind == CampaignLayout.SpawnKind.Decoration)
            {
                if (spawn.Size.y > spawn.Size.x * 3f)
                {
                    runtimeObjects.Add(PirateWorldArt.CreateHoistChains(spawn.Position, spawn.Size));
                    continue;
                }
                PirateArtKind clutter = spawn.Optional ? PirateArtKind.Crates :
                    (spawn.NodeIndex % 3 == 0 ? PirateArtKind.Barrel : spawn.NodeIndex % 3 == 1 ? PirateArtKind.Skull : PirateArtKind.Banner);
                GameObject detail = PirateWorldArt.Create(spawn.Optional ? "Optional lookout treasure crates" : "Chapter detail",
                    clutter,spawn.Position,spawn.Size);
                runtimeObjects.Add(detail);
                foreach (SpriteRenderer renderer in detail.GetComponentsInChildren<SpriteRenderer>()) renderer.sortingOrder = -3;
                if (clutter != PirateArtKind.Banner) AnchorPresentationToFloor(detail,layout);
                continue;
            }
            if (spawn.Kind == CampaignLayout.SpawnKind.Hint)
            {
                GameObject hintObject = new GameObject("Instruction: " + spawn.Text);
                hintObject.transform.position = spawn.Position;
                runtimeObjects.Add(hintObject);
                BoxCollider2D area = hintObject.AddComponent<BoxCollider2D>();
                area.isTrigger = true; area.size = spawn.Size;
                hintObject.AddComponent<CampaignHint>().Initialize(this, spawn.Text);
                continue;
            }
            if (spawn.Kind == CampaignLayout.SpawnKind.DarkZone)
            {
                GameObject dark = new GameObject("Unlit gallery — parrot reconnaissance");
                dark.transform.position = spawn.Position;
                runtimeObjects.Add(dark);
                dark.AddComponent<DarkZone>().Initialize(spawn.Size);
                continue;
            }
            PirateArtKind art = ArtFor(spawn);
            GameObject item = PirateWorldArt.Create(spawn.Kind.ToString(), art, spawn.Position, spawn.Size);
            runtimeObjects.Add(item);
            foreach (SpriteRenderer renderer in item.GetComponentsInChildren<SpriteRenderer>())
                renderer.flipX = spawn.Facing < 0;
            if (spawn.Kind == CampaignLayout.SpawnKind.Checkpoint || spawn.Kind == CampaignLayout.SpawnKind.Cannon ||
                spawn.Kind == CampaignLayout.SpawnKind.JumpPad) AnchorPresentationToFloor(item,layout);
            BoxCollider2D collider = item.AddComponent<BoxCollider2D>();
            collider.size = spawn.Size;
            collider.isTrigger = true;
            switch (spawn.Kind)
            {
                case CampaignLayout.SpawnKind.Checkpoint:
                    Checkpoint checkpoint = item.AddComponent<Checkpoint>();
                    Vector2 standing = layout.Route[Mathf.Clamp(spawn.NodeIndex, 0, layout.Route.Count - 1)].StandingPosition(halfHeight);
                    checkpoint.Configure(standing);
                    checkpoint.Activated += _ => {
                        PirateAudio.Play(PirateSound.Checkpoint);
                        if (SaveCampaign()) ShowStatus("Lantern lit. Checkpoint saved.", 3f);
                        blackTide?.SetSafeCheckpoint(standing - Vector2.up * (halfHeight + .02f));
                    };
                    break;
                case CampaignLayout.SpawnKind.Upgrade:
                    item.AddComponent<AbilityPickup>().Configure(spawn.Ability);
                    break;
                case CampaignLayout.SpawnKind.Chain:
                case CampaignLayout.SpawnKind.Anchor:
                    item.AddComponent<HookAnchor>().Configure(spawn.Kind == CampaignLayout.SpawnKind.Anchor, spawn.Value);
                    if (spawn.Kind == CampaignLayout.SpawnKind.Chain && spawn.Value > 0f)
                    {
                        PirateWorldArt.Create("Hanging chain links", PirateArtKind.Chain,
                            spawn.Position + Vector2.down * (spawn.Value * .5f), new Vector2(.25f,spawn.Value), item.transform);
                    }
                    break;
                case CampaignLayout.SpawnKind.Cannon:
                    Cannon cannon = item.AddComponent<Cannon>();
                    cannon.Initialize(playerMovement.transform, PirateWorldArt.GetSprite(PirateArtKind.Cannonball));
                    cannon.SetActivationRange(spawn.Value > 0f ? spawn.Value : 18f);
                    break;
                case CampaignLayout.SpawnKind.Spikes:
                    item.AddComponent<InstantKillHazard>().Configure(HazardKind.Spikes);
                    break;
                case CampaignLayout.SpawnKind.Snare:
                    SnareTrap snare = item.AddComponent<SnareTrap>();
                    snare.Configure();
                    float snareTop = item.GetComponent<PirateWorldVisual>().OpaqueWorldBounds.max.y;
                    float? underside = PresentationCeilingAbove(layout, new Vector2(spawn.Position.x, snareTop));
                    if (underside.HasValue) snare.AttachToOverhead(new Vector2(spawn.Position.x, underside.Value));
                    else Debug.LogWarning("PIRATE_SNARE_WITHOUT_SUPPORT position=" + spawn.Position);
                    break;
                case CampaignLayout.SpawnKind.Plant:
                    HangingPlant plant = item.AddComponent<HangingPlant>();
                    plant.Initialize(playerMovement.transform);
                    string plantReceipt = RegisterEnemy(scoreSignature, spawnIndex, true);
                    plant.SetDefeatCallback(() => RecordEnemyDefeat(plantReceipt));
                    break;
                case CampaignLayout.SpawnKind.RopeGate:
                    collider.isTrigger = false;
                    int groundLayer = LayerMask.NameToLayer("Ground");
                    item.layer = groundLayer >= 0 ? groundLayer : 6;
                    item.AddComponent<SaberCuttable>().Configure();
                    break;
                case CampaignLayout.SpawnKind.JumpPad:
                    item.AddComponent<CampaignJumpPad>().Configure(spawn.Value > 0f ? spawn.Value : 18f);
                    break;
            }
        }
        SpawnTreasures();
        if (ChapterIndex == 3)
        {
            GameObject tideObject = new GameObject("Black tide - final chapter pressure");
            runtimeObjects.Add(tideObject);
            blackTide = tideObject.AddComponent<BlackTide>();
            blackTide.Initialize(new Rect(layout.Bounds.x, layout.Bounds.y, layout.Bounds.width, layout.Bounds.height), playerLife, abilities);
        }
        ShowStatus(CampaignChapter.Objectives[ChapterIndex], 5f);
        Debug.Log($"PIRATE_CAMPAIGN_READY: chapter={ChapterIndex}, seed={layout.Seed}, rooms={layout.Rooms.Count(r => r.IsLeaf)}, " +
            $"platforms={layout.Platforms.Count}, strategyGeometryValid={layout.GeometryValid}, signature={layout.Signature()}, treasures={TotalTreasures}, found={CollectedTreasures}");
    }

    private void SpawnTreasures()
    {
        PirateTreasureArtLibrary art = PirateTreasureArtLibrary.Load();
        if (art == null || !art.IsComplete)
            throw new InvalidOperationException("Treasure art has not been imported. Run the campaign build preparation.");
        foreach (PirateTreasureLayout.Item treasure in treasureLayout)
        {
            if (PirateCampaignSession.TreasureLedger.Contains(treasure.Key)) continue;
            GameObject pickup = new GameObject("Treasure " + treasure.Kind + " " + treasure.Key);
            runtimeObjects.Add(pickup);
            pickup.AddComponent<TreasurePickup>().Initialize(this, treasure, art.Get(treasure.Kind));
        }
    }

    private string RegisterEnemy(string signature, int spawnIndex, bool plant)
    {
        string receipt = PirateRunStatistics.EnemyReceipt(ChapterIndex, signature, spawnIndex, plant);
        chapterEnemyReceipts.Add(receipt);
        return receipt;
    }

    private void RecordEnemyDefeat(string receipt)
    {
        if (!initialized || isVictory || transitioning || !chapterEnemyReceipts.Contains(receipt) ||
            !PirateCampaignSession.Statistics.RecordDefeat(receipt)) return;
        SaveCampaign();
    }

    public bool CollectTreasure(string key)
    {
        if (!initialized || isVictory || transitioning || isPaused || upgradeBlocked || Time.timeScale == 0f ||
            !treasureLayout.Any(item => item.Key == key) || !PirateCampaignSession.TreasureLedger.Collect(key)) return false;
        PirateAudio.Play(PirateSound.Pickup);
        int value = PirateTreasureLedger.ValueOf(key);
        if (value == 150) ShowStatus("Relic +150 pts", 1.8f);
        SaveCampaign();
        return true;
    }

    private static float? PresentationSurfaceBelow(CampaignLayout layout, Vector2 point)
    {
        float highest = float.NegativeInfinity;
        foreach (CampaignLayout.Platform platform in layout.Platforms)
            if (point.x >= platform.Bounds.xMin && point.x <= platform.Bounds.xMax &&
                platform.SurfaceY <= point.y+.01f) highest = Mathf.Max(highest,platform.SurfaceY);
        foreach (RectInt solid in layout.Solids)
            if (point.x >= solid.xMin && point.x <= solid.xMax && solid.yMax <= point.y+.01f)
                highest = Mathf.Max(highest,solid.yMax);
        return float.IsNegativeInfinity(highest) ? (float?)null : highest;
    }

    private static float? PresentationCeilingAbove(CampaignLayout layout, Vector2 point)
    {
        float lowest = float.PositiveInfinity;
        foreach (CampaignLayout.Platform platform in layout.Platforms)
            if (point.x >= platform.Bounds.xMin && point.x <= platform.Bounds.xMax && platform.Bounds.yMin >= point.y)
                lowest = Mathf.Min(lowest, platform.Bounds.yMin);
        foreach (RectInt solid in layout.Solids)
            if (point.x >= solid.xMin && point.x <= solid.xMax && solid.yMin >= point.y)
                lowest = Mathf.Min(lowest, solid.yMin);
        return float.IsPositiveInfinity(lowest) ? (float?)null : lowest;
    }

    private static void AnchorPresentationToFloor(GameObject item, CampaignLayout layout)
    {
        float? surface = PresentationSurfaceBelow(layout,item.transform.position);
        if (surface.HasValue) item.GetComponent<PirateWorldVisual>()?.SetGroundSurface(surface.Value);
    }

    private static PirateArtKind ArtFor(CampaignLayout.Spawn spawn)
    {
        switch (spawn.Kind)
        {
            case CampaignLayout.SpawnKind.Checkpoint: return PirateArtKind.Checkpoint;
            case CampaignLayout.SpawnKind.Cannon: return PirateArtKind.Cannon;
            case CampaignLayout.SpawnKind.Chain: return PirateArtKind.Anchor;
            case CampaignLayout.SpawnKind.Anchor: return PirateArtKind.Anchor;
            case CampaignLayout.SpawnKind.Spikes: return PirateArtKind.Spikes;
            case CampaignLayout.SpawnKind.Snare: return PirateArtKind.Snare;
            case CampaignLayout.SpawnKind.Plant: return PirateArtKind.Plant;
            case CampaignLayout.SpawnKind.RopeGate: return PirateArtKind.Rope;
            case CampaignLayout.SpawnKind.JumpPad: return PirateArtKind.JumpPad;
            case CampaignLayout.SpawnKind.Upgrade:
                switch (spawn.Ability)
                {
                    case PirateUpgrade.SpringLeg: return PirateArtKind.SpringLeg;
                    case PirateUpgrade.DoubleJump: return PirateArtKind.DoubleJumpLeg;
                    case PirateUpgrade.Saber1: return PirateArtKind.Saber;
                    case PirateUpgrade.Saber2: return PirateArtKind.SaberUpgrade;
                    case PirateUpgrade.Parrot: return PirateArtKind.Parrot;
                    default: return PirateArtKind.HookUpgrade;
                }
            default: return PirateArtKind.Skull;
        }
    }

    private void HandleUpgrade(PirateUpgrade upgrade)
    {
        PirateCampaignSession.Remember(upgrade);
        string[] descriptions = { "Hook I: hold RMB near a hanging chain.",
            "Hook II: RMB to catch a ring. A/D to swing. Space to leap.",
            "Spring leg: bounce off spikes once. Land safely to recharge.",
            "Leg III: press Space again in the air.",
            "Saber: LMB to cut ropes and strike enemies.",
            "Saber II: hold S + A/D to slide along spikes.",
            "Parrot: Q to scout. WASD to fly. Q to return." };
        ShowStatus(descriptions[(int)upgrade], 6f);
        hud?.ShowUpgrade(upgrade);
        SaveCampaign();
    }

    private void HandleScout(bool active)
    {
        cameraFollow?.SetTarget(active ? abilities.ScoutTransform : playerMovement.transform, false);
        if (active) ShowStatus(blackTide != null ? "Scouting holds the tide. WASD to fly. Q to return. Enemies stay active." :
            "WASD to fly. Q to return. Your pirate is still vulnerable.", 4f);
    }

    public void SetContextHint(string value) => contextualHint = value;

    private void SetupTowerRun()
    {
        TowerRouteLayout route = generator.TowerRoute;
        float halfHeight = playerMovement.GetComponent<Collider2D>().bounds.extents.y;
        Vector3 startPosition = route.Steps[0].StandingPosition(halfHeight);
        Vector3 finishPosition = route.Steps[route.Steps.Count - 1].StandingPosition(halfHeight);
        startHeight = startPosition.y;
        finishHeight = finishPosition.y;
        MovePlayerToStart(startPosition);
        SpawnGoal(finishPosition + Vector3.up * 0.35f);

        for (int i = 8; i < route.Steps.Count - 1; i += 8)
            SpawnCheckpoint(route.Steps[i].StandingPosition(halfHeight));
        SpawnAbilityPickup(route.Steps[route.Steps.Count / 3].StandingPosition(halfHeight) + Vector2.up * 0.2f);

        for (int i = 0; i < route.Steps.Count - 4; i += 8)
        {
            var step = route.Steps[i];
            var chain = CreateMarker($"Chain {i}", new Vector3(step.CenterX, step.SurfaceY + 4.2f, 0f),
                new Color(0.85f, 0.68f, 0.2f), new Vector2(0.16f, 0.8f));
            chain.AddComponent<HookAnchor>();
        }

        for (int i = 12; i < route.Steps.Count - 4; i += 12)
        {
            var step = route.Steps[i];
            var hazard = CreateMarker($"Side Spikes {i}", new Vector3(2f, step.SurfaceY - 0.25f, 0f),
                new Color(0.9f, 0.22f, 0.16f), new Vector2(1f, 0.4f));
            hazard.AddComponent<InstantKillHazard>();
            var cannonObject = CreateMarker($"Side Cannon {i}", new Vector3(route.Width - 2f, step.SurfaceY + 0.5f, 0f),
                new Color(0.16f, 0.18f, 0.24f), new Vector2(0.8f, 0.6f));
            Cannon cannon = cannonObject.AddComponent<Cannon>();
            cannon.Initialize(playerMovement.transform, GetMarkerSprite());
            cannon.SetActivationRange(3f);
        }

        ShowStatus("Climb the wooden ledges. Hold Space to jump higher.", 6f);
        Debug.Log($"PIRATE_TOWER_READY: seed={generator.CurrentSeed}, sections={route.SectionCount}, " +
            $"landings={route.Steps.Count - 1}, safeJump={route.SafeJumpHeight:F2}, start={startPosition}");
    }

    private void MovePlayerToStart(Vector3 position)
    {
        playerMovement.transform.position = position;
        playerMovement.GetComponent<Rigidbody2D>().position = position;
        Physics2D.SyncTransforms();
        playerMovement.ResetMotion();
        playerMovement.SetControlsEnabled(true);
        playerLife.SetCheckpoint(position);
        playerLife.SetKillHeight(position.y - 12f);

        if (cameraFollow != null)
        {
            cameraFollow.SetTarget(playerMovement.transform, true);
        }
    }

    private void SpawnGoal(Vector3 position)
    {
        GameObject goalObject = PirateWorldArt.Create(ChapterIndex == 3 ? "Pirate King" : "Passage to next location",
            chapter == null || ChapterIndex == 3 ? PirateArtKind.PirateKing : PirateArtKind.Door,
            position, new Vector2(1.7f,2.1f));
        if (generator != null && generator.Campaign != null) AnchorPresentationToFloor(goalObject,generator.Campaign);
        runtimeObjects.Add(goalObject);
        BoxCollider2D collider = goalObject.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(1.4f,2f); collider.isTrigger = true;

        goal = goalObject.AddComponent<Goal>();
        goal.Reached += CompleteRun;
    }

    private void SpawnCheckpoint(Vector3 position)
    {
        GameObject checkpointObject = CreateMarker(
            "Checkpoint",
            position,
            new Color(0.2f, 0.75f, 1f, 0.75f),
            new Vector2(0.65f, 1.8f));

        Checkpoint checkpoint = checkpointObject.AddComponent<Checkpoint>();
        checkpoint.Activated += _ => ShowStatus("Checkpoint saved.", 2.5f);
    }

    private void SpawnAbilityPickup(Vector3 position)
    {
        GameObject pickupObject = CreateMarker(
            "Upgrade - Double Jump",
            position,
            new Color(0.85f, 0.35f, 1f, 0.95f),
            Vector2.one * 0.75f);

        AbilityPickup pickup = pickupObject.AddComponent<AbilityPickup>();
        pickup.Collected += () => ShowStatus("Leg upgraded: double jump!", 3f);
    }

    private void SpawnRoomHazards(IReadOnlyList<Room> rooms)
    {
        for (int index = 2; index < rooms.Count - 1; index += 4)
        {
            Room room = rooms[index];
            float x = index % 2 == 0 ? room.bounds.xMin + 2.5f : room.bounds.xMax - 2.5f;
            float y = room.bounds.yMin + 0.8f;

            GameObject hazard = CreateMarker(
                $"Spike Trap {index}",
                new Vector3(x, y, 0f),
                new Color(0.95f, 0.2f, 0.18f, 0.95f),
                new Vector2(1.35f, 0.45f));

            hazard.AddComponent<InstantKillHazard>();
        }
    }

    private void SpawnHookAnchors(IReadOnlyList<Room> rooms)
    {
        for (int index = 0; index < rooms.Count - 1; index += 3)
        {
            Room room = rooms[index];
            float anchorY = index == 0 ? room.bounds.yMin + 4.5f : room.bounds.yMax - 2f;
            Vector3 position = new Vector3(room.bounds.center.x, anchorY, 0f);
            GameObject anchor = CreateMarker(
                $"Hanging Chain {index}",
                position,
                new Color(0.78f, 0.63f, 0.3f, 0.95f),
                new Vector2(0.28f, 1.4f));

            anchor.AddComponent<HookAnchor>();
        }
    }

    private void SpawnCannons(IReadOnlyList<Room> rooms)
    {
        for (int index = 3; index < rooms.Count - 1; index += 5)
        {
            Room room = rooms[index];
            bool placeOnLeft = index % 2 == 1;
            float x = placeOnLeft ? room.bounds.xMin + 1.5f : room.bounds.xMax - 1.5f;
            float y = room.bounds.yMin + 1.35f;

            GameObject cannonObject = CreateMarker(
                $"Cannon {index}",
                new Vector3(x, y, 0f),
                new Color(0.16f, 0.18f, 0.24f, 1f),
                new Vector2(0.95f, 0.75f));

            Cannon cannon = cannonObject.AddComponent<Cannon>();
            cannon.Initialize(playerMovement.transform, GetMarkerSprite());
        }
    }

    private GameObject CreateMarker(string objectName, Vector3 position, Color color, Vector2 size)
    {
        GameObject marker = new GameObject(objectName);
        marker.transform.position = position;
        marker.transform.localScale = new Vector3(size.x, size.y, 1f);

        SpriteRenderer spriteRenderer = marker.AddComponent<SpriteRenderer>();
        spriteRenderer.sprite = GetMarkerSprite();
        spriteRenderer.color = color;
        spriteRenderer.sortingOrder = 20;

        BoxCollider2D collider = marker.AddComponent<BoxCollider2D>();
        collider.isTrigger = true;

        runtimeObjects.Add(marker);
        return marker;
    }

    private Sprite GetMarkerSprite()
    {
        if (markerSprite != null)
        {
            return markerSprite;
        }

        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            name = "Runtime Marker Texture",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        markerSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        markerSprite.name = "Runtime Marker Sprite";
        markerSprite.hideFlags = HideFlags.HideAndDontSave;
        return markerSprite;
    }

    public void RestartWithSeed(int requestedSeed)
    {
        if (transitioning) return;
        hud?.ClearUpgrade();
        playerLife?.SetExitProtected(false);
        isVictory = false;
        transitioning = false;
        if (chapter != null)
        {
            PirateCampaignSession.Begin(requestedSeed);
            SetPaused(false);
            if (ChapterIndex != 0)
            {
                transitioning = true;
                SceneManager.LoadScene(CampaignChapter.SceneNames[0]);
                return;
            }
            PirateCampaignSession.Restore(abilities);
            playerLife.SetDeathCount(0);
        }
        SetPaused(false);

        if (generator != null)
        {
            generator.GenerateWithSeed(requestedSeed);
        }

        SetupRunObjects();
        SaveCampaign();
    }

    private void CompleteRun()
    {
        if (transitioning || isVictory || playerLife.IsRespawning) return;
        if (chapter != null && ChapterIndex < 3)
        {
            StartCoroutine(AdvanceChapter());
            return;
        }
        isVictory = true;
        FreezeAtExit();
        if (goal != null) cameraFollow?.SetTarget(goal.transform,true);
        ShowStatus("Letter delivered! The king closes the sea gates. The tide falls.", 10f);
        SaveCampaign();
    }

    private void FreezeAtExit()
    {
        blackTide?.StopAndDrain();
        playerLife.SetExitProtected(true);
        hud?.ClearUpgrade();
        abilities?.Scout.ReturnToPirate();
        playerMovement.ResetMotion();
        ApplyControlState();
    }

    private IEnumerator AdvanceChapter()
    {
        transitioning = true;
        FreezeAtExit();
        if (abilities != null)
            foreach (PirateUpgrade upgrade in abilities.CaptureProgression()) PirateCampaignSession.Remember(upgrade);
        ShowStatus("Path unlocked. " + CampaignChapter.Titles[ChapterIndex + 1], 3f);
        PirateAudio.Play(PirateSound.Door);
        PirateCampaignSession.Deaths = playerLife.DeathCount;
        SaveCampaign(ChapterIndex + 1);
        yield return new WaitForSecondsRealtime(1.0f);
        PirateCampaignSession.Advance(ChapterIndex + 1);
        AsyncOperation load = SceneManager.LoadSceneAsync(CampaignChapter.SceneNames[ChapterIndex + 1]);
        while (!load.isDone) yield return null;
    }

    public void CompleteChapterForIntegrationTest()
    {
        if (Environment.GetCommandLineArgs().Contains("-pirateQuestCampaignTest")) CompleteRun();
    }

    private void SetPaused(bool value)
    {
        isPaused = value;
        ApplyControlState();
    }

    private void ApplyControlState()
    {
        bool frozen = isPaused || isVictory || transitioning || upgradeBlocked;
        Time.timeScale = frozen ? 0f : 1f;
        if (playerMovement != null)
        {
            playerMovement.SetModalInputBlocked(upgradeBlocked);
            if (!upgradeBlocked || isPaused || isVictory || transitioning)
                playerMovement.SetControlsEnabled(!frozen && (abilities == null || !abilities.IsScouting));
        }
    }

    private void HandleDeath()
    {
        PirateCampaignSession.Deaths = playerLife.DeathCount;
        SaveCampaign();
        ShowStatus("Back to the last lantern...", 2f);
    }

    private void HandleRespawn()
    {
        if (isVictory || transitioning)
        {
            FreezeAtExit();
            return;
        }
        if (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking) SetMenuBlocked(true);
        ApplyControlState();
        ShowStatus("Try again!", 1.5f);
    }

    private void ShowStatus(string message, float duration)
    {
        statusMessage = message;
        statusMessageUntil = Time.unscaledTime + duration;
    }

    private void ClearRuntimeObjects()
    {
        blackTide = null;
        if (goal != null)
        {
            goal.Reached -= CompleteRun;
            goal = null;
        }

        foreach (GameObject runtimeObject in runtimeObjects)
        {
            if (runtimeObject != null)
            {
                runtimeObject.SetActive(false);
                Destroy(runtimeObject);
            }
        }

        runtimeObjects.Clear();
        treasureLayout.Clear();
        chapterEnemyReceipts.Clear();
        autosaveElapsed = 0f;
    }

    private static Vector3 ToWorldPosition(Vector2Int gridPosition, float verticalOffset)
    {
        return new Vector3(gridPosition.x + 0.5f, gridPosition.y + verticalOffset, 0f);
    }

    private float halfHeightForCheckpoint() => playerMovement.transform.position.y - playerMovement.GetComponent<Collider2D>().bounds.min.y;

    private void OnGUI()
    {
        if (!initialized)
        {
            return;
        }

        EnsureGuiStyles();
        if (PirateFrontEnd.Instance != null && PirateFrontEnd.Instance.IsBlocking) return;
        if (chapter != null)
        {
            DrawCampaignHud();
            return;
        }
        float progress = Mathf.Clamp01(Mathf.InverseLerp(startHeight, finishHeight, playerMovement.transform.position.y));
        int seed = generator != null ? generator.CurrentSeed : 20260918;

        GUI.Box(new Rect(16f, 16f, 650f, 116f), string.Empty);
        GUI.Label(new Rect(30f, 22f, 620f, 24f), $"PIRATE QUEST    {progress * 100f:0}%    Deaths: {playerLife.DeathCount}    Seed: {seed}", hudStyle);
        GUI.Label(new Rect(30f, 49f, 620f, 24f), "A/D Move   Hold Space Jump   Shift Dash", hudStyle);
        GUI.Label(new Rect(30f, 75f, 620f, 24f), "Hold RMB Hook   A/D Swing   Space Leap", hudStyle);
        GUI.Label(new Rect(30f, 101f, 620f, 24f), "R Restart   N New tower   Esc Pause", hudStyle);

        if (Time.unscaledTime < statusMessageUntil && !string.IsNullOrWhiteSpace(statusMessage))
        {
            GUI.Box(new Rect(Screen.width * 0.5f - 320f, Screen.height - 80f, 640f, 60f), string.Empty);
            GUI.Label(new Rect(Screen.width * 0.5f - 305f, Screen.height - 75f, 610f, 50f), statusMessage, centeredStyle);
        }

        if (isPaused)
        {
            DrawCenterOverlay("PAUSED", "Esc to resume");
        }
        else if (isVictory)
        {
            DrawCenterOverlay("LETTER DELIVERED", "R to restart. N for a new tower.");
        }
    }

    public void SetMenuBlocked(bool blocked)
    {
        isPaused = blocked;
        ApplyControlState();
    }

    public void RestartFromCheckpoint()
    {
        if (playerLife == null || isVictory || transitioning) return;
        hud?.ClearUpgrade();
        abilities?.Scout.ReturnToPirate();
        playerLife.RespawnImmediately();
        playerMovement.SetControlsEnabled(!isPaused && !isVictory && !transitioning);
        cameraFollow?.SetTarget(playerMovement.transform,true);
        ShowStatus("Back to the last lantern.",2f);
    }

    public bool SaveCampaign(int nextChapter = -1)
    {
        if (PirateFrontEnd.IsAutomationRun || !initialized || chapter == null || abilities == null ||
            PirateFrontEnd.Instance == null || !PirateFrontEnd.Instance.HasPlayableRun) return true;
        if (generator.Campaign.Seed != PirateCampaignSession.Seed) return true;
        if (transitioning && nextChapter < 0 && ChapterIndex < 3) nextChapter = ChapterIndex + 1;
        Vector3 checkpoint = playerLife.CheckpointPosition;
        PirateCampaignSession.Deaths = playerLife.DeathCount;
        PirateSaveData save = new PirateSaveData {
            seed = PirateCampaignSession.Seed,
            chapter = nextChapter >= 0 ? nextChapter : ChapterIndex,
            upgrades = abilities.CaptureProgression().Select(value => (int)value).ToArray(),
            hasCheckpoint = nextChapter < 0,
            checkpointX = checkpoint.x, checkpointY = checkpoint.y,
            deaths = playerLife.DeathCount,
            layoutSignature = nextChapter < 0 ? generator.Campaign.Signature() : string.Empty,
            completed = isVictory,
            treasureReceipts = PirateCampaignSession.TreasureLedger.Capture(),
            introSeen = PirateCampaignSession.IntroSeen
        };
        PirateCampaignSession.Statistics.CaptureInto(save);
        bool saved = PirateSaveStore.Write(save);
        saveWarning = saved ? null : "Save failed. Keep the game open; retry from Pause.";
        return saved;
    }

    private void RestoreSavedCheckpoint()
    {
        PirateSaveData saved = PirateCampaignSession.ConsumeRestore();
        if (saved == null) return;
        if (saved.hasCheckpoint)
        {
            CampaignLayout layout = generator.Campaign;
            Vector2 point = new Vector2(saved.checkpointX,saved.checkpointY);
            Collider2D playerCollider = playerMovement.GetComponent<Collider2D>();
            bool inBounds = point.x > layout.Bounds.xMin && point.x < layout.Bounds.xMax &&
                point.y > layout.Bounds.yMin && point.y < layout.Bounds.yMax;
            bool matchingMap = string.Equals(saved.layoutSignature,layout.Signature(),StringComparison.Ordinal);
            bool noWall = Physics2D.OverlapBox(point,playerCollider.bounds.size*.8f,0f,playerMovement.GroundLayer) == null;
            RaycastHit2D support = Physics2D.Raycast(point,Vector2.down,playerCollider.bounds.extents.y+.4f,playerMovement.GroundLayer);
            if (inBounds && matchingMap && noWall && support.collider != null)
            {
                MovePlayerToStart(point);
                ShowStatus("Expedition resumed at the last lantern.",4f);
                Debug.Log("PIRATE_SAVE_RESTORED: chapter="+ChapterIndex+" checkpoint="+point);
            }
            else
            {
                ShowStatus("Map updated. Back at the chapter entrance. Gear kept.",5f);
                Debug.LogWarning($"PIRATE_SAVE_CHECKPOINT_FALLBACK bounds={inBounds} signature={matchingMap} clear={noWall} support={support.collider!=null}");
            }
        }

        if (saved.completed && ChapterIndex == 3)
        {
            isVictory = true;
            FreezeAtExit();
            if (goal != null) cameraFollow?.SetTarget(goal.transform,true);
            ShowStatus("Expedition complete. Your letter has reached the king.",10f);
            Debug.Log("PIRATE_SAVE_COMPLETION_RESTORED: chapter="+ChapterIndex+" protected="+playerLife.IsExitProtected);
        }
    }

    private void DrawCampaignHud()
    {
        if (transitioning) DrawCenterOverlay("NEXT CHAPTER", "Climbing higher...");
        else if (isPaused) DrawCenterOverlay("PAUSED", "Esc to resume. R to restart. N for a new seed.");
        else if (isVictory) PirateScorePresentation.Draw(PirateCampaignSession.Statistics,
            PirateCampaignSession.TreasureLedger, playerLife.DeathCount);
        else if (Time.unscaledTime < chapterTitleUntil)
        {
            Color previous = GUI.color;
            GUI.color = new Color(1f,1f,1f,Mathf.Clamp01(chapterTitleUntil-Time.unscaledTime));
            GUI.Label(new Rect(80f,Screen.height*.22f,Screen.width-160f,50f),CampaignChapter.Titles[ChapterIndex],titleStyle);
            GUI.color = previous;
        }
    }

    private void DrawCenterOverlay(string title, string subtitle)
    {
        Rect panel = new Rect(Screen.width * 0.5f - 300f, Screen.height * 0.5f - 108f, 600f, 216f);
        GUI.Box(panel, string.Empty);
        GUI.Label(new Rect(panel.x + 20f, panel.y + 35f, panel.width - 40f, 50f), title, titleStyle);
        GUI.Label(new Rect(panel.x + 20f, panel.y + 96f, panel.width - 40f, 96f), subtitle, centeredStyle);
    }

    private void EnsureGuiStyles()
    {
        if (hudStyle != null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = Color.white }
        };
        PirateFrontEnd.KeepLabelStatesIdentical(hudStyle);

        titleStyle = new GUIStyle(hudStyle)
        {
            fontSize = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        centeredStyle = new GUIStyle(hudStyle)
        {
            fontSize = 18,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true
        };
    }
}
