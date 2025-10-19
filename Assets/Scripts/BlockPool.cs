using System.Collections.Generic;
using UnityEngine;

public class BlockPool : MonoBehaviour
{
    public static BlockPool Instance { get; private set; }

    [Header("Pool")]
    public GameObject blockPrefab; 
    public int initialSize = 200;

    private Queue<GameObject> pool = new Queue<GameObject>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (blockPrefab == null)
        {
            Debug.LogError("[BlockPool] blockPrefab not set!");
            return;
        }

        for (int i = 0; i < initialSize; i++)
        {
            var go = Instantiate(blockPrefab, transform);
            go.SetActive(false);
            pool.Enqueue(go);
        }

        Debug.Log($"[BlockPool] Awake: created pool size={pool.Count}");
    }

    public GameObject Get()
    {
        GameObject go;
        if (pool.Count > 0)
        {
            go = pool.Dequeue();
            go.SetActive(true);
        }
        else
        {
            go = Instantiate(blockPrefab);
            Debug.Log("[BlockPool] Pool empty - Instantiating new block");
        }
        return go;
    }

    public void Return(GameObject go)
    {
        if (go == null) return;

        go.transform.SetParent(transform, worldPositionStays: false);
        go.SetActive(false);
        pool.Enqueue(go);
    }
}