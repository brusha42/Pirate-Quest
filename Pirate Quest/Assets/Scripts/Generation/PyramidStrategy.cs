using UnityEngine;
using System.Collections.Generic;

public class PyramidStrategy : IRoomStrategy
{
    private float jumpHeight;
    private float jumpDistance;

    public PyramidStrategy(float jumpHeight, float jumpDistance)
    {
        this.jumpHeight = jumpHeight;
        this.jumpDistance = jumpDistance;
    }

    public void PopulateRoom(Room room, int[,] levelGrid, LevelGenerator generator)
    {
        Vector2Int targetExit = GetHighestExit(room);

        if (targetExit == Vector2Int.zero)
        {
            targetExit = room.GetCeilingCenter();
        }

        BuildStairs(room, targetExit, levelGrid, generator);

        AddBackgroundPlatforms(room, levelGrid, generator);
    }

    private Vector2Int GetHighestExit(Room room)
    {
        Vector2Int highest = Vector2Int.zero;
        float maxY = float.MinValue;

        foreach (var exit in room.exits)
        {
            if (exit.y > maxY)
            {
                maxY = exit.y;
                highest = exit;
            }
        }

        if (highest == Vector2Int.zero && room.exitPoints.Count > 0)
        {
            foreach (var point in room.exitPoints)
            {
                if (point.y > maxY)
                {
                    maxY = point.y;
                    highest = point;
                }
            }
        }

        return highest;
    }

    private void BuildStairs(Room room, Vector2Int target, int[,] levelGrid, LevelGenerator generator)
    {
        int startX = (int)room.bounds.xMin + 2;
        int startY = (int)room.bounds.yMin + 1;
        int targetX = target.x;
        int targetY = target.y;

        int stepHeight = Mathf.Max(1, (int)jumpHeight);
        int stepWidth = Mathf.Max(2, (int)jumpDistance);

        int currentX = startX;
        int currentY = startY;

        int direction = targetX > startX ? 1 : -1;

        while (currentY < targetY - stepHeight)
        {
            int platformWidth = Random.Range(stepWidth, stepWidth + 2);

            for (int i = 0; i < platformWidth; i++)
            {
                int px = currentX + i * direction;
                if (IsInsideRoom(room, px, currentY))
                {
                    generator.SetPlatform(px, currentY);
                }
            }

            currentY += stepHeight;
            currentX += (stepWidth + 1) * direction;

            if (currentX < room.bounds.xMin + 1 || currentX > room.bounds.xMax - stepWidth)
            {
                direction *= -1;
            }
        }

        for (int i = -2; i <= 2; i++)
        {
            int px = targetX + i;
            if (IsInsideRoom(room, px, targetY - 1))
            {
                generator.SetPlatform(px, targetY - 1);
            }
        }
    }

    private void AddBackgroundPlatforms(Room room, int[,] levelGrid, LevelGenerator generator)
    {
        int count = Random.Range(1, 3);

        for (int i = 0; i < count; i++)
        {
            int x = Random.Range((int)room.bounds.xMin + 2, (int)room.bounds.xMax - 4);
            int y = Random.Range((int)room.bounds.yMin + 3, (int)room.bounds.yMax - 2);
            int width = Random.Range(3, 6);

            for (int px = 0; px < width; px++)
            {
                if (IsInsideRoom(room, x + px, y) && generator.IsTileWalkable(x + px, y))
                {
                    generator.SetPlatform(x + px, y);
                }
            }
        }
    }

    private bool IsInsideRoom(Room room, int x, int y)
    {
        return x > room.bounds.xMin && x < room.bounds.xMax - 1 &&
               y > room.bounds.yMin && y < room.bounds.yMax - 1;
    }
}