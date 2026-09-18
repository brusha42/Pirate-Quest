using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class CampaignBackdropRegression
{
    public static bool Verify(LevelGenerator generator, out string detail)
    {
        detail = "Missing generated world.";
        if (generator == null || generator.Campaign == null) return false;
        CampaignLayout map = generator.Campaign;
        FieldInfo field = typeof(LevelGenerator).GetField("backgroundTilemap", BindingFlags.Instance | BindingFlags.NonPublic);
        Tilemap background = field?.GetValue(generator) as Tilemap;
        if (background == null) { detail = "Background tilemap missing."; return false; }
        FieldInfo wallField = typeof(LevelGenerator).GetField("wallTilemap", BindingFlags.Instance | BindingFlags.NonPublic);
        Tilemap walls = wallField?.GetValue(generator) as Tilemap;
        int occupied = 0, empty = 0, mismatches = 0, hullCells = 0, hullUnsolid = 0, hullUnwalled = 0, perimeterGaps = 0;
        for (int x = 0; x < map.Bounds.xMax; x++)
        for (int y = 0; y < map.Bounds.yMax; y++)
        {
            bool expected = map.ContainsArchitectureCell(x, y);
            bool painted = background.HasTile(new Vector3Int(x, y, 0));
            if (painted) occupied++; else empty++;
            if (expected != painted) mismatches++;
            if (!expected || map.ContainsWorldCell(x, y) || KeepOut(map, x, y)) continue;
            hullCells++;
            bool solid = SolidCovers(map, x, y);
            if (!solid) hullUnsolid++;
            if (walls != null && !walls.HasTile(new Vector3Int(x, y, 0))) hullUnwalled++;
        }
        int total = occupied + empty;
        bool nonrectangular = total > 0 && occupied > 0 && empty > total / 10;
        bool coveredRoute = true;
        foreach (CampaignLayout.RouteNode node in map.Route)
            coveredRoute &= map.ContainsWorldCell(Mathf.FloorToInt(node.FeetPosition.x), Mathf.FloorToInt(node.FeetPosition.y + .5f));
        GameObject scenery = GameObject.Find("Chapter scenery");
        int wallOrnaments = 0, floatingOrnaments = 0;
        if (scenery != null)
            foreach (PirateWorldVisual visual in scenery.GetComponentsInChildren<PirateWorldVisual>())
            {
                if (visual.Renderer == null || !visual.Renderer.enabled ||
                    (visual.Kind != PirateArtKind.Window && visual.Kind != PirateArtKind.Banner &&
                     visual.Kind != PirateArtKind.LanternLit && visual.Kind != PirateArtKind.Vines)) continue;
                wallOrnaments++;
                Vector3 center = visual.OpaqueWorldBounds.center;
                if (!map.ContainsArchitectureCell(Mathf.FloorToInt(center.x), Mathf.FloorToInt(center.y))) floatingOrnaments++;
            }
        bool enclosed = PerimeterEnclosed(map, out perimeterGaps);
        detail = $"painted={occupied} exterior={empty} mismatches={mismatches} nonrectangular={nonrectangular} " +
            $"routeCovered={coveredRoute} hull={hullCells} hullUnsolid={hullUnsolid} hullUnwalled={hullUnwalled} " +
            $"perimeterGaps={perimeterGaps} wallOrnaments={wallOrnaments} floatingOrnaments={floatingOrnaments} physicalTraversal=False";
        return mismatches == 0 && nonrectangular && coveredRoute && hullUnsolid == 0 && hullUnwalled == 0 &&
            enclosed && hullCells > 0 && wallOrnaments > 0 && floatingOrnaments == 0;
    }

    private static bool SolidCovers(CampaignLayout map, int x, int y)
    {
        var cell = new Vector2Int(x, y);
        foreach (RectInt solid in map.Solids)
            if (solid.Contains(cell)) return true;
        return false;
    }

    private static bool KeepOut(CampaignLayout map, int x, int y)
    {
        var tile = new Rect(x, y, 1f, 1f);
        foreach (CampaignLayout.RouteNode node in map.Route)
            if (tile.Overlaps(new Rect(node.FeetPosition.x - .29f, node.FeetPosition.y + .035f, .58f, .95f))) return true;
        foreach (CampaignLayout.SecretCache cache in map.SecretCaches)
        {
            if (tile.Overlaps(new Rect(cache.ChamberBounds.xMin - .4f, cache.ChamberBounds.yMin - .4f,
                cache.ChamberBounds.width + .8f, cache.ChamberBounds.height + .8f))) return true;
            foreach (Vector2 feet in cache.ReturnPath)
                if (tile.Overlaps(new Rect(feet.x - .29f, feet.y + .035f, .58f, 1.04f))) return true;
        }
        return false;
    }

    private static bool PerimeterEnclosed(CampaignLayout map, out int gaps)
    {
        gaps = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        bool any = false;
        for (int x = 0; x < map.Bounds.xMax; x++)
        for (int y = 0; y < map.Bounds.yMax; y++)
        {
            if (!map.ContainsArchitectureCell(x, y)) continue;
            any = true;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x >= maxX) maxX = x + 1;
            if (y >= maxY) maxY = y + 1;
        }
        if (!any) { gaps = 1; return false; }
        for (int x = minX; x < maxX; x++)
        {
            if (Gap(map, x, minY)) gaps++;
            if (Gap(map, x, maxY - 1)) gaps++;
        }
        for (int y = minY; y < maxY; y++)
        {
            if (Gap(map, minX, y)) gaps++;
            if (Gap(map, maxX - 1, y)) gaps++;
        }
        return gaps == 0;
    }

    private static bool Gap(CampaignLayout map, int x, int y) =>
        !map.ContainsWorldCell(x, y) && !KeepOut(map, x, y) && !SolidCovers(map, x, y);
}
