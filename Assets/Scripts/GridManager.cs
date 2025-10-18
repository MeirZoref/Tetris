using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// GridManager: holds grid dimensions, optional Tilemap (for exact visual alignment),
/// and an internal grid storing locked block Transforms.
/// 
/// Responsibilities:
/// - Provide CellToWorld / WorldToCell mapping (uses Tilemap if assigned).
/// - Provide IsValidPosition checks (uses grid occupancy).
/// - Add blocks to the grid (snaps them to cell centers and parents them).
/// - Detect full rows and clear rows (ClearRowsImmediate).
/// - Detect Game Over condition.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Size")]
    public int width = 10;
    public int height = 22;
    public float cellSize = 1f; // fallback if no tilemap assigned

    [Header("References")]
    public Tilemap tilemap;                 // optional: drag your background Tilemap here
    public Transform lockedBlocksParent;    // parent for locked blocks (create empty GameObject and assign)

    // internal grid storage (stores Transform of the block sitting in the cell)
    private Transform[,] grid;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        grid = new Transform[width, height];
    }

    // ---- Coordinate helpers ----
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

    public bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    // ---- Occupancy / validation ----
    /// <summary>
    /// True if all given cells are inside grid and not currently occupied by locked blocks.
    /// Note: cells with y >= height are considered spawn-space and are allowed (not blocking).
    /// </summary>
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

    // ---- Locking / adding blocks ----
    /// <summary>
    /// Add block Transforms (pooled GameObjects) to the grid.
    /// Caller should have snapped the blocks roughly into place already; this method
    /// will snap each transform to the exact cell center and parent it under lockedBlocksParent.
    /// Returns the list of blocks actually added (gameobjects).
    /// </summary>
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
                // y >= height - still parent it (spawn area). Put into top-most cell for tracking if desired.
                int clampedY = Mathf.Min(cell.y, height - 1);
                grid[cell.x, clampedY] = t;
            }

            if (lockedBlocksParent != null)
                t.SetParent(lockedBlocksParent, worldPositionStays: true);

            added.Add(t.gameObject);
        }

        return added;
    }

    // ---- Row detection & clearing ----
    /// <summary>
    /// Returns list of y indices (ascending) that are full (no null in the row).
    /// </summary>
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

            // ensure remaining newGrid slots above newY are null (they are by default)
        }

        // Replace the internal grid with the rebuilt one
        grid = newGrid;

        return clearedBlocks;
    }

    
    
    // /// <summary>
    // /// Clears the given rows immediately (no animation).
    // /// Returns the cleared block GameObjects (so caller can return them to pool or animate them).
    // /// Note: rows list should be in ascending order; it will work regardless but sorts internally.
    // /// </summary>
    // public List<GameObject> ClearRowsImmediate(List<int> rows)
    // {
    //     if (rows == null || rows.Count == 0) return new List<GameObject>();
    //     rows.Sort();
    //     List<GameObject> clearedBlocks = new List<GameObject>();
    //
    //     // For each row to clear: remove those blocks, then shift above rows down by 1
    //     foreach (int row in rows)
    //     {
    //         // collect blocks in row
    //         for (int x = 0; x < width; x++)
    //         {
    //             var t = grid[x, row];
    //             if (t != null)
    //             {
    //                 clearedBlocks.Add(t.gameObject);
    //                 grid[x, row] = null;
    //             }
    //         }
    //
    //         // shift everything above this row down by 1
    //         for (int y = row + 1; y < height; y++)
    //         {
    //             for (int x = 0; x < width; x++)
    //             {
    //                 var above = grid[x, y];
    //                 grid[x, y - 1] = above;
    //                 if (above != null)
    //                 {
    //                     // move transform down visually by one cell
    //                     Vector3 newPos = CellToWorld(new Vector2Int(x, y - 1));
    //                     above.position = new Vector3(newPos.x, newPos.y, above.position.z);
    //                 }
    //                 grid[x, y] = null;
    //             }
    //         }
    //     }
    //
    //     return clearedBlocks;
    // }

    // ---- Game over / utilities ----
    
    
    /// <summary>
    /// Returns true if any block occupies the topmost row (or above threshold).
    /// </summary>
    public bool IsGameOver()
    {
        // if any block occupies y >= height - 1, consider it game over
        for (int x = 0; x < width; x++)
        {
            if (grid[x, height - 1] != null) return true;
        }
        return false;
    }

    /// <summary>
    /// Debug helper to print grid occupancy
    /// </summary>
    public void DebugPrintGrid()
    {
        string s = "";
        for (int y = height - 1; y >= 0; y--)
        {
            for (int x = 0; x < width; x++)
            {
                s += (grid[x, y] != null) ? "[X]" : "[ ]";
            }
            s += "\n";
        }
        Debug.Log(s);
    }
}


// using UnityEngine;
// using UnityEngine.Tilemaps;
//  
// /// <summary>
// /// GridManager: now aware of a Tilemap. If a Tilemap is assigned, CellToWorld/WorldToCell
// /// use the Tilemap API (GetCellCenterWorld / WorldToCell) for perfect visual alignment.
// /// </summary>
// public class GridManager : MonoBehaviour
// {
//     public static GridManager Instance { get; private set; }
//
//     [Header("Grid Size")]
//     public int width = 10;
//     public int height = 22;
//     public float cellSize = 1f; // fallback if no tilemap assigned
//
//     [Header("References (optional)")]
//     public Tilemap tilemap;                 // drag your background Tilemap here
//     public Transform lockedBlocksParent;    // optional parent for locked blocks
//
//     void Awake()
//     {
//         if (Instance != null && Instance != this) { Destroy(gameObject); return; }
//         Instance = this;
//     }
//
//     /// <summary>
//     /// Convert a cell coordinate (x,y) to world position (center of that cell).
//     /// If a Tilemap is assigned we use GetCellCenterWorld to match visuals exactly.
//     /// </summary>
//     public Vector3 CellToWorld(Vector2Int cell)
//     {
//         if (tilemap != null)
//         {
//             var cellPos = new Vector3Int(cell.x, cell.y, 0);
//             return tilemap.GetCellCenterWorld(cellPos);
//         }
//         // fallback: assume 1 unit == 1 cell and centers at integers
//         return new Vector3(cell.x * cellSize, cell.y * cellSize, 0f);
//     }
//
//     /// <summary>
//     /// Convert a world position to a grid cell.
//     /// If tilemap present, use its WorldToCell.
//     /// </summary>
//     public Vector2Int WorldToCell(Vector3 world)
//     {
//         if (tilemap != null)
//         {
//             Vector3Int cell = tilemap.WorldToCell(world);
//             return new Vector2Int(cell.x, cell.y);
//         }
//         int x = Mathf.RoundToInt(world.x / cellSize);
//         int y = Mathf.RoundToInt(world.y / cellSize);
//         return new Vector2Int(x, y);
//     }
//
//     public bool IsInside(Vector2Int cell)
//     {
//         return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
//     }
//
//     public Rect GridWorldRect()
//     {
//         Vector3 min = CellToWorld(new Vector2Int(0, 0));
//         Vector3 max = CellToWorld(new Vector2Int(width - 1, height - 1));
//         // Because CellToWorld returns the center, compute extents
//         float w = Mathf.Abs(max.x - min.x) + cellSize;
//         float h = Mathf.Abs(max.y - min.y) + cellSize;
//         return new Rect(min.x - cellSize / 2f, min.y - cellSize / 2f, w, h);
//     }
// }



// using UnityEngine;
//
// /// <summary>
// /// GridManager holds grid dimensions and coordinate helpers.
// /// It is intentionally lightweight (no locking/clearing yet).
// /// Cells are integer coordinates where (0,0) is bottom-left cell center.
// /// World coordinates map 1 unit == 1 cell; cell centers lie on integer positions.
// /// </summary>
// public class GridManager : MonoBehaviour
// {
//     public static GridManager Instance { get; private set; }
//
//     [Header("Grid Size")]
//     public int width = 10;
//     public int height = 22;
//     public float cellSize = 1f;
//
//     [Header("Optional")]
//     public Transform lockedBlocksParent; // later we'll parent settled blocks here
//
//     void Awake()
//     {
//         if (Instance != null && Instance != this) { Destroy(gameObject); return; }
//         Instance = this;
//     }
//
//     /// <summary>
//     /// Convert a cell coordinate (x,y) to world position (center of that cell).
//     /// Example: CellToWorld(0,0) -> Vector3(0,0,0) if origin is at (0,0).
//     /// </summary>
//     public Vector3 CellToWorld(Vector2Int cell)
//     {
//         // cellSize scaling included; keep center at integer coords
//         return new Vector3(cell.x * cellSize, cell.y * cellSize, 0f);
//     }
//
//     /// <summary>
//     /// Convert a world position to a grid cell (rounds to nearest integer cell).
//     /// </summary>
//     public Vector2Int WorldToCell(Vector3 world)
//     {
//         int x = Mathf.RoundToInt(world.x / cellSize);
//         int y = Mathf.RoundToInt(world.y / cellSize);
//         return new Vector2Int(x, y);
//     }
//
//     /// <summary>
//     /// Is the given cell inside the logical grid (0..width-1, 0..height-1)
//     /// </summary>
//     public bool IsInside(Vector2Int cell)
//     {
//         return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
//     }
//
//     /// <summary>
//     /// For debugging: get world rect (min, max) for the whole grid in world coords.
//     /// </summary>
//     public Rect GridWorldRect()
//     {
//         Vector3 min = CellToWorld(new Vector2Int(0, 0));
//         Vector3 max = CellToWorld(new Vector2Int(width - 1, height - 1));
//         return new Rect(min.x - cellSize / 2f, min.y - cellSize / 2f,
//                         (max.x - min.x) + cellSize, (max.y - min.y) + cellSize);
//     }
// }
