using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class PirateAnimationPreviewExporter
{
    [Serializable]
    private sealed class PreviewManifest
    {
        public string animation = "run";
        public string sourceSheet;
        public int width;
        public int height;
        public float framesPerSecond;
        public bool loop;
        public int footPixelX;
        public int footPixelY;
        public string[] frames;
    }

    [MenuItem("Coursework/Export Pirate Run Preview")]
    public static void ExportRunPreview()
    {
        PirateAnimationLibrary library = AssetDatabase.LoadAssetAtPath<PirateAnimationLibrary>(PirateAnimationImporter.LibraryPath);
        if (library == null || !library.IsComplete)
        {
            throw new InvalidDataException("Import the pirate animation library before exporting its preview.");
        }

        string outputDirectory = Path.GetFullPath(GetOutputDirectory());
        ExportRunPreview(library, outputDirectory);
    }

    public static void ExportRunPreview(PirateAnimationLibrary library, string outputDirectory)
    {
        PirateAnimationClip run = library.GetClip(PlayerVisualAnimator.AnimationState.Run);
        if (run == null || !run.IsValid) throw new InvalidDataException("The run clip is empty.");

        string sourcePath = AssetDatabase.GetAssetPath(run.GetFrame(0).texture);
        Texture2D sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!sourceTexture.LoadImage(File.ReadAllBytes(sourcePath)))
            {
                throw new InvalidDataException("Could not read the original animation texture.");
            }

            Color32[] sourcePixels = sourceTexture.GetPixels32();
            float leftExtent = 0f;
            float rightExtent = 0f;
            float bottomExtent = 0f;
            float topExtent = 0f;
            for (int index = 0; index < run.FrameCount; index++)
            {
                Sprite frame = run.GetFrame(index);
                if (AssetDatabase.GetAssetPath(frame.texture) != sourcePath)
                {
                    throw new InvalidDataException("This exporter expects all run frames in one source sheet.");
                }

                leftExtent = Mathf.Max(leftExtent, frame.pivot.x);
                rightExtent = Mathf.Max(rightExtent, frame.rect.width - frame.pivot.x);
                bottomExtent = Mathf.Max(bottomExtent, frame.pivot.y);
                topExtent = Mathf.Max(topExtent, frame.rect.height - frame.pivot.y);
            }

            const int padding = 24;
            int footX = Mathf.CeilToInt(leftExtent) + padding;
            int footY = Mathf.CeilToInt(bottomExtent) + padding;
            int width = footX + Mathf.CeilToInt(rightExtent) + padding;
            int height = footY + Mathf.CeilToInt(topExtent) + padding;
            SerializedObject serializedLibrary = new SerializedObject(library);
            SerializedProperty serializedRun = serializedLibrary.FindProperty("run");
            PreviewManifest manifest = new PreviewManifest
            {
                sourceSheet = sourcePath,
                width = width,
                height = height,
                framesPerSecond = serializedRun.FindPropertyRelative("framesPerSecond").floatValue,
                loop = serializedRun.FindPropertyRelative("loop").boolValue,
                footPixelX = footX,
                footPixelY = footY,
                frames = new string[run.FrameCount]
            };

            Directory.CreateDirectory(outputDirectory);
            for (int index = 0; index < run.FrameCount; index++)
            {
                string fileName = $"run-{index:00}.png";
                manifest.frames[index] = fileName;
                WriteFrame(run.GetFrame(index), sourcePixels, sourceTexture.width, sourceTexture.height,
                    width, height, footX, footY, Path.Combine(outputDirectory, fileName));
            }

            File.WriteAllText(Path.Combine(outputDirectory, "run-preview.json"), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            Debug.Log($"PIRATE_ANIMATION_PREVIEW_SUCCESS frames={run.FrameCount} canvas={width}x{height} fps={manifest.framesPerSecond} path={outputDirectory}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(sourceTexture);
        }
    }

    private static void WriteFrame(Sprite sprite, Color32[] sourcePixels, int sourceWidth, int sourceHeight,
        int canvasWidth, int canvasHeight, int footX, int footY, string path)
    {
        Color32 background = new Color32(22, 29, 43, 255);
        Color32[] canvas = new Color32[canvasWidth * canvasHeight];
        for (int index = 0; index < canvas.Length; index++) canvas[index] = background;

        Rect rect = sprite.rect;
        int sourceX = Mathf.RoundToInt(rect.x);
        int sourceY = Mathf.RoundToInt(rect.y);
        int width = Mathf.RoundToInt(rect.width);
        int height = Mathf.RoundToInt(rect.height);
        int destinationX = footX - Mathf.RoundToInt(sprite.pivot.x);
        int destinationY = footY - Mathf.RoundToInt(sprite.pivot.y);
        if (sourceX < 0 || sourceY < 0 || sourceX + width > sourceWidth || sourceY + height > sourceHeight ||
            destinationX < 0 || destinationY < 0 || destinationX + width > canvasWidth || destinationY + height > canvasHeight)
        {
            throw new InvalidDataException($"Frame {sprite.name} extends beyond its source or preview canvas.");
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = sourcePixels[(sourceY + y) * sourceWidth + sourceX + x];
                int alpha = pixel.a;
                canvas[(destinationY + y) * canvasWidth + destinationX + x] = new Color32(
                    (byte)((pixel.r * alpha + background.r * (255 - alpha) + 127) / 255),
                    (byte)((pixel.g * alpha + background.g * (255 - alpha) + 127) / 255),
                    (byte)((pixel.b * alpha + background.b * (255 - alpha) + 127) / 255),
                    255);
            }
        }

        Texture2D texture = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGBA32, false);
        try
        {
            texture.SetPixels32(canvas);
            texture.Apply(false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static string GetOutputDirectory()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        for (int index = 0; index < arguments.Length; index++)
        {
            if (!string.Equals(arguments[index], "-pirateAnimationPreview", StringComparison.OrdinalIgnoreCase)) continue;
            if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("-"))
            {
                throw new ArgumentException("-pirateAnimationPreview requires an output directory.");
            }
            return arguments[index + 1];
        }
        return Path.Combine("Builds", "AnimationPreview");
    }
}
