using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
// ─────────────────────────────────────────────
public class Roulette : MonoBehaviour
{
    [Header("─ 룰렛 UI ─")]
    public RectTransform rouletteRect;
    public Transform wheelTransform;

    [Header("─ 결과 UI ─")]
    public GameObject resultPanel;

    [Tooltip("원소별 배경 이미지 (3개)")]
    public Image[] resultBackgroundImages = new Image[3];

    [Tooltip("카드가 생성될 컨테이너 (3개)")]
    public Transform[] resultCardContainers = new Transform[3];

    [Tooltip("리롤 버튼")]
    public Button rerollButton;

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
    public float spinDuration = 3f;
    public float slideDuration = 0.5f;
    public float fadeDuration = 0.3f;
    public float slideDistance = 1000f;

    [Header("─ 리롤 설정 ─")]
    public int maxRerolls = 2;

    // ── 결과 (외부 참조용) ──
    public RouletteSegment ResultSegment { get; private set; }
    public GameObject ResultCardPrefab { get; private set; }

    // ── 내부 상태 ──
    public bool IsSpinning => _isSpinning;
    private bool _isSpinning;
    private bool _rerollRequested;
    private bool _confirmRequested;
    private Vector2 _rouletteShowPos;
    private Vector2 _rouletteHidePos;
    private readonly List<GameObject> _currentCardInstances = new List<GameObject>();
    private readonly List<GameObject> _currentCardPrefabs = new List<GameObject>();
    private readonly List<RouletteSegment> _currentCardSegments = new List<RouletteSegment>();
    private int _rerollsRemaining;
    private TMP_Text _rerollButtonText;
    private Vector2[] _containerOrigPositions;

    // ─────────────────────────────────────────
    //  초기화
    // ─────────────────────────────────────────
    void Start()
    {
        if (rouletteRect != null)
        {
            _rouletteShowPos = rouletteRect.anchoredPosition;
            _rouletteHidePos = _rouletteShowPos - new Vector2(0f, slideDistance);
            rouletteRect.anchoredPosition = _rouletteHidePos;
            rouletteRect.gameObject.SetActive(false);
        }

        if (resultPanel != null)
            resultPanel.SetActive(false);

        // 컨테이너 원래 위치 저장
        if (resultCardContainers != null)
        {
            _containerOrigPositions = new Vector2[resultCardContainers.Length];
            for (int i = 0; i < resultCardContainers.Length; i++)
            {
                if (resultCardContainers[i] != null)
                {
                    var rt = resultCardContainers[i].GetComponent<RectTransform>();
                    _containerOrigPositions[i] = rt != null ? rt.anchoredPosition : Vector2.zero;
                }
            }
        }

        if (rerollButton != null)
        {
            _rerollButtonText = rerollButton.GetComponentInChildren<TMP_Text>();
            rerollButton.onClick.AddListener(() => _rerollRequested = true);
        }

        _rerollsRemaining = maxRerolls;
    }

    // ─────────────────────────────────────────
    //  메인 플로우
    // ─────────────────────────────────────────
    public IEnumerator SpinAndReward()
    {
        _isSpinning = true;
        ResultSegment = null;
        ResultCardPrefab = null;
        _rerollsRemaining = Mathf.Min(_rerollsRemaining + 1, maxRerolls);

        bool reroll = true;

        while (reroll)
        {
            reroll = false;

            // 1) 룰렛 슬라이드 업
            if (rouletteRect != null)
            {
                rouletteRect.anchoredPosition = _rouletteHidePos;
                rouletteRect.gameObject.SetActive(true);
                yield return StartCoroutine(SlideRoulette(_rouletteHidePos, _rouletteShowPos));
            }

            // 2) 회전
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

            float finalZ = startZ + totalRotation;
            if (wheelTransform != null)
                wheelTransform.localEulerAngles = new Vector3(0f, 0f, finalZ);

            // 3) 세그먼트 확정
            float resultAngle = ((finalZ % 360f) + 360f) % 360f;
            ResultSegment = GetSegmentAtAngle(resultAngle);
            if (ResultSegment == null && segments.Length > 0)
                ResultSegment = segments[0];

            yield return new WaitForSeconds(0.5f);

            // 4) 룰렛 내려가기 + 결과 UI 표시
            if (ResultSegment != null)
            {
                SetupResultUI(ResultSegment);
                StartCoroutine(SlideRouletteAndHide(_rouletteShowPos, _rouletteHidePos));
                yield return StartCoroutine(FadeResultPanel(0f, 1f));

                UpdateRerollButton();
                _rerollRequested = false;
                _confirmRequested = false;

                // 카드 클릭 또는 리롤 대기
                while (!_rerollRequested && !_confirmRequested)
                    yield return null;

                // 남은 카드 인스턴스 정리 (선택된 카드는 이미 중앙 이동 후 남아있을 수 있음)
                foreach (var inst in _currentCardInstances)
                    if (inst != null) Destroy(inst);
                _currentCardInstances.Clear();

                // 결과 패널 페이드아웃
                yield return StartCoroutine(FadeResultPanel(1f, 0f));
                resultPanel.SetActive(false);

                if (_rerollRequested)
                {
                    _rerollsRemaining--;
                    reroll = true;
                }
            }
            else
            {
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
    //  결과 UI 세팅
    // ─────────────────────────────────────────
    private void SetupResultUI(RouletteSegment segment)
    {
        // 컨테이너 재활성화 + 위치 원복 (이전 선택에서 이동/숨긴 것 복원)
        if (resultCardContainers != null)
        {
            for (int i = 0; i < resultCardContainers.Length; i++)
            {
                if (resultCardContainers[i] == null) continue;
                resultCardContainers[i].gameObject.SetActive(true);
                if (_containerOrigPositions != null && i < _containerOrigPositions.Length)
                {
                    var rt = resultCardContainers[i].GetComponent<RectTransform>();
                    if (rt != null) rt.anchoredPosition = _containerOrigPositions[i];
                }
            }
        }

        foreach (var inst in _currentCardInstances)
            if (inst != null) Destroy(inst);
        _currentCardInstances.Clear();
        _currentCardPrefabs.Clear();
        _currentCardSegments.Clear();

        ResultCardPrefab = null;
        ResultSegment = segment;

        UpdateResultBackground();

        if (segment?.cardUIPrefabs == null || segment.cardUIPrefabs.Length == 0) return;

        for (int i = 0; i < 3; i++)
        {
            var prefab = segment.cardUIPrefabs[UnityEngine.Random.Range(0, segment.cardUIPrefabs.Length)];
            if (prefab == null) continue;

            _currentCardPrefabs.Add(prefab);
            _currentCardSegments.Add(segment);

            var container = (resultCardContainers != null && i < resultCardContainers.Length)
                ? resultCardContainers[i] : null;
            if (container == null) continue;

            var inst = Instantiate(prefab, container);
            _currentCardInstances.Add(inst);

            // 버튼 추가
            var btn = inst.GetComponent<Button>();
            if (btn == null) btn = inst.AddComponent<Button>();

            if (btn.targetGraphic == null)
            {
                var img = inst.GetComponent<Image>();
                if (img == null)
                {
                    img = inst.AddComponent<Image>();
                    img.color = Color.clear;
                }
                img.raycastTarget = true;
                btn.targetGraphic = img;
            }

            // 자식 Image는 raycast 차단하지 않게
            foreach (var childImg in inst.GetComponentsInChildren<Image>(true))
            {
                if (childImg.gameObject != inst)
                    childImg.raycastTarget = false;
            }

            int capturedIndex = _currentCardInstances.Count - 1;
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(() => OnCardClicked(capturedIndex));
        }

        ResultCardPrefab = _currentCardPrefabs.Count > 0 ? _currentCardPrefabs[0] : null;
    }

    // ─────────────────────────────────────────
    //  카드 클릭: 다른 카드 제거 → 중앙 이동 → 확인
    // ─────────────────────────────────────────
    private void OnCardClicked(int index)
    {
        if (_confirmRequested || _rerollRequested) return;
        StartCoroutine(SelectAndConfirm(index));
    }

    private IEnumerator SelectAndConfirm(int index)
    {
        ResultCardPrefab = index < _currentCardPrefabs.Count ? _currentCardPrefabs[index] : null;
        ResultSegment = index < _currentCardSegments.Count ? _currentCardSegments[index] : null;

        // 다른 컨테이너(배경+카드) 숨기기
        for (int i = 0; i < resultCardContainers.Length; i++)
        {
            if (i != index && resultCardContainers[i] != null)
                resultCardContainers[i].gameObject.SetActive(false);
        }

        // 선택된 컨테이너를 X 중앙으로 이동 (Y 유지)
        var container = (index < resultCardContainers.Length) ? resultCardContainers[index] : null;
        if (container != null)
        {
            var rt = container as RectTransform ?? container.GetComponent<RectTransform>();
            if (rt != null)
            {
                Vector2 startPos = rt.anchoredPosition;
                Vector2 targetPos = new Vector2(0f, startPos.y);
                float elapsed = 0f;
                float duration = 0.35f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
                    rt.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
                    yield return null;
                }
                rt.anchoredPosition = targetPos;
            }
            else
            {
                yield return new WaitForSeconds(0.2f);
            }
        }
        else
        {
            yield return new WaitForSeconds(0.2f);
        }

        _confirmRequested = true;
    }

    // ─────────────────────────────────────────
    //  리롤 버튼
    // ─────────────────────────────────────────
    private void UpdateRerollButton()
    {
        if (_rerollButtonText != null)
            _rerollButtonText.text = $"ReRoll {_rerollsRemaining}";
        if (rerollButton != null)
            rerollButton.gameObject.SetActive(_rerollsRemaining > 0);
    }

    private void UpdateResultBackground()
    {
        if (resultBackgroundImages == null || ResultSegment == null) return;
        foreach (var img in resultBackgroundImages)
        {
            if (img == null) continue;
            img.sprite = ResultSegment.backgroundImage;
            img.enabled = ResultSegment.backgroundImage != null;
        }
    }

    // ─────────────────────────────────────────
    //  슬라이드 / 페이드 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator SlideRoulette(Vector2 from, Vector2 to)
    {
        if (rouletteRect == null) yield break;
        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / slideDuration);
            float eased = t * t * (3f - 2f * t);
            rouletteRect.anchoredPosition = Vector2.Lerp(from, to, eased);
            yield return null;
        }
        rouletteRect.anchoredPosition = to;
    }

    private IEnumerator SlideRouletteAndHide(Vector2 from, Vector2 to)
    {
        yield return StartCoroutine(SlideRoulette(from, to));
        if (rouletteRect != null)
            rouletteRect.gameObject.SetActive(false);
    }

    private IEnumerator FadeResultPanel(float from, float to)
    {
        if (resultPanel == null) yield break;
        resultPanel.SetActive(true);

        var graphics = resultPanel.GetComponentsInChildren<Graphic>(true);

        // 원래 색 캡처
        Color[] origColors = new Color[graphics.Length];
        for (int i = 0; i < graphics.Length; i++)
            origColors[i] = graphics[i].color;

        // 페이드인 시: 이전 페이드아웃으로 알파0이 된 Graphic만 복원 (원래 투명한 건 제외)
        if (to > from)
        {
            for (int i = 0; i < graphics.Length; i++)
            {
                Color c = origColors[i];
                if (c.a < 0.01f && (c.r > 0.01f || c.g > 0.01f || c.b > 0.01f))
                {
                    c.a = 1f;
                    graphics[i].color = c;
                    origColors[i] = c;
                }
            }
        }

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
            float alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / fadeDuration));
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] == null) continue;
                Color c = origColors[i];
                c.a = origColors[i].a * alpha;
                graphics[i].color = c;
            }
            yield return null;
        }

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
            if (Mathf.Approximately(end, 0f) && seg.endAngle > 0f)
                end = 360f;

            if (start < end)
            {
                if (angle >= start && angle < end) return seg;
            }
            else
            {
                if (angle >= start || angle < end) return seg;
            }
        }
        return null;
    }
}
