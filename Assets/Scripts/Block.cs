using UnityEngine;

/// <summary>
/// Lightweight helper for pooled block GameObjects.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Block : MonoBehaviour
{
    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    public void SetColor(Color c)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.color = c;
    }

    public void ResetState()
    {
        transform.localScale = Vector3.one;
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.color = Color.white;
    }

    public void SetSortingOrder(int order)
    {
        if (sr == null) sr = GetComponent<SpriteRenderer>();
        sr.sortingOrder = order;
    }
}