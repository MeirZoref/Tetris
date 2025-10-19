using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Size")]
    public int width = 10;
    public int height = 22;
    public float cellSize = 1f; // fallback if no tilemap assigned

    [Header("References")]
    public Tilemap tilemap;
    public Transform lockedBlocksParent;

    // internal grid storage (stores Transform of the block sitting in the cell)
    private Transform[,] grid;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        grid = new Transform[width, height];
    }
    
    public Vector3 CellToWorld(Vector2Int cell)
    {
        if (tilemap != null)
        {
            Vector3Int cellPos = new Vector3Int(cell.x, cell.y, 0);
            return tilemap.GetCellCenterWorld(cellPos);
        }
        return new Vector3(cell.x * cellSize, cell.y * cellSize, 0f);
    }

    public Vector2Int WorldToCell(Vector3 world)
    {
        if (tilemap != null)
        {
            Vector3Int c = tilemap.WorldToCell(world);
            return new Vector2Int(c.x, c.y);
        }
        int x = Mathf.RoundToInt(world.x / cellSize);
        int y = Mathf.RoundToInt(world.y / cellSize);
        return new Vector2Int(x, y);
    }

    public bool IsValidPosition(IEnumerable<Vector2Int> cells)
    {
        foreach (var c in cells)
        {
            if (c.x < 0 || c.x >= width) return false;
            if (c.y < 0) return false; // below floor
            if (c.y < height)
            {
                if (grid[c.x, c.y] != null) return false;
            }
            // cells with y >= height are allowed (spawn above visible area)
        }
        return true;
    }
    
    public List<GameObject> AddBlocksToGrid(IEnumerable<Transform> blockTransforms)
    {
        var added = new List<GameObject>();
        foreach (var t in blockTransforms)
        {
            Vector2Int cell = WorldToCell(t.position);

            if (cell.x < 0 || cell.x >= width || cell.y < 0)
            {
                // out of bounds below/side - ignore or log (shouldn't happen)
                Debug.LogWarning($"[GridManager] AddBlocksToGrid: block out-of-bounds cell={cell}");
                continue;
            }

            // Snap to cell center for visual correctness
            Vector3 center = CellToWorld(cell);
            t.position = new Vector3(center.x, center.y, t.position.z);

            // If cell inside visible grid, register it
            if (cell.y < height)
            {
                if (grid[cell.x, cell.y] != null)
                {
                    // There is already a block here (should be rare) - log and overwrite
                    Debug.LogWarning($"[GridManager] Overwriting existing block at {cell}");
                }
                grid[cell.x, cell.y] = t;
            }
            else
            {
                int clampedY = Mathf.Min(cell.y, height - 1);
                grid[cell.x, clampedY] = t;
            }

            if (lockedBlocksParent != null)
                t.SetParent(lockedBlocksParent, worldPositionStays: true);

            added.Add(t.gameObject);
        }

        return added;
    }
    
    public List<int> GetFullRows()
    {
        List<int> full = new List<int>();
        for (int y = 0; y < height; y++)
        {
            bool rowFull = true;
            for (int x = 0; x < width; x++)
            {
                if (grid[x, y] == null) { rowFull = false; break; }
            }
            if (rowFull) full.Add(y);
        }
        return full;
    }

    public List<GameObject> ClearRowsImmediate(List<int> rows)
    {
        if (rows == null || rows.Count == 0) return new List<GameObject>();

        // Normalize rows: remove duplicates and only keep those within [0,height-1]
        HashSet<int> toClearSet = new HashSet<int>();
        foreach (var r in rows)
        {
            if (r >= 0 && r < height) toClearSet.Add(r);
        }
        if (toClearSet.Count == 0) return new List<GameObject>();

        List<GameObject> clearedBlocks = new List<GameObject>();

        // New grid that will become the final grid after clearing
        Transform[,] newGrid = new Transform[width, height];

        // For each column, pack cells from bottom up skipping cleared rows.
        for (int x = 0; x < width; x++)
        {
            int newY = 0; // next free row in newGrid for this column (bottom-up)

            for (int y = 0; y < height; y++)
            {
                // If this row is marked for clearing, collect the block (if any)
                if (toClearSet.Contains(y))
                {
                    var t = grid[x, y];
                    if (t != null)
                    {
                        clearedBlocks.Add(t.gameObject);
                        // We do NOT put it into newGrid; effectively removed.
                    }
                    // nothing placed into newGrid here
                }
                else
                {
                    // Row y is not cleared: move it to the next available newY in newGrid
                    var t = grid[x, y];
                    newGrid[x, newY] = t;
                    if (t != null)
                    {
                        // Move the transform to the new world position of (x,newY)
                        Vector3 newPos = CellToWorld(new Vector2Int(x, newY));
                        t.position = new Vector3(newPos.x, newPos.y, t.position.z);
                    }
                    newY++;
                }
            }
        }

        // Replace the internal grid with the rebuilt one
        grid = newGrid;

        return clearedBlocks;
    }
    
    public void ResetGrid()
    {
        // Return children of lockedBlocksParent to BlockPool (safe if lockedBlocksParent null too)
        if (lockedBlocksParent != null)
        {
            List<GameObject> children = new List<GameObject>();
            foreach (Transform t in lockedBlocksParent)
            {
                if (t != null && t.gameObject != null)
                    children.Add(t.gameObject);
            }
            foreach (var g in children)
            {
                BlockPool.Instance.Return(g);
            }
        }

        // Clear internal grid storage
        grid = new Transform[width, height];
    }
    
    public bool IsGameOver()
    {
        // if any block occupies y >= height - 1, consider it game over
        for (int x = 0; x < width; x++)
        {
            if (grid[x, height - 1] != null) return true;
        }
        return false;
    }
    
}
