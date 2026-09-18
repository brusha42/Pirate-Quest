using UnityEngine;
using System.Collections.Generic;

public class GridStrategy : IRoomStrategy
{
    private float jumpHeight;
    private float jumpDistance;
    private float maxFallDistance;

    private float horizontalStep;
    private float verticalStep;
    private float platformWidth;

    public GridStrategy(float jumpHeight, float jumpDistance, float maxFallDistance)
    {
        this.jumpHeight = jumpHeight;
        this.jumpDistance = jumpDistance;
        this.maxFallDistance = maxFallDistance;

        this.horizontalStep = jumpDistance * 0.9f;
        this.verticalStep = jumpHeight * 0.8f;
        this.platformWidth = 3f;
    }

    public void PopulateRoom(Room room, int[,] levelGrid, LevelGenerator generator)
    {
        PlatformGrid grid = new PlatformGrid(room, horizontalStep, verticalStep, platformWidth);

        Vector2Int entrance = GetLowestPoint(room.entrances, room);
        Vector2Int exit = GetHighestPoint(room.exits, room);

        if (entrance == Vector2Int.zero)
            entrance = new Vector2Int((int)room.bounds.xMin + 2, (int)room.bounds.yMin + 1);

        if (exit == Vector2Int.zero)
            exit = new Vector2Int((int)room.bounds.xMax - 3, (int)room.bounds.yMax - 2);

        grid.BlockRegionNearPoint(entrance, 2);
        grid.BlockRegionNearPoint(exit, 2);

        List<Vector2Int> mainPath = grid.BuildPath(entrance, exit);

        int branchCount = Random.Range(1, 3);
        List<Vector2Int> allPlatforms = new List<Vector2Int>(mainPath);

        for (int i = 0; i < branchCount; i++)
        {
            Vector2Int branchTarget = grid.GetRandomUpperPoint();
            if (branchTarget != Vector2Int.zero)
            {
                Vector2Int branchStart = mainPath[Random.Range(0, Mathf.Min(3, mainPath.Count))];
                List<Vector2Int> branch = grid.BuildPath(branchStart, branchTarget, maxSteps: 4);
                allPlatforms.AddRange(branch);
            }
        }

        foreach (Vector2Int platformPos in allPlatforms)
        {
            CreatePlatform(platformPos, room, generator);
        }
    }

    private void CreatePlatform(Vector2Int center, Room room, LevelGenerator generator)
    {
        int width = (int)platformWidth;

        int leftExtension = 0;
        for (int i = 1; i <= width / 2; i++)
        {
            int checkX = center.x - i;
            if (IsInsideRoom(room, checkX, center.y) && generator.IsTileWalkable(checkX, center.y))
            {
                leftExtension = i;
            }
            else
            {
                break;
            }
        }

        int rightExtension = 0;
        for (int i = 1; i <= width / 2; i++)
        {
            int checkX = center.x + i;
            if (IsInsideRoom(room, checkX, center.y) && generator.IsTileWalkable(checkX, center.y))
            {
                rightExtension = i;
            }
            else
            {
                break;
            }
        }

        for (int x = center.x - leftExtension; x <= center.x + rightExtension; x++)
        {
            if (IsInsideRoom(room, x, center.y))
            {
                generator.SetPlatform(x, center.y);
            }
        }
    }

    private Vector2Int GetLowestPoint(List<Vector2Int> points, Room room)
    {
        if (points.Count == 0) return Vector2Int.zero;

        Vector2Int lowest = points[0];
        foreach (var point in points)
        {
            if (point.y < lowest.y)
                lowest = point;
        }
        return lowest;
    }

    private Vector2Int GetHighestPoint(List<Vector2Int> points, Room room)
    {
        if (points.Count == 0) return Vector2Int.zero;

        Vector2Int highest = points[0];
        foreach (var point in points)
        {
            if (point.y > highest.y)
                highest = point;
        }
        return highest;
    }

    private bool IsInsideRoom(Room room, int x, int y)
    {
        return x > room.bounds.xMin && x < room.bounds.xMax - 1 &&
               y > room.bounds.yMin && y < room.bounds.yMax - 1;
    }

    private class PlatformGrid
    {
        private Room room;
        private float hStep;
        private float vStep;
        private float platformWidth;
        private List<Vector2Int> gridPoints;
        private HashSet<Vector2Int> blockedPoints;

        public PlatformGrid(Room room, float horizontalStep, float verticalStep, float platformWidth)
        {
            this.room = room;
            this.hStep = horizontalStep;
            this.vStep = verticalStep;
            this.platformWidth = platformWidth;
            this.gridPoints = new List<Vector2Int>();
            this.blockedPoints = new HashSet<Vector2Int>();

            GenerateGrid();
        }

        private void GenerateGrid()
        {
            bool offset = false;

            for (float y = room.bounds.yMin + vStep; y < room.bounds.yMax - 1; y += vStep)
            {
                float startX = room.bounds.xMin + (offset ? hStep / 2 : 0);

                for (float x = startX + 2; x < room.bounds.xMax - 2; x += hStep)
                {
                    Vector2Int point = new Vector2Int(Mathf.RoundToInt(x), Mathf.RoundToInt(y));
                    gridPoints.Add(point);
                }

                offset = !offset;
            }
        }

        public void BlockRegionNearPoint(Vector2Int point, int radius)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    Vector2Int blockPoint = new Vector2Int(point.x + dx, point.y + dy);
                    blockedPoints.Add(blockPoint);
                }
            }
        }

        public List<Vector2Int> BuildPath(Vector2Int start, Vector2Int target, int maxSteps = 50)
        {
            List<Vector2Int> path = new List<Vector2Int>();
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>(blockedPoints);

            Vector2Int current = GetClosestGridPoint(start, visited);
            if (current == Vector2Int.zero)
                return path;

            path.Add(current);
            visited.Add(current);

            for (int step = 0; step < maxSteps; step++)
            {
                Vector2Int next = GetNextPlatform(current, target, visited);

                if (next == Vector2Int.zero)
                    break;

                path.Add(next);
                visited.Add(next);
                current = next;

                if (Vector2Int.Distance(current, target) < vStep * 1.5f)
                    break;
            }

            return path;
        }

        private Vector2Int GetClosestGridPoint(Vector2Int point, HashSet<Vector2Int> visited)
        {
            Vector2Int closest = Vector2Int.zero;
            float minDist = float.MaxValue;

            foreach (var gridPoint in gridPoints)
            {
                if (visited.Contains(gridPoint))
                    continue;

                float dist = Vector2Int.Distance(point, gridPoint);
                if (dist < minDist)
                {
                    minDist = dist;
                    closest = gridPoint;
                }
            }

            return closest;
        }

        private Vector2Int GetNextPlatform(Vector2Int current, Vector2Int target, HashSet<Vector2Int> visited)
        {
            List<Vector2Int> candidates = new List<Vector2Int>();

            foreach (var gridPoint in gridPoints)
            {
                if (visited.Contains(gridPoint))
                    continue;

                float horizontalDist = Mathf.Abs(gridPoint.x - current.x);
                float verticalDist = gridPoint.y - current.y;

                if (horizontalDist <= hStep * 1.2f)
                {
                    if (verticalDist > 0 && verticalDist <= vStep * 1.2f)
                    {
                        candidates.Add(gridPoint);
                    }
                    else if (verticalDist < 0 && Mathf.Abs(verticalDist) <= vStep * 2f)
                    {
                        candidates.Add(gridPoint);
                    }
                }
            }

            if (candidates.Count == 0)
                return Vector2Int.zero;

            candidates.Sort((a, b) =>
            {
                float distA = Vector2Int.Distance(a, target);
                float distB = Vector2Int.Distance(b, target);

                if (Random.value < 0.3f)
                    return Random.value > 0.5f ? 1 : -1;

                return distA.CompareTo(distB);
            });

            return candidates[0];
        }

        public Vector2Int GetRandomUpperPoint()
        {
            List<Vector2Int> upperPoints = new List<Vector2Int>();

            foreach (var point in gridPoints)
            {
                if (point.y > room.bounds.center.y && !blockedPoints.Contains(point))
                {
                    upperPoints.Add(point);
                }
            }

            if (upperPoints.Count == 0)
                return Vector2Int.zero;

            return upperPoints[Random.Range(0, upperPoints.Count)];
        }
    }
}
