using System;
using UnityEngine;

[Serializable]
public sealed class PirateAnimationClip
{
    [SerializeField] private Sprite[] frames = Array.Empty<Sprite>();
    [SerializeField, Min(0.1f)] private float framesPerSecond = 10f;
    [SerializeField] private bool loop = true;

    public int FrameCount => frames != null ? frames.Length : 0;
    public bool IsValid
    {
        get
        {
            if (FrameCount == 0) return false;
            foreach (Sprite frame in frames)
            {
                if (frame == null) return false;
            }
            return true;
        }
    }

    public PirateAnimationClip(Sprite[] frames, float framesPerSecond, bool loop)
    {
        this.frames = frames;
        this.framesPerSecond = framesPerSecond;
        this.loop = loop;
    }

    public int GetFrameIndex(float elapsed)
    {
        int frame = Mathf.Max(0, Mathf.FloorToInt(elapsed * framesPerSecond));
        return loop ? frame % Mathf.Max(1, FrameCount) : Mathf.Min(frame, FrameCount - 1);
    }

    public Sprite GetFrame(int index)
    {
        return FrameCount > 0 ? frames[Mathf.Clamp(index, 0, FrameCount - 1)] : null;
    }
}

[CreateAssetMenu(menuName = "Pirate Quest/Animation Library")]
public sealed class PirateAnimationLibrary : ScriptableObject
{
    public const string ResourceName = "PirateAnimationLibrary";

    [SerializeField] private PirateAnimationClip idle;
    [SerializeField] private PirateAnimationClip run;
    [SerializeField] private PirateAnimationClip jump;
    [SerializeField] private PirateAnimationClip jumpApex;
    [SerializeField] private PirateAnimationClip fall;
    [SerializeField] private PirateAnimationClip grapple;
    [SerializeField] private PirateAnimationClip dash;

    public int RunFrameCount => run != null ? run.FrameCount : 0;
    public bool IsComplete => idle != null && idle.IsValid && idle.FrameCount >= 4 &&
                              run != null && run.IsValid && run.FrameCount >= 8 &&
                              jump != null && jump.IsValid && jumpApex != null && jumpApex.IsValid &&
                              fall != null && fall.IsValid && grapple != null && grapple.IsValid &&
                              dash != null && dash.IsValid;

    public PirateAnimationClip GetClip(PlayerVisualAnimator.AnimationState state)
    {
        switch (state)
        {
            case PlayerVisualAnimator.AnimationState.Run: return run;
            case PlayerVisualAnimator.AnimationState.Jump: return jump;
            case PlayerVisualAnimator.AnimationState.JumpApex: return jumpApex;
            case PlayerVisualAnimator.AnimationState.Fall: return fall;
            case PlayerVisualAnimator.AnimationState.Grapple: return grapple;
            case PlayerVisualAnimator.AnimationState.Dash: return dash;
            default: return idle;
        }
    }

#if UNITY_EDITOR
    public void ConfigureFromSheet(Sprite[] orderedFrames)
    {
        if (orderedFrames == null || orderedFrames.Length != 16)
        {
            throw new ArgumentException("The pirate sheet must provide 16 frames in row-major order.");
        }

        idle = new PirateAnimationClip(Slice(orderedFrames, 0, 4), 5f, true);
        run = new PirateAnimationClip(Slice(orderedFrames, 4, 8), 12f, true);
        jump = new PirateAnimationClip(Slice(orderedFrames, 12, 1), 1f, false);
        jumpApex = new PirateAnimationClip(Slice(orderedFrames, 13, 1), 1f, false);
        fall = new PirateAnimationClip(Slice(orderedFrames, 14, 1), 1f, false);
        grapple = new PirateAnimationClip(Slice(orderedFrames, 15, 1), 1f, false);
        dash = new PirateAnimationClip(Slice(orderedFrames, 13, 1), 1f, false);
    }

    private static Sprite[] Slice(Sprite[] source, int start, int count)
    {
        Sprite[] result = new Sprite[count];
        Array.Copy(source, start, result, 0, count);
        return result;
    }
#endif
}
