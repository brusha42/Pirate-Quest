using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.InputSystem;

public static class CourseworkVerification
{
    private const string MainScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PirateSpritePath = "Assets/Sprites/Characters/pirate-idle-v1.png";
    private const string TileSpriteFolder = "Assets/Sprites/Tiles";

    public static void ConfigurePirateSprite()
    {
        try
        {
            AssetDatabase.ImportAsset(PirateSpritePath, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(PirateSpritePath) as TextureImporter;
            Require(importer != null, "Не удалось получить TextureImporter для спрайта пирата.");

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 1000f;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TileSpriteFolder }))
            {
                string tileTexturePath = AssetDatabase.GUIDToAssetPath(guid);
                TextureImporter tileImporter = AssetImporter.GetAtPath(tileTexturePath) as TextureImporter;

                if (tileImporter == null)
                {
                    continue;
                }

                tileImporter.mipmapEnabled = false;
                tileImporter.filterMode = FilterMode.Point;
                tileImporter.wrapMode = TextureWrapMode.Clamp;
                tileImporter.textureCompression = TextureImporterCompression.Uncompressed;
                tileImporter.SaveAndReimport();
            }

            Sprite pirateSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PirateSpritePath);
            Require(pirateSprite != null, "Спрайт пирата не импортировался как Sprite.");

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            Require(player != null, "В сцене не найден игрок для назначения спрайта.");

            SpriteRenderer renderer = player.GetComponent<SpriteRenderer>();
            Require(renderer != null, "У игрока отсутствует SpriteRenderer.");
            renderer.sprite = pirateSprite;
            renderer.sortingOrder = 10;
            EditorUtility.SetDirty(renderer);
            EditorSceneManager.MarkSceneDirty(renderer.gameObject.scene);
            EditorSceneManager.SaveScene(renderer.gameObject.scene);

            Debug.Log("COURSEWORK_PIRATE_SPRITE_SUCCESS: " + PirateSpritePath);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("COURSEWORK_PIRATE_SPRITE_FAILED: " + exception.Message);
            EditorApplication.Exit(1);
        }
    }

    public static void VerifyProject()
    {
        try
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            VerifySceneConfiguration();
            VerifyGeneratorSeeds();
            Debug.Log("COURSEWORK_VERIFY_SUCCESS: конфигурация сцены и генератор прошли проверку.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("COURSEWORK_VERIFY_FAILED: " + exception.Message);
            EditorApplication.Exit(1);
        }
    }

    public static void PrepareVerifyAndBuild()
    {
        try
        {
            PirateAnimationImporter.ImportAndVerify();
            PirateAnimationPreviewExporter.ExportRunPreview();
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            VerifySceneConfiguration();
            VerifyGeneratorSeeds();
            Debug.Log("COURSEWORK_VERIFY_SUCCESS: surface geometry and full layout seed determinism.");
            BuildWindowsDevelopment();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void BuildWindowsDevelopment()
    {
        const string buildPath = "Builds/Windows/PirateQuest.exe";

        try
        {
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = buildPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows build failed: {report.summary.result}, errors={report.summary.totalErrors}");
            }

            Debug.Log(
                $"COURSEWORK_BUILD_SUCCESS: path={buildPath}, " +
                $"size={report.summary.totalSize}, warnings={report.summary.totalWarnings}");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("COURSEWORK_BUILD_FAILED: " + exception.Message);
            EditorApplication.Exit(1);
        }
    }

    private static void VerifySceneConfiguration()
    {
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        Require(player != null, "В основной сцене отсутствует PlayerMovement.");
        Require(player.GetComponent<Rigidbody2D>() != null, "У игрока отсутствует Rigidbody2D.");
        Require(player.GetComponent<Collider2D>() != null, "У игрока отсутствует Collider2D.");

        SpriteRenderer playerRenderer = player.GetComponent<SpriteRenderer>();
        Require(playerRenderer != null && playerRenderer.sprite != null, "У игрока отсутствует спрайт.");
        Require(AssetDatabase.GetAssetPath(playerRenderer.sprite) == PirateSpritePath,
            "Игроку назначен временный спрайт вместо ассета пирата.");

        PlayerInput playerInput = player.GetComponent<PlayerInput>();
        Require(playerInput != null && playerInput.actions != null, "У игрока не настроен PlayerInput.");
        Require(playerInput.actions.FindAction("Move") != null, "В Input Actions отсутствует Move.");
        Require(playerInput.actions.FindAction("Jump") != null, "В Input Actions отсутствует Jump.");
        Require(playerInput.actions.FindAction("Sprint") != null, "В Input Actions отсутствует Sprint для рывка.");
        Require(playerInput.actions.FindAction("Interact") != null, "В Input Actions отсутствует Interact для крюка.");
        Require(playerInput.actions.FindAction("Attack") != null, "В Input Actions отсутствует Attack для крюка мышью.");

        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TileSpriteFolder }))
        {
            string tileTexturePath = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter tileImporter = AssetImporter.GetAtPath(tileTexturePath) as TextureImporter;
            Require(tileImporter != null && tileImporter.filterMode == FilterMode.Point && !tileImporter.mipmapEnabled,
                $"Текстура {tileTexturePath} должна использовать Point-фильтрацию без mip maps.");
        }

        Require(UnityEngine.Object.FindFirstObjectByType<CameraFollow>() != null, "В сцене отсутствует CameraFollow.");
        Require(UnityEngine.Object.FindFirstObjectByType<LevelGenerator>() != null, "В сцене отсутствует LevelGenerator.");
    }

    private static void VerifyGeneratorSeeds()
    {
        LevelGenerator generator = UnityEngine.Object.FindFirstObjectByType<LevelGenerator>();
        int[] seeds = { 20260918, 7, 42, 2026, 18092026, -13579, 0, int.MaxValue, int.MinValue, 918 };

        foreach (int seed in seeds)
        {
            generator.GenerateWithSeed(seed);
            List<Room> firstRooms = generator.GetAllRooms();
            string firstSnapshot = CreateSnapshot(firstRooms) + CreateRouteSnapshot(generator);

            Require(firstRooms.Count >= 2, $"Seed {seed}: создано недостаточно комнат ({firstRooms.Count}).");
            Require(IsConnected(firstRooms), $"Seed {seed}: граф комнат не связен.");
            Require(generator.AllRoomsValidated,
                $"Seed {seed}: проверено {generator.ValidatedRoomCount} из {generator.RoomCount} комнат.");
            Require(generator.GuaranteedRoutesValid && generator.GuaranteedRouteRoomCount == generator.RoomCount,
                $"Seed {seed}: не построен гарантированный маршрут через каждую комнату.");

            float verticalSpan = firstRooms.Max(room => room.bounds.center.y) - firstRooms.Min(room => room.bounds.center.y);
            Require(verticalSpan >= 10f, $"Seed {seed}: башня почти не имеет вертикального подъёма ({verticalSpan:0.0}).");

            generator.GenerateWithSeed(seed);
            string secondSnapshot = CreateSnapshot(generator.GetAllRooms()) + CreateRouteSnapshot(generator);
            Require(firstSnapshot == secondSnapshot, $"Seed {seed}: повторная генерация дала другой результат.");

            Debug.Log(
                $"COURSEWORK_SEED_OK: seed={seed}, rooms={generator.RoomCount}, " +
                $"validated={generator.ValidatedRoomCount}, routes={generator.GuaranteedRouteRoomCount}, " +
                $"size={generator.LevelSize.x}x{generator.LevelSize.y}");
        }
    }

    private static string CreateRouteSnapshot(LevelGenerator generator)
    {
        Require(generator.TowerRoute != null, "Main scene must use the route-first tower generator.");
        Require(generator.TowerRoute.GeometryValid, "Tower surface geometry exceeds jump limits.");
        return string.Join("|", generator.TowerRoute.Steps.Select(step =>
            $"{step.Tiles.x},{step.Tiles.y},{step.Tiles.width},{step.Tiles.height}"));
    }

    private static bool IsConnected(IReadOnlyList<Room> rooms)
    {
        if (rooms.Count == 0)
        {
            return false;
        }

        HashSet<Room> visited = new HashSet<Room>();
        Queue<Room> queue = new Queue<Room>();
        queue.Enqueue(rooms[0]);
        visited.Add(rooms[0]);

        while (queue.Count > 0)
        {
            Room current = queue.Dequeue();
            foreach (Room connected in current.connectedRooms)
            {
                if (visited.Add(connected))
                {
                    queue.Enqueue(connected);
                }
            }
        }

        return visited.Count == rooms.Count;
    }

    private static string CreateSnapshot(IEnumerable<Room> rooms)
    {
        return string.Join(
            "|",
            rooms.Select(room => string.Format(
                CultureInfo.InvariantCulture,
                "{0:R},{1:R},{2:R},{3:R}",
                room.bounds.x,
                room.bounds.y,
                room.bounds.width,
                room.bounds.height)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
