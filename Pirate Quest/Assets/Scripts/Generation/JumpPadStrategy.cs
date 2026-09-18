using UnityEngine;

public class JumpPadStrategy : IRoomStrategy
{
    private float jumpHeight;
    private float jumpDistance;

    public JumpPadStrategy(float jumpHeight, float jumpDistance)
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
        int jumpPadX = targetExit.x;
        int jumpPadY = (int)room.bounds.yMin + 2;
        for (int x = jumpPadX - 2; x <= jumpPadX + 2; x++)
        {
            if (IsInsideRoom(room, x, jumpPadY))
            {
                generator.SetPlatform(x, jumpPadY);
            }
        }
        int backPlatformY = (int)room.bounds.yMin + (int)(room.bounds.height * 0.6f);
        int backPlatformX = jumpPadX > room.bounds.center.x ?
            (int)room.bounds.xMin + 3 : (int)room.bounds.xMax - 6;

        for (int x = 0; x < 5; x++)
        {
            if (IsInsideRoom(room, backPlatformX + x, backPlatformY))
            {
                generator.SetPlatform(backPlatformX + x, backPlatformY);
            }
        }
        int bottomPlatformX = (int)room.bounds.center.x;
        int bottomPlatformY = (int)room.bounds.yMin + 1;

        for (int x = -3; x <= 3; x++)
        {
            if (IsInsideRoom(room, bottomPlatformX + x, bottomPlatformY))
            {
                generator.SetPlatform(bottomPlatformX + x, bottomPlatformY);
            }
        }
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

    private bool IsInsideRoom(Room room, int x, int y)
    {
        return x > room.bounds.xMin && x < room.bounds.xMax - 1 &&
               y > room.bounds.yMin && y < room.bounds.yMax - 1;
    }
}