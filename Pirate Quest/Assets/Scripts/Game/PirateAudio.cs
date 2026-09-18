using System;
using System.Collections.Generic;
using UnityEngine;

public enum PirateSound { Jump, Land, Dash, Grapple, Saber, Spring, Pickup, Checkpoint, Cannon, Death, Door, Parrot }

public sealed class PirateAudio : MonoBehaviour
{
    private const int SampleRate = 22050;
    private const float FadeSeconds = 0.4f;
    private const string MenuResource = "Audio/PirateMenu";
    private static readonly string[] GameplayResources =
    {
        "Audio/PirateLevelEnergetic",
        "Audio/PirateLevelTense",
        "Audio/PirateLevelEnergy",
        "Audio/PirateLevelWave",
        "Audio/PirateLevelDark"
    };

    private enum MusicMode { None, Menu, Gameplay }

    private static PirateAudio instance;
    private readonly Dictionary<PirateSound, AudioClip> sounds = new Dictionary<PirateSound, AudioClip>();
    private AudioSource music;
    private AudioSource effects;
    private int chapter = -1;
    private float musicVolume;
    private float sfxVolume;
    private bool silentAutomation;
    private MusicMode mode;
    private AudioClip menuClip;
    private AudioClip[] gameplayClips;
    private int[] playlist = Array.Empty<int>();
    private int playlistIndex = -1;
    private int lastGameplayIndex = -1;
    private readonly System.Random shuffle = new System.Random();
    private float fade = 1f;
    private float fadeTarget = 1f;
    private AudioClip pendingClip;
    private bool pendingLoop;
    private bool clipsLoaded;

    public static float MusicVolume
    {
        get => Ensure().musicVolume;
        set
        {
            PirateAudio audio = Ensure();
            audio.musicVolume = Mathf.Clamp01(value);
            audio.ApplyMusicVolume();
            PlayerPrefs.SetFloat("PirateQuest.MusicVolume", audio.musicVolume);
        }
    }
    public static float SfxVolume
    {
        get => Ensure().sfxVolume;
        set
        {
            PirateAudio audio = Ensure();
            audio.sfxVolume = Mathf.Clamp01(value);
            audio.effects.volume = audio.silentAutomation ? 0f : audio.sfxVolume;
            PlayerPrefs.SetFloat("PirateQuest.SfxVolume", audio.sfxVolume);
        }
    }

    private static PirateAudio Ensure()
    {
        if (instance != null) return instance;
        GameObject audioObject = new GameObject("Pirate Quest - original audio");
        instance = audioObject.AddComponent<PirateAudio>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
        music = gameObject.AddComponent<AudioSource>();
        music.loop = true;
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.priority = 100;
        effects = gameObject.AddComponent<AudioSource>();
        effects.playOnAwake = false;
        effects.spatialBlend = 0f;
        effects.priority = 40;
        musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("PirateQuest.MusicVolume", 0.22f));
        sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat("PirateQuest.SfxVolume", 0.65f));
        foreach (string argument in Environment.GetCommandLineArgs())
            if (string.Equals(argument, "-pirateAudioMute", StringComparison.OrdinalIgnoreCase) ||
                (argument.StartsWith("-pirateQuest", StringComparison.OrdinalIgnoreCase) &&
                (argument.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 argument.IndexOf("capture", StringComparison.OrdinalIgnoreCase) >= 0))) silentAutomation = true;
        fade = 1f;
        fadeTarget = 1f;
        ApplyMusicVolume();
        effects.volume = silentAutomation ? 0f : sfxVolume;
        LoadRecordedClips();
    }

    private void Update()
    {
        if (pendingClip != null)
        {
            fade = Mathf.MoveTowards(fade, 0f, Time.unscaledDeltaTime / FadeSeconds);
            ApplyMusicVolume();
            if (fade > 0.001f) return;
            music.clip = pendingClip;
            music.loop = pendingLoop;
            pendingClip = null;
            music.Play();
            fadeTarget = 1f;
        }

        if (fade != fadeTarget)
        {
            fade = Mathf.MoveTowards(fade, fadeTarget, Time.unscaledDeltaTime / FadeSeconds);
            ApplyMusicVolume();
        }

        if (mode == MusicMode.Gameplay && pendingClip == null && music.clip != null && !music.loop &&
            !music.isPlaying && fadeTarget > 0f)
            PlayNextGameplayTrack(false);
    }

    public static void SetChapter(int requestedChapter)
    {
        PirateAudio audio = Ensure();
        audio.chapter = Mathf.Clamp(requestedChapter, 0, 3);
        audio.EnsureListener();
        if (audio.mode != MusicMode.Menu) PlayGameplay();
    }

    public static void PlayMenu()
    {
        PirateAudio audio = Ensure();
        audio.EnsureListener();
        audio.LoadRecordedClips();
        if (audio.menuClip == null) return;
        if (audio.mode == MusicMode.Menu && audio.music.clip == audio.menuClip && audio.music.isPlaying &&
            audio.pendingClip == null)
            return;
        audio.mode = MusicMode.Menu;
        audio.CrossfadeTo(audio.menuClip, true);
    }

    public static void PlayGameplay()
    {
        PirateAudio audio = Ensure();
        audio.EnsureListener();
        audio.LoadRecordedClips();
        if (audio.gameplayClips == null || audio.gameplayClips.Length == 0) return;
        if (audio.mode == MusicMode.Gameplay && audio.music.isPlaying && audio.pendingClip == null)
            return;
        audio.mode = MusicMode.Gameplay;
        audio.PlayNextGameplayTrack(true);
    }

    public static void Play(PirateSound sound)
    {
        PirateAudio audio = Ensure();
        if (!audio.sounds.TryGetValue(sound, out AudioClip clip))
        {
            clip = CreateEffect(sound);
            audio.sounds.Add(sound, clip);
        }
        audio.effects.PlayOneShot(clip);
    }

    public static bool VerifyGeneratedAudio(out string detail)
    {
        PirateAudio audio = Ensure();
        audio.LoadRecordedClips();
        bool valid = audio.menuClip != null && audio.menuClip.length > 10f;
        int loadedLevels = 0;
        if (audio.gameplayClips != null)
            foreach (AudioClip clip in audio.gameplayClips)
            {
                if (clip == null || clip.length <= 10f) continue;
                loadedLevels++;
            }
        valid &= loadedLevels == GameplayResources.Length;

        var fingerprints = new HashSet<int>();
        int last = -1;
        for (int round = 0; round < 4; round++)
        {
            int[] order = ShuffleTrackOrder(GameplayResources.Length, last, new System.Random(7109 + round * 17));
            valid &= order != null && order.Length == GameplayResources.Length;
            var unique = new HashSet<int>(order);
            valid &= unique.Count == GameplayResources.Length;
            if (last >= 0 && GameplayResources.Length > 1) valid &= order[0] != last;
            last = order[order.Length - 1];
            fingerprints.Add(order[0] * 31 + order[1]);
        }
        valid &= fingerprints.Count >= 2;

        float maximumPeak = 0f;
        foreach (PirateSound sound in Enum.GetValues(typeof(PirateSound)))
        {
            if (!audio.sounds.TryGetValue(sound, out AudioClip effect))
            {
                effect = CreateEffect(sound);
                audio.sounds.Add(sound, effect);
            }
            float[] samples = new float[effect.samples];
            effect.GetData(samples, 0);
            float peak = 0f;
            foreach (float sample in samples) peak = Mathf.Max(peak, Mathf.Abs(sample));
            maximumPeak = Mathf.Max(maximumPeak, peak);
            valid &= peak > 0.02f && peak < 1f;
        }
        detail = $"menu={(audio.menuClip != null ? audio.menuClip.length : 0f):F1}s, " +
            $"levels={loadedLevels}/{GameplayResources.Length}, effects={audio.sounds.Count}, " +
            $"peak={maximumPeak:F3}, shuffleDistinct={fingerprints.Count}, valid={valid}";
        return valid;
    }

    public static int[] ShuffleTrackOrder(int count, int avoidFirst, System.Random random)
    {
        if (count <= 0) return Array.Empty<int>();
        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        for (int i = count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        if (count > 1 && avoidFirst >= 0 && order[0] == avoidFirst)
        {
            int swap = 1 + random.Next(count - 1);
            (order[0], order[swap]) = (order[swap], order[0]);
        }
        return order;
    }

    public static AudioClip CreateMusicPreview(int chapterIndex = 0)
    {
        PirateAudio audio = Ensure();
        audio.LoadRecordedClips();
        if (audio.menuClip != null) return audio.menuClip;
        return ComposeScore(Mathf.Clamp(chapterIndex, 0, 3));
    }

    private void LoadRecordedClips()
    {
        if (clipsLoaded) return;
        menuClip = Resources.Load<AudioClip>(MenuResource);
        gameplayClips = new AudioClip[GameplayResources.Length];
        int loaded = 0;
        for (int i = 0; i < GameplayResources.Length; i++)
        {
            gameplayClips[i] = Resources.Load<AudioClip>(GameplayResources[i]);
            if (gameplayClips[i] != null) loaded++;
        }
        clipsLoaded = menuClip != null && loaded == GameplayResources.Length;
    }

    private void PlayNextGameplayTrack(bool restartPlaylist)
    {
        if (gameplayClips == null || gameplayClips.Length == 0) return;
        if (restartPlaylist || playlist.Length == 0 || playlistIndex + 1 >= playlist.Length)
        {
            playlist = ShuffleTrackOrder(gameplayClips.Length, lastGameplayIndex, shuffle);
            playlistIndex = 0;
        }
        else playlistIndex++;
        lastGameplayIndex = playlist[playlistIndex];
        AudioClip clip = gameplayClips[lastGameplayIndex];
        if (clip == null) return;
        CrossfadeTo(clip, false);
    }

    private void CrossfadeTo(AudioClip clip, bool loop)
    {
        if (clip == null) return;
        if (music.clip == clip && music.isPlaying && music.loop == loop && pendingClip == null)
            return;
        if (!music.isPlaying || music.clip == null || fade <= 0.01f)
        {
            music.clip = clip;
            music.loop = loop;
            pendingClip = null;
            music.Play();
            fade = 0f;
            fadeTarget = 1f;
            ApplyMusicVolume();
            return;
        }
        pendingClip = clip;
        pendingLoop = loop;
        fadeTarget = 0f;
    }

    private void ApplyMusicVolume()
    {
        music.volume = silentAutomation ? 0f : musicVolume * fade;
    }

    private void EnsureListener()
    {
        if (Camera.main != null && FindFirstObjectByType<AudioListener>() == null)
            Camera.main.gameObject.AddComponent<AudioListener>();
    }

    private static AudioClip ComposeScore(int biome)
    {
        float pulse = new[] { 0.25f, 0.222222f, 0.285714f, 0.24f }[biome];
        float duration = 16f * 6f * pulse;
        float[] data = new float[Mathf.CeilToInt(duration * SampleRate)];
        int[] roots = { 50, 50, 58, 57, 55, 50, 53, 57, 50, 58, 55, 57, 53, 55, 57, 50 };
        int[,] melody =
        {
            { 74, 77, 76, 74, 69, 72 }, { 74, -1, 69, 72, 74, 77 },
            { 77, 81, 79, 77, 74, 72 }, { 76, 73, 69, 73, 76, -1 },
            { 79, 77, 74, 72, 70, 74 }, { 77, 74, 69, 72, 74, -1 },
            { 77, 79, 81, 77, 72, 69 }, { 73, 76, 79, 76, 73, -1 },
            { 81, 77, 74, 77, 81, 84 }, { 82, 81, 77, 74, 77, -1 },
            { 79, 82, 81, 79, 74, 70 }, { 76, 79, 81, 79, 76, 73 },
            { 77, 81, 84, 81, 79, 77 }, { 79, 77, 74, 70, 74, -1 },
            { 76, 73, 69, 73, 76, 81 }, { 77, 76, 74, 74, -1, -1 }
        };
        int transpose = new[] { 0, -2, 3, 0 }[biome];
        var random = new System.Random(7109 + biome);
        for (int bar = 0; bar < 16; bar++)
        {
            float barStart = bar * pulse * 6f;
            int root = roots[bar] + transpose;
            AddNote(data, barStart, pulse * 2.8f, root - 12, 0.16f, 0);
            AddNote(data, barStart + pulse * 3f, pulse * 2.8f, root - 5, 0.12f, 0);
            bool minor = roots[bar] != 58 && roots[bar] != 53 && roots[bar] != 57;
            for (int beat = 0; beat < 6; beat++)
            {
                float start = barStart + beat * pulse;
                int note = melody[bar, beat];
                if (note >= 0) AddNote(data, start, pulse * 0.88f, note + transpose, biome == 2 ? 0.09f : 0.12f, biome == 3 ? 2 : 1);
                int chordTone = beat % 3 == 0 ? root : beat % 3 == 1 ? root + (minor ? 3 : 4) : root + 7;
                AddNote(data, start + 0.018f, pulse * 1.5f, chordTone + 12, biome == 2 ? 0.06f : 0.045f, 2);
                AddPercussion(data, start, beat % 3 == 0, biome, random);
            }
        }
        int delay = Mathf.RoundToInt(pulse * 1.5f * SampleRate);
        float[] dry = (float[])data.Clone();
        for (int i = 0; i < data.Length; i++) data[i] = Mathf.Clamp(data[i] + dry[(i + data.Length - delay) % data.Length] * 0.13f, -0.9f, 0.9f);
        return Clip("Original pirate score - chapter " + (biome + 1), data);
    }

    private static void AddNote(float[] data, float start, float duration, int midi, float gain, int voice)
    {
        float frequency = 440f * Mathf.Pow(2f, (midi - 69f) / 12f);
        int first = Mathf.RoundToInt(start * SampleRate);
        int count = Mathf.RoundToInt(duration * SampleRate);
        for (int i = 0; i < count; i++)
        {
            float time = i / (float)SampleRate;
            float phase = 2f * Mathf.PI * frequency * time;
            float envelope = Mathf.Min(1f, time / 0.008f) * Mathf.Pow(1f - i / (float)count, voice == 2 ? 2.4f : 0.8f);
            float tone = voice == 0 ? Mathf.Sin(phase) + Mathf.Sin(phase * 2f) * 0.15f
                : voice == 1 ? Mathf.Sin(phase + Mathf.Sin(time * 27f) * 0.018f) + Mathf.Sin(phase * 2f) * 0.22f + Mathf.Sin(phase * 3f) * 0.1f
                : Mathf.Sin(phase) + Mathf.Sin(phase * 2.002f) * 0.38f + Mathf.Sin(phase * 3f) * 0.13f;
            data[(first + i) % data.Length] += tone * envelope * gain;
        }
    }

    private static void AddPercussion(float[] data, float start, bool downbeat, int biome, System.Random random)
    {
        int first = Mathf.RoundToInt(start * SampleRate);
        int count = Mathf.RoundToInt((downbeat ? 0.1f : 0.04f) * SampleRate);
        float gain = biome == 2 ? 0.025f : biome == 1 ? 0.055f : 0.038f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float envelope = Mathf.Exp(-t * (downbeat ? 45f : 100f));
            float value = downbeat ? Mathf.Sin(2f * Mathf.PI * (100f * t - 160f * t * t)) : (float)random.NextDouble() * 2f - 1f;
            data[(first + i) % data.Length] += value * envelope * gain;
        }
    }

    private static AudioClip CreateEffect(PirateSound sound)
    {
        float length = sound == PirateSound.Death ? 0.5f : sound == PirateSound.Cannon ? 0.42f : sound == PirateSound.Door ? 0.7f : 0.24f;
        if (sound == PirateSound.Pickup || sound == PirateSound.Checkpoint) length = 0.55f;
        float[] data = new float[Mathf.CeilToInt(length * SampleRate)];
        var random = new System.Random(812 + (int)sound);
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            float p = t / length;
            float envelope = Mathf.Min(1f, t / 0.004f) * Mathf.Pow(1f - p, 1.7f);
            float noise = (float)random.NextDouble() * 2f - 1f;
            float phase;
            float sample;
            switch (sound)
            {
                case PirateSound.Jump: phase = 2f * Mathf.PI * (220f * t + 850f * t * t); sample = Mathf.Sin(phase) * 0.24f; break;
                case PirateSound.Spring: phase = 2f * Mathf.PI * (160f * t + 1800f * t * t); sample = Mathf.Sin(phase + Mathf.Sin(t * 85f) * 2f) * 0.3f; break;
                case PirateSound.Land: sample = (Mathf.Sin(t * 390f) * 0.6f + noise * 0.3f) * 0.2f * Mathf.Exp(-t * 20f); break;
                case PirateSound.Dash: sample = noise * 0.26f + Mathf.Sin(2f * Mathf.PI * (600f * t - 900f * t * t)) * 0.08f; break;
                case PirateSound.Saber: sample = noise * Mathf.Sin(p * Mathf.PI) * 0.3f + Mathf.Sin(t * 4900f) * Mathf.Exp(-t * 30f) * 0.1f; break;
                case PirateSound.Grapple: sample = (Mathf.Sin(t * 3400f) + Mathf.Sin(t * 5900f) * 0.35f) * Mathf.Exp(-t * 15f) * 0.22f; break;
                case PirateSound.Cannon: sample = (noise * 0.55f + Mathf.Sin(2f * Mathf.PI * (90f * t - 65f * t * t)) * 0.55f) * 0.55f; break;
                case PirateSound.Death: sample = Mathf.Sin(2f * Mathf.PI * (250f * t - 175f * t * t)) * 0.24f + noise * 0.04f; break;
                case PirateSound.Parrot: sample = Mathf.Sin(2f * Mathf.PI * (1100f * t + Mathf.Sin(t * 30f) * 4f)) * 0.14f; break;
                default: sample = 0f; break;
            }
            data[i] = sample * envelope;
        }
        if (sound == PirateSound.Pickup || sound == PirateSound.Checkpoint || sound == PirateSound.Door)
        {
            int firstNote = sound == PirateSound.Checkpoint ? 74 : sound == PirateSound.Door ? 62 : 77;
            AddNote(data, 0f, length * 0.6f, firstNote, 0.2f, 2);
            AddNote(data, length * 0.22f, length * 0.6f, firstNote + 3, 0.18f, 2);
            AddNote(data, length * 0.44f, length * 0.55f, firstNote + 7, 0.17f, 2);
        }
        return Clip("Original effect - " + sound, data);
    }

    private static AudioClip Clip(string name, float[] samples)
    {
        AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        foreach (AudioClip clip in sounds.Values) Destroy(clip);
        instance = null;
    }
}
