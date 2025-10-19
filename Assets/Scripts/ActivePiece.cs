using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ActivePiece : MonoBehaviour
{
    // Base shapes (rotation 0)
    private static readonly Dictionary<TetrominoType, Vector2Int[]> baseShapes = new Dictionary<TetrominoType, Vector2Int[]>()
    {
        { TetrominoType.I, new Vector2Int[]{ new(-1,0), new(0,0), new(1,0), new(2,0) } },
        { TetrominoType.O, new Vector2Int[]{ new(0,0), new(1,0), new(0,1), new(1,1) } },
        { TetrominoType.T, new Vector2Int[]{ new(-1,0), new(0,0), new(1,0), new(0,1) } },
        { TetrominoType.S, new Vector2Int[]{ new(0,0), new(1,0), new(-1,1), new(0,1) } },
        { TetrominoType.Z, new Vector2Int[]{ new(-1,0), new(0,0), new(0,1), new(1,1) } },
        { TetrominoType.J, new Vector2Int[]{ new(-1,0), new(0,0), new(1,0), new(-1,1) } },
        { TetrominoType.L, new Vector2Int[]{ new(-1,0), new(0,0), new(1,0), new(1,1) } },
    };

    // Generic kick list (used for most pieces)
    private static readonly Vector2Int[] genericKicks = new Vector2Int[] {
        new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1)
    };

    // Expanded kicks for I-piece (helps rotating near walls / corners)
    private static readonly Vector2Int[] kicksForI = new Vector2Int[] {
        new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(-1,0),
        new Vector2Int(2,0), new Vector2Int(-2,0), new Vector2Int(0,1)
    };

    private TetrominoType type;
    private Color color;
    private Vector2Int origin;
    private Vector2Int[] offsets;
    private List<GameObject> blocks = new List<GameObject>();

    private int rotation = 0;

    [Header("Gravity & Lock")]
    public float fallInterval = 1f;
    public float lockDelay = 0.5f;

    [Header("DAS / ARR (horizontal)")]
    public float horizontalDAS = 0.12f;
    public float horizontalARR = 0.06f;

    [Header("Soft-drop (replace gravity)")]
    public float downARR = 0.04f;

    [Header("Lock reset settings")]
    public int maxLockResets = 2;

    // Rotation spam protection
    [Header("Rotation tuning")]
    [Tooltip("Minimum seconds between successful rotation attempts (prevents spamming at ground).")]
    public float rotationCooldown = 0.10f; // 100ms default
    private float rotationCooldownTimer = 0f;
    
    // Remaining allowed resets in the current lock epoch
    private int remainingLockResets = -1; // -1 means no active epoch currently
    
    private Coroutine fallCoroutine;
    private Coroutine lockCoroutine;
    private bool settled = false;

    // hold monitoring
    private bool leftHeld = false;
    private bool rightHeld = false;
    private float leftHoldTimer = 0f;
    private float rightHoldTimer = 0f;
    private float leftRepeatTimer = 0f;
    private float rightRepeatTimer = 0f;

    private bool downHeld = false;
    
    public static void Spawn(TetrominoType type, Vector2Int spawnOrigin, Color color)
    {
        var go = new GameObject("Piece_" + type.ToString());
        var piece = go.AddComponent<ActivePiece>();
        piece.Initialize(type, spawnOrigin, color);
    }

    public void Initialize(TetrominoType t, Vector2Int spawnOrigin, Color c)
    {
        type = t;
        origin = spawnOrigin;
        color = c;
        offsets = baseShapes[t];

        blocks.Clear();
        for (int i = 0; i < offsets.Length; i++)
        {
            var blockGo = BlockPool.Instance.Get();
            blockGo.transform.SetParent(transform, worldPositionStays: false);
            var blk = blockGo.GetComponent<Block>();
            blk.ResetState();
            blk.SetColor(color);
            blk.SetSortingOrder(20);
            blocks.Add(blockGo);
        }

        ApplyPositionToBlocks();
        fallCoroutine = StartCoroutine(FallLoop());
    }

    void Update()
    {
        if (settled) return;

        // update rotation cooldown timer
        if (rotationCooldownTimer > 0f) rotationCooldownTimer -= Time.deltaTime;

        HandleInputDASARR(Time.deltaTime);
    }

    private void HandleInputDASARR(float dt)
    {
        // LEFT
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            TryMove(Vector2Int.left, countsForLockReset: true);
            leftHeld = true; leftHoldTimer = leftRepeatTimer = 0f;
        }

        if (Input.GetKeyUp(KeyCode.LeftArrow))
        {
            leftHeld = false; 
            leftHoldTimer = leftRepeatTimer = 0f;
        }

        if (leftHeld)
        {
            leftHoldTimer += dt;
            if (leftHoldTimer >= horizontalDAS)
            {
                leftRepeatTimer += dt;
                if (leftRepeatTimer >= horizontalARR)
                {
                    TryMove(Vector2Int.left, countsForLockReset: true); 
                    leftRepeatTimer = 0f;
                }
            }
        }

        // RIGHT
        if (Input.GetKeyDown(KeyCode.RightArrow))
        {
            TryMove(Vector2Int.right, countsForLockReset: true);
            rightHeld = true; rightHoldTimer = rightRepeatTimer = 0f;
        }

        if (Input.GetKeyUp(KeyCode.RightArrow))
        {
            rightHeld = false; 
            rightHoldTimer = rightRepeatTimer = 0f;
        }

        if (rightHeld)
        {
            rightHoldTimer += dt;
            if (rightHoldTimer >= horizontalDAS)
            {
                rightRepeatTimer += dt;
                if (rightRepeatTimer >= horizontalARR)
                {
                    TryMove(Vector2Int.right, countsForLockReset: true); 
                    rightRepeatTimer = 0f;
                }
            }
        }

        // DOWN
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            TryMove(Vector2Int.down, countsForLockReset: false);
            if (lockCoroutine != null) { OnSettle(); return; }
            downHeld = true;
        }
        if (Input.GetKeyUp(KeyCode.DownArrow)) downHeld = false;

        // ROTATE (Up) - apply rotation cooldown and special cases
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            // If there's a rotation cooldown active, ignore spam
            if (rotationCooldownTimer > 0f) return;

            // O piece should not move on rotation — treat as no-op
            if (type == TetrominoType.O) return;

            // Try to rotate and if successful start short cooldown
            bool rotated = TryRotateWithKicks(+1);
            if (rotated)
            {
                rotationCooldownTimer = rotationCooldown;
            }
        }
    }

    private IEnumerator FallLoop()
    {
        while (true)
        {
            float wait = downHeld ? downARR : fallInterval;
            if (wait <= 0f) wait = 0.01f;

            yield return new WaitForSeconds(wait);

            if (settled) yield break;

            if (!TryMove(Vector2Int.down, countsForLockReset: false))
            {
                if (lockCoroutine == null) StartLockCountdown();
            }
        }
    }

    private void StartLockCountdown()
    {
        // Begin a "lock epoch" if none active: this epoch persists even if the lock countdown
        // is temporarily canceled by a move/rotation that makes the piece ungrounded.
        if (remainingLockResets < 0)
        {
            remainingLockResets = maxLockResets;
        }

        // Start (or restart) the countdown coroutine
        if (lockCoroutine != null) StopCoroutine(lockCoroutine);
        lockCoroutine = StartCoroutine(LockCountdown());
        Debug.Log($"[ActivePiece] Lock started (remaining resets = {remainingLockResets})");
    }

    private IEnumerator LockCountdown()
    {
        float t = 0f;
        while (t < lockDelay)
        {
            yield return null;
            t += Time.deltaTime;
            if (settled) yield break;
        }
        OnSettle();
        lockCoroutine = null;
    }

    private void OnSettle()
    {
        if (settled) return;
        settled = true;
        Debug.Log("[ActivePiece] Settled at origin " + origin + " (locking blocks)");

        if (fallCoroutine != null) { StopCoroutine(fallCoroutine); fallCoroutine = null; }

        leftHeld = rightHeld = downHeld = false;
        leftHoldTimer = rightHoldTimer = leftRepeatTimer = rightRepeatTimer = 0f;

        var transforms = new List<Transform>();
        foreach (var b in blocks) if (b != null) transforms.Add(b.transform);

        var added = GridManager.Instance.AddBlocksToGrid(transforms);

        var fullRows = GridManager.Instance.GetFullRows();
        if (fullRows.Count > 0)
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.StartClearCoroutine(fullRows);
            }
            else
            {
                var cleared = GridManager.Instance.ClearRowsImmediate(fullRows);
                foreach (var go in cleared) BlockPool.Instance.Return(go);
                if (PieceSpawner.Instance != null) PieceSpawner.Instance.SpawnNextRandom();
            }
        }
        else
        {
            if (PieceSpawner.Instance != null) PieceSpawner.Instance.SpawnNextRandom();
            if (GridManager.Instance.IsGameOver())
            {
                if (GameManager.Instance != null) GameManager.Instance.GameOver();
                else Debug.Log("[ActivePiece] GameOver (no GameManager present to handle it).");
            }
        }
        
        remainingLockResets = -1;
        Destroy(gameObject);
    }

    private bool TryMove(Vector2Int delta, bool countsForLockReset = true)
    {
        var candidateCells = GetCandidateCells(origin + delta, offsets);
        if (IsValidPosition(candidateCells))
        {
            origin += delta;
            ApplyPositionToBlocks();
            if (countsForLockReset) HandleSuccessfulActionDuringLock();
            return true;
        }
        return false;
    }

    private void HandleSuccessfulActionDuringLock()
    {
        // Only consider "player-action while in the lock epoch" if an epoch is active
        if (remainingLockResets < 0) return;

        // Each successful horizontal move or rotation consumes one allowed reset,
        // regardless of whether it makes the piece ungrounded or not
        remainingLockResets = Mathf.Max(0, remainingLockResets - 1);
        Debug.Log($"[ActivePiece] Player action during lock epoch, remaining resets = {remainingLockResets}");

        // If the piece can now fall (i.e., the action opened space), cancel active countdown,
        // but do NOT reset remainingLockResets (we keep the epoch state)
        var below = GetCandidateCells(origin + Vector2Int.down, offsets);
        if (IsValidPosition(below))
        {
            if (lockCoroutine != null)
            {
                StopCoroutine(lockCoroutine);
                lockCoroutine = null;
                Debug.Log("[ActivePiece] Lock cancelled (piece can fall) — epoch continues");
            }
            return;
        }

        // If still grounded after the action:
        if (remainingLockResets > 0)
        {
            // Restart the countdown to give the player another short window
            if (lockCoroutine != null) StopCoroutine(lockCoroutine);
            lockCoroutine = StartCoroutine(LockCountdown());
            Debug.Log($"[ActivePiece] Lock reset (remaining {remainingLockResets})");
        }
        else
        {
            // No resets left: ensure a countdown is running so the piece will settle soon.
            // If the countdown was cancelled earlier (lockCoroutine == null) start it now.
            if (lockCoroutine == null)
            {
                lockCoroutine = StartCoroutine(LockCountdown());
                Debug.Log("[ActivePiece] No resets left — starting final lock countdown");
            }
            else
            {
                Debug.Log("[ActivePiece] No resets left — letting current countdown finish");
            }
        }
    }

    // Try to rotate using an appropriate kick table and return true on success
    private bool TryRotateWithKicks(int direction)
    {
        // O-piece: already handled earlier as no-op (but checking for safety)
        if (type == TetrominoType.O) return false;

        var rotated = RotateOffsets(offsets, direction);

        // choose kicks depending on type
        Vector2Int[] kicks = (type == TetrominoType.I) ? kicksForI : genericKicks;

        foreach (var kick in kicks)
        {
            var candidate = GetCandidateCells(origin + kick, rotated);
            if (IsValidPosition(candidate))
            {
                offsets = rotated;
                origin += kick;
                rotation = (rotation + direction + 4) % 4;
                ApplyPositionToBlocks();
                // rotation counts as a player action for lock resets
                HandleSuccessfulActionDuringLock();
                return true;
            }
        }

        // failed rotation
        return false;
    }

    private void ApplyPositionToBlocks()
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            var go = blocks[i];
            Vector2Int cell = origin + offsets[i];
            Vector3 world = GridManager.Instance.CellToWorld(cell);
            go.transform.position = new Vector3(world.x, world.y, 0f);
        }
    }

    private IEnumerable<Vector2Int> GetCandidateCells(Vector2Int candidateOrigin, Vector2Int[] candidateOffsets)
    {
        foreach (var off in candidateOffsets) yield return candidateOrigin + off;
    }

    private bool IsValidPosition(IEnumerable<Vector2Int> cells)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[ActivePiece] GridManager.Instance is null!");
            return false;
        }
        return GridManager.Instance.IsValidPosition(cells);
    }

    private Vector2Int[] RotateOffsets(Vector2Int[] source, int direction)
    {
        Vector2Int[] result = new Vector2Int[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var v = source[i];
            Vector2Int r;
            if (direction > 0)
                r = new Vector2Int(v.y, -v.x); // CW
            else
                r = new Vector2Int(-v.y, v.x); // CCW
            result[i] = r;
        }
        return result;
    }
}
