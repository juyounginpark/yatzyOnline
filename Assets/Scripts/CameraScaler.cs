using UnityEngine;

/// <summary>
/// 기준 해상도 1080×1920 기준으로 Orthographic 카메라의 size를
/// 실제 화면 비율에 맞게 런타임에 자동 조정합니다.
///
/// ※ 사용법: Main Camera 게임 오브젝트에 이 컴포넌트를 추가하세요.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraScaler : MonoBehaviour
{
    [Header("─ 기준 해상도 ─")]
    [Tooltip("설계 기준 너비 (픽셀)")]
    public float referenceWidth = 1080f;

    [Tooltip("설계 기준 높이 (픽셀)")]
    public float referenceHeight = 1920f;

    [Header("─ 기준 Orthographic Size ─")]
    [Tooltip("기준 해상도(1080×1920)에서의 카메라 orthographicSize 값")]
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

        // 기준 비율: 1080/1920 ≈ 0.5625 (세로형)
        float referenceAspect = referenceWidth / referenceHeight;
        float screenAspect    = Screen.width / (float)Screen.height;

        // 세로형 게임: Height 고정, Width 변동
        // 화면이 더 좁으면 ortho size 증가 (더 넓게 보임)
        _cam.orthographicSize = baseOrthoSize * (referenceAspect / screenAspect);
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
