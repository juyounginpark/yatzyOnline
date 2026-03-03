using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  룰렛 세그먼트 정의
// ─────────────────────────────────────────────
[Serializable]
public class RouletteSegment
{
    public string segmentName = "세그먼트";

    [Tooltip("시작 각도 (0° = 포인터 위치, 시계 방향)")]
    public float startAngle;

    [Tooltip("끝 각도")]
    public float endAngle;

    [Tooltip("원소 배경 이미지 (결과 UI에 표시)")]
    public Sprite backgroundImage;

    [Tooltip("이 원소의 카드 UI 프리팹 데이터베이스 (랜덤 1개 선택)")]
    public GameObject[] cardUIPrefabs;
}

// ─────────────────────────────────────────────
//  레벨업 룰렛 시스템
//  1) 룰렛이 아래에서 올라옴
//  2) n초 동안 회전 후 원소 확정
//  3) 룰렛이 내려가며 결과 UI 페이드인
//  4) 리롤 버튼 → 카드 제거, 룰렛 다시 올라옴
//  5) 확인 버튼 → 결과 확정
// ─────────────────────────────────────────────
public class Roulette : MonoBehaviour
{
    [Header("─ 룰렛 UI ─")]
    [Tooltip("룰렛 전체 패널 RectTransform (슬라이드 애니메이션용)")]
    public RectTransform rouletteRect;

    [Tooltip("회전할 룰렛 휠")]
    public Transform wheelTransform;

    [Header("─ 결과 UI ─")]
    [Tooltip("결과 패널 (Panel)")]
    public GameObject resultPanel;

    [Tooltip("원소별 배경 이미지가 표시될 Image")]
    public Image resultBackgroundImage;

    [Tooltip("카드 UI 프리팹이 생성될 부모 Transform")]
    public Transform resultCardContainer;

    [Tooltip("리롤 버튼")]
    public Button rerollButton;

    [Tooltip("확인 버튼")]
    public Button confirmButton;

    [Header("─ 세그먼트 설정 ─")]
    public RouletteSegment[] segments = new RouletteSegment[]
    {
        new RouletteSegment { segmentName = "노랑",   startAngle = 150f, endAngle = 280f },
        new RouletteSegment { segmentName = "초록",   startAngle = 60f,  endAngle = 150f },
        new RouletteSegment { segmentName = "파랑",   startAngle = 340f, endAngle = 60f  },
        new RouletteSegment { segmentName = "빨강",   startAngle = 280f, endAngle = 320f },
        new RouletteSegment { segmentName = "마젠타", startAngle = 320f, endAngle = 340f },
    };

    [Header("─ 타이밍 설정 ─")]
    [Tooltip("룰렛 회전 시간 (초)")]
    public float spinDuration = 3f;

    [Tooltip("슬라이드 애니메이션 시간")]
    public float slideDuration = 0.5f;

    [Tooltip("페이드 애니메이션 시간")]
    public float fadeDuration = 0.3f;

    [Tooltip("룰렛 슬라이드 거리 (아래 방향)")]
    public float slideDistance = 1000f;

    // ── 결과 (외부 참조용) ──
    public RouletteSegment ResultSegment { get; private set; }
    public GameObject ResultCardPrefab { get; private set; }

    // ── 내부 상태 ──
    private bool _isSpinning;
    private bool _rerollRequested;
    private bool _confirmRequested;
    private Vector2 _rouletteShowPos;
    private Vector2 _rouletteHidePos;
    private GameObject _currentCardInstance;

    public bool IsSpinning => _isSpinning;

    // ─────────────────────────────────────────
    //  초기화
    // ─────────────────────────────────────────
    void Start()
    {
        // 룰렛 표시/숨김 위치
        if (rouletteRect != null)
        {
            _rouletteShowPos = rouletteRect.anchoredPosition;
            _rouletteHidePos = _rouletteShowPos - new Vector2(0f, slideDistance);
            rouletteRect.anchoredPosition = _rouletteHidePos;
            rouletteRect.gameObject.SetActive(false);
        }

        // 결과 패널 초기 숨김
        if (resultPanel != null)
            resultPanel.SetActive(false);

        // 버튼 이벤트
        if (rerollButton != null)
            rerollButton.onClick.AddListener(() => _rerollRequested = true);
        if (confirmButton != null)
            confirmButton.onClick.AddListener(() => _confirmRequested = true);
    }

    // ─────────────────────────────────────────
    //  메인 플로우: 스핀 → 결과 → 리롤/확인 루프
    // ─────────────────────────────────────────
    public IEnumerator SpinAndReward()
    {
        Debug.Log($"[Roulette] SpinAndReward 시작 — rouletteRect={rouletteRect}, wheelTransform={wheelTransform}, resultPanel={resultPanel}");
        _isSpinning = true;
        ResultSegment = null;
        ResultCardPrefab = null;

        bool reroll = true;

        while (reroll)
        {
            reroll = false;

            // ── 1) 룰렛 아래에서 올라오기 ──
            Debug.Log($"[Roulette] 1) 슬라이드 업 — rouletteRect null? {rouletteRect == null}");
            if (rouletteRect != null)
            {
                rouletteRect.anchoredPosition = _rouletteHidePos;
                rouletteRect.gameObject.SetActive(true);
                yield return StartCoroutine(SlideRoulette(_rouletteHidePos, _rouletteShowPos));
            }

            // ── 2) n초 동안 회전 (ease-out cubic 감속) ──
            float targetAngle = UnityEngine.Random.Range(0f, 360f);
            float totalRotation = 360f * UnityEngine.Random.Range(3, 6) + targetAngle;
            float startZ = wheelTransform != null ? wheelTransform.localEulerAngles.z : 0f;

            float elapsed = 0f;
            while (elapsed < spinDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / spinDuration);
                float eased = 1f - (1f - t) * (1f - t) * (1f - t);

                if (wheelTransform != null)
                    wheelTransform.localEulerAngles = new Vector3(0f, 0f, startZ + totalRotation * eased);

                yield return null;
            }

            // 최종 각도 고정
            float finalZ = startZ + totalRotation;
            if (wheelTransform != null)
                wheelTransform.localEulerAngles = new Vector3(0f, 0f, finalZ);

            // ── 3) 원소 확정 (0도 위치) ──
            float resultAngle = ((finalZ % 360f) + 360f) % 360f;
            ResultSegment = GetSegmentAtAngle(resultAngle);

            if (ResultSegment == null && segments.Length > 0)
                ResultSegment = segments[0];

            Debug.Log($"[Roulette] 당첨: {ResultSegment?.segmentName} (각도: {resultAngle:F1}°)");

            yield return new WaitForSeconds(0.5f);

            // ── 4) 룰렛 내려가기 + 결과 UI 페이드인 (동시) ──
            if (ResultSegment != null)
            {
                // 결과 UI 내용 세팅
                SetupResultUI(ResultSegment);

                // 룰렛 슬라이드 다운 (fire-and-forget)
                StartCoroutine(SlideRouletteAndHide(_rouletteShowPos, _rouletteHidePos));

                // 결과 패널 페이드인 (동시 진행, 이것을 yield)
                yield return StartCoroutine(FadeResultPanel(0f, 1f));

                // ── 5) 리롤 / 확인 대기 ──
                _rerollRequested = false;
                _confirmRequested = false;

                while (!_rerollRequested && !_confirmRequested)
                    yield return null;

                // 카드 인스턴스 제거
                if (_currentCardInstance != null)
                {
                    Destroy(_currentCardInstance);
                    _currentCardInstance = null;
                }

                // 결과 패널 페이드아웃
                yield return StartCoroutine(FadeResultPanel(1f, 0f));
                resultPanel.SetActive(false);

                if (_rerollRequested)
                    reroll = true;
            }
            else
            {
                // 세그먼트 없음 — 룰렛만 숨기기
                if (rouletteRect != null)
                {
                    yield return StartCoroutine(SlideRoulette(_rouletteShowPos, _rouletteHidePos));
                    rouletteRect.gameObject.SetActive(false);
                }
            }
        }

        _isSpinning = false;
    }

    // ─────────────────────────────────────────
    //  결과 UI 세팅: 배경 이미지 + 카드 UI 프리팹 생성
    // ─────────────────────────────────────────
    private void SetupResultUI(RouletteSegment segment)
    {
        // 배경 이미지
        if (resultBackgroundImage != null)
        {
            resultBackgroundImage.sprite = segment.backgroundImage;
            resultBackgroundImage.enabled = segment.backgroundImage != null;
        }

        // 기존 카드 인스턴스 정리
        if (_currentCardInstance != null)
        {
            Destroy(_currentCardInstance);
            _currentCardInstance = null;
        }

        // 데이터베이스에서 랜덤 카드 UI 프리팹 선택 + 생성
        ResultCardPrefab = null;

        if (segment.cardUIPrefabs != null && segment.cardUIPrefabs.Length > 0)
        {
            int idx = UnityEngine.Random.Range(0, segment.cardUIPrefabs.Length);
            ResultCardPrefab = segment.cardUIPrefabs[idx];

            if (ResultCardPrefab != null && resultCardContainer != null)
                _currentCardInstance = Instantiate(ResultCardPrefab, resultCardContainer);
        }
    }

    // ─────────────────────────────────────────
    //  룰렛 슬라이드 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator SlideRoulette(Vector2 from, Vector2 to)
    {
        if (rouletteRect == null) yield break;

        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep
            rouletteRect.anchoredPosition = Vector2.Lerp(from, to, eased);
            yield return null;
        }

        rouletteRect.anchoredPosition = to;
    }

    // ─────────────────────────────────────────
    //  룰렛 슬라이드 다운 + 자동 비활성화
    // ─────────────────────────────────────────
    private IEnumerator SlideRouletteAndHide(Vector2 from, Vector2 to)
    {
        yield return StartCoroutine(SlideRoulette(from, to));

        if (rouletteRect != null)
            rouletteRect.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────
    //  결과 패널 페이드 애니메이션 (모든 Graphic 알파)
    // ─────────────────────────────────────────
    private IEnumerator FadeResultPanel(float from, float to)
    {
        if (resultPanel == null) yield break;

        resultPanel.SetActive(true);

        var graphics = resultPanel.GetComponentsInChildren<Graphic>(true);

        // 페이드인 시: 이전 페이드아웃으로 알파 0이 된 Graphic 복원
        if (to > from)
        {
            for (int i = 0; i < graphics.Length; i++)
            {
                Color c = graphics[i].color;
                c.a = 1f;
                graphics[i].color = c;
            }
        }

        Color[] origColors = new Color[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
            origColors[i] = graphics[i].color;

        // 시작 알파 적용
        for (int i = 0; i < graphics.Length; i++)
        {
            Color c = origColors[i];
            c.a = origColors[i].a * from;
            graphics[i].color = c;
        }

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeDuration);
            float alpha = Mathf.Lerp(from, to, t);

            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null) continue;
                Color c = origColors[i];
                c.a = origColors[i].a * alpha;
                graphics[i].color = c;
            }

            yield return null;
        }

        // 최종 알파
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] == null) continue;
            Color c = origColors[i];
            c.a = origColors[i].a * to;
            graphics[i].color = c;
        }
    }

    // ─────────────────────────────────────────
    //  각도 → 세그먼트 판별
    // ─────────────────────────────────────────
    private RouletteSegment GetSegmentAtAngle(float angle)
    {
        angle = ((angle % 360f) + 360f) % 360f;

        foreach (var seg in segments)
        {
            float start = ((seg.startAngle % 360f) + 360f) % 360f;
            float end = ((seg.endAngle % 360f) + 360f) % 360f;

            // endAngle가 360인 경우 보정
            if (Mathf.Approximately(end, 0f) && seg.endAngle > 0f)
                end = 360f;

            if (start < end)
            {
                if (angle >= start && angle < end)
                    return seg;
            }
            else // wrap around (예: 340° → 60°)
            {
                if (angle >= start || angle < end)
                    return seg;
            }
        }

        return null;
    }
}
