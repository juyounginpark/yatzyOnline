using System.Collections;
using UnityEngine;
using TMPro;

public class HP : MonoBehaviour
{
    [Header("─ HP 설정 ─")]
    public float maxHP = 1000f;

    [Header("─ UI ─")]
    public TMP_Text playerHPText;
    public TMP_Text oppHPText;

    [Header("─ 애니메이션 ─")]
    [Tooltip("숫자 내려가는 시간")]
    public float animDuration = 0.6f;

    [Header("─ 색상 ─")]
    [Tooltip("데미지 시 변할 색상")]
    public Color damageColor = Color.red;

    [Tooltip("회복 시 변할 색상")]
    public Color healColor = Color.green;

    [Header("─ 탄성 스케일 ─")]
    [Tooltip("최대 확대 배율")]
    public float bounceScale = 1.4f;

    [Tooltip("탄성 애니메이션 시간")]
    public float bounceDuration = 0.5f;

    [Tooltip("탄성 진동 횟수")]
    public int bounceCount = 3;

    // ─── 내부 상태 ───
    private float _playerHP;
    private float _oppHP;
    private Color _playerOriginalColor;
    private Color _oppOriginalColor;
    private Vector3 _playerOriginalScale;
    private Vector3 _oppOriginalScale;
    private Coroutine _playerAnim;
    private Coroutine _oppAnim;

    public float PlayerHP => _playerHP;
    public float OppHP => _oppHP;

    void Start()
    {
        _playerHP = maxHP;
        _oppHP = maxHP;

        if (playerHPText != null)
        {
            _playerOriginalColor = playerHPText.color;
            _playerOriginalScale = playerHPText.transform.localScale;
        }
        if (oppHPText != null)
        {
            _oppOriginalColor = oppHPText.color;
            _oppOriginalScale = oppHPText.transform.localScale;
        }

        UpdateText(playerHPText, _playerHP);
        UpdateText(oppHPText, _oppHP);
    }

    public void DamagePlayer(float damage)
    {
        float from = _playerHP;
        _playerHP = Mathf.Max(0f, _playerHP - damage);
        if (_playerAnim != null) StopCoroutine(_playerAnim);
        _playerAnim = StartCoroutine(AnimateHP(playerHPText, from, _playerHP,
            _playerOriginalColor, _playerOriginalScale));
    }

    public void DamageOpp(float damage)
    {
        float from = _oppHP;
        _oppHP = Mathf.Max(0f, _oppHP - damage);
        if (_oppAnim != null) StopCoroutine(_oppAnim);
        _oppAnim = StartCoroutine(AnimateHP(oppHPText, from, _oppHP,
            _oppOriginalColor, _oppOriginalScale));
    }

    public void HealPlayer(float amount)
    {
        float from = _playerHP;
        _playerHP = Mathf.Min(maxHP, _playerHP + amount);
        if (_playerAnim != null) StopCoroutine(_playerAnim);
        _playerAnim = StartCoroutine(AnimateHP(playerHPText, from, _playerHP,
            _playerOriginalColor, _playerOriginalScale));
    }

    public void HealOpp(float amount)
    {
        float from = _oppHP;
        _oppHP = Mathf.Min(maxHP, _oppHP + amount);
        if (_oppAnim != null) StopCoroutine(_oppAnim);
        _oppAnim = StartCoroutine(AnimateHP(oppHPText, from, _oppHP,
            _oppOriginalColor, _oppOriginalScale));
    }

    private IEnumerator AnimateHP(TMP_Text text, float from, float to,
        Color originalColor, Vector3 originalScale)
    {
        if (text == null) yield break;

        float totalDuration = Mathf.Max(animDuration, bounceDuration);
        float elapsed = 0f;

        while (elapsed < totalDuration)
        {
            elapsed += Time.deltaTime;

            // ── 숫자 변화 + 색상 그라데이션 ──
            if (elapsed <= animDuration)
            {
                float t = Mathf.Clamp01(elapsed / animDuration);
                float eased = 1f - (1f - t) * (1f - t);

                float current = Mathf.Lerp(from, to, eased);
                text.text = Mathf.CeilToInt(current).ToString();

                // 감소 → 빨강, 증가 → 초록
                Color effectColor = (to < from) ? damageColor : healColor;
                if (t < 0.5f)
                    text.color = Color.Lerp(originalColor, effectColor, t * 2f);
                else
                    text.color = Color.Lerp(effectColor, originalColor, (t - 0.5f) * 2f);
            }

            // ── 탄성 스케일 ──
            if (elapsed <= bounceDuration)
            {
                float bt = Mathf.Clamp01(elapsed / bounceDuration);
                float scale = EvaluateBounce(bt);
                text.transform.localScale = originalScale * scale;
            }

            yield return null;
        }

        // 최종 보정
        text.text = Mathf.CeilToInt(to).ToString();
        text.color = originalColor;
        text.transform.localScale = originalScale;
    }

    /// <summary>
    /// 탄성 커브: 1 → bounceScale → 1 (감쇠 진동)
    /// </summary>
    private float EvaluateBounce(float t)
    {
        // 감쇠 진폭
        float decay = 1f - t;
        // 진동: sin 파로 bounceCount만큼 왕복
        float wave = Mathf.Sin(t * bounceCount * Mathf.PI);
        // 1 + (bounceScale-1) * 감쇠 * 진동  →  시작에 크게 튀고 점점 줄어듦
        return 1f + (bounceScale - 1f) * decay * Mathf.Abs(wave);
    }

    private void UpdateText(TMP_Text text, float value)
    {
        if (text != null)
            text.text = Mathf.CeilToInt(value).ToString();
    }
}
