using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public static class PirateAnimationImporter
{
    public const string SheetPath = "Assets/Sprites/Characters/pirate-animation-sheet.png";
    public const string LibraryPath = "Assets/Resources/PirateAnimationLibrary.asset";
    private const int Columns = 4;
    private const int Rows = 4;
    private const float StandingHeight = 1.28f;

    [MenuItem("Coursework/Import Pirate Animation Sheet")]
    public static void ImportAndVerify()
    {
        if (!File.Exists(SheetPath))
        {
            throw new FileNotFoundException("Expected a transparent 4 x 4 pirate animation sheet.", SheetPath);
        }

        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(SheetPath)))
            {
                throw new InvalidDataException("Could not decode pirate animation PNG.");
            }

            ImportSheet(source);
            CreateLibrary();
            VerifyImportedFrames();
            Debug.Log("PIRATE_ANIMATION_IMPORT_SUCCESS idle=4 run=8 actionPoses=4");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static RectInt[] FindCells(Color32[] pixels, int width, int height)
    {
        bool[] occupiedRows = new bool[height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width && !occupiedRows[y]; x++)
            {
                occupiedRows[y] = pixels[y * width + x].a >= 32;
            }
        }

        List<Vector2Int> rowBands = FindBands(occupiedRows, height / (Rows * 3));
        if (rowBands.Count != Rows)
        {
            throw new InvalidDataException($"Expected 4 separated rows of pirate frames; found {rowBands.Count}. Check alpha gaps in the sheet.");
        }

        RectInt[] cells = new RectInt[Rows * Columns];
        for (int row = 0; row < Rows; row++)
        {
            Vector2Int rowBand = rowBands[Rows - 1 - row];
            bool[] occupiedColumns = new bool[width];
            for (int x = 0; x < width; x++)
            {
                for (int y = rowBand.x; y <= rowBand.y && !occupiedColumns[x]; y++)
                {
                    occupiedColumns[x] = pixels[y * width + x].a >= 32;
                }
            }

            List<Vector2Int> columnBands = FindBands(occupiedColumns, width / (Columns * 5));
            if (columnBands.Count != Columns)
            {
                throw new InvalidDataException($"Pirate sheet row {row} has {columnBands.Count} separated figures instead of 4.");
            }

            for (int column = 0; column < Columns; column++)
            {
                Vector2Int band = columnBands[column];
                int left = Mathf.Max(0, band.x - 2);
                int right = Mathf.Min(width, band.y + 3);
                int bottom = Mathf.Max(0, rowBand.x - 2);
                int top = Mathf.Min(height, rowBand.y + 3);
                cells[row * Columns + column] = new RectInt(left, bottom, right - left, top - bottom);
            }
        }

        return cells;
    }

    private static List<Vector2Int> FindBands(bool[] occupied, int minimumLength)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        int start = -1;
        int lastOccupied = -1;
        for (int index = 0; index <= occupied.Length + 2; index++)
        {
            if (index < occupied.Length && occupied[index])
            {
                if (start < 0) start = index;
                lastOccupied = index;
            }
            else if (start >= 0 && index - lastOccupied > 1)
            {
                if (lastOccupied - start + 1 >= minimumLength) result.Add(new Vector2Int(start, lastOccupied));
                start = -1;
            }
        }
        return result;
    }

    private static void ImportSheet(Texture2D source)
    {
        Color32[] pixels = source.GetPixels32();
        RectInt[] cells = FindCells(pixels, source.width, source.height);
        RectInt[] contentBounds = new RectInt[Columns * Rows];
        for (int index = 0; index < contentBounds.Length; index++)
        {
            contentBounds[index] = FindOpaqueBounds(pixels, source.width,
                cells[index], index);
        }

        int[] idleHeights = contentBounds.Take(4).Select(bounds => bounds.height).OrderBy(height => height).ToArray();
        float pixelsPerUnit = (idleHeights[1] + idleHeights[2]) * 0.5f / StandingHeight;
        AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(SheetPath);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.alphaIsTransparency = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = Mathf.NextPowerOfTwo(Mathf.Max(source.width, source.height));
        importer.isReadable = false;
        TextureImporterSettings settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
        importer.SaveAndReimport();

        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        Dictionary<string, GUID> existingIds = provider.GetSpriteRects().ToDictionary(rect => rect.name, rect => rect.spriteID);
        SpriteRect[] rectangles = new SpriteRect[Columns * Rows];
        List<SpriteNameFileIdPair> nameIdPairs = new List<SpriteNameFileIdPair>();
        for (int index = 0; index < rectangles.Length; index++)
        {
            RectInt cell = cells[index];
            string frameName = GetFrameName(index);
            GUID id = existingIds.TryGetValue(frameName, out GUID existingId) ? existingId : GUID.Generate();
            rectangles[index] = new SpriteRect
            {
                name = frameName,
                spriteID = id,
                rect = new Rect(cell.x, cell.y, cell.width, cell.height),
                alignment = SpriteAlignment.Custom,
                pivot = new Vector2(
                    FindTorsoCentre(pixels, source.width, cell, contentBounds[index]) / cell.width,
                    contentBounds[index].yMin / (float)cell.height),
                border = Vector4.zero
            };
            nameIdPairs.Add(new SpriteNameFileIdPair(frameName, id));
        }

        provider.SetSpriteRects(rectangles);
        provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(nameIdPairs);
        provider.Apply();
        importer.SaveAndReimport();
        Debug.Log($"Pirate sheet: {source.width}x{source.height}; 16 alpha-separated frames; {pixelsPerUnit:F2} pixels/unit.");
    }

    private static float FindTorsoCentre(Color32[] pixels, int textureWidth, RectInt cell, RectInt bounds)
    {
        List<float> centres = new List<float>();
        int firstRow = bounds.yMin + Mathf.RoundToInt(bounds.height * 0.48f);
        int lastRow = bounds.yMin + Mathf.RoundToInt(bounds.height * 0.68f);
        for (int y = firstRow; y <= lastRow; y++)
        {
            int longestStart = -1;
            int longestLength = 0;
            int spanStart = -1;
            for (int x = bounds.xMin; x <= bounds.xMax; x++)
            {
                bool opaque = x < bounds.xMax && pixels[(cell.y + y) * textureWidth + cell.x + x].a >= 128;
                if (opaque && spanStart < 0) spanStart = x;
                if (!opaque && spanStart >= 0)
                {
                    if (x - spanStart > longestLength)
                    {
                        longestStart = spanStart;
                        longestLength = x - spanStart;
                    }
                    spanStart = -1;
                }
            }
            if (longestLength > 4) centres.Add(longestStart + longestLength * 0.5f);
        }
        centres.Sort();
        return centres.Count > 0 ? centres[centres.Count / 2] : bounds.center.x;
    }

    private static RectInt FindOpaqueBounds(Color32[] pixels, int textureWidth, RectInt cell, int frameIndex)
    {
        int minX = cell.width;
        int minY = cell.height;
        int maxX = -1;
        int maxY = -1;
        int transparentPixels = 0;
        for (int y = 0; y < cell.height; y++)
        {
            for (int x = 0; x < cell.width; x++)
            {
                if (pixels[(cell.y + y) * textureWidth + cell.x + x].a < 32)
                {
                    transparentPixels++;
                    continue;
                }

                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
        {
            throw new InvalidDataException($"Pirate frame {frameIndex} is empty.");
        }

        if (transparentPixels < cell.width * cell.height / 20)
        {
            throw new InvalidDataException($"Pirate frame {frameIndex} has no transparent background.");
        }

        return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void CreateLibrary()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

        Dictionary<string, Sprite> sprites = AssetDatabase.LoadAllAssetsAtPath(SheetPath)
            .OfType<Sprite>().ToDictionary(sprite => sprite.name);
        Sprite[] orderedFrames = Enumerable.Range(0, Columns * Rows).Select(index => sprites[GetFrameName(index)]).ToArray();
        PirateAnimationLibrary library = AssetDatabase.LoadAssetAtPath<PirateAnimationLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<PirateAnimationLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        library.ConfigureFromSheet(orderedFrames);
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets();
    }

    public static void VerifyImportedFrames()
    {
        PirateAnimationLibrary library = AssetDatabase.LoadAssetAtPath<PirateAnimationLibrary>(LibraryPath);
        if (library == null || !library.IsComplete)
        {
            throw new InvalidDataException("Pirate animation library is incomplete.");
        }

        PirateAnimationClip run = library.GetClip(PlayerVisualAnimator.AnimationState.Run);
        HashSet<Sprite> uniqueFrames = new HashSet<Sprite>();
        for (int index = 0; index < run.FrameCount; index++) uniqueFrames.Add(run.GetFrame(index));
        if (uniqueFrames.Count != 8 || run.GetFrameIndex(0f) == run.GetFrameIndex(0.1f))
        {
            throw new InvalidDataException("Pirate run animation must advance through eight separate sprite frames.");
        }

        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!source.LoadImage(File.ReadAllBytes(SheetPath))) throw new InvalidDataException("Cannot read source animation pixels.");
            Color32[] pixels = source.GetPixels32();
            RectInt[] cells = FindCells(pixels, source.width, source.height);
            HashSet<ulong> pixelFingerprints = new HashSet<ulong>();
            for (int index = 4; index < 12; index++)
            {
                RectInt bounds = FindOpaqueBounds(pixels, source.width, cells[index], index);
                pixelFingerprints.Add(GetPixelFingerprint(pixels, source.width, cells[index], bounds));
            }
            if (pixelFingerprints.Count != 8)
            {
                throw new InvalidDataException($"Only {pixelFingerprints.Count} different run images found. Sprite references alone do not establish animation.");
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(source);
        }
    }

    private static ulong GetPixelFingerprint(Color32[] pixels, int textureWidth, RectInt cell, RectInt bounds)
    {
        const ulong prime = 1099511628211UL;
        ulong hash = 14695981039346656037UL;
        unchecked
        {
            hash = (hash ^ (uint)bounds.width) * prime;
            hash = (hash ^ (uint)bounds.height) * prime;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    Color32 pixel = pixels[(cell.y + y) * textureWidth + cell.x + x];
                    if (pixel.a < 32) pixel = new Color32(0, 0, 0, 0);
                    hash = (hash ^ pixel.r) * prime;
                    hash = (hash ^ pixel.g) * prime;
                    hash = (hash ^ pixel.b) * prime;
                    hash = (hash ^ pixel.a) * prime;
                }
            }
        }
        return hash;
    }

    private static string GetFrameName(int index)
    {
        if (index < 4) return $"pirate_idle_{index:00}";
        if (index < 12) return $"pirate_run_{index - 4:00}";
        return new[] { "pirate_jump_rise", "pirate_jump_apex", "pirate_fall", "pirate_grapple" }[index - 12];
    }
}
