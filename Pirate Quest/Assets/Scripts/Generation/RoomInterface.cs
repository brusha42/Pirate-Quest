using UnityEngine;

public interface IRoomStrategy
{
    void PopulateRoom(Room room, int[,] levelGrid, LevelGenerator generator);
}