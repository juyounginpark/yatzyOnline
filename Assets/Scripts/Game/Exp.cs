using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Exp : MonoBehaviour
{
    [Header("─ EXP 설정 ─")]
    [Tooltip("체크 해제 시 경험치가 전혀 차지 않음 (레벨업/룰렛 비활성)")]
    public bool expEnabled = true;

    [Tooltip("레벨당 필요 경험치 증가량 (1레벨=차수, 2레벨=차수×2, ...)")]
    public int expPerLevelStep = 100;

    [Header("─ UI ─")]
    [Tooltip("경험치 바 배경 RawImage")]
    public RawImage expBarBackground;

    [Tooltip("경험치 바 게이지 RawImage")]
    public RawImage expBarImage;

    [Tooltip("바 위에 표시할 텍스트 (현재EXP/필요EXP)")]
    public TMP_Text expText;

    [Header("─ 색상 ─")]
    public Color backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    public Color gaugeColor = new Color(0.2f, 0.8f, 0.3f, 1f);

    [Header("─ 애니메이션 ─")]
    [Tooltip("EXP 바 채움 애니메이션 시간")]
    public float fillDuration = 0.6f;

    [Tooltip("LEVEL UP! 텍스트 표시 시간")]
    public float levelUpDisplayTime = 1.0f;

    [Header("─ LEVEL UP 색상 ─")]
    public Color levelUpGaugeColor = new Color(1f, 0.85f, 0.2f, 1f);
    public Color levelUpTextColor  = new Color(1f, 0.9f, 0.1f, 1f);

    // ─── 내부 상태 ───
    private int _currentExp;
    private int _currentLevel = 1;
    private float _barFullWidth;
    private bool _isAnimating;

    public int CurrentExp => _currentExp;
    public int CurrentLevel => _currentLevel;
    public bool IsAnimating => _isAnimating;

    /// <summary>
    /// 현재 레벨에서 레벨업에 필요한 총 경험치
    /// </summary>
    public int ExpForCurrentLevel => expPerLevelStep * _currentLevel;

    void Start()
    {
        if (expBarImage != null)
            _barFullWidth = expBarImage.rectTransform.sizeDelta.x;

        // 색상 적용
        if (expBarBackground != null)
            expBarBackground.color = backgroundColor;
        if (expBarImage != null)
            expBarImage.color = gaugeColor;

        UpdateBar();
    }

    // ─────────────────────────────────────────
    //  경험치 추가 (동기 — 애니메이션 없이 즉시 반영)
    // ─────────────────────────────────────────
    /// <summary>
    /// 경험치를 추가하고 레벨업 횟수를 반환 (애니메이션 없음)
    /// </summary>
    public int AddExp(int amount)
    {
        if (!expEnabled) return 0;

        int oldLevel = _currentLevel;
        _currentExp += amount;

        while (_currentExp >= ExpForCurrentLevel)
        {
            _currentExp -= ExpForCurrentLevel;
            _currentLevel++;
        }

        UpdateBar();
        return _currentLevel - oldLevel;
    }

    // ─────────────────────────────────────────
    //  경험치 추가 (코루틴 — 애니메이션 포함)
    // ─────────────────────────────────────────
    /// <summary>
    /// 경험치를 추가하며 바 채움 애니메이션과 LEVEL UP 연출을 실행합니다.
    /// onLevelUp: 레벨업 시 호출할 코루틴 (예: 룰렛). LEVEL UP 표시 후 이 코루틴이 끝날 때까지 EXP 애니메이션이 멈춥니다.
    /// onComplete: 전체 완료 시 레벨업 횟수를 전달합니다.
    /// </summary>
    public IEnumerator AddExpAnimated(int amount, System.Func<IEnumerator> onLevelUp = null, System.Action<int> onComplete = null)
    {
        if (!expEnabled)
        {
            onComplete?.Invoke(0);
            yield break;
        }

        _isAnimating = true;

        int levelsGained = 0;
        int remaining = amount;

        while (remaining > 0)
        {
            int needed = ExpForCurrentLevel;
            int space = needed - _currentExp;  // 레벨업까지 남은 양

            if (remaining < space)
            {
                // 레벨업 없이 바만 채우기
                int targetExp = _currentExp + remaining;
                yield return StartCoroutine(AnimateBar(_currentExp, targetExp, needed));
                _currentExp = targetExp;
                remaining = 0;
            }
            else
            {
                // 바를 꽉 채우고 → LEVEL UP 연출 → 다음 레벨
                yield return StartCoroutine(AnimateBar(_currentExp, needed, needed));
                remaining -= space;

                // ── LEVEL UP 연출 (금색 바 + 펄스 — 색상은 유지) ──
                yield return StartCoroutine(ShowLevelUp());

                _currentExp = 0;
                _currentLevel++;
                levelsGained++;
                Debug.Log($"[Exp] 레벨업! Lv.{_currentLevel} (다음 필요 EXP: {ExpForCurrentLevel})");

                // ── 레벨업 콜백 대기 (룰렛 등) — LEVEL UP 바 유지 ──
                if (onLevelUp != null)
                {
                    IEnumerator levelUpRoutine = onLevelUp();
                    if (levelUpRoutine != null)
                        yield return StartCoroutine(levelUpRoutine);
                }

                // ── LEVEL UP 상태 해제 + 바 초기화 ──
                EndLevelUp();
                UpdateBar();
            }
        }

        UpdateBar();
        _isAnimating = false;

        onComplete?.Invoke(levelsGained);
    }

    // ─────────────────────────────────────────
    //  바 채움 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator AnimateBar(int fromExp, int toExp, int maxExp)
    {
        float fromRatio = maxExp > 0 ? Mathf.Clamp01((float)fromExp / maxExp) : 0f;
        float toRatio   = maxExp > 0 ? Mathf.Clamp01((float)toExp   / maxExp) : 0f;

        float elapsed = 0f;
        // 채울 비율에 비례하여 시간 조절 (최소 0.15초)
        float duration = Mathf.Max(0.15f, fillDuration * (toRatio - fromRatio));

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // ease-out quad
            float eased = 1f - (1f - t) * (1f - t);

            float ratio = Mathf.Lerp(fromRatio, toRatio, eased);
            SetBarRatio(ratio);

            // 텍스트도 보간
            int displayExp = Mathf.RoundToInt(Mathf.Lerp(fromExp, toExp, eased));
            if (expText != null)
                expText.text = $"{displayExp}/{maxExp}";

            yield return null;
        }

        SetBarRatio(toRatio);
        if (expText != null)
            expText.text = $"{toExp}/{maxExp}";
    }

    // ─────────────────────────────────────────
    //  LEVEL UP 연출 (색상 유지 — EndLevelUp으로 해제)
    // ─────────────────────────────────────────
    private Color _savedGaugeColor;
    private Color _savedTextColor;
    private Vector2 _savedBgSize;

    private IEnumerator ShowLevelUp()
    {
        // 바 색상을 금색으로 변경
        _savedGaugeColor = gaugeColor;
        if (expBarImage != null)
            expBarImage.color = levelUpGaugeColor;

        // 텍스트에 LEVEL UP! 표시
        _savedTextColor = Color.white;
        if (expText != null)
        {
            _savedTextColor = expText.color;
            expText.text = "LEVEL UP!";
            expText.color = levelUpTextColor;
        }

        // 펄스 애니메이션
        float elapsed = 0f;
        float pulseDuration = levelUpDisplayTime;
        RectTransform bgRt = null;

        if (expBarBackground != null)
        {
            bgRt = expBarBackground.rectTransform;
            _savedBgSize = bgRt.sizeDelta;
        }

        while (elapsed < pulseDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / pulseDuration;

            float pulse;
            if (t < 0.3f)
                pulse = Mathf.Lerp(1f, 1.08f, t / 0.3f);
            else
                pulse = Mathf.Lerp(1.08f, 1f, (t - 0.3f) / 0.7f);

            if (bgRt != null)
                bgRt.sizeDelta = _savedBgSize * pulse;

            if (expText != null)
            {
                float alpha = 0.7f + 0.3f * Mathf.Sin(t * Mathf.PI * 4f);
                Color c = expText.color;
                c.a = alpha;
                expText.color = c;
            }

            yield return null;
        }

        // 배경 크기만 원복, 금색 바 + LEVEL UP 텍스트는 유지
        if (bgRt != null)
            bgRt.sizeDelta = _savedBgSize;

        // 텍스트 알파만 완전 불투명으로
        if (expText != null)
        {
            Color c = expText.color;
            c.a = 1f;
            expText.color = c;
        }
    }

    /// <summary>
    /// LEVEL UP 상태 해제 — 바 색상, 텍스트를 원래대로 복원
    /// </summary>
    private void EndLevelUp()
    {
        if (expBarImage != null)
            expBarImage.color = _savedGaugeColor;

        if (expText != null)
            expText.color = _savedTextColor;
    }

    // ─────────────────────────────────────────
    //  바 비율 직접 설정 (0~1)
    // ─────────────────────────────────────────
    private void SetBarRatio(float ratio)
    {
        if (expBarImage == null) return;
        var rt = expBarImage.rectTransform;
        Vector2 size = rt.sizeDelta;
        size.x = _barFullWidth * Mathf.Clamp01(ratio);
        rt.sizeDelta = size;
    }

    // ─────────────────────────────────────────
    //  바 + 텍스트 즉시 갱신
    // ─────────────────────────────────────────
    private void UpdateBar()
    {
        int needed = ExpForCurrentLevel;
        float ratio = needed > 0 ? Mathf.Clamp01((float)_currentExp / needed) : 0f;
        SetBarRatio(ratio);

        if (expText != null)
            expText.text = $"{_currentExp}/{needed}";
    }

    // ─────────────────────────────────────────
    //  외부에서 레벨/경험치 직접 설정 (저장/로드용)
    // ─────────────────────────────────────────
    public void SetExp(int level, int exp)
    {
        _currentLevel = Mathf.Max(1, level);
        _currentExp = Mathf.Max(0, exp);
        UpdateBar();
    }
}
