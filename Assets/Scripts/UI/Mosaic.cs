using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  Mosaic
//  RawImage에 부착하면, 그 UI의 화면상 위치/크기 영역을
//  픽셀틱(모자이크)하게 가려준다. UI 포함 화면 전체를 대상으로 한다.
//
//  원리:
//   1) 매 프레임 끝(WaitForEndOfFrame)에 '합성된 최종 화면'(UI 포함)을
//      ScreenCapture.CaptureScreenshotIntoRenderTexture 로 저해상도 RT에 캡처
//      → 화면해상도 ÷ pixelSize 크기라서 곧 블록화(다운샘플).
//   2) RawImage가 그 RT에서 '자기 영역(uvRect)'만 잘라 Point 필터로 확대
//      → 자기 위치의 화면(UI 포함)이 픽셀 모자이크로 보인다.
//
//  ※ 보조 카메라 방식과 달리 ScreenSpaceOverlay UI까지 모자이크된다.
//  ※ 캡처에 자기 자신도 포함되지만, 영역→영역 1:1 매핑이라 모자이크가
//     자기 모자이크를 다시 찍어도 동일하게 수렴(안정적, 무한축소 없음).
//
//  사용법: RawImage 오브젝트에 추가하고 pixelSize만 조절.
//          화면이 상하로 뒤집혀 보이면 flipVertical 체크.
// ─────────────────────────────────────────────
[RequireComponent(typeof(RawImage))]
[DisallowMultipleComponent]
public class Mosaic : MonoBehaviour
{
    [Header("─ 모자이크 ─")]
    [Tooltip("모자이크 블록 크기(화면 픽셀). 클수록 더 굵게 뭉개진다")]
    [Range(2f, 128f)] public float pixelSize = 16f;

    [Header("─ 성능 ─")]
    [Tooltip("갱신 간격(초). 0이면 매 프레임. >0이면 그 간격마다만 캡처(그 사이엔 정지된 모자이크 유지)")]
    public float updateInterval = 0f;

    [Header("─ 보정 ─")]
    [Tooltip("캡처가 상하 반전돼 보이면 체크")]
    public bool flipVertical = false;

    private RawImage      _raw;
    private RenderTexture _rt;
    private int           _rtW, _rtH;
    private float         _timer;
    private Coroutine     _loop;

    void Awake() => _raw = GetComponent<RawImage>();

    void OnEnable()
    {
        EnsureRenderTexture();
        _loop = StartCoroutine(CaptureLoop());
    }

    void OnDisable()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;
    }

    void OnDestroy() => ReleaseRT();

    void LateUpdate()
    {
        EnsureRenderTexture();
        UpdateUvRect();
    }

    // ─────────────────────────────────────────
    //  매 프레임 끝에 화면(UI 포함)을 저해상도 RT로 캡처 → 블록화
    // ─────────────────────────────────────────
    private IEnumerator CaptureLoop()
    {
        var wait = new WaitForEndOfFrame();
        while (true)
        {
            yield return wait;

            bool capture = updateInterval <= 0f;
            if (!capture)
            {
                _timer -= Time.unscaledDeltaTime;
                if (_timer <= 0f) { capture = true; _timer = updateInterval; }
            }

            if (capture && _rt != null)
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
        }
    }

    void EnsureRenderTexture()
    {
        int px = Mathf.Max(2, Mathf.RoundToInt(pixelSize));
        int w  = Mathf.Max(1, Mathf.CeilToInt(Screen.width  / (float)px));
        int h  = Mathf.Max(1, Mathf.CeilToInt(Screen.height / (float)px));

        if (_rt != null && w == _rtW && h == _rtH) return;

        ReleaseRT();
        _rt = new RenderTexture(w, h, 0)
        {
            filterMode = FilterMode.Point,    // 확대 시 블록(픽셀틱)
            wrapMode   = TextureWrapMode.Clamp,
        };
        _rt.Create();
        _rtW = w; _rtH = h;

        if (_raw != null) _raw.texture = _rt;
    }

    void ReleaseRT()
    {
        if (_rt == null) return;
        _rt.Release();
        Destroy(_rt);
        _rt = null;
    }

    // RawImage의 화면상 사각형을 정규화(0~1)해 uvRect로 → RT에서 그 영역만 샘플
    void UpdateUvRect()
    {
        if (_raw == null) return;

        var rect = _raw.rectTransform;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners); // [0]=좌하 [1]=좌상 [2]=우상 [3]=우하

        Canvas canvas = _raw.canvas;
        Camera uiCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera : null;

        Vector2 bl = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);

        float sw = Mathf.Max(1, Screen.width);
        float sh = Mathf.Max(1, Screen.height);

        float x = bl.x / sw;
        float y = bl.y / sh;
        float w = (tr.x - bl.x) / sw;
        float h = (tr.y - bl.y) / sh;

        if (flipVertical) y = 1f - y - h;

        // 화면 밖으로 벗어나도 안전하게 클램프
        x = Mathf.Clamp01(x);
        y = Mathf.Clamp01(y);
        w = Mathf.Clamp(w, 0f, 1f - x);
        h = Mathf.Clamp(h, 0f, 1f - y);

        _raw.uvRect = new Rect(x, y, w, h);
    }
}
