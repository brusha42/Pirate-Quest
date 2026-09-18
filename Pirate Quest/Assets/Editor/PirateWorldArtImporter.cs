using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class PirateWorldArtImporter
{
    public const string Folder = "Assets/Sprites/World";
    public const string LibraryPath = "Assets/Resources/PirateWorldArtLibrary.asset";
    private static readonly string[] SheetPaths = { Folder + "/pirate-objects-a.png", Folder + "/pirate-objects-b.png", Folder + "/pirate-biome-materials.png" };

    [MenuItem("Coursework/Import Campaign World Art")]
    public static void ImportAndVerify()
    {
        const string menuPath = "Assets/Resources/PirateMenuBackground.png";
        if (File.Exists(menuPath))
        {
            AssetDatabase.ImportAsset(menuPath, ImportAssetOptions.ForceSynchronousImport);
            var menuImporter = (TextureImporter)AssetImporter.GetAtPath(menuPath);
            menuImporter.textureType = TextureImporterType.Default;
            menuImporter.filterMode = FilterMode.Point;
            menuImporter.mipmapEnabled = false;
            menuImporter.npotScale = TextureImporterNPOTScale.None;
            menuImporter.textureCompression = TextureImporterCompression.Uncompressed;
            menuImporter.maxTextureSize = 2048;
            menuImporter.SaveAndReimport();
        }
        var objectSprites = new List<Sprite>();
        for (int sheet = 0; sheet < 2; sheet++)
            objectSprites.AddRange(ImportAtlas(SheetPaths[sheet], true, sheet * 16));
        Sprite[] materialSprites = ImportAtlas(SheetPaths[2], false, 0);
        EnsureFolder("Assets/Resources");
        EnsureFolder(Folder + "/Materials");
        var tiles = new TileBase[16];
        for (int i = 0; i < tiles.Length; i++)
        {
            string path = Folder + "/Materials/biome_" + i / 4 + "_material_" + i % 4 + ".asset";
            Tile tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }
            tile.sprite = materialSprites[i];
            tile.colliderType = Tile.ColliderType.None;
            tile.color = Color.white;
            EditorUtility.SetDirty(tile);
            tiles[i] = tile;
        }
        var library = AssetDatabase.LoadAssetAtPath<PirateWorldArtLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<PirateWorldArtLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        ReadOpaqueGeometry(objectSprites.ToArray(), out Rect[] opaqueBounds, out Vector2 cannonMuzzle);
        library.Configure(objectSprites.ToArray(), tiles, opaqueBounds, cannonMuzzle);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
        if (!library.IsComplete || !library.HasOpaqueGeometry) throw new InvalidDataException("Campaign art library incomplete.");
        Debug.Log("PIRATE_WORLD_ART_SUCCESS objects=32 materials=16 chapters=4 transparent=True");
        Debug.Log("PIRATE_WORLD_ANCHORS_SUCCESS opaqueBounds=32 cannonMuzzle="+cannonMuzzle+" sourceAlpha=True");
    }

    private static void ReadOpaqueGeometry(Sprite[] sprites, out Rect[] bounds, out Vector2 muzzle)
    {
        bounds = new Rect[sprites.Length];
        muzzle = Vector2.zero;
        for (int sheet = 0; sheet < 2; sheet++)
        {
            var source = new Texture2D(2,2,TextureFormat.RGBA32,false);
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(SheetPaths[sheet]))) throw new InvalidDataException("Invalid object atlas.");
                Color32[] pixels = source.GetPixels32();
                for (int i = sheet*16; i < (sheet+1)*16; i++)
                {
                    Sprite sprite = sprites[i];
                    Rect region = sprite.rect;
                    int left = (int)region.xMax, right = -1, bottom = (int)region.yMax, top = -1;
                    for (int y = (int)region.yMin; y < (int)region.yMax; y++)
                    for (int x = (int)region.xMin; x < (int)region.xMax; x++)
                        if (pixels[y*source.width+x].a >= 32)
                        { left = Math.Min(left,x); right = Math.Max(right,x); bottom = Math.Min(bottom,y); top = Math.Max(top,y); }
                    if (right < left || top < bottom) throw new InvalidDataException("No opaque art in "+sprite.name);
                    float ppu = sprite.pixelsPerUnit;
                    bounds[i] = new Rect((left-region.x-sprite.pivot.x)/ppu,(bottom-region.y-sprite.pivot.y)/ppu,
                        (right-left+1)/ppu,(top-bottom+1)/ppu);
                    if (i == (int)PirateArtKind.Cannon)
                    {
                        int column = left+Mathf.RoundToInt((right-left)*.06f);
                        int low = top, high = bottom;
                        for (int y = bottom; y <= top; y++)
                            if (pixels[y*source.width+column].a >= 32) { low = Math.Min(low,y); high = Math.Max(high,y); }
                        if (high < low) throw new InvalidDataException("Cannon muzzle silhouette missing.");
                        muzzle = new Vector2((column-left+.5f)/(right-left+1f),((low+high)*.5f-bottom+.5f)/(top-bottom+1f));
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(source); }
        }
    }

    private static Sprite[] ImportAtlas(string path, bool transparentObjects, int offset)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Missing generated campaign atlas", path);
        var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(path))) throw new InvalidDataException("Invalid PNG: " + path);
            Color32[] pixels = source.GetPixels32();
            RectInt[] cells = transparentObjects ? FindObjectCells(pixels, source.width, source.height) : UniformCells(source.width, source.height);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = transparentObjects ? 100f : source.width / 4f;
            importer.alphaIsTransparency = transparentObjects;
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
            var existing = provider.GetSpriteRects().ToDictionary(x => x.name, x => x.spriteID);
            var rectangles = new SpriteRect[16];
            var pairs = new List<SpriteNameFileIdPair>();
            for (int i = 0; i < 16; i++)
            {
                string name = transparentObjects ? "object_" + (offset + i).ToString("00") : "biome_material_" + i.ToString("00");
                GUID id = existing.TryGetValue(name, out GUID oldId) ? oldId : GUID.Generate();
                RectInt cell = cells[i];
                rectangles[i] = new SpriteRect
                {
                    name = name, spriteID = id, rect = new Rect(cell.x, cell.y, cell.width, cell.height),
                    alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f), border = Vector4.zero
                };
                pairs.Add(new SpriteNameFileIdPair(name, id));
            }
            provider.SetSpriteRects(rectangles);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(pairs);
            provider.Apply();
            importer.SaveAndReimport();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            if (sprites.Length != 16) throw new InvalidDataException("Expected 16 sprites in " + path);
            return sprites;
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    private static RectInt[] UniformCells(int width, int height)
    {
        var result = new RectInt[16];
        for (int row = 0; row < 4; row++)
        for (int column = 0; column < 4; column++)
        {
            int x = column * width / 4, right = (column + 1) * width / 4;
            int y = (3 - row) * height / 4, top = (4 - row) * height / 4;
            result[row * 4 + column] = new RectInt(x, y, right - x, top - y);
        }
        return result;
    }

    private static RectInt[] FindObjectCells(Color32[] pixels, int width, int height)
    {
        if (pixels.Count(p => p.a < 32) < pixels.Length / 5)
            throw new InvalidDataException("Object atlas has a painted background, not genuine transparency.");
        int[] xCounts = new int[width];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++) if (pixels[y * width + x].a >= 32) xCounts[x]++;
        int[] xCuts = { 0, FindSeparator(xCounts, width / 4), FindSeparator(xCounts, width / 2), FindSeparator(xCounts, width * 3 / 4), width };
        var cells = new RectInt[16];
        for (int column = 0; column < 4; column++)
        {
            int[] yCounts = new int[height];
            for (int y = 0; y < height; y++)
                for (int x = xCuts[column]; x < xCuts[column + 1]; x++) if (pixels[y * width + x].a >= 32) yCounts[y]++;
            int[] yCuts = { 0, FindSeparator(yCounts, height / 4), FindSeparator(yCounts, height / 2), FindSeparator(yCounts, height * 3 / 4), height };
            for (int row = 0; row < 4; row++)
            {
                var cell = new RectInt(xCuts[column], yCuts[3 - row], xCuts[column + 1] - xCuts[column], yCuts[4 - row] - yCuts[3 - row]);
                int left = cell.xMax, right = -1, bottom = cell.yMax, top = -1;
                for (int y = cell.yMin; y < cell.yMax; y++)
                for (int x = cell.xMin; x < cell.xMax; x++)
                    if (pixels[y * width + x].a >= 32)
                    {
                        left = Math.Min(left, x); right = Math.Max(right, x);
                        bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                    }
                if (right - left < 12 || top - bottom < 12) throw new InvalidDataException($"Empty sprite cell {row},{column}.");
                if ((cell.xMin > 0 && left <= cell.xMin) || (cell.xMax < width && right >= cell.xMax - 1) ||
                    (cell.yMin > 0 && bottom <= cell.yMin) || (cell.yMax < height && top >= cell.yMax - 1))
                    throw new InvalidDataException($"Atlas sprite crosses separator {row},{column}; inspect before import.");
                int padding = 2;
                int paddedLeft = Math.Max(0, left - padding), paddedBottom = Math.Max(0, bottom - padding);
                cells[row * 4 + column] = new RectInt(paddedLeft, paddedBottom,
                    Math.Min(width, right + 1 + padding) - paddedLeft, Math.Min(height, top + 1 + padding) - paddedBottom);
            }
        }
        return cells;
    }

    private static int FindSeparator(int[] occupied, int expected)
    {
        int radius = occupied.Length / 10;
        int selected = expected;
        int score = int.MaxValue;
        for (int i = Math.Max(2, expected - radius); i <= Math.Min(occupied.Length - 3, expected + radius); i++)
        {
            int candidate = (occupied[i - 1] + occupied[i] + occupied[i + 1]) * occupied.Length + Math.Abs(i - expected);
            if (candidate < score) { selected = i; score = candidate; }
        }
        if (occupied[selected] != 0) throw new InvalidDataException("Cannot separate generated sprite objects; alpha gap missing.");
        return selected;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }
}
