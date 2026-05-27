using UnityEngine;

/// <summary>
/// 기준 해상도 1920×1080(가로형) 기준으로 Orthographic 카메라의 size를
/// 실제 화면 비율에 맞게 런타임에 자동 조정합니다.
///
/// ※ 사용법: Main Camera 게임 오브젝트에 이 컴포넌트를 추가하세요.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraScaler : MonoBehaviour
{
    [Header("─ 기준 해상도 ─")]
    [Tooltip("설계 기준 너비 (픽셀)")]
    public float referenceWidth = 1920f;

    [Tooltip("설계 기준 높이 (픽셀)")]
    public float referenceHeight = 1080f;

    [Header("─ 기준 Orthographic Size ─")]
    [Tooltip("기준 해상도(1920×1080)에서의 카메라 orthographicSize 값")]
    public float baseOrthoSize = 5f;

    private Camera _cam;
    private int _lastWidth;
    private int _lastHeight;

    // ─────────────────────────────────────────
    //  게임 시작 전 자동으로 Main Camera에 부착
    // ─────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void AutoAttach()
    {
        // 씬 로드 완료 후 Main Camera를 찾아 컴포넌트 추가
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                              UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<CameraScaler>() == null)
        {
            mainCam.gameObject.AddComponent<CameraScaler>();
        }
        // 한 번만 처리
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // ─────────────────────────────────────────
    //  초기화 및 업데이트
    // ─────────────────────────────────────────
    void Awake()
    {
        _cam = GetComponent<Camera>();
        ApplyScale();
    }

    void Update()
    {
        // 해상도가 변경됐을 때만 재계산
        if (Screen.width != _lastWidth || Screen.height != _lastHeight)
            ApplyScale();
    }

    // ─────────────────────────────────────────
    //  스케일 계산
    // ─────────────────────────────────────────
    void ApplyScale()
    {
        if (_cam == null) return;

        _lastWidth  = Screen.width;
        _lastHeight = Screen.height;

        // 기준 비율: 1920/1080 ≈ 1.778 (16:9 가로형)
        float referenceAspect = referenceWidth / referenceHeight;
        float screenAspect    = Screen.width / (float)Screen.height;

        // 가로형 게임: 화면이 기준보다 좁으면(스퀘어 쪽) ortho size 증가
        // → 좌우 컨텐츠를 잘리지 않게 zoom out
        if (screenAspect < referenceAspect)
            _cam.orthographicSize = baseOrthoSize * (referenceAspect / screenAspect);
        else
            _cam.orthographicSize = baseOrthoSize;
    }

#if UNITY_EDITOR
    // 에디터 Game View 해상도 변경 시 즉시 반영
    void OnValidate()
    {
        if (_cam == null) _cam = GetComponent<Camera>();
        ApplyScale();
    }
#endif
}
