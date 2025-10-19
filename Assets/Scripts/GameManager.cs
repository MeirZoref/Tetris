using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Clear animation (timing)")]
    public float preClearDelay = 0.25f;
    public float postClearDelay = 0.12f;

    [Header("UI (assign in inspector)")]
    public GameObject mainMenuPanel; // contains Start/Quit buttons
    public GameObject gameOverPanel; // contains final score + Restart/Quit
    public GameObject hudPanel; // small HUD with score while playing
    public Text hudScoreText; // live score in-game
    public Text finalScoreText; // text in game over panel showing final score

    [Header("Behavior")]
    public bool isGameOver { get; private set; } = false;
    public bool gameRunning { get; private set; } = false;

    private int score = 0;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        // initial UI state
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (hudPanel != null) hudPanel.SetActive(false);

        // ensure timescale normal
        Time.timeScale = 1f;
    }
    
    // Called by UI Start button
    public void StartGame()
    {
        Debug.Log("[GameManager] StartGame()");
        // Reset game state and UI
        isGameOver = false;
        gameRunning = true;
        score = 0;
        UpdateHUD();

        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (hudPanel != null) hudPanel.SetActive(true);

        // Reset grid & pool to ensure a fresh start (in case of restart).
        if (GridManager.Instance != null) GridManager.Instance.ResetGrid();

        // Reset any Game Over freeze
        Time.timeScale = 1f;

        // Spawn first piece (via PieceSpawner)
        if (PieceSpawner.Instance != null) PieceSpawner.Instance.SpawnNextRandom();
    }

    // Called by UI Restart button in the Game Over panel
    public void RestartGame()
    {
        Debug.Log("[GameManager] RestartGame()");
        // Hide game over and restart fresh
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        StartGame();
    }

    // Called by UI Quit button
    public void QuitGame()
    {
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    
    public void StartClearCoroutine(List<int> rows)
    {
        StartCoroutine(HandleClearAndContinue(rows));
    }

    // Determines score for simultaneous lines and updates HUD
    private int PointsForLines(int lineCount)
    {
        switch (lineCount)
        {
            case 1: return 10;
            case 2: return 30;
            case 3: return 50;
            case 4: return 100;
            default:
                // If an unusual number > 4 happens, scale linearly
                return 10 * lineCount;
        }
    }
    
    public IEnumerator HandleClearAndContinue(List<int> rows)
    {
        if (rows == null || rows.Count == 0)
        {
            // nothing to clear — spawn next piece
            if (!isGameOver && PieceSpawner.Instance != null) PieceSpawner.Instance.SpawnNextRandom();
            yield break;
        }

        // Award score immediately for these cleared rows
        int points = PointsForLines(rows.Count);
        score += points;
        UpdateHUD();

        yield return new WaitForSeconds(preClearDelay);

        // Clear rows immediately (GridManager will move above blocks down)
        List<GameObject> cleared = GridManager.Instance.ClearRowsImmediate(rows);

        // Return cleared blocks to pool so they can be reused
        foreach (var g in cleared)
        {
            BlockPool.Instance.Return(g);
        }

        yield return new WaitForSeconds(postClearDelay);

        // After clearing: spawn next piece (unless game over)
        if (!isGameOver && PieceSpawner.Instance != null)
        {
            PieceSpawner.Instance.SpawnNextRandom();
        }

        // Check game over after the clear
        if (GridManager.Instance.IsGameOver())
        {
            GameOver();
        }
    }

    // When the game ends: show panel, freeze board, stop spawning
    public void GameOver()
    {
        if (isGameOver) return;
        isGameOver = true;
        gameRunning = false;

        Debug.Log("[GameManager] GAME OVER (score=" + score + ")");

        // Stop normal time to "freeze" gameplay (coroutines using WaitForSeconds will stop)
        Time.timeScale = 0f;

        // Show game over UI
        if (gameOverPanel != null) gameOverPanel.SetActive(true);
        if (hudPanel != null) hudPanel.SetActive(false);

        // Update final score text
        if (finalScoreText != null) finalScoreText.text = $"Score: {score}";

        // Destroy any active piece objects so they don't show moving underneath
        var activePieces = FindObjectsByType<ActivePiece>(FindObjectsSortMode.None);
        foreach (var ap in activePieces)
        {
            Destroy(ap.gameObject);
        }
    }

    // Update score on HUD
    private void UpdateHUD()
    {
        if (hudScoreText != null) hudScoreText.text = $"Score: {score}";
    }
}
