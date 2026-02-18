using System.Collections;
using UnityEngine;

public class CardHover : MonoBehaviour
{
    [HideInInspector] public Vector3 baseLocalPos;
    [HideInInspector] public Quaternion baseLocalRot;
    [HideInInspector] public int baseSortingOrder;

    public float hoverOffset = 0.5f;
    public float hoverDuration = 0.15f;

    private Coroutine _anim;
    private bool _isHovered;

    public void SetBase(Vector3 localPos, Quaternion localRot, int sortingOrder)
    {
        baseLocalPos = localPos;
        baseLocalRot = localRot;
        baseSortingOrder = sortingOrder;
        ApplySortingOrder(baseSortingOrder);
    }

    public void Hover()
    {
        if (_isHovered) return;
        _isHovered = true;
        AnimateTo(baseLocalPos + Vector3.up * hoverOffset);
    }

    public void Unhover()
    {
        if (!_isHovered) return;
        _isHovered = false;
        AnimateTo(baseLocalPos);
    }

    public void RefreshHover()
    {
        if (_isHovered)
            AnimateTo(baseLocalPos + Vector3.up * hoverOffset);
    }

    private void AnimateTo(Vector3 targetPos)
    {
        if (_anim != null)
            StopCoroutine(_anim);
        _anim = StartCoroutine(AnimateCoroutine(targetPos));
    }

    private IEnumerator AnimateCoroutine(Vector3 targetPos)
    {
        Vector3 fromPos = transform.localPosition;
        float elapsed = 0f;

        while (elapsed < hoverDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / hoverDuration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            transform.localPosition = Vector3.Lerp(fromPos, targetPos, ease);
            yield return null;
        }

        transform.localPosition = targetPos;
        _anim = null;
    }

    private void ApplySortingOrder(int order)
    {
        var renderers = GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
            r.sortingOrder = order;

        var canvases = GetComponentsInChildren<Canvas>();
        foreach (var c in canvases)
            c.sortingOrder = order;
    }
}
