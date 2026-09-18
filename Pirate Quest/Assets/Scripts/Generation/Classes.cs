using UnityEngine;
using System.Collections.Generic;

public class BSPNode
{
    public Rect bounds;
    public BSPNode left;
    public BSPNode right;
    public Room room;

    public BSPNode(Rect bounds)
    {
        this.bounds = bounds;
    }

    public bool IsLeaf()
    {
        return left == null && right == null;
    }
}

public class Room
{
    public Rect bounds;
    public Vector2Int gridPosition;
    public Vector2Int gridSize;
    public RoomType roomType;
    public List<Room> connectedRooms = new List<Room>();

    public List<Vector2Int> exitPoints = new List<Vector2Int>();

    public List<Vector2Int> entrances = new List<Vector2Int>();
    public List<Vector2Int> exits = new List<Vector2Int>();

    public Room(Rect bounds)
    {
        this.bounds = bounds;
        this.gridPosition = new Vector2Int((int)bounds.x, (int)bounds.y);
        this.gridSize = new Vector2Int((int)bounds.width, (int)bounds.height);
        this.roomType = RoomType.Normal;
    }

    public Vector2Int GetFloorCenter()
    {
        return new Vector2Int((int)bounds.center.x, (int)bounds.yMin + 1);
    }

    public Vector2Int GetCeilingCenter()
    {
        return new Vector2Int((int)bounds.center.x, (int)bounds.yMax - 1);
    }
}

public enum RoomType
{
    Normal,
    Start,
    Boss,
    Treasure,
    Challenge
}