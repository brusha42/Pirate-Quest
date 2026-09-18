using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CampaignBuildVerification
{
    public static void PrepareVerifyAndBuild()
    {
        try
        {
            VerifyToolingImports();
            PirateAnimationImporter.ImportAndVerify();
            PirateWorldArtImporter.ImportAndVerify();
            BrineCrawlerArtImporter.ImportAndVerify();
            PirateTreasureArtImporter.ImportAndVerify();
            PirateAudioPreviewExporter.Export();
            VerifyRecordedMusicImports();
            PrepareBranding();
            PrepareScenes();
            VerifySeededLayouts();
            Require(PirateTreasureRegression.Verify(out string treasureDetail), "Treasure contracts: " + treasureDetail);
            Debug.Log("PIRATE_TREASURE_DATA_SUCCESS " + treasureDetail);
            PreserveLocalServicesSetting();
            string[] scenes = CampaignChapter.SceneNames.Select(name => "Assets/Scenes/Campaign/"+name+".unity").ToArray();
            BuildOptions options = Environment.GetCommandLineArgs().Contains("-pirateDevelopmentBuild") ? BuildOptions.Development : BuildOptions.None;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes,
                locationPathName = "Builds/Windows/PirateQuest.exe",
                target = BuildTarget.StandaloneWindows64,
                options = options
            });
            Require(report.summary.result == BuildResult.Succeeded,"Campaign Windows build failed");
            PreserveLocalServicesSetting();
            Debug.Log($"PIRATE_CAMPAIGN_BUILD_SUCCESS scenes=4 size={report.summary.totalSize} warnings={report.summary.totalWarnings}");
            BuildMacPlayerIfSupported(scenes, options);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("PIRATE_CAMPAIGN_BUILD_FAILED: "+exception.Message);
            EditorApplication.Exit(1);
        }
    }

    public static void PrepareVerifyAndBuildMac()
    {
        try
        {
            PrepareBranding();
            PreserveLocalServicesSetting();
            string[] scenes = CampaignChapter.SceneNames.Select(name => "Assets/Scenes/Campaign/"+name+".unity").ToArray();
            foreach (string scene in scenes)
                Require(File.Exists(scene), "Missing campaign scene " + scene);
            BuildMacPlayerIfSupported(scenes, BuildOptions.None);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError("PIRATE_CAMPAIGN_BUILD_FAILED: "+exception.Message);
            EditorApplication.Exit(1);
        }
    }

    private static void PreserveLocalServicesSetting()
    {
        UnityEngine.Object settings = Unsupported.GetSerializedAssetInterfaceSingleton("UnityConnectSettings");
        Require(settings != null, "Live UnityConnect project settings are unavailable.");
        using (var serialized = new SerializedObject(settings))
        {
            SerializedProperty enabled = serialized.FindProperty("m_Enabled");
            Require(enabled != null && enabled.propertyType == SerializedPropertyType.Boolean,
                "UnityConnect root enabled flag is unavailable.");
            enabled.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
            Require(!serialized.FindProperty("m_Enabled").boolValue, "UnityConnect live setting was not preserved.");
        }
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        string saved = File.ReadAllText("ProjectSettings/UnityConnectSettings.asset");
        var rootFlags = System.Text.RegularExpressions.Regex.Matches(saved, @"(?m)^  m_Enabled: [01]\r?$");
        Require(saved.Contains("--- !u!310 &1") && saved.Contains("UnityConnectSettings:") && rootFlags.Count == 1,
            "Unexpected UnityConnect project file format; refusing to rewrite it.");
        if (System.Text.RegularExpressions.Regex.IsMatch(saved, @"(?m)^  m_Enabled: 1\r?$"))
        {
            UnityEditorInternal.InternalEditorUtility.SaveToSerializedFileAndForget(new[] { settings },
                "ProjectSettings/UnityConnectSettings.asset", true);
            saved = File.ReadAllText("ProjectSettings/UnityConnectSettings.asset");
        }
        Require(System.Text.RegularExpressions.Regex.IsMatch(saved, @"(?m)^  m_Enabled: 0\r?$"),
            "UnityConnect project file was not saved with the original disabled setting.");
        using (var verified = new SerializedObject(settings))
            Require(!verified.FindProperty("m_Enabled").boolValue, "UnityConnect live setting changed while saving.");
        Debug.Log("PIRATE_LOCAL_SERVICES_PRESERVED rootEnabled=False accountPreferencesChanged=False");
    }

    private static void VerifyToolingImports()
    {
        const string root = "Packages/com.unity.2d.tooling/Editor/Insider/SpriteAtlas/SpriteAtlasIssueReport/";
        string[] names = { "SpriteAtlasTextureSpaceUsedIssue/SpriteAtlasTextureSpaceUsedIssueSettings",
            "SourceTextureWithCompressionIssue/SourceTextureWithCompressionIssue" };
        foreach (string name in names)
        foreach (string extension in new[] { ".uss", ".uxml" })
        {
            string path = root + name + extension;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Require(AssetDatabase.LoadMainAssetAtPath(path) != null, "Bundled 2D tooling asset cannot be loaded: " + path);
        }
        Debug.Log("PIRATE_TOOLING_IMPORT_SUCCESS assets=4 project=" + Application.dataPath);
    }

    private static void VerifyRecordedMusicImports()
    {
        string[] paths =
        {
            "Assets/Resources/Audio/PirateMenu.mp3",
            "Assets/Resources/Audio/PirateLevelEnergetic.mp3",
            "Assets/Resources/Audio/PirateLevelTense.mp3",
            "Assets/Resources/Audio/PirateLevelEnergy.wav",
            "Assets/Resources/Audio/PirateLevelWave.wav",
            "Assets/Resources/Audio/PirateLevelDark.wav"
        };
        AssetDatabase.Refresh();
        foreach (string path in paths)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            Require(clip != null && clip.length > 10f, "Recorded music missing or too short: " + path);
        }
        Debug.Log("PIRATE_RECORDED_MUSIC_IMPORT_SUCCESS clips=6");
    }

    private static void ConfigureMacArchitecture()
    {
        NamedBuildTarget standalone = NamedBuildTarget.Standalone;
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.SetArchitecture(standalone, 2);
        string platform = BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX);
        EditorUserBuildSettings.SetPlatformSettings(platform, "Architecture", "x64arm64");
        int architecture = PlayerSettings.GetArchitecture(standalone);
        string platformArchitecture = EditorUserBuildSettings.GetPlatformSettings(platform, "Architecture");
        Debug.Log($"PIRATE_MAC_ARCHITECTURE playerSettings={architecture} platform={platformArchitecture}");
        if (architecture != 2 &&
            !string.Equals(platformArchitecture, "x64arm64", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(platformArchitecture, "OSXUniversal", StringComparison.OrdinalIgnoreCase))
        {
            PlayerSettings.SetArchitecture(standalone, 1);
            EditorUserBuildSettings.SetPlatformSettings(platform, "Architecture", "arm64");
            Debug.LogWarning("PIRATE_MAC_ARCHITECTURE_FALLBACK ARM64 playerSettings=" +
                PlayerSettings.GetArchitecture(standalone) + " platform=" +
                EditorUserBuildSettings.GetPlatformSettings(platform, "Architecture"));
        }
    }

    private static void BuildMacPlayerIfSupported(string[] scenes, BuildOptions options)
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX))
        {
            Debug.LogWarning("PIRATE_CAMPAIGN_BUILD_MAC_SKIPPED StandaloneOSX module is not installed in this Editor.");
            return;
        }
        ConfigureMacArchitecture();
        const string macPath = "../campaign-macos-fixed/PirateQuest.app";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(macPath)));
        if (Directory.Exists(macPath))
            Directory.Delete(macPath, true);
        BuildReport mac = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenes,
            locationPathName = macPath,
            target = BuildTarget.StandaloneOSX,
            options = options
        });
        Require(mac.summary.result == BuildResult.Succeeded, "Campaign macOS build failed");
        Debug.Log($"PIRATE_CAMPAIGN_BUILD_MAC_SUCCESS size={mac.summary.totalSize} warnings={mac.summary.totalWarnings} path={Path.GetFullPath(macPath)}");
    }

    private static void PrepareBranding()
    {
        const string iconPath = "Assets/Sprites/World/pirate-game-icon.png";
        AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(iconPath) as TextureImporter;
        Require(importer != null, "Game icon missing");
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
        int[] sizes = PlayerSettings.GetIconSizes(UnityEditor.Build.NamedBuildTarget.Standalone, IconKind.Any);
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Standalone, sizes.Select(_ => icon).ToArray(), IconKind.Any);
        PlayerSettings.bundleVersion = "0.7.0";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        Debug.Log($"PIRATE_BRANDING_SUCCESS iconSizes={sizes.Length} version={PlayerSettings.bundleVersion} fullscreenMode={PlayerSettings.fullScreenMode}");
    }

    public static void PrepareScenes()
    {
        const string folder = "Assets/Scenes/Campaign";
        Directory.CreateDirectory(folder);
        AssetDatabase.Refresh();
        for (int chapterIndex = 0; chapterIndex < 4; chapterIndex++)
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity",OpenSceneMode.Single);
            LevelGenerator generator = UnityEngine.Object.FindFirstObjectByType<LevelGenerator>();
            Require(generator != null,"Generator missing in template scene");
            generator.ClearLevel();
            CampaignChapter chapter = UnityEngine.Object.FindFirstObjectByType<CampaignChapter>();
            if (chapter == null) chapter = new GameObject("Campaign Chapter").AddComponent<CampaignChapter>();
            chapter.chapterIndex = chapterIndex;
            string path = folder+"/"+CampaignChapter.SceneNames[chapterIndex]+".unity";
            Require(EditorSceneManager.SaveScene(scene,path),"Cannot save chapter scene "+path);
            Debug.Log("PIRATE_CAMPAIGN_SCENE_SAVED: "+path);
        }
        EditorBuildSettings.scenes = CampaignChapter.SceneNames.Select(name => new EditorBuildSettingsScene(folder+"/"+name+".unity",true)).ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(folder+"/Dock.unity",OpenSceneMode.Single);
    }

    public static void VerifySeededLayouts()
    {
        CampaignLayout.RunDeterministicSelfTest();
        PlayerMovement player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
        Require(player != null,"Player movement needed for actual physical envelopes");
        int[] seeds = {20260918,7,42,2026,18092026,-13579,0,int.MaxValue,int.MinValue,918};
        int count = 0;
        foreach (int seed in seeds)
        for (int chapter = 0; chapter < 4; chapter++)
        {
            float chapterSpeed = PirateMovementProfile.ForChapter(chapter);
            CampaignLayout first = CampaignLayout.Create(seed,chapter,player.JumpLaunchSpeed,player.GravityStrength,Time.fixedDeltaTime,chapterSpeed);
            CampaignLayout second = CampaignLayout.Create(seed,chapter,player.JumpLaunchSpeed,player.GravityStrength,Time.fixedDeltaTime,chapterSpeed);
            Require(first.GeometryValid,$"Seed {seed} chapter {chapter}: "+string.Join("; ",first.ValidationErrors));
            Require(first.Signature() == second.Signature(),"Non deterministic campaign "+seed+"/"+chapter);
            Require(first.Rooms.Count(r => r.IsLeaf) >= 4,"Insufficient split/fill regions");
            Require(first.Route.Max(r=>r.FeetPosition.x)-first.Route.Min(r=>r.FeetPosition.x)>20f,"Missing required horizontal traversal");
            Require(first.Spawns.Any(s=>s.Kind == CampaignLayout.SpawnKind.Checkpoint),"No checkpoint before campaign challenges");
            Debug.Log($"PIRATE_CAMPAIGN_LAYOUT_PASS seed={seed} chapter={chapter} rooms={first.Rooms.Count(r=>r.IsLeaf)} " +
                $"nodes={first.Route.Count} splits={first.AcceptedSplits} compatible={first.CompatibilityChecks} signature={first.Signature()} physicalPlaythrough=False");
            count++;
        }
        Debug.Log($"PIRATE_CAMPAIGN_GEOMETRY_SUCCESS layouts={count} physicalPlaythrough=False");
    }

    private static void Require(bool condition,string text)
    {
        if (!condition) throw new InvalidOperationException(text);
    }
}
