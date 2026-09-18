using UnityEngine;
using UnityEngine.Tilemaps;
using System.Collections.Generic;
using System.Linq;

public class LevelGenerator : MonoBehaviour
{
    private enum RoomStrategyMode
    {
        Grid,
        Mixed,
        Pyramid,
        JumpPad
    }

    [Header("Level Size Settings")]
    [Tooltip("Желаемое количество комнат. Реальное может отличаться на ±20%")]
    [SerializeField] private int targetRoomCount = 15;
    [Tooltip("Минимальное расстояние между комнатами")]
    [SerializeField] private float roomSpacing = 3f;
    [Tooltip("Насколько плотно располагать комнаты (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float roomDensity = 0.6f;

    [Header("Room Size Settings")]
    [SerializeField] private int minRoomSize = 8;
    [SerializeField] private int maxRoomSize = 20;
    [Tooltip("Предпочтительная ширина комнат")]
    [SerializeField] private int preferredRoomWidth = 15;
    [Tooltip("Предпочтительная высота комнат")]
    [SerializeField] private int preferredRoomHeight = 12;
    [Tooltip("Разброс размеров комнат (0-1)")]
    [Range(0f, 1f)]
    [SerializeField] private float roomSizeVariation = 0.5f;

    [Header("BSP Settings")]
    [SerializeField] private int minSplitSize = 25;
    [Tooltip("Минимальное количество разбиений")]
    [SerializeField] private int minSplits = 3;
    [Tooltip("Максимальное количество разбиений")]
    [SerializeField] private int maxSplits = 6;

    [Header("Corridor Settings")]
    [SerializeField] private int minCorridorWidth = 2;
    [SerializeField] private int maxCorridorWidth = 3;
    [SerializeField] private int maxVerticalCorridorHeight = 12;
    [SerializeField] private bool addPlatformsToTallCorridors = true;

    [Header("Tilemap References")]
    [SerializeField] private Tilemap backgroundTilemap;
    [SerializeField] private Tilemap wallTilemap;
    [SerializeField] private Tilemap platformTilemap;

    [Header("Tile Assets")]
    [SerializeField] private TileBase bgTile;
    [SerializeField] private TileBase wallTile;
    [SerializeField] private TileBase platformTile;

    [Header("Player Movement Settings")]
    [SerializeField] private float playerJumpHeight = 3f;
    [SerializeField] private float playerJumpDistance = 4f;
    [SerializeField] private float playerMaxFallDistance = 8f;

    [Header("Validation Settings")]
    [SerializeField] private bool validateRoomTraversability = true;
    [SerializeField] private bool autoFixUnreachableRooms = true;

    [Header("Generation Control")]
    [Tooltip("Основная башня с единым маршрутом. Старый BSP сохранён для экспериментов.")]
    [SerializeField] private bool useVerifiedTowerLayout = true;
    [SerializeField] private bool generateOnStart = false;
    [SerializeField] private int seed = 20260918;
    [SerializeField] private RoomStrategyMode roomStrategy = RoomStrategyMode.Grid;

    private BSPNode rootNode;
    private List<Room> allRooms = new List<Room>();
    private List<Corridor> allCorridors = new List<Corridor>();
    private readonly List<List<Vector2Int>> guaranteedRoomRoutes = new List<List<Vector2Int>>();
    private int[,] levelGrid;
    private HashSet<Room> validatedRooms = new HashSet<Room>();

    private int levelWidth;
    private int levelHeight;
    private GameObject towerCollisionRoot;
    public TowerRouteLayout TowerRoute { get; private set; }
    public CampaignLayout Campaign { get; private set; }
    public bool UsesTowerRoute => useVerifiedTowerLayout;

    public event System.Action<LevelGenerator> GenerationCompleted;

    public int CurrentSeed => seed;
    public Vector2Int LevelSize => new Vector2Int(levelWidth, levelHeight);
    public int RoomCount => allRooms.Count;
    public int ValidatedRoomCount => validatedRooms.Count;
    public int GuaranteedRouteRoomCount => guaranteedRoomRoutes.Count;
    public bool GuaranteedRoutesValid { get; private set; }
    public bool AllRoomsValidated => allRooms.Count > 0 &&
                                     validatedRooms.Count == allRooms.Count &&
                                     GuaranteedRoutesValid;

    private class Corridor
    {
        public List<Vector2Int> tiles = new List<Vector2Int>();
        public List<CorridorSegment> segments = new List<CorridorSegment>();
        public Room roomA;
        public Room roomB;
        public Vector2Int connectionPointA;
        public Vector2Int connectionPointB;

        public Corridor(Room a, Room b)
        {
            roomA = a;
            roomB = b;
        }
    }

    private class CorridorSegment
    {
        public Vector2Int start;
        public Vector2Int end;
        public bool isVertical;
        public int length;
        public bool needsPlatforms;

        public CorridorSegment(Vector2Int start, Vector2Int end, bool isVertical)
        {
            this.start = start;
            this.end = end;
            this.isVertical = isVertical;
            this.length = isVertical ?
                Mathf.Abs(end.y - start.y) :
                Mathf.Abs(end.x - start.x);
        }
    }

    public enum TileType
    {
        Empty = 0,
        Background = 1,
        Wall = 2,
        Platform = 3
    }

    void Start()
    {
        if (generateOnStart)
        {
            GenerateLevel();
        }
    }

    public void GenerateLevel()
    {
        Random.State previousRandomState = Random.state;
        Random.InitState(seed);

        try
        {
            GenerateLevelInternal();
        }
        finally
        {
            Random.state = previousRandomState;
        }

        GenerationCompleted?.Invoke(this);
    }

    public void GenerateWithSeed(int requestedSeed)
    {
        seed = requestedSeed;
        GenerateLevel();
    }

    private void GenerateLevelInternal()
    {
        CampaignChapter chapter = FindFirstObjectByType<CampaignChapter>();
        if (chapter != null)
        {
            GenerateCampaign(chapter.chapterIndex);
            return;
        }
        if (useVerifiedTowerLayout)
        {
            GenerateTowerRoute();
            return;
        }
        allRooms.Clear();
        allCorridors.Clear();
        validatedRooms.Clear();
        guaranteedRoomRoutes.Clear();
        GuaranteedRoutesValid = false;

        ClearLevel();

        CalculateLevelBounds();
        levelGrid = new int[levelWidth, levelHeight];

        rootNode = new BSPNode(new Rect(0, 0, levelWidth, levelHeight));
        int targetSplits = CalculateOptimalSplits();
        SplitNode(rootNode, 0, targetSplits);

        CreateRooms(rootNode);
        ConnectRooms(rootNode);
        FillLevelGrid();
        AddWalls();
        CalculateRoomConnections();
        PopulateRoomsWithStrategies();
        BuildGuaranteedRoomRoutes();

        if (addPlatformsToTallCorridors)
        {
            AddPlatformsToCorridors();
        }

        if (validateRoomTraversability)
        {
            ValidateRoomTraversability();
        }

        BuildLevel();
    }

    private void CalculateLevelBounds()
    {
        float aspectRatio = 0.75f;
        int roomsHorizontal = Mathf.CeilToInt(Mathf.Sqrt(targetRoomCount * aspectRatio));
        int roomsVertical = Mathf.CeilToInt(targetRoomCount / (float)roomsHorizontal);

        int avgRoomWidth = Mathf.RoundToInt(preferredRoomWidth * (1f + roomSizeVariation * 0.5f));
        int avgRoomHeight = Mathf.RoundToInt(preferredRoomHeight * (1f + roomSizeVariation * 0.5f));

        levelWidth = roomsHorizontal * avgRoomWidth + (roomsHorizontal - 1) * Mathf.RoundToInt(roomSpacing * 2);
        levelHeight = roomsVertical * avgRoomHeight + (roomsVertical - 1) * Mathf.RoundToInt(roomSpacing * 2);

        levelWidth = Mathf.RoundToInt(levelWidth * (2f - roomDensity));
        levelHeight = Mathf.RoundToInt(levelHeight * (2f - roomDensity));

        levelWidth = Mathf.Clamp(levelWidth, 60, 300);
        levelHeight = Mathf.Clamp(levelHeight, 80, 400);
    }

    private int CalculateOptimalSplits()
    {
        int optimalSplits = Mathf.CeilToInt(Mathf.Log(targetRoomCount, 2));
        return Mathf.Clamp(optimalSplits, minSplits, maxSplits);
    }

    public void ClearLevel()
    {
        if (towerCollisionRoot != null)
        {
            towerCollisionRoot.SetActive(false);
            if (Application.isPlaying) Destroy(towerCollisionRoot);
            else DestroyImmediate(towerCollisionRoot);
            towerCollisionRoot = null;
        }
        TowerRoute = null;
        Campaign = null;
        if (backgroundTilemap != null) backgroundTilemap.ClearAllTiles();
        if (wallTilemap != null) wallTilemap.ClearAllTiles();
        if (platformTilemap != null) platformTilemap.ClearAllTiles();

        allRooms.Clear();
        allCorridors.Clear();
        validatedRooms.Clear();
        guaranteedRoomRoutes.Clear();
        GuaranteedRoutesValid = false;
    }

    public void RegenerateRoomContent()
    {
        if (useVerifiedTowerLayout)
        {
            GenerateLevel();
            return;
        }
        if (allRooms.Count == 0)
            return;

        if (platformTilemap != null) platformTilemap.ClearAllTiles();

        for (int x = 0; x < levelWidth; x++)
        {
            for (int y = 0; y < levelHeight; y++)
            {
                if (levelGrid[x, y] == (int)TileType.Platform)
                {
                    levelGrid[x, y] = (int)TileType.Background;
                }
            }
        }

        CalculateRoomConnections();
        PopulateRoomsWithStrategies();
        BuildGuaranteedRoomRoutes();

        if (addPlatformsToTallCorridors)
        {
            AddPlatformsToCorridors();
        }

        if (validateRoomTraversability)
        {
            ValidateRoomTraversability();
        }

        BuildLevelContent();
    }

    #region BSP Splitting

    private void SplitNode(BSPNode node, int iteration, int maxIterations)
    {
        if (iteration >= maxIterations)
            return;

        if (node.bounds.width < minSplitSize && node.bounds.height < minSplitSize)
            return;

        bool splitHorizontal = Random.value > 0.5f;

        float aspectRatio = node.bounds.width / node.bounds.height;

        if (aspectRatio > 1.3f)
        {
            splitHorizontal = false;
        }
        else if (aspectRatio < 0.7f)
        {
            splitHorizontal = true;
        }

        float dimensionToSplit = splitHorizontal ? node.bounds.height : node.bounds.width;

        if (dimensionToSplit < minSplitSize * 2)
        {
            splitHorizontal = !splitHorizontal;
            dimensionToSplit = splitHorizontal ? node.bounds.height : node.bounds.width;

            if (dimensionToSplit < minSplitSize * 2)
                return;
        }

        float minSplit = minSplitSize;
        float maxSplit = dimensionToSplit - minSplitSize;

        if (maxSplit <= minSplit)
            return;

        float centerPoint = dimensionToSplit / 2f;
        float variation = (dimensionToSplit * 0.3f) * roomSizeVariation;
        float split = centerPoint + Random.Range(-variation, variation);
        split = Mathf.Clamp(split, minSplit, maxSplit);

        if (splitHorizontal)
        {
            node.left = new BSPNode(new Rect(node.bounds.x, node.bounds.y, node.bounds.width, split));
            node.right = new BSPNode(new Rect(node.bounds.x, node.bounds.y + split, node.bounds.width, node.bounds.height - split));
        }
        else
        {
            node.left = new BSPNode(new Rect(node.bounds.x, node.bounds.y, split, node.bounds.height));
            node.right = new BSPNode(new Rect(node.bounds.x + split, node.bounds.y, node.bounds.width - split, node.bounds.height));
        }

        SplitNode(node.left, iteration + 1, maxIterations);
        SplitNode(node.right, iteration + 1, maxIterations);
    }

    #endregion

    #region Room Creation

    private void CreateRooms(BSPNode node)
    {
        if (node == null)
            return;

        if (node.IsLeaf())
        {
            float widthVariation = preferredRoomWidth * roomSizeVariation;
            float heightVariation = preferredRoomHeight * roomSizeVariation;

            float targetWidth = preferredRoomWidth + Random.Range(-widthVariation, widthVariation);
            float targetHeight = preferredRoomHeight + Random.Range(-heightVariation, heightVariation);

            float width = Mathf.Clamp(targetWidth, minRoomSize, Mathf.Min(maxRoomSize, node.bounds.width - roomSpacing * 2));
            float height = Mathf.Clamp(targetHeight, minRoomSize, Mathf.Min(maxRoomSize, node.bounds.height - roomSpacing * 2));

            if (width < minRoomSize || height < minRoomSize)
                return;

            float maxOffsetX = Mathf.Max(0, node.bounds.width - width - roomSpacing * 2);
            float maxOffsetY = Mathf.Max(0, node.bounds.height - height - roomSpacing * 2);

            float offsetX = maxOffsetX > 0 ? Random.Range(0, maxOffsetX) : 0;
            float offsetY = maxOffsetY > 0 ? Random.Range(0, maxOffsetY) : 0;

            float x = node.bounds.x + roomSpacing + offsetX;
            float y = node.bounds.y + roomSpacing + offsetY;

            node.room = new Room(new Rect(x, y, width, height));
            allRooms.Add(node.room);
        }
        else
        {
            CreateRooms(node.left);
            CreateRooms(node.right);
        }
    }

    #endregion

    #region Room Connection

    private void ConnectRooms(BSPNode node)
    {
        if (node == null || node.IsLeaf())
            return;

        ConnectRooms(node.left);
        ConnectRooms(node.right);

        List<Room> leftRooms = GetRoomsFromNode(node.left);
        List<Room> rightRooms = GetRoomsFromNode(node.right);

        if (leftRooms.Count > 0 && rightRooms.Count > 0)
        {
            Room roomA = null;
            Room roomB = null;
            float minDistance = float.MaxValue;

            foreach (Room left in leftRooms)
            {
                foreach (Room right in rightRooms)
                {
                    float distance = Vector2.Distance(left.bounds.center, right.bounds.center);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        roomA = left;
                        roomB = right;
                    }
                }
            }

            if (roomA != null && roomB != null)
            {
                CreateSmartCorridor(roomA, roomB);

                roomA.connectedRooms.Add(roomB);
                roomB.connectedRooms.Add(roomA);
            }
        }
    }

    private List<Room> GetRoomsFromNode(BSPNode node)
    {
        List<Room> rooms = new List<Room>();

        if (node == null)
            return rooms;

        if (node.IsLeaf() && node.room != null)
        {
            rooms.Add(node.room);
        }
        else
        {
            rooms.AddRange(GetRoomsFromNode(node.left));
            rooms.AddRange(GetRoomsFromNode(node.right));
        }

        return rooms;
    }

    private void CreateSmartCorridor(Room roomA, Room roomB)
    {
        Corridor corridor = new Corridor(roomA, roomB);

        Vector2Int pointA = FindBestConnectionPoint(roomA, roomB);
        Vector2Int pointB = FindBestConnectionPoint(roomB, roomA);

        corridor.connectionPointA = pointA;
        corridor.connectionPointB = pointB;

        roomA.exitPoints.Add(pointA);
        roomB.exitPoints.Add(pointB);

        int deltaY = Mathf.Abs(pointB.y - pointA.y);

        if (deltaY > maxVerticalCorridorHeight)
        {
            CreateStaircaseCorridor(pointA, pointB, corridor);
        }
        else
        {
            CreateLShapedCorridor(pointA, pointB, corridor);
        }

        allCorridors.Add(corridor);
    }

    private Vector2Int FindBestConnectionPoint(Room fromRoom, Room toRoom)
    {
        Vector2 direction = toRoom.bounds.center - fromRoom.bounds.center;

        int x, y;

        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
        {
            if (direction.x > 0)
            {
                x = (int)fromRoom.bounds.xMax - 1;
                y = Mathf.Clamp((int)toRoom.bounds.center.y,
                    (int)fromRoom.bounds.yMin + 2,
                    (int)fromRoom.bounds.yMax - 2);
            }
            else
            {
                x = (int)fromRoom.bounds.xMin + 1;
                y = Mathf.Clamp((int)toRoom.bounds.center.y,
                    (int)fromRoom.bounds.yMin + 2,
                    (int)fromRoom.bounds.yMax - 2);
            }
        }
        else
        {
            x = Mathf.Clamp((int)toRoom.bounds.center.x,
                (int)fromRoom.bounds.xMin + 2,
                (int)fromRoom.bounds.xMax - 2);

            if (direction.y > 0)
            {
                y = (int)fromRoom.bounds.yMax - 1;
            }
            else
            {
                y = (int)fromRoom.bounds.yMin + 2;
            }
        }

        return new Vector2Int(x, y);
    }

    private void CreateLShapedCorridor(Vector2Int pointA, Vector2Int pointB, Corridor corridor)
    {
        int corridorWidth = Random.Range(minCorridorWidth, maxCorridorWidth + 1);

        if (Random.value > 0.5f)
        {
            CorridorSegment horizontal = new CorridorSegment(pointA,
                new Vector2Int(pointB.x, pointA.y), false);
            CorridorSegment vertical = new CorridorSegment(
                new Vector2Int(pointB.x, pointA.y), pointB, true);

            corridor.segments.Add(horizontal);
            corridor.segments.Add(vertical);

            CreateHorizontalCorridor(pointA.x, pointB.x, pointA.y, corridorWidth, corridor);
            CreateVerticalCorridor(pointA.y, pointB.y, pointB.x, corridorWidth, corridor);
        }
        else
        {
            CorridorSegment vertical = new CorridorSegment(pointA,
                new Vector2Int(pointA.x, pointB.y), true);
            CorridorSegment horizontal = new CorridorSegment(
                new Vector2Int(pointA.x, pointB.y), pointB, false);

            corridor.segments.Add(vertical);
            corridor.segments.Add(horizontal);

            CreateVerticalCorridor(pointA.y, pointB.y, pointA.x, corridorWidth, corridor);
            CreateHorizontalCorridor(pointA.x, pointB.x, pointB.y, corridorWidth, corridor);
        }
    }

    private void CreateStaircaseCorridor(Vector2Int pointA, Vector2Int pointB, Corridor corridor)
    {
        int corridorWidth = Random.Range(minCorridorWidth, maxCorridorWidth + 1);

        int currentX = pointA.x;
        int currentY = pointA.y;
        int targetX = pointB.x;
        int targetY = pointB.y;

        int directionX = targetX > currentX ? 1 : -1;
        int stepHeight = maxVerticalCorridorHeight / 2;
        int stepWidth = 4;

        while (Mathf.Abs(currentY - targetY) > stepHeight)
        {
            int nextX = currentX + stepWidth * directionX;
            CorridorSegment horizontal = new CorridorSegment(
                new Vector2Int(currentX, currentY),
                new Vector2Int(nextX, currentY), false);
            corridor.segments.Add(horizontal);
            CreateHorizontalCorridor(currentX, nextX, currentY, corridorWidth, corridor);

            int nextY = currentY + (targetY > currentY ? stepHeight : -stepHeight);
            CorridorSegment vertical = new CorridorSegment(
                new Vector2Int(nextX, currentY),
                new Vector2Int(nextX, nextY), true);
            vertical.needsPlatforms = true;
            corridor.segments.Add(vertical);
            CreateVerticalCorridor(currentY, nextY, nextX, corridorWidth, corridor);

            currentX = nextX;
            currentY = nextY;
        }

        if (currentX != targetX)
        {
            CreateHorizontalCorridor(currentX, targetX, currentY, corridorWidth, corridor);
        }
        if (currentY != targetY)
        {
            CreateVerticalCorridor(currentY, targetY, targetX, corridorWidth, corridor);
        }
    }

    private void CreateHorizontalCorridor(int x1, int x2, int y, int width, Corridor corridor)
    {
        int start = Mathf.Min(x1, x2);
        int end = Mathf.Max(x1, x2);

        for (int x = start; x <= end; x++)
        {
            for (int offset = 0; offset < width; offset++)
            {
                int currentY = y + offset;
                if (IsInBounds(x, currentY))
                {
                    corridor.tiles.Add(new Vector2Int(x, currentY));
                }
            }
        }
    }

    private void CreateVerticalCorridor(int y1, int y2, int x, int width, Corridor corridor)
    {
        int start = Mathf.Min(y1, y2);
        int end = Mathf.Max(y1, y2);

        for (int y = start; y <= end; y++)
        {
            for (int offset = 0; offset < width; offset++)
            {
                int currentX = x + offset;
                if (IsInBounds(currentX, y))
                {
                    corridor.tiles.Add(new Vector2Int(currentX, y));
                }
            }
        }
    }

    #endregion

    #region Level Grid

    private void FillLevelGrid()
    {
        for (int x = 0; x < levelWidth; x++)
        {
            for (int y = 0; y < levelHeight; y++)
            {
                levelGrid[x, y] = (int)TileType.Empty;
            }
        }

        foreach (Room room in allRooms)
        {
            for (int x = (int)room.bounds.xMin; x < (int)room.bounds.xMax; x++)
            {
                for (int y = (int)room.bounds.yMin; y < (int)room.bounds.yMax; y++)
                {
                    if (IsInBounds(x, y))
                    {
                        levelGrid[x, y] = (int)TileType.Background;
                    }
                }
            }
        }

        foreach (Corridor corridor in allCorridors)
        {
            foreach (Vector2Int tile in corridor.tiles)
            {
                if (IsInBounds(tile.x, tile.y))
                {
                    levelGrid[tile.x, tile.y] = (int)TileType.Background;
                }
            }
        }

        ClearRoomEntrances();
    }

    private void ClearRoomEntrances()
    {
        foreach (Corridor corridor in allCorridors)
        {
            ClearEntranceArea(corridor.connectionPointA, 2);
            ClearEntranceArea(corridor.connectionPointB, 2);
        }
    }

    private void ClearEntranceArea(Vector2Int center, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int x = center.x + dx;
                int y = center.y + dy;

                if (IsInBounds(x, y))
                {
                    levelGrid[x, y] = (int)TileType.Background;
                }
            }
        }
    }

    private void AddWalls()
    {
        int[,] tempGrid = new int[levelWidth, levelHeight];
        System.Array.Copy(levelGrid, tempGrid, levelGrid.Length);

        for (int x = 0; x < levelWidth; x++)
        {
            for (int y = 0; y < levelHeight; y++)
            {
                if (tempGrid[x, y] == (int)TileType.Background)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int nx = x + dx;
                            int ny = y + dy;

                            if (IsInBounds(nx, ny) && tempGrid[nx, ny] == (int)TileType.Empty)
                            {
                                levelGrid[nx, ny] = (int)TileType.Wall;
                            }
                        }
                    }
                }
            }
        }

        foreach (Room room in allRooms)
        {
            for (int x = (int)room.bounds.xMin; x < (int)room.bounds.xMax; x++)
            {
                int floorY = (int)room.bounds.yMin;

                if (IsInBounds(x, floorY))
                {
                    bool isEntrance = false;

                    foreach (Vector2Int entrance in room.exitPoints)
                    {
                        if (Mathf.Abs(entrance.x - x) <= 2 && Mathf.Abs(entrance.y - floorY) <= 1)
                        {
                            isEntrance = true;
                            break;
                        }
                    }

                    if (!isEntrance && levelGrid[x, floorY] != (int)TileType.Background)
                    {
                        levelGrid[x, floorY] = (int)TileType.Wall;
                    }
                }
            }
        }

        CleanupWallsAroundCorridors();
    }

    private void CleanupWallsAroundCorridors()
    {
        foreach (Corridor corridor in allCorridors)
        {
            foreach (Vector2Int tile in corridor.tiles)
            {
                if (IsInBounds(tile.x, tile.y))
                {
                    if (levelGrid[tile.x, tile.y] == (int)TileType.Wall)
                    {
                        levelGrid[tile.x, tile.y] = (int)TileType.Background;
                    }
                }
            }

            ClearConnectionPoint(corridor.connectionPointA);
            ClearConnectionPoint(corridor.connectionPointB);
        }
    }

    private void ClearConnectionPoint(Vector2Int point)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int x = point.x + dx;
                int y = point.y + dy;

                if (IsInBounds(x, y))
                {
                    if (levelGrid[x, y] == (int)TileType.Wall)
                    {
                        bool isRoomFloor = false;

                        foreach (Room room in allRooms)
                        {
                            if (x >= room.bounds.xMin && x < room.bounds.xMax &&
                                y == (int)room.bounds.yMin)
                            {
                                float dist = Vector2.Distance(new Vector2(x, y), point);
                                if (dist > 2f)
                                {
                                    isRoomFloor = true;
                                    break;
                                }
                            }
                        }

                        if (!isRoomFloor)
                        {
                            levelGrid[x, y] = (int)TileType.Background;
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region Corridor Platforms

    private void AddPlatformsToCorridors()
    {
        foreach (Corridor corridor in allCorridors)
        {
            foreach (CorridorSegment segment in corridor.segments)
            {
                if (segment.isVertical && (segment.length > maxVerticalCorridorHeight / 2 || segment.needsPlatforms))
                {
                    AddPlatformsToVerticalSegment(segment);
                }
            }
        }
    }

    private void AddPlatformsToVerticalSegment(CorridorSegment segment)
    {
        int platformSpacing = Mathf.Max(1, Mathf.FloorToInt(playerJumpHeight * 0.75f));
        int startY = Mathf.Min(segment.start.y, segment.end.y);
        int endY = Mathf.Max(segment.start.y, segment.end.y);

        for (int y = startY + platformSpacing; y < endY; y += platformSpacing)
        {
            int platformWidth = Random.Range(2, 4);
            int platformX = segment.start.x;

            for (int x = 0; x < platformWidth; x++)
            {
                if (IsInBounds(platformX + x, y) && levelGrid[platformX + x, y] == (int)TileType.Background)
                {
                    levelGrid[platformX + x, y] = (int)TileType.Platform;
                }
            }
        }
    }

    #endregion

    #region Traversability Validation

    private void ValidateRoomTraversability()
    {
        validatedRooms.Clear();

        if (allRooms.Count == 0)
            return;

        Room startRoom = allRooms.OrderBy(r => r.bounds.y).First();

        Queue<Room> toCheck = new Queue<Room>();
        toCheck.Enqueue(startRoom);
        validatedRooms.Add(startRoom);

        while (toCheck.Count > 0)
        {
            Room current = toCheck.Dequeue();

            foreach (Room connected in current.connectedRooms)
            {
                if (!validatedRooms.Contains(connected))
                {
                    if (IsRoomTraversable(current, connected))
                    {
                        validatedRooms.Add(connected);
                        toCheck.Enqueue(connected);
                    }
                    else if (autoFixUnreachableRooms)
                    {
                        FixRoomConnection(current, connected);

                        if (IsRoomTraversable(current, connected))
                        {
                            validatedRooms.Add(connected);
                            toCheck.Enqueue(connected);
                        }
                    }
                }
            }
        }
    }

    private bool IsRoomTraversable(Room from, Room to)
    {
        Corridor corridor = allCorridors.Find(c =>
            (c.roomA == from && c.roomB == to) ||
            (c.roomA == to && c.roomB == from));

        if (corridor == null)
            return false;

        foreach (CorridorSegment segment in corridor.segments)
        {
            if (segment.isVertical &&
                segment.length > playerJumpHeight * 1.5f &&
                !segment.needsPlatforms)
            {
                return false;
            }
        }

        return true;
    }

    private void FixRoomConnection(Room from, Room to)
    {
        Corridor corridor = allCorridors.Find(c =>
            (c.roomA == from && c.roomB == to) ||
            (c.roomA == to && c.roomB == from));

        if (corridor == null)
            return;

        foreach (CorridorSegment segment in corridor.segments)
        {
            if (segment.isVertical && segment.length > playerJumpHeight * 1.5f)
            {
                segment.needsPlatforms = true;
                AddPlatformsToVerticalSegment(segment);
            }
        }
    }

    #endregion

    #region Room Connections Calculation

    private void CalculateRoomConnections()
    {
        foreach (Room room in allRooms)
        {
            room.entrances.Clear();
            room.exits.Clear();

            foreach (Vector2Int exitPoint in room.exitPoints)
            {
                if (exitPoint.y < room.bounds.center.y)
                {
                    room.entrances.Add(exitPoint);
                }
                else
                {
                    room.exits.Add(exitPoint);
                }
            }

            if (room.entrances.Count == 0 && room.exits.Count == 0)
            {
                if (room.exitPoints.Count > 0)
                {
                    room.entrances.Add(room.exitPoints[0]);
                    if (room.exitPoints.Count > 1)
                    {
                        room.exits.Add(room.exitPoints[room.exitPoints.Count - 1]);
                    }
                }
            }
        }
    }

    #endregion

    #region Room Population with Strategies

    private void PopulateRoomsWithStrategies()
    {
        foreach (Room room in allRooms)
        {
            IRoomStrategy strategy = SelectStrategy();

            if (strategy != null)
            {
                strategy.PopulateRoom(room, levelGrid, this);
            }
        }
    }

    private IRoomStrategy SelectStrategy()
    {
        switch (roomStrategy)
        {
            case RoomStrategyMode.Pyramid:
                return new PyramidStrategy(playerJumpHeight, playerJumpDistance);
            case RoomStrategyMode.JumpPad:
                return new JumpPadStrategy(playerJumpHeight, playerJumpDistance);
            case RoomStrategyMode.Mixed:
                float random = Random.value;
                if (random < 0.4f)
                {
                    return new PyramidStrategy(playerJumpHeight, playerJumpDistance);
                }

                return random < 0.8f
                    ? new GridStrategy(playerJumpHeight, playerJumpDistance, playerMaxFallDistance)
                    : new JumpPadStrategy(playerJumpHeight, playerJumpDistance);
            default:
                return new GridStrategy(playerJumpHeight, playerJumpDistance, playerMaxFallDistance);
        }
    }

    private void BuildGuaranteedRoomRoutes()
    {
        guaranteedRoomRoutes.Clear();

        int verticalStep = Mathf.Max(1, Mathf.RoundToInt(playerJumpHeight * 0.65f));
        int horizontalStep = Mathf.Max(1, Mathf.FloorToInt(playerJumpDistance * 0.5f));

        foreach (Room room in allRooms)
        {
            int floorY = Mathf.RoundToInt(room.bounds.yMin);
            int highestPlatformY = Mathf.RoundToInt(room.bounds.yMax) - 2;
            int roomCenterX = Mathf.RoundToInt(room.bounds.center.x);
            int minimumCenterX = Mathf.CeilToInt(room.bounds.xMin) + 3;
            int maximumCenterX = Mathf.FloorToInt(room.bounds.xMax) - 3;

            if (minimumCenterX > maximumCenterX)
            {
                minimumCenterX = maximumCenterX = roomCenterX;
            }

            List<Vector2Int> route = new List<Vector2Int>
            {
                new Vector2Int(Mathf.Clamp(roomCenterX, minimumCenterX, maximumCenterX), floorY)
            };

            int stepIndex = 0;
            for (int y = floorY + verticalStep; y <= highestPlatformY; y += verticalStep)
            {
                int direction = stepIndex % 2 == 0 ? -1 : 1;
                int platformCenterX = Mathf.Clamp(
                    roomCenterX + direction * horizontalStep,
                    minimumCenterX,
                    maximumCenterX);

                PlaceGuaranteedPlatform(platformCenterX, y);
                route.Add(new Vector2Int(platformCenterX, y));
                stepIndex++;
            }

            AddConnectionLedges(room, route);

            guaranteedRoomRoutes.Add(route);
        }

        GuaranteedRoutesValid = ValidateGuaranteedRoomRoutes(verticalStep);
    }

    private void PlaceGuaranteedPlatform(int centerX, int y)
    {
        const int halfWidth = 2;

        for (int x = centerX - halfWidth; x <= centerX + halfWidth; x++)
        {
            SetPlatform(x, y);
        }
    }

    private void AddConnectionLedges(Room room, IReadOnlyList<Vector2Int> route)
    {
        if (route.Count < 2)
        {
            return;
        }

        int minimumX = Mathf.CeilToInt(room.bounds.xMin) + 1;
        int maximumX = Mathf.FloorToInt(room.bounds.xMax) - 2;

        foreach (Vector2Int connection in room.exitPoints)
        {
            Vector2Int nearestStep = route[1];

            for (int stepIndex = 2; stepIndex < route.Count; stepIndex++)
            {
                if (Mathf.Abs(route[stepIndex].y - connection.y) <
                    Mathf.Abs(nearestStep.y - connection.y))
                {
                    nearestStep = route[stepIndex];
                }
            }

            int connectionX = Mathf.Clamp(connection.x, minimumX, maximumX);
            int startX = Mathf.Min(connectionX, nearestStep.x);
            int endX = Mathf.Max(connectionX, nearestStep.x);

            for (int x = startX; x <= endX; x++)
            {
                SetPlatform(x, nearestStep.y);
            }
        }
    }

    private bool ValidateGuaranteedRoomRoutes(int maximumVerticalStep)
    {
        if (guaranteedRoomRoutes.Count != allRooms.Count || guaranteedRoomRoutes.Count == 0)
        {
            return false;
        }

        for (int roomIndex = 0; roomIndex < guaranteedRoomRoutes.Count; roomIndex++)
        {
            List<Vector2Int> route = guaranteedRoomRoutes[roomIndex];
            Room room = allRooms[roomIndex];

            if (route.Count < 2)
            {
                return false;
            }

            for (int stepIndex = 1; stepIndex < route.Count; stepIndex++)
            {
                Vector2Int previous = route[stepIndex - 1];
                Vector2Int current = route[stepIndex];
                int verticalDistance = current.y - previous.y;
                int horizontalDistance = Mathf.Abs(current.x - previous.x);

                if (verticalDistance <= 0 || verticalDistance > maximumVerticalStep ||
                    horizontalDistance > playerJumpDistance ||
                    !IsInBounds(current.x, current.y) ||
                    levelGrid[current.x, current.y] != (int)TileType.Platform)
                {
                    return false;
                }
            }

            int expectedTop = Mathf.RoundToInt(room.bounds.yMax) - 2 - maximumVerticalStep;
            if (route[route.Count - 1].y < expectedTop)
            {
                return false;
            }

            int minimumX = Mathf.CeilToInt(room.bounds.xMin) + 1;
            int maximumX = Mathf.FloorToInt(room.bounds.xMax) - 2;

            foreach (Vector2Int connection in room.exitPoints)
            {
                Vector2Int nearestStep = route
                    .Skip(1)
                    .OrderBy(step => Mathf.Abs(step.y - connection.y))
                    .First();
                int connectionX = Mathf.Clamp(connection.x, minimumX, maximumX);

                for (int x = Mathf.Min(connectionX, nearestStep.x);
                     x <= Mathf.Max(connectionX, nearestStep.x);
                     x++)
                {
                    if (!IsInBounds(x, nearestStep.y) ||
                        levelGrid[x, nearestStep.y] != (int)TileType.Platform)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    #endregion

    #region Level Building

    private void BuildLevel()
    {
        if (backgroundTilemap == null || wallTilemap == null)
            return;

        for (int x = 0; x < levelWidth; x++)
        {
            for (int y = 0; y < levelHeight; y++)
            {
                TileType tileType = (TileType)levelGrid[x, y];
                Vector3Int tilePosition = new Vector3Int(x, y, 0);

                switch (tileType)
                {
                    case TileType.Background:
                        if (backgroundTilemap != null && bgTile != null)
                            backgroundTilemap.SetTile(tilePosition, bgTile);
                        break;

                    case TileType.Wall:
                        if (wallTilemap != null && wallTile != null)
                            wallTilemap.SetTile(tilePosition, wallTile);
                        break;

                    case TileType.Platform:
                        if (platformTilemap != null && platformTile != null)
                            platformTilemap.SetTile(tilePosition, platformTile);
                        break;
                }
            }
        }

        SetupTilemapColliders();
        RefreshTilemaps();
    }

    private void BuildLevelContent()
    {
        for (int x = 0; x < levelWidth; x++)
        {
            for (int y = 0; y < levelHeight; y++)
            {
                TileType tileType = (TileType)levelGrid[x, y];
                Vector3Int tilePosition = new Vector3Int(x, y, 0);

                if (tileType == TileType.Platform)
                {
                    if (platformTilemap != null && platformTile != null)
                        platformTilemap.SetTile(tilePosition, platformTile);
                }
            }
        }

        RefreshTilemaps();
    }

    private void SetupTilemapColliders()
    {
        SetupSolidCollider(wallTilemap);
        SetupPlatformCollider(platformTilemap);
    }

    private void SetupSolidCollider(Tilemap tilemap)
    {
        if (tilemap == null) return;

        TilemapCollider2D tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
        if (tilemapCollider == null)
        {
            tilemapCollider = tilemap.gameObject.AddComponent<TilemapCollider2D>();
        }

        CompositeCollider2D compositeCollider = tilemap.GetComponent<CompositeCollider2D>();
        if (compositeCollider == null)
        {
            compositeCollider = tilemap.gameObject.AddComponent<CompositeCollider2D>();
            tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
        }

        Rigidbody2D rb = tilemap.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Static;
        }
    }

    private void SetupPlatformCollider(Tilemap tilemap)
    {
        if (tilemap == null) return;

        TilemapCollider2D tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
        if (tilemapCollider == null)
        {
            tilemapCollider = tilemap.gameObject.AddComponent<TilemapCollider2D>();
        }

        PlatformEffector2D platformEffector = tilemap.GetComponent<PlatformEffector2D>();
        if (platformEffector == null)
        {
            platformEffector = tilemap.gameObject.AddComponent<PlatformEffector2D>();
        }

        tilemapCollider.usedByEffector = true;
        platformEffector.useOneWay = true;
        platformEffector.surfaceArc = 180f;
        platformEffector.sideArc = 1f;

        Rigidbody2D rb = tilemap.GetComponent<Rigidbody2D>();
        if (rb == null)
        {
            rb = tilemap.gameObject.AddComponent<Rigidbody2D>();
        }
        rb.bodyType = RigidbodyType2D.Static;
    }

    private void RefreshTilemaps()
    {
        if (backgroundTilemap != null) backgroundTilemap.RefreshAllTiles();
        if (wallTilemap != null) wallTilemap.RefreshAllTiles();
        if (platformTilemap != null) platformTilemap.RefreshAllTiles();
    }

    #endregion

    #region Utilities

    private void GenerateCampaign(int chapterIndex)
    {
        ClearLevel();
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        if (player == null) throw new System.InvalidOperationException("Campaign requires the actual player jump settings.");
        player.ConfigureChapterSpeed(chapterIndex);
        Campaign = CampaignLayout.Create(seed, chapterIndex, player.JumpLaunchSpeed,
            player.GravityStrength, Time.fixedDeltaTime, player.MoveSpeed);
        levelWidth = Campaign.Bounds.xMax;
        levelHeight = Campaign.Bounds.yMax;
        levelGrid = new int[levelWidth, levelHeight];

        foreach (Collider2D existing in platformTilemap.GetComponents<Collider2D>()) existing.enabled = false;
        foreach (Collider2D existing in wallTilemap.GetComponents<Collider2D>()) existing.enabled = false;
        towerCollisionRoot = new GameObject("Campaign Physical Geometry");
        towerCollisionRoot.transform.SetParent(transform, false);
        backgroundTilemap.GetComponent<TilemapRenderer>().sortingOrder = -20;
        wallTilemap.GetComponent<TilemapRenderer>().sortingOrder = 0;
        platformTilemap.GetComponent<TilemapRenderer>().sortingOrder = 5;
        Color[] backgrounds = { new Color(.58f,.65f,.72f), new Color(.69f,.59f,.55f), new Color(.55f,.68f,.6f), new Color(.63f,.6f,.75f) };
        backgroundTilemap.color = backgrounds[chapterIndex];
        wallTilemap.color = PirateWorldArt.WallTint(chapterIndex);
        platformTilemap.color = new Color(1f,.79f,.49f);

        for (int x = 0; x < levelWidth; x++)
        for (int y = 0; y < levelHeight; y++)
        {
            if (!Campaign.ContainsArchitectureCell(x, y)) continue;
            levelGrid[x,y] = (int)TileType.Background;
            int material = Campaign.ContainsWorldCell(x, y) ? 0 : 1;
            backgroundTilemap.SetTile(new Vector3Int(x,y,0), PirateWorldArt.GetTile(chapterIndex, material));
        }
        int solidIndex = 0;
        foreach (RectInt solid in Campaign.Solids)
        {
            AddTowerCollider($"Campaign Wall {solidIndex++}", solid.center, solid.size, false);
            for (int x = solid.xMin; x < solid.xMax; x++)
            for (int y = solid.yMin; y < solid.yMax; y++)
            {
                if (!IsInBounds(x,y)) continue;
                levelGrid[x,y] = (int)TileType.Wall;
                wallTilemap.SetTile(new Vector3Int(x,y,0), PirateWorldArt.GetTile(chapterIndex, 1));
            }
        }
        int platformIndex = 0;
        foreach (var platform in Campaign.Platforms)
        {
            AddTowerCollider($"Campaign Landing {platformIndex++}", platform.Bounds.center,
                platform.Bounds.size, platform.OneWay);
            GameObject art = PirateWorldArt.CreatePlatform("Timber Ledge", platform.Bounds,
                chapterIndex, towerCollisionRoot.transform);
            foreach (SpriteRenderer renderer in art.GetComponentsInChildren<SpriteRenderer>()) renderer.sortingOrder = 5;
        }
        foreach (CampaignLayout.SecretCache cache in Campaign.SecretCaches)
        {
            if (!cache.Concealed) continue;
            GameObject cover = new GameObject("Hidden treasure chamber " + cache.Id);
            cover.transform.SetParent(towerCollisionRoot.transform, false);
            cover.AddComponent<SecretCacheCover>().Initialize(cache, chapterIndex, player.transform);
        }
        foreach (var roomData in Campaign.Rooms.Where(r => r.IsLeaf))
        {
            RectInt bounds = roomData.Bounds;
            Room room = new Room(new Rect(bounds.x,bounds.y,bounds.width,bounds.height));
            if (allRooms.Count > 0)
            {
                Room previous = allRooms[allRooms.Count - 1];
                previous.connectedRooms.Add(room); room.connectedRooms.Add(previous);
            }
            allRooms.Add(room);
        }
        GuaranteedRoutesValid = false;
        Physics2D.SyncTransforms();
    }

    private void GenerateTowerRoute()
    {
        ClearLevel();
        PlayerMovement player = FindFirstObjectByType<PlayerMovement>();
        if (player == null) throw new System.InvalidOperationException("Tower requires PlayerMovement to size jumps.");
        TowerRoute = TowerRouteLayout.Create(seed, targetRoomCount, player.JumpLaunchSpeed,
            player.GravityStrength, Time.fixedDeltaTime);
        levelWidth = TowerRoute.Width;
        levelHeight = TowerRoute.Height;
        levelGrid = new int[levelWidth, levelHeight];

        for (int x = 0; x < levelWidth; x++)
        for (int y = 1; y < levelHeight; y++)
        {
            bool wall = x == 0 || x == levelWidth - 1 || y == 1;
            levelGrid[x, y] = (int)(wall ? TileType.Wall : TileType.Background);
            var cell = new Vector3Int(x, y, 0);
            if (wall) wallTilemap.SetTile(cell, wallTile);
            else backgroundTilemap.SetTile(cell, bgTile);
        }
        backgroundTilemap.color = new Color(0.38f, 0.44f, 0.51f, 1f);
        wallTilemap.color = Color.white;
        platformTilemap.color = new Color(1f, 0.84f, 0.6f, 1f);
        backgroundTilemap.GetComponent<TilemapRenderer>().sortingOrder = -20;
        wallTilemap.GetComponent<TilemapRenderer>().sortingOrder = 0;
        platformTilemap.GetComponent<TilemapRenderer>().sortingOrder = 5;

        foreach (Collider2D existing in platformTilemap.GetComponents<Collider2D>()) existing.enabled = false;
        foreach (Collider2D existing in wallTilemap.GetComponents<Collider2D>()) existing.enabled = false;
        towerCollisionRoot = new GameObject("Verified Tower Colliders");
        towerCollisionRoot.transform.SetParent(transform, false);
        AddTowerCollider("Floor", new Vector2(levelWidth * 0.5f, 1.5f), new Vector2(levelWidth, 1f), false);
        AddTowerCollider("Left Wall", new Vector2(0.5f, levelHeight * 0.5f), new Vector2(1f, levelHeight), false);
        AddTowerCollider("Right Wall", new Vector2(levelWidth - 0.5f, levelHeight * 0.5f), new Vector2(1f, levelHeight), false);

        for (int i = 1; i < TowerRoute.Steps.Count; i++)
        {
            TowerRouteLayout.Step step = TowerRoute.Steps[i];
            for (int x = step.Tiles.xMin; x < step.Tiles.xMax; x++)
            {
                var cell = new Vector3Int(x, step.Tiles.y, 0);
                levelGrid[x, step.Tiles.y] = (int)TileType.Platform;
                platformTilemap.SetTile(cell, platformTile);
                platformTilemap.SetTileFlags(cell, TileFlags.None);
                platformTilemap.SetTransformMatrix(cell,
                    Matrix4x4.TRS(new Vector3(0f, 0.41f, 0f), Quaternion.identity, new Vector3(1f, 0.18f, 1f)));
            }
            AddTowerCollider($"Landing {i}", new Vector2(step.CenterX, step.SurfaceY - 0.09f),
                new Vector2(step.Tiles.width, 0.18f), true);
        }

        for (int i = 0; i < TowerRoute.SectionCount; i++)
        {
            float bottom = TowerRoute.Steps[i * 4].SurfaceY;
            float top = TowerRoute.Steps[(i + 1) * 4].SurfaceY;
            Room room = new Room(new Rect(1f, bottom, levelWidth - 2, top - bottom));
            if (allRooms.Count > 0)
            {
                Room previous = allRooms[allRooms.Count - 1];
                previous.connectedRooms.Add(room);
                room.connectedRooms.Add(previous);
            }
            allRooms.Add(room);
            guaranteedRoomRoutes.Add(TowerRoute.Steps.Skip(i * 4).Take(5)
                .Select(step => new Vector2Int(Mathf.RoundToInt(step.CenterX), Mathf.RoundToInt(step.SurfaceY))).ToList());
            validatedRooms.Add(room);
        }
        GuaranteedRoutesValid = TowerRoute.GeometryValid;
        Physics2D.SyncTransforms();
    }

    private void AddTowerCollider(string label, Vector2 position, Vector2 size, bool oneWay)
    {
        GameObject support = new GameObject(label);
        support.layer = platformTilemap.gameObject.layer;
        support.transform.SetParent(towerCollisionRoot.transform, false);
        support.transform.position = position;
        BoxCollider2D collider = support.AddComponent<BoxCollider2D>();
        collider.size = size;
        if (oneWay)
        {
            PlatformEffector2D effector = support.AddComponent<PlatformEffector2D>();
            effector.useOneWay = true;
            effector.useOneWayGrouping = true;
            effector.useSideFriction = false;
            effector.useSideBounce = false;
            effector.surfaceArc = 160f;
            collider.usedByEffector = true;
        }
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < levelWidth && y >= 0 && y < levelHeight;
    }

    public bool IsTileWalkable(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        return levelGrid[x, y] == (int)TileType.Background;
    }

    public bool IsTileSolid(int x, int y)
    {
        if (!IsInBounds(x, y)) return true;
        return levelGrid[x, y] == (int)TileType.Wall;
    }

    public void SetPlatform(int x, int y)
    {
        if (IsInBounds(x, y))
        {
            levelGrid[x, y] = (int)TileType.Platform;
        }
    }

    public List<Room> GetAllRooms()
    {
        return new List<Room>(allRooms);
    }

    public float GetPlayerJumpHeight() => playerJumpHeight;
    public float GetPlayerJumpDistance() => playerJumpDistance;

    #endregion
}
