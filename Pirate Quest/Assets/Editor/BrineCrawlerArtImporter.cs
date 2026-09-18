using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class BrineCrawlerArtImporter
{
    public const string AtlasPath = "Assets/Resources/BrineCrawlerAtlas.png";
    public const string LibraryPath = "Assets/Resources/BrineCrawlerArtLibrary.asset";
    private static readonly string[] Names = { "brine_00_idle", "brine_01_walk", "brine_02_windup", "brine_03_lunge" };

    [MenuItem("Coursework/Import Brine Crawler Art")]
    public static void ImportAndVerify()
    {
        if (!File.Exists(AtlasPath)) throw new FileNotFoundException("Missing generated crawler atlas", AtlasPath);
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(AtlasPath))) throw new InvalidDataException("Invalid crawler PNG.");
            Color32[] pixels = source.GetPixels32();
            int transparent = pixels.Count(pixel => pixel.a < 32);
            if (transparent < pixels.Length / 5) throw new InvalidDataException("Crawler atlas needs genuine alpha, not a painted background.");
            RectInt[] opaque = FindFrameBounds(pixels, source.width, source.height);
            float ppu = opaque.Max(cell => cell.width) / BrineCrawlerArtLibrary.MaximumWorldWidth;
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
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
                RectInt art = opaque[i];
                int left = Math.Max(0, art.xMin - 2), bottom = Math.Max(0, art.yMin - 2);
                int right = Math.Min(source.width, art.xMax + 2), top = Math.Min(source.height, art.yMax + 2);
                Rect region = new Rect(left, bottom, right - left, top - bottom);
                GUID id = existing.TryGetValue(Names[i], out GUID oldId) ? oldId : GUID.Generate();
                Vector2 pivot = new Vector2((art.center.x - region.x) / region.width,
                    (art.yMin - region.y) / region.height);
                rectangles[i] = new SpriteRect { name = Names[i], spriteID = id, rect = region,
                    alignment = SpriteAlignment.Custom, pivot = pivot, border = Vector4.zero };
                pairs.Add(new SpriteNameFileIdPair(Names[i], id));
            }
            provider.SetSpriteRects(rectangles);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(pairs);
            provider.Apply();
            importer.SaveAndReimport();
            UnityEngine.Object[] importedAssets = AssetDatabase.LoadAllAssetsAtPath(AtlasPath);
            Sprite[] sprites = importedAssets.OfType<Sprite>().OrderBy(sprite => sprite.name).ToArray();
            Debug.Log("PIRATE_CRAWLER_IMPORT_DIAGNOSTIC assets=" + string.Join(",", importedAssets.Select(asset => asset.GetType().Name + ":" + asset.name)));
            if (sprites.Length != 4) throw new InvalidDataException("Crawler atlas must import exactly four frames; actual=" + sprites.Length);
            var alphaBounds = new Rect[4];
            for (int i = 0; i < 4; i++)
            {
                if (sprites[i].name != Names[i]) throw new InvalidDataException("Crawler sprite order changed.");
                Rect region = sprites[i].rect;
                Vector2 pivot = sprites[i].pivot;
                alphaBounds[i] = new Rect((opaque[i].x - region.x - pivot.x) / ppu,
                    (opaque[i].y - region.y - pivot.y) / ppu, opaque[i].width / ppu, opaque[i].height / ppu);
                if (Mathf.Abs(alphaBounds[i].yMin) > 0.002f ||
                    Mathf.Abs(sprites[i].pixelsPerUnit - ppu) > 0.01f ||
                    alphaBounds[i].width > BrineCrawlerArtLibrary.MaximumWorldWidth + 0.002f)
                    throw new InvalidDataException("Crawler frame scale or floor pivot is inconsistent: " + Names[i]);
            }
            var library = AssetDatabase.LoadAssetAtPath<BrineCrawlerArtLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<BrineCrawlerArtLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.Configure(sprites, alphaBounds, true);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            if (!library.IsComplete) throw new InvalidDataException("Crawler runtime art library incomplete.");
            Debug.Log($"PIRATE_BRINE_CRAWLER_ART_SUCCESS sprites=4 sourceAlpha=True sharedPPU={ppu:F3} " +
                $"maxWidth={BrineCrawlerArtLibrary.MaximumWorldWidth:F2} floorPivots=4 atlas={source.width}x{source.height}");
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    private static RectInt[] FindFrameBounds(Color32[] pixels, int width, int height)
    {
        int[] columnCounts = new int[width];
        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++) if (pixels[y * width + x].a >= 32) columnCounts[x]++;
        int middle = FindSeparator(columnCounts);
        int[] columns = { 0, middle, width };
        var result = new RectInt[4];
        for (int column = 0; column < 2; column++)
        {
            int[] rows = new int[height];
            for (int y = 0; y < height; y++)
            for (int x = columns[column]; x < columns[column + 1]; x++)
                if (pixels[y * width + x].a >= 32) rows[y]++;
            int rowMiddle = FindSeparator(rows);
            int[] cuts = { 0, rowMiddle, height };
            for (int row = 0; row < 2; row++)
            {
                int yMin = cuts[1 - row], yMax = cuts[2 - row];
                int left = width, right = -1, bottom = height, top = -1;
                for (int y = yMin; y < yMax; y++)
                for (int x = columns[column]; x < columns[column + 1]; x++)
                    if (pixels[y * width + x].a >= 32)
                    { left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y); }
                if (right - left < 30 || top - bottom < 30) throw new InvalidDataException("Empty crawler atlas cell.");
                result[row * 2 + column] = new RectInt(left, bottom, right - left + 1, top - bottom + 1);
            }
        }
        return result;
    }

    private static int FindSeparator(int[] counts)
    {
        int middle = counts.Length / 2, radius = counts.Length / 7;
        for (int distance = 0; distance <= radius; distance++)
        {
            int left = middle - distance, right = middle + distance;
            if (left > 1 && counts[left - 1] == 0 && counts[left] == 0 && counts[left + 1] == 0) return left;
            if (right < counts.Length - 2 && counts[right - 1] == 0 && counts[right] == 0 && counts[right + 1] == 0) return right;
        }
        throw new InvalidDataException("Crawler sprites overlap the separator; inspect the source atlas.");
    }
}
