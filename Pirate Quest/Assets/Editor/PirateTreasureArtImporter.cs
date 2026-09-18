using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class PirateTreasureArtImporter
{
    public const string AtlasPath = "Assets/Resources/PirateTreasureAtlas.png";
    public const string LibraryPath = "Assets/Resources/PirateTreasureArtLibrary.asset";
    private static readonly string[] Names = { "treasure_00_doubloon", "treasure_01_gem", "treasure_02_relic", "treasure_03_letter" };

    [MenuItem("Coursework/Import Treasure Art")]
    public static void ImportAndVerify()
    {
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(AtlasPath))) throw new InvalidDataException("Invalid treasure PNG.");
            Color32[] pixels = source.GetPixels32();
            int alpha = pixels.Count(pixel => pixel.a < 32);
            if (alpha < pixels.Length / 4) throw new InvalidDataException("Treasure atlas must have genuine transparent alpha.");
            var regions = new Rect[4];
            for (int i = 0; i < 4; i++)
            {
                int x0 = (i % 2) * source.width / 2, x1 = (i % 2 + 1) * source.width / 2;
                int y0 = (1 - i / 2) * source.height / 2, y1 = (2 - i / 2) * source.height / 2;
                int left = x1, right = -1, bottom = y1, top = -1;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    if (pixels[y * source.width + x].a < 32) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
                if (right - left < 24 || top - bottom < 24 || left <= x0 + 2 || right >= x1 - 3 ||
                    bottom <= y0 + 2 || top >= y1 - 3)
                    throw new InvalidDataException("Treasure cell is empty or crosses its transparent gutter: " + Names[i]);
                regions[i] = new Rect(left - 2, bottom - 2, right - left + 5, top - bottom + 5);
            }
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 128;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(source.width, source.height));
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existing = provider.GetSpriteRects().ToDictionary(rect => rect.name, rect => rect.spriteID);
            var rectangles = new SpriteRect[4];
            var pairs = new List<SpriteNameFileIdPair>();
            for (int i = 0; i < 4; i++)
            {
                GUID id = existing.TryGetValue(Names[i], out GUID oldId) ? oldId : GUID.Generate();
                rectangles[i] = new SpriteRect { name = Names[i], spriteID = id, rect = regions[i],
                    alignment = SpriteAlignment.Center, pivot = Vector2.one * .5f, border = Vector4.zero };
                pairs.Add(new SpriteNameFileIdPair(Names[i], id));
            }
            provider.SetSpriteRects(rectangles);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(pairs);
            provider.Apply(); importer.SaveAndReimport();
            Sprite[] sprites = AssetDatabase.LoadAllAssetsAtPath(AtlasPath).OfType<Sprite>().OrderBy(sprite => sprite.name).ToArray();
            if (sprites.Length != 4 || !sprites.Select(sprite => sprite.name).SequenceEqual(Names))
                throw new InvalidDataException("Treasure atlas must import exactly four named sprites.");
            var library = AssetDatabase.LoadAssetAtPath<PirateTreasureArtLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<PirateTreasureArtLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Configure(sprites); EditorUtility.SetDirty(library); AssetDatabase.SaveAssets();
            if (!library.IsComplete) throw new InvalidDataException("Treasure art library is incomplete.");
            Debug.Log($"PIRATE_TREASURE_ART_SUCCESS sprites=4 genuineAlpha=True transparentPixels={alpha} atlas={source.width}x{source.height}");
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }
}
