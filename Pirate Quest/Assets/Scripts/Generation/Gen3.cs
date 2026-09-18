using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class TowerPro : MonoBehaviour
{
    [Header("Тайлы")]
    public Tilemap groundTilemap;
    public Tilemap platformTilemap;
    public Tilemap spikeTilemap;

    public TileBase wallTile;
    public TileBase platformTile;
    public TileBase spikeTile;

    [Header("Размеры")]
    public int width = 30;
    public int height = 100;
    public int floorHeight = 8;

    private void Start() => Generate();

    [ContextMenu("Generate")]
    public void Generate()
    {
        groundTilemap.ClearAllTiles();
        platformTilemap.ClearAllTiles();
        spikeTilemap.ClearAllTiles();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x == 0 || x == width - 1 || y == 0)
                    groundTilemap.SetTile(new Vector3Int(x, y, 0), wallTile);
            }
        }

        Vector2Int lastExit = new Vector2Int(width / 2, 2);

        for (int y = floorHeight; y < height; y += floorHeight)
        {
            lastExit = GenerateFloor(y, lastExit);
        }
    }

    Vector2Int GenerateFloor(int yLevel, Vector2Int entrance)
    {
        int exitX = Random.Range(2, width - 3);

        int wallX = Random.Range(6, width - 6);
        int passageY = yLevel - Random.Range(2, 5);

        for (int wy = yLevel - floorHeight + 1; wy < yLevel; wy++)
        {
            if (wy < passageY || wy > passageY + 2)
            {
                groundTilemap.SetTile(new Vector3Int(wallX, wy, 0), wallTile);
            }
        }

        int platformsCount = 3;
        for (int i = 0; i < platformsCount; i++)
        {
            int nextX = (i == platformsCount - 1) ? exitX : Random.Range(2, width - 6);
            int nextY = yLevel - (platformsCount - i);

            int platWidth = Random.Range(3, 6);

            for (int x = 0; x < platWidth; x++)
            {
                Vector3Int pos = new Vector3Int(nextX + x, nextY, 0);

                if (groundTilemap.HasTile(pos)) continue;

                if (pos.x <= 0 || pos.x >= width - 1) continue;

                platformTilemap.SetTile(pos, platformTile);

                if (Random.value < 0.15f && i > 0)
                {
                    Vector3Int spikePos = pos + Vector3Int.up;
                    if (!groundTilemap.HasTile(spikePos) && !platformTilemap.HasTile(spikePos))
                    {
                        spikeTilemap.SetTile(spikePos, spikeTile);
                    }
                }
            }
        }

        return new Vector2Int(exitX, yLevel);
    }
}