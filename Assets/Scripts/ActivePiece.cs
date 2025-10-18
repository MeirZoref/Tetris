using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Active piece controller:
/// - builds from pooled blocks
/// - manages input (left/right/rotate/soft/hard drop)
/// - uses a FallLoop coroutine and a LockCountdown coroutine for suspend-before-settle
/// - simple rotation & small kick list
/// 
/// Phase 2: settling just stops the piece; later we'll transfer blocks into locked layer.
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

    // simple kicks to try on rotation (0 = none, then right, left, up)
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

    [Header("Timing")]
    public float fallInterval = 1f;
    public float softDropMultiplier = 0.2f;
    public float lockDelay = 0.5f;

    private Coroutine fallCoroutine;
    private Coroutine lockCoroutine;
    private bool settled = false;

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

        // use pooled blocks
        blocks.Clear();
        for (int i = 0; i < offsets.Length; i++)
        {
            var blockGo = BlockPool.Instance.Get();
            blockGo.transform.SetParent(transform, worldPositionStays: false);
            var blk = blockGo.GetComponent<Block>();
            blk.ResetState();
            blk.SetColor(color);
            blk.SetSortingOrder(20); // ensure on top
            blocks.Add(blockGo);
        }

        ApplyPositionToBlocks();

        // start fall loop
        fallCoroutine = StartCoroutine(FallLoop());
    }

    void Update()
    {
        if (settled) return;
        HandleInput();
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.LeftArrow)) TryMove(Vector2Int.left);
        if (Input.GetKeyDown(KeyCode.RightArrow)) TryMove(Vector2Int.right);
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.X)) TryRotate(+1);
        if (Input.GetKeyDown(KeyCode.Z)) TryRotate(-1);
        if (Input.GetKeyDown(KeyCode.Space)) HardDrop();
        // soft drop is handled in coroutine by checking hold
    }

    private IEnumerator FallLoop()
    {
        while (true)
        {
            float wait = fallInterval;
            if (Input.GetKey(KeyCode.DownArrow)) wait = fallInterval * softDropMultiplier;
            yield return new WaitForSeconds(wait);

            if (settled) yield break;

            if (!TryMove(Vector2Int.down))
            {
                // can't move down -> start lock countdown
                if (lockCoroutine == null)
                {
                    lockCoroutine = StartCoroutine(LockCountdown());
                }
            }
        }
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

    // Called when lock completes - currently we stop motion (settle visually)
    private void OnSettle()
    {
        settled = true;
        // optionally, detach children so this piece GameObject can be destroyed later
        // For now we leave blocks as children to inspect them.
        Debug.Log("[ActivePiece] Settled at origin " + origin);
        // Stop falling coroutine if active
        if (fallCoroutine != null) StopCoroutine(fallCoroutine);
        // FUTURE: write blocks to locked grid / tilemap here
    }

    // Try to move; returns true if succeeded
    private bool TryMove(Vector2Int delta)
    {
        var candidateCells = GetCandidateCells(origin + delta, offsets);
        if (IsValidPosition(candidateCells))
        {
            origin += delta;
            ApplyPositionToBlocks();
            CancelLockIfActive();
            return true;
        }
        return false;
    }

    private void CancelLockIfActive()
    {
        if (lockCoroutine != null)
        {
            StopCoroutine(lockCoroutine);
            lockCoroutine = null;
        }
    }

    private void HardDrop()
    {
        while (true)
        {
            var candidate = GetCandidateCells(origin + Vector2Int.down, offsets);
            if (IsValidPosition(candidate))
            {
                origin += Vector2Int.down;
            }
            else
            {
                // lock immediately
                ApplyPositionToBlocks();
                OnSettle();
                break;
            }
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
                CancelLockIfActive();
                return;
            }
        }
        // failed rotation
    }

    // Apply origin+offsets to the pooled block GameObjects (world positions)
    private void ApplyPositionToBlocks()
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            var go = blocks[i];
            Vector2Int cell = origin + offsets[i];
            Vector3 world = GridManager.Instance.CellToWorld(cell);
            // ensure slight z above tilemap so blocks are visible
            go.transform.position = new Vector3(world.x, world.y, -0.1f);
        }
    }

    // Utility: produce the candidate cells for given origin and offsets
    private IEnumerable<Vector2Int> GetCandidateCells(Vector2Int candidateOrigin, Vector2Int[] candidateOffsets)
    {
        foreach (var off in candidateOffsets) yield return candidateOrigin + off;
    }

    // For Phase 2: valid position = inside grid and optionally not overlapping locked blocks (future)
    private bool IsValidPosition(IEnumerable<Vector2Int> cells)
    {
        foreach (var c in cells)
        {
            if (!GridManager.Instance.IsInside(c)) return false;
            // future: also check locked blocks layer or Tilemap here
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
