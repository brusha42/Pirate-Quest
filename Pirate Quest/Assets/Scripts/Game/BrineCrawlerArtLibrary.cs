using UnityEngine;

public enum BrineCrawlerFrame { Idle, Walk, Windup, Lunge }

public sealed class BrineCrawlerArtLibrary : ScriptableObject
{
    public const float MaximumWorldWidth = 2.35f;
    [SerializeField] private Sprite[] frames;
    [SerializeField] private Rect[] opaqueBounds;
    [SerializeField] private bool verifiedTransparency;
    public bool IsComplete => frames != null && frames.Length == 4 &&
        frames[0] != null && frames[1] != null && frames[2] != null && frames[3] != null &&
        opaqueBounds != null && opaqueBounds.Length == 4 && verifiedTransparency;
    public Sprite Get(BrineCrawlerFrame frame) => IsComplete ? frames[(int)frame] : null;
    public Rect GetOpaqueBounds(BrineCrawlerFrame frame) => IsComplete ? opaqueBounds[(int)frame] : default;
    public void Configure(Sprite[] sprites, Rect[] alphaBounds, bool realTransparency)
    {
        frames = sprites;
        opaqueBounds = alphaBounds;
        verifiedTransparency = realTransparency;
    }
}
