using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns pieces. For Phase 2 we call SpawnNext() at Start to create one piece.
/// </summary>
public class PieceSpawner : MonoBehaviour
{
    public static PieceSpawner Instance { get; private set; }

    [Header("Spawn")]
    public int spawnX = 5;
    public int spawnY = 20;

    [Header("Colors")]
    public Color[] tetrominoColors; // fill 7 colors in inspector

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        
        Debug.Log("[PieceSpawner] Awake");
    }

    void Start()
    {
        // ensure GridManager exists
        if (GridManager.Instance == null)
        {
            Debug.LogError("[PieceSpawner] No GridManager found!");
            return;
        }

        // default colors if not set
        if (tetrominoColors == null || tetrominoColors.Length < 7)
        {
            tetrominoColors = new Color[] {
                Color.cyan, Color.yellow, Color.magenta,
                Color.green, Color.red, Color.blue, new Color(1f, 0.5f, 0f)
            };
        }

        SpawnNextRandom();
        
        Debug.Log("[PieceSpawner] Start: spawnedfirst piece"); 
    }

    public void SpawnNextRandom()
    {
        System.Array types = System.Enum.GetValues(typeof(TetrominoType));
        TetrominoType randomType = (TetrominoType)types.GetValue(Random.Range(0, types.Length));
        Spawn(randomType);
    }

    public void Spawn(TetrominoType type)
    {
        Vector2Int origin = new Vector2Int(spawnX, spawnY);
        Color color = tetrominoColors[(int)type % tetrominoColors.Length];
        ActivePiece.Spawn(type, origin, color);
    }
}

/// <summary>
/// 7 tetromino types
/// </summary>
public enum TetrominoType { I = 0, O = 1, T = 2, S = 3, Z = 4, J = 5, L = 6 }
