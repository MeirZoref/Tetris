using UnityEngine;
using UnityEngine.Tilemaps;
 
/// <summary>
/// GridManager: now aware of a Tilemap. If a Tilemap is assigned, CellToWorld/WorldToCell
/// use the Tilemap API (GetCellCenterWorld / WorldToCell) for perfect visual alignment.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Grid Size")]
    public int width = 10;
    public int height = 22;
    public float cellSize = 1f; // fallback if no tilemap assigned

    [Header("References (optional)")]
    public Tilemap tilemap;                 // drag your background Tilemap here
    public Transform lockedBlocksParent;    // optional parent for locked blocks

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Convert a cell coordinate (x,y) to world position (center of that cell).
    /// If a Tilemap is assigned we use GetCellCenterWorld to match visuals exactly.
    /// </summary>
    public Vector3 CellToWorld(Vector2Int cell)
    {
        if (tilemap != null)
        {
            var cellPos = new Vector3Int(cell.x, cell.y, 0);
            return tilemap.GetCellCenterWorld(cellPos);
        }
        // fallback: assume 1 unit == 1 cell and centers at integers
        return new Vector3(cell.x * cellSize, cell.y * cellSize, 0f);
    }

    /// <summary>
    /// Convert a world position to a grid cell.
    /// If tilemap present, use its WorldToCell.
    /// </summary>
    public Vector2Int WorldToCell(Vector3 world)
    {
        if (tilemap != null)
        {
            Vector3Int cell = tilemap.WorldToCell(world);
            return new Vector2Int(cell.x, cell.y);
        }
        int x = Mathf.RoundToInt(world.x / cellSize);
        int y = Mathf.RoundToInt(world.y / cellSize);
        return new Vector2Int(x, y);
    }

    public bool IsInside(Vector2Int cell)
    {
        return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
    }

    public Rect GridWorldRect()
    {
        Vector3 min = CellToWorld(new Vector2Int(0, 0));
        Vector3 max = CellToWorld(new Vector2Int(width - 1, height - 1));
        // Because CellToWorld returns the center, compute extents
        float w = Mathf.Abs(max.x - min.x) + cellSize;
        float h = Mathf.Abs(max.y - min.y) + cellSize;
        return new Rect(min.x - cellSize / 2f, min.y - cellSize / 2f, w, h);
    }
}



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
