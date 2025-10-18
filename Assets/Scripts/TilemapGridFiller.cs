using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Fill a Tilemap with a background tile for the GridManager play area.
/// ExecuteInEditMode so the tilemap is visible while editing.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Tilemap))]
public class TilemapGridFiller : MonoBehaviour
{
    [Header("Grid Filler Settings")]
    public GridManager gridManager;           // optional reference (will use GridManager.Instance if null)
    public TileBase backgroundTile;          // tile to paint for each cell
    public bool fillInEditMode = true;       // if true, fill even while not playing
    public bool clearOnDisable = false;      // optionally clear tiles when disabled

    Tilemap tilemap;

    void OnEnable()
    {
        tilemap = GetComponent<Tilemap>();
        if (gridManager == null && GridManager.Instance != null) gridManager = GridManager.Instance;
        if (backgroundTile == null) return;
#if UNITY_EDITOR
        if (fillInEditMode) Fill();
#endif
        if (Application.isPlaying) Fill();
    }

    void OnDisable()
    {
        if (clearOnDisable && tilemap != null)
        {
            tilemap.ClearAllTiles();
        }
    }

    /// <summary>
    /// Fill the entire grid area with background tiles.
    /// This is idempotent — repeated calls don't duplicate painting.
    /// </summary>
    [ContextMenu("Fill Grid")]
    public void Fill()
    {
        if (tilemap == null) tilemap = GetComponent<Tilemap>();
        if (gridManager == null)
        {
            Debug.LogWarning("[TilemapGridFiller] gridManager is null; cannot fill.");
            return;
        }

        tilemap.ClearAllTiles();

        int w = gridManager.width;
        int h = gridManager.height;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                var cell = new Vector3Int(x, y, 0);
                tilemap.SetTile(cell, backgroundTile);
            }
        }
        // Force refresh
        tilemap.RefreshAllTiles();
        Debug.Log($"[TilemapGridFiller] Filled {w}x{h} cells.");
    }

    // For convenience, call Fill from inspector via Update if requested
    void Update()
    {
#if UNITY_EDITOR
        // keep tilemap in sync while editing (optional: comment out to stop continuous updates)
        if (!Application.isPlaying && fillInEditMode)
        {
            // Optional: only fill when missing tiles to avoid editor slowdown
            if (tilemap != null && tilemap.GetTile(new Vector3Int(0, 0, 0)) != backgroundTile)
            {
                Fill();
            }
        }
#endif
    }
}
