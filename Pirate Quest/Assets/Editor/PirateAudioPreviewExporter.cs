using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class PirateAudioPreviewExporter
{
    public static void Export()
    {
        string[] args = Environment.GetCommandLineArgs();
        int option = Array.IndexOf(args, "-pirateAudioPreview");
        if (option < 0 || option + 1 >= args.Length) return;
        string output = Path.GetFullPath(args[option + 1]);
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        AudioClip clip = PirateAudio.CreateMusicPreview();
        try
        {
            int count = Mathf.Min(clip.samples, clip.frequency * 10);
            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            using (var writer = new BinaryWriter(File.Create(output)))
            {
                int byteCount = count * clip.channels * 2;
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + byteCount);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                writer.Write((short)1); writer.Write((short)clip.channels); writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2); writer.Write((short)(clip.channels * 2)); writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(byteCount);
                for (int i = 0; i < count * clip.channels; i++)
                {
                    float fade = Mathf.Min(1f, (count * clip.channels - i) / (clip.frequency * clip.channels * 0.3f));
                    writer.Write((short)(Mathf.Clamp(samples[i] * fade, -1f, 1f) * short.MaxValue));
                }
            }
            Debug.Log($"PIRATE_AUDIO_PREVIEW_SUCCESS: {output}, seconds={count/(float)clip.frequency:F1}, recordedMenuTrack=True");
        }
        finally
        {
            if (clip != null && !AssetDatabase.Contains(clip))
                UnityEngine.Object.DestroyImmediate(clip);
        }
    }
}
