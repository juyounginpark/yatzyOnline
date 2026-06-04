using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  Juicer — 긴장감 연출 중앙 유틸 (싱글턴)
//  - 카메라 흔들림 / 히트스톱(타임스케일 순간 감속) / 화면 플래시 / 글리치 토글
//  - 조커 오픈(시스템 에러 연출), 올인 임팩트 등 합성 효과 제공
//  - 모든 참조(flash/glitch)는 비어 있어도 안전하게 무시됨 → 카메라 효과만 동작
//  ※ 흔들림은 효과 시작 시 카메라 위치를 기준으로 복원하므로,
//    카메라를 동시에 다른 코드가 이동시키는 구간(쇼다운 줌)에서는 호출하지 말 것.
// ─────────────────────────────────────────────
public class Juicer : MonoBehaviour
{
    public static Juicer Instance { get; private set; }

    [Header("─ 카메라 ─")]
    [Tooltip("흔들 카메라 (비우면 Camera.main)")]
    public Camera targetCamera;

    [Header("─ 화면 플래시 (선택) ─")]
    [Tooltip("풀스크린 Image의 CanvasGroup (alpha로 번쩍임)")]
    public CanvasGroup flashGroup;

    [Tooltip("플래시 색을 입힐 Image (선택)")]
    public Image flashImage;

    [Header("─ 글리치 오버레이 (선택) ─")]
    [Tooltip("조커 등에서 잠깐 켜질 오브젝트 (노이즈/주사선 이미지 등)")]
    public GameObject glitchOverlay;

    [Header("─ 기본값 ─")]
    public Color jokerFlashColor = new Color(1f, 0.15f, 0.15f, 1f);

    private Coroutine _shakeCo, _flashCo, _hitstopCo, _glitchCo;
    private Vector3 _shakeBase;
    private bool    _shaking;

    private Camera Cam => targetCamera != null ? targetCamera : Camera.main;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        if (glitchOverlay != null) glitchOverlay.SetActive(false);
        if (flashGroup != null) flashGroup.alpha = 0f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Time.timeScale != 1f) Time.timeScale = 1f;   // 안전 복원
    }

    // ─────────────────────────────────────────
    //  카메라 흔들림
    // ─────────────────────────────────────────
    public void Shake(float amount = 0.25f, float duration = 0.35f)
    {
        var cam = Cam;
        if (cam == null) return;

        if (!_shaking) { _shakeBase = cam.transform.position; _shaking = true; }
        if (_shakeCo != null) StopCoroutine(_shakeCo);
        _shakeCo = StartCoroutine(ShakeRoutine(cam, amount, duration));
    }

    private IEnumerator ShakeRoutine(Camera cam, float amount, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float damp = 1f - Mathf.Clamp01(t / duration);
            Vector2 off = Random.insideUnitCircle * (amount * damp);
            cam.transform.position = _shakeBase + new Vector3(off.x, off.y, 0f);
            yield return null;
        }
        cam.transform.position = _shakeBase;
        _shaking = false;
        _shakeCo = null;
    }

    // ─────────────────────────────────────────
    //  히트스톱 (타임스케일 순간 감속 → 복원). 짧게만 쓸 것.
    // ─────────────────────────────────────────
    public void Hitstop(float scale = 0.05f, float duration = 0.1f)
    {
        if (_hitstopCo != null) StopCoroutine(_hitstopCo);
        _hitstopCo = StartCoroutine(HitstopRoutine(Mathf.Clamp01(scale), duration));
    }

    private IEnumerator HitstopRoutine(float scale, float duration)
    {
        Time.timeScale = scale;
        float t = 0f;
        while (t < duration) { t += Time.unscaledDeltaTime; yield return null; }
        Time.timeScale = 1f;
        _hitstopCo = null;
    }

    // ─────────────────────────────────────────
    //  화면 플래시
    // ─────────────────────────────────────────
    public void Flash(float duration = 0.2f, float peakAlpha = 0.55f, Color? color = null)
    {
        if (flashGroup == null && flashImage == null) return;
        if (flashImage != null && color.HasValue)
        {
            Color c = color.Value; c.a = flashImage.color.a;
            flashImage.color = c;
        }
        if (_flashCo != null) StopCoroutine(_flashCo);
        _flashCo = StartCoroutine(FlashRoutine(duration, peakAlpha));
    }

    private IEnumerator FlashRoutine(float duration, float peak)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            float a = Mathf.Lerp(peak, 0f, k);   // 즉시 번쩍 → 사라짐
            SetFlashAlpha(a);
            yield return null;
        }
        SetFlashAlpha(0f);
        _flashCo = null;
    }

    private void SetFlashAlpha(float a)
    {
        if (flashGroup != null) flashGroup.alpha = a;
        else if (flashImage != null)
        {
            Color c = flashImage.color; c.a = a; flashImage.color = c;
        }
    }

    private void Glitch(float duration)
    {
        if (glitchOverlay == null) return;
        if (_glitchCo != null) StopCoroutine(_glitchCo);
        _glitchCo = StartCoroutine(GlitchRoutine(duration));
    }

    private IEnumerator GlitchRoutine(float duration)
    {
        glitchOverlay.SetActive(true);
        float t = 0f;
        while (t < duration) { t += Time.unscaledDeltaTime; yield return null; }
        glitchOverlay.SetActive(false);
        _glitchCo = null;
    }

    // ─────────────────────────────────────────
    //  합성 효과
    // ─────────────────────────────────────────

    /// <summary>조커 오픈 — 시스템 에러 연출 (흔들림+감속+적색 플래시+글리치+shatter).</summary>
    public void JokerGlitch()
    {
        Shake(0.4f, 0.6f);
        Hitstop(0.05f, 0.13f);
        Flash(0.3f, 0.6f, jokerFlashColor);
        Glitch(0.45f);
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.shatter);
            SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionShake, 0.6f);
        }
    }

    /// <summary>올인 — 강한 임팩트 (흔들림+감속+백색 플래시+tensionShake).</summary>
    public void AllInImpact()
    {
        Shake(0.3f, 0.45f);
        Hitstop(0.08f, 0.12f);
        Flash(0.22f, 0.5f, Color.white);
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionShake);
    }

    /// <summary>카드 공개 직전 짧은 긴장 펀치 (가벼운 흔들림).</summary>
    public void RevealTick()
    {
        Shake(0.08f, 0.12f);
    }
}
