using UnityEngine;

public enum PirateTreasureKind { Doubloon, Gem, Relic, Letter }

[CreateAssetMenu(menuName = "Pirate Quest/Treasure Art")]
public sealed class PirateTreasureArtLibrary : ScriptableObject
{
    [SerializeField] private Sprite[] sprites = new Sprite[4];
    public bool IsComplete => sprites != null && sprites.Length == 4 &&
        sprites[0] != null && sprites[1] != null && sprites[2] != null && sprites[3] != null;
    public Sprite Get(PirateTreasureKind kind) => IsComplete ? sprites[(int)kind] : null;
    public void Configure(Sprite[] value) => sprites = value;
    public static PirateTreasureArtLibrary Load() => Resources.Load<PirateTreasureArtLibrary>("PirateTreasureArtLibrary");
}
