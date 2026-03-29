using UnityEngine;

public class CardHover : MonoBehaviour
{
    [HideInInspector] public Vector3 baseLocalPos;
    [HideInInspector] public Quaternion baseLocalRot;
    [HideInInspector] public int baseSortingOrder;

    public float hoverOffset = 1.0f;
    public float smoothSpeed = 10f;

    // 상태
    private bool _isHovered;
    private bool _isDragging;
    private Vector3 _dragLocalPos;

    // 출렁임
    private float _waveAmount;
    private float _waveDecay = 6f;
    private float _waveFreq = 12f;
    private float _waveTime;

    public void SetBase(Vector3 localPos, Quaternion localRot, int sortingOrder)
    {
        baseLocalPos = localPos;
        baseLocalRot = localRot;
        baseSortingOrder = sortingOrder;
        ApplySortingOrder(baseSortingOrder);
    }

    public void Hover() => _isHovered = true;
    public void Unhover() => _isHovered = false;

    public void StartDrag() => _isDragging = true;

    public void UpdateDrag(Vector3 localPos) => _dragLocalPos = localPos;

    public void EndDrag() => _isDragging = false;

    // 출렁임 트리거
    public void TriggerWave(float amount)
    {
        _waveAmount = amount;
        _waveTime = 0f;
    }

    void LateUpdate()
    {
        // 목표 위치 결정
        Vector3 target;
        Quaternion targetRot;

        if (_isDragging)
        {
            target = _dragLocalPos;
            targetRot = Quaternion.identity;
        }
        else
        {
            target = baseLocalPos;
            targetRot = baseLocalRot;

            if (_isHovered)
                target += Vector3.up * hoverOffset;
        }

        // 출렁임 적용
        if (_waveAmount > 0.001f)
        {
            _waveTime += Time.deltaTime;
            float wave = _waveAmount * Mathf.Sin(_waveTime * _waveFreq) * Mathf.Exp(-_waveDecay * _waveTime);
            target += Vector3.up * wave;

            if (Mathf.Abs(wave) < 0.001f)
                _waveAmount = 0f;
        }

        // SmoothDamp 대신 고정 Lerp (떨림 방지)
        float t = 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime);
        transform.localPosition = Vector3.Lerp(transform.localPosition, target, t);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, t);
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
