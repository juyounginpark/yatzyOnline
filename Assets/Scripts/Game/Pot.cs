using System.Collections;
using UnityEngine;
using TMPro;

// ─────────────────────────────────────────────
//  Pot — 중앙 팟(누적된 LP) 표시/관리
//  - 베팅으로 들어온 LP를 누적, 승자에게 전액 지급
//  - 표시는 숫자가 부드럽게 올라가는 애니메이션
// ─────────────────────────────────────────────
public class Pot : MonoBehaviour
{
    [Header("─ UI ─")]
    public TMP_Text potText;

    [Tooltip("표시 형식 ({0} = 팟 금액)")]
    public string format = "POT {0}";

    [Header("─ 애니메이션 ─")]
    [Tooltip("숫자 변화 시간")]
    public float animDuration = 0.3f;

    [Tooltip("칩 적립 시 텍스트 펀치 배율")]
    public float punchScale = 1.3f;

    [Tooltip("펀치 시간(초)")]
    public float punchDuration = 0.25f;

    private float _amount;
    private Coroutine _anim;
    private Coroutine _punch;
    private Vector3 _textBaseScale = Vector3.one;

    public float Amount => _amount;

    void Start()
    {
        if (potText != null) _textBaseScale = potText.transform.localScale;
        UpdateText(_amount);
    }

    /// <summary>팟에 LP 추가.</summary>
    public void Add(float v)
    {
        if (v <= 0f) return;
        float from = _amount;
        _amount += v;
        Animate(from, _amount);
        Punch();
    }

    private void Punch()
    {
        if (potText == null || !gameObject.activeInHierarchy || punchDuration <= 0f) return;
        if (_punch != null) StopCoroutine(_punch);
        _punch = StartCoroutine(PunchRoutine());
    }

    private IEnumerator PunchRoutine()
    {
        float t = 0f;
        Transform tr = potText.transform;
        while (t < punchDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / punchDuration);
            // 1 → punchScale → 1 (sin 한 번)
            float s = 1f + (punchScale - 1f) * Mathf.Sin(k * Mathf.PI);
            tr.localScale = _textBaseScale * s;
            yield return null;
        }
        tr.localScale = _textBaseScale;
        _punch = null;
    }

    /// <summary>팟을 비우고 누적분을 반환(승자 지급용).</summary>
    public float Take()
    {
        float v = _amount;
        float from = _amount;
        _amount = 0f;
        Animate(from, 0f);
        return v;
    }

    /// <summary>즉시 0으로 리셋(애니메이션 없음).</summary>
    public void ResetPot()
    {
        if (_anim != null) StopCoroutine(_anim);
        _amount = 0f;
        UpdateText(0f);
    }

    private void Animate(float from, float to)
    {
        if (_anim != null) StopCoroutine(_anim);
        if (!gameObject.activeInHierarchy || animDuration <= 0f)
        {
            UpdateText(to);
            return;
        }
        _anim = StartCoroutine(AnimRoutine(from, to));
    }

    private IEnumerator AnimRoutine(float from, float to)
    {
        float t = 0f;
        while (t < animDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / animDuration);
            UpdateText(Mathf.Lerp(from, to, k));
            yield return null;
        }
        UpdateText(to);
    }

    private void UpdateText(float v)
    {
        if (potText != null)
            potText.text = string.Format(format, Mathf.CeilToInt(v));
    }
}
