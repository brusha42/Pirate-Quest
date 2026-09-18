using UnityEngine;

[CreateAssetMenu(fileName = "LevelConfig", menuName = "Game/Level Configuration")]
public class LevelConfiguration : ScriptableObject
{
    [Header("Dimensions")]
    public int levelWidth = 100;
    public int levelHeight = 150;

    [Header("Room Settings")]
    public int minRoomSize = 8;
    public int maxRoomSize = 20;
    public float roomPadding = 2f;
    public int minRoomCount = 8;

    [Header("BSP Settings")]
    public int maxSplitIterations = 4;

    [Header("Corridor Settings")]
    public int corridorWidth = 3;

    [Header("Difficulty Curve")]
    public AnimationCurve difficultyCurve;

    [Header("Object Spawn Chances")]
    [Range(0f, 1f)] public float spikeChance = 0.5f;
    [Range(0f, 1f)] public float chainChance = 0.6f;
    [Range(0f, 1f)] public float ropeChance = 0.4f;
    [Range(0f, 1f)] public float enemyChance = 0.3f;

    [Header("Treasure Settings")]
    public int minTreasureRooms = 2;
    public int maxTreasureRooms = 5;
}