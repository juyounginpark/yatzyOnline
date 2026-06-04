using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  UIPuller
//  - 버튼을 누르면 할당한 UI가 X축으로 n만큼 이동(애니메이션)
//  - 다시 누르면 원래 위치로 복귀 (토글)
// ─────────────────────────────────────────────
public class UIPuller : MonoBehaviour
{
    [Header("─ 참조 ─")]
    [Tooltip("누르면 토글되는 버튼")]
    public Button toggleButton;

    [Tooltip("이동시킬 UI (비우면 이 오브젝트의 RectTransform)")]
    public RectTransform target;

    [Header("─ 이동 설정 ─")]
    [Tooltip("X축 이동량 (n). 양수=오른쪽, 음수=왼쪽")]
    public float xOffset = 500f;

    [Tooltip("이동 애니메이션 시간(초)")]
    public float duration = 0.5f;

    [Tooltip("이징 커브 (0→1)")]
    public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Vector2 _closedPos;
    private bool _isOpen;
    private Coroutine _anim;

    void Awake()
    {
        if (target == null) target = GetComponent<RectTransform>();
    }

    void Start()
    {
        if (target != null) _closedPos = target.anchoredPosition;
        if (toggleButton != null) toggleButton.onClick.AddListener(Toggle);
    }

    void OnDestroy()
    {
        if (toggleButton != null) toggleButton.onClick.RemoveListener(Toggle);
    }

    // ─────────────────────────────────────────
    //  토글: 이동 ↔ 원위치
    // ─────────────────────────────────────────
    public void Toggle()
    {
        if (target == null) return;

        _isOpen = !_isOpen;
        Vector2 dest = _isOpen ? _closedPos + new Vector2(xOffset, 0f) : _closedPos;

        if (_anim != null) StopCoroutine(_anim);
        _anim = StartCoroutine(MoveTo(dest));
    }

    private IEnumerator MoveTo(Vector2 dest)
    {
        if (duration <= 0f)
        {
            target.anchoredPosition = dest;
            yield break;
        }

        Vector2 from = target.anchoredPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;   // 타임스케일 0(메뉴)에서도 동작
            float k = ease.Evaluate(Mathf.Clamp01(t / duration));
            target.anchoredPosition = Vector2.LerpUnclamped(from, dest, k);
            yield return null;
        }
        target.anchoredPosition = dest;
    }
}
