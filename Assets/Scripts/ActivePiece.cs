using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Active piece controller (DAS/ARR for left/right; soft-drop replaces gravity).
/// - Left/Right: DAS + ARR repeating (hold = steady repeat)
/// - Down: replaces gravity when held (gravity waits downARR instead of fallInterval)
/// - Only horizontal moves and rotations count for lock-reset; gravity/down moves do not.
/// - UpArrow rotates clockwise. Space unused.
/// </summary>
public class ActivePiece : MonoBehaviour
{
    // base shapes (rotation 0)
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

    // simple kicks to try on rotation
    private static readonly Vector2Int[] simpleKicks = new Vector2Int[] {
        new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1)
    };

    // instance fields
    private TetrominoType type;
    private Color color;
    private Vector2Int origin;
    private Vector2Int[] offsets; // current rotated offsets (length 4)
    private List<GameObject> blocks = new List<GameObject>();

    private int rotation = 0; // 0..3

    [Header("Gravity & Lock")]
    public float fallInterval = 1f;      // base gravity interval
    public float lockDelay = 0.5f;       // lock delay (seconds)

    [Header("DAS / ARR (horizontal)")]
    public float horizontalDAS = 0.12f;  // initial delay before repeating left/right
    public float horizontalARR = 0.06f;  // repeat interval for left/right while holding

    [Header("Soft-drop (replace gravity)")]
    [Tooltip("When Down is held, gravity uses this interval instead of fallInterval.")]
    public float downARR = 0.04f;        // soft-drop interval when Down is held
    // (we do immediate single-step on KeyDown, then FallLoop will drive subsequent steps at downARR)

    [Header("Lock reset settings")]
    public int maxLockResets = 5;        // allowed successful reposition attempts while grounded

    private Coroutine fallCoroutine;
    private Coroutine lockCoroutine;
    private bool settled = false;

    // lock reset counter
    private int lockResetCount = 0;

    // hold/timer state for horizontal keys
    private bool leftHeld = false;
    private bool rightHeld = false;
    private float leftHoldTimer = 0f;
    private float rightHoldTimer = 0f;
    private float leftRepeatTimer = 0f;
    private float rightRepeatTimer = 0f;

    // down state (replace gravity)
    private bool downHeld = false;

    // Spawn helper
    public static void Spawn(TetrominoType type, Vector2Int spawnOrigin, Color color)
    {
        var go = new GameObject("Piece_" + type.ToString());
        var piece = go.AddComponent<ActivePiece>();
        piece.Initialize(type, spawnOrigin, color);
    }

    public void Initialize(TetrominoType t, Vector2Int spawnOrigin, Color c)
    {
        this.type = t;
        this.origin = spawnOrigin;
        this.color = c;
        this.offsets = baseShapes[t];

        // create pooled blocks
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

        // start gravity coroutine
        fallCoroutine = StartCoroutine(FallLoop());
    }

    void Update()
    {
        if (settled) return;
        HandleInputDASARR(Time.deltaTime);
    }

    // Input handler for DAS/ARR (left/right) and down flag (replace gravity)
    private void HandleInputDASARR(float dt)
    {
        // LEFT
        if (Input.GetKeyDown(KeyCode.LeftArrow))
        {
            // immediate single step (counts for lock reset)
            TryMove(Vector2Int.left, countsForLockReset: true);
            leftHeld = true;
            leftHoldTimer = 0f;
            leftRepeatTimer = 0f;
        }
        if (Input.GetKeyUp(KeyCode.LeftArrow))
        {
            leftHeld = false;
            leftHoldTimer = 0f;
            leftRepeatTimer = 0f;
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
            rightHeld = true;
            rightHoldTimer = 0f;
            rightRepeatTimer = 0f;
        }
        if (Input.GetKeyUp(KeyCode.RightArrow))
        {
            rightHeld = false;
            rightHoldTimer = 0f;
            rightRepeatTimer = 0f;
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

        // DOWN - replace gravity:
        if (Input.GetKeyDown(KeyCode.DownArrow))
        {
            // immediate single step (do NOT count for lock reset)
            TryMove(Vector2Int.down, countsForLockReset: false);

            // if in lock window, pressing Down forces immediate settle
            if (lockCoroutine != null)
            {
                OnSettle();
                return;
            }

            downHeld = true;
        }
        if (Input.GetKeyUp(KeyCode.DownArrow))
        {
            downHeld = false;
        }

        // ROTATE (Up)
        if (Input.GetKeyDown(KeyCode.UpArrow))
        {
            TryRotate(+1); // rotations count for lock resets via TryRotate -> HandleSuccessfulActionDuringLock
        }

        // Space intentionally unused.
    }

    // Fall loop: gravity drives down movement. When downHeld = true, we use downARR as the interval.
    private IEnumerator FallLoop()
    {
        while (true)
        {
            float wait = downHeld ? downARR : fallInterval;
            // small safety: never wait zero
            if (wait <= 0f) wait = 0.01f;

            yield return new WaitForSeconds(wait);

            if (settled) yield break;

            // gravity move does NOT count for lock reset (countsForLockReset: false)
            if (!TryMove(Vector2Int.down, countsForLockReset: false))
            {
                if (lockCoroutine == null)
                    StartLockCountdown();
            }
        }
    }

    private void StartLockCountdown()
    {
        lockResetCount = 0;
        if (lockCoroutine != null) StopCoroutine(lockCoroutine);
        lockCoroutine = StartCoroutine(LockCountdown());
        Debug.Log("[ActivePiece] Lock started");
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
        Debug.Log("[ActivePiece] Settled at origin " + origin + " (lockResetCount=" + lockResetCount + ")");
        if (fallCoroutine != null) StopCoroutine(fallCoroutine);
        leftHeld = rightHeld = downHeld = false;
        // FUTURE: transfer blocks into locked layer/tilemap here
    }

    /// <summary>
    /// Try to move the piece. 'countsForLockReset' controls whether this move should
    /// be treated as a player action that may reset the lock timer. Use false for gravity/soft-drop.
    /// </summary>
    private bool TryMove(Vector2Int delta, bool countsForLockReset = true)
    {
        var candidateCells = GetCandidateCells(origin + delta, offsets);
        if (IsValidPosition(candidateCells))
        {
            origin += delta;
            ApplyPositionToBlocks();

            // Only horizontal moves and rotations call HandleSuccessfulActionDuringLock by passing true.
            if (countsForLockReset)
                HandleSuccessfulActionDuringLock();

            return true;
        }
        return false;
    }

    private void HandleSuccessfulActionDuringLock()
    {
        if (lockCoroutine == null) return;

        var below = GetCandidateCells(origin + Vector2Int.down, offsets);
        if (IsValidPosition(below))
        {
            // piece can fall -> cancel lock
            StopCoroutine(lockCoroutine);
            lockCoroutine = null;
            lockResetCount = 0;
            Debug.Log("[ActivePiece] Lock cancelled (piece can fall)");
            return;
        }

        // Still grounded: increment reset count and maybe restart timer
        lockResetCount++;
        if (lockResetCount <= maxLockResets)
        {
            StopCoroutine(lockCoroutine);
            lockCoroutine = StartCoroutine(LockCountdown());
            Debug.Log($"[ActivePiece] Lock reset (count {lockResetCount}/{maxLockResets})");
        }
        else
        {
            Debug.Log($"[ActivePiece] Lock reset limit reached ({lockResetCount}). No more resets.");
        }
    }

    private void TryRotate(int direction)
    {
        var rotated = RotateOffsets(offsets, direction);
        foreach (var kick in simpleKicks)
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
                return;
            }
        }
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
        foreach (var c in cells)
        {
            if (!GridManager.Instance.IsInside(c)) return false;
            // future: also check locked tiles here
        }
        return true;
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
