using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal GameManager: handles line-clear animation + continuation and GameOver.
/// Attach one instance in the scene (GameObject named "GameManager").
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Clear animation")]
    public float preClearDelay = 0.25f;  // short pause before clearing (for effect)
    public float postClearDelay = 0.12f; // pause after clearing before next piece

    public bool isGameOver { get; private set; } = false;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>
    /// Public helper to start the clear coroutine from other objects (e.g. ActivePiece).
    /// </summary>
    public void StartClearCoroutine(List<int> rows)
    {
        StartCoroutine(AnimateClearAndContinue(rows));
    }

    /// <summary>
    /// Animate (very simply) and clear rows, return cleared blocks to pool, then spawn next piece.
    /// You can expand this to play particle effects / score UI / sound.
    /// </summary>
    public IEnumerator AnimateClearAndContinue(List<int> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            // nothing - just spawn next
            if (PieceSpawner.Instance != null) PieceSpawner.Instance.SpawnNextRandom();
            yield break;
        }

        Debug.Log("[GameManager] Clearing rows: " + string.Join(",", rows));

        // simple pre-clear delay for animation timing
        yield return new WaitForSeconds(preClearDelay);

        // perform immediate clear (GridManager handles shifting down)
        List<GameObject> cleared = GridManager.Instance.ClearRowsImmediate(rows);

        // return cleared blocks to pool
        foreach (var g in cleared)
        {
            BlockPool.Instance.Return(g);
        }

        // optional post-clear pause
        yield return new WaitForSeconds(postClearDelay);

        // spawn next piece
        if (PieceSpawner.Instance != null) PieceSpawner.Instance.SpawnNextRandom();

        // check game over
        if (GridManager.Instance.IsGameOver())
        {
            GameOver();
        }
    }

    public void GameOver()
    {
        if (isGameOver) return;
        isGameOver = true;
        Debug.Log("[GameManager] GAME OVER");
        // TODO: show UI, stop spawning, stop timers, etc.
    }
}
