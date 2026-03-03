using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class Exp : MonoBehaviour
{
    [Header("─ EXP 설정 ─")]
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

    // ─── 내부 상태 ───
    private int _currentExp;
    private int _currentLevel = 1;
    private float _barFullWidth;

    public int CurrentExp => _currentExp;
    public int CurrentLevel => _currentLevel;

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
    //  경험치 추가
    // ─────────────────────────────────────────
    /// <summary>
    /// 경험치를 추가하고 레벨업 횟수를 반환
    /// </summary>
    public int AddExp(int amount)
    {
        int oldLevel = _currentLevel;
        _currentExp += amount;

        // 레벨업 체크 (연속 레벨업 대응)
        while (_currentExp >= ExpForCurrentLevel)
        {
            _currentExp -= ExpForCurrentLevel;
            _currentLevel++;
            Debug.Log($"[Exp] 레벨업! Lv.{_currentLevel} (다음 필요 EXP: {ExpForCurrentLevel})");
        }

        UpdateBar();
        return _currentLevel - oldLevel;
    }

    // ─────────────────────────────────────────
    //  바 + 텍스트 갱신
    // ─────────────────────────────────────────
    private void UpdateBar()
    {
        int needed = ExpForCurrentLevel;
        float ratio = needed > 0 ? Mathf.Clamp01((float)_currentExp / needed) : 0f;

        // RawImage 폭으로 진행도 표현
        if (expBarImage != null)
        {
            var rt = expBarImage.rectTransform;
            Vector2 size = rt.sizeDelta;
            size.x = _barFullWidth * ratio;
            rt.sizeDelta = size;
        }

        // 텍스트 표시: 현재EXP / 필요EXP
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
