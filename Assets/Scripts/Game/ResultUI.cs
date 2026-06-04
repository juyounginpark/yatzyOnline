using System.Collections;
using UnityEngine;
using TMPro;

// ─────────────────────────────────────────────
//  판정 결과 표시 UI (플레이어/상대 공용 TMP 하나)
//  - 마지막 카드가 천천히 뒤집힐 때 타이밍 맞춰 콤보명+점수 페이드인
//  - 최소 플로팅 시간(기본 2초) 보장 후, 상대편 보러 갈 때 페이드아웃 + 비활성화
// ─────────────────────────────────────────────
public class ResultUI : MonoBehaviour
{
    [Header("─ 결과 TMP (공용) ─")]
    public TMP_Text resultText;

    [Header("─ 표시 형식 ({0}=콤보명, {1}=점수) ─")]
    public string format = "{0}\n{1:F0}";

    [Header("─ 페이드 ─")]
    [Tooltip("기본 페이드인 시간 (호출 시 카드 뒤집기 시간으로 덮어씀)")]
    public float fadeInDuration = 1.0f;
    public float fadeOutDuration = 0.4f;

    [Tooltip("표시 후 최소 유지(플로팅) 시간")]
    public float minFloatTime = 2f;

    [Header("─ 카메라 줌 영향 방지 ─")]
    [Tooltip("결과 TMP의 캔버스를 Screen Space - Overlay로 강제 (카메라 줌과 무관)")]
    public bool forceOverlayCanvas = true;

    [Header("─ 펀치 ─")]
    [Tooltip("표시 시 텍스트 펀치 배율")]
    public float punchScale = 1.35f;
    [Tooltip("펀치 시간(초)")]
    public float punchDuration = 0.3f;

    private float _shownAt;        // 완전히 떠오른 시각
    private Coroutine _fade;
    private Coroutine _punch;
    private Vector3 _baseScale = Vector3.one;

    void Awake()
    {
        if (resultText != null)
        {
            _baseScale = resultText.transform.localScale;
            resultText.alpha = 0f;
            resultText.gameObject.SetActive(false);

            // 캔버스를 Overlay로 → 카메라 줌/이동에 영향받지 않음
            if (forceOverlayCanvas)
            {
                var c = resultText.GetComponentInParent<Canvas>();
                if (c != null) c.rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
        }
    }

    // 콤보명+점수 표시 + 페이드인 (fadeOverride>0이면 그 시간으로 — 카드 뒤집기와 타이밍 일치)
    public IEnumerator ShowResult(string comboName, float score, float fadeOverride = -1f)
    {
        if (resultText == null) yield break;

        float dur = fadeOverride > 0f ? fadeOverride : fadeInDuration;
        resultText.text = string.Format(format, comboName, score);
        resultText.gameObject.SetActive(true);

        if (_punch != null) StopCoroutine(_punch);
        _punch = StartCoroutine(Punch());

        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(Fade(0f, 1f, dur));
        yield return _fade;

        _shownAt = Time.time;  // 완전히 보인 시점부터 플로팅 시간 계산
    }

    private IEnumerator Punch()
    {
        if (resultText == null || punchDuration <= 0f) yield break;
        Transform tr = resultText.transform;
        float t = 0f;
        while (t < punchDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / punchDuration);
            float s = 1f + (punchScale - 1f) * Mathf.Sin(k * Mathf.PI);
            tr.localScale = _baseScale * s;
            yield return null;
        }
        tr.localScale = _baseScale;
        _punch = null;
    }

    // 최소 플로팅 시간 보장 후 페이드아웃 + 비활성화
    public IEnumerator HideResult()
    {
        if (resultText == null || !resultText.gameObject.activeSelf) yield break;

        float remain = minFloatTime - (Time.time - _shownAt);
        if (remain > 0f) yield return new WaitForSeconds(remain);

        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(Fade(resultText.alpha, 0f, fadeOutDuration));
        yield return _fade;

        resultText.gameObject.SetActive(false);
    }

    /// <summary>즉시 페이드아웃 (minFloat 대기 없음) — 상대 패로 넘어갈 때 등.</summary>
    public IEnumerator FadeOut()
    {
        if (resultText == null || !resultText.gameObject.activeSelf) yield break;
        if (_fade != null) StopCoroutine(_fade);
        _fade = StartCoroutine(Fade(resultText.alpha, 0f, fadeOutDuration));
        yield return _fade;
        resultText.gameObject.SetActive(false);
    }

    private IEnumerator Fade(float from, float to, float dur)
    {
        if (dur <= 0f) { resultText.alpha = to; yield break; }

        float e = 0f;
        resultText.alpha = from;
        while (e < dur)
        {
            e += Time.deltaTime;
            resultText.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(e / dur));
            yield return null;
        }
        resultText.alpha = to;
    }
}
