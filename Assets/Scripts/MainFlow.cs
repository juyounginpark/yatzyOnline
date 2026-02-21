using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  메인 턴 관리
//  - 플레이어 / 상대 턴 전환
//  - 제한 시간 초과 시 자동 턴넘김
//  - 턴 종료 시 슬롯 카드가 상대 스폰으로 날아가 타격
// ─────────────────────────────────────────────
public class MainFlow : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public Deck deck;
    public OppDeck oppDeck;
    public Slot[] playerSlots;
    public Slot[] oppSlots;

    [Header("─ UI ─")]
    public Button endTurnButton;

    [Header("─ 참조 (점수 표시용) ─")]
    public GameFlow gameFlow;
    public GameUI gameUI;

    [Header("─ 턴 설정 ─")]
    public float turnTime = 30f;

    [Header("─ 쇼케이스 설정 ─")]
    [Tooltip("중간 전시 시간")]
    public float showcaseTime = 1f;

    [Tooltip("쇼케이스 위치로 이동 시간")]
    public float showcaseMoveDuration = 0.5f;

    [Tooltip("쇼케이스 카드 간 간격")]
    public float showcaseSpacing = 1.2f;

    [Header("─ 공격 애니메이션 ─")]
    [Tooltip("카드가 날아가는 시간")]
    public float attackDuration = 0.4f;

    [Tooltip("카드 간 발사 딜레이")]
    public float attackStagger = 0.06f;

    [Header("─ 피격 연출 ─")]
    [Tooltip("피격 흔들림 시간")]
    public float hitShakeDuration = 0.35f;

    [Tooltip("덱 흔들림 강도")]
    public float hitShakeIntensity = 0.15f;

    [Tooltip("카메라 흔들림 강도")]
    public float cameraShakeIntensity = 0.08f;

    // ─── 상태 ───
    private bool _isPlayerTurn = true;
    private float _timer;
    private bool _isTransitioning;

    // AI 참조 (상대 턴 활동 중에는 타이머로 강제 전환 안 함)
    private OppAuto _oppAuto;

    public bool IsPlayerTurn => _isPlayerTurn;
    public float TimeRemaining => Mathf.Max(0f, _timer);
    public bool IsTransitioning => _isTransitioning;

    void Start()
    {
        _timer = turnTime;
        _isPlayerTurn = true;

        // OppAuto 참조 캐시
        _oppAuto = FindObjectOfType<OppAuto>();

        if (endTurnButton != null)
            endTurnButton.onClick.AddListener(EndTurn);

        UpdateInteraction();
    }

    void Update()
    {
        if (_isTransitioning) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            // 상대 턴이고 AI가 활동 중이면 타이머로 강제 전환하지 않음
            // (AI가 배치/플립 완료 후 스스로 EndTurn 호출)
            if (!_isPlayerTurn && _oppAuto != null && _oppAuto.IsActing)
                return;

            EndTurn();
        }
    }

    // ─────────────────────────────────────────
    //  턴 넘기기 (버튼 onClick에 연결)
    // ─────────────────────────────────────────
    public void EndTurn()
    {
        if (_isTransitioning) return;
        StartCoroutine(DoEndTurn());
    }

    // ─────────────────────────────────────────
    //  턴 전환 시퀀스
    // ─────────────────────────────────────────
    private IEnumerator DoEndTurn()
    {
        _isTransitioning = true;

        // 내 스폰 위치
        Transform mySpawn = _isPlayerTurn
            ? (deck.deckSpawnPoint != null ? deck.deckSpawnPoint : deck.transform)
            : (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint : oppDeck.transform);

        // 타격 목표: 상대의 스폰 위치
        Transform target = _isPlayerTurn
            ? (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint : oppDeck.transform)
            : (deck.deckSpawnPoint != null ? deck.deckSpawnPoint : deck.transform);

        // ── 상대 턴 종료 시: 슬롯 카드 점수/콤보를 UI에 표시 ──
        Slot[] slotsToRelease = _isPlayerTurn ? playerSlots : oppSlots;
        if (!_isPlayerTurn && slotsToRelease != null)
        {
            float score = 0f;
            string rule = "";
            score = EvaluateSlots(slotsToRelease, out rule);

            if (score > 0f && gameUI != null && gameUI.scoreText != null)
            {
                gameUI.isScoreOverridden = true;  // Update() 덮어쓰기 차단
                gameUI.scoreText.gameObject.SetActive(true);
                gameUI.scoreText.text = $"+{score:F1}\n({rule})";
                yield return new WaitForSeconds(1f);
                gameUI.scoreText.gameObject.SetActive(false);
                gameUI.isScoreOverridden = false;  // 차단 해제
            }
        }

        // 슬롯에서 카드 수거
        List<GameObject> flyingCards = new List<GameObject>();

        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot == null || !slot.HasCard) continue;
                var card = slot.ReleaseCard();
                if (card != null)
                {
                    foreach (var r in card.GetComponentsInChildren<Renderer>())
                        r.sortingOrder = 500;
                    flyingCards.Add(card);
                }
            }
        }

        if (flyingCards.Count > 0)
        {
            // 쇼케이스: PlayerSpawn과 OppSpawn 중간 지점에 카드 나열
            Vector3 showcaseCenter = (mySpawn.position + target.position) * 0.5f;
            showcaseCenter.z = 0f;

            yield return StartCoroutine(ArrangeAtShowcase(flyingCards, showcaseCenter));

            // 전시 대기 (무슨 카드 냈는지 확인)
            yield return new WaitForSeconds(showcaseTime);

            // 상대 방향으로 회전 후 날리기
            yield return StartCoroutine(FlyAndHit(flyingCards, target.position));

            // 피격 연출: 맞은 쪽 덱 흔들림 + 카메라 흔들림
            StartCoroutine(ShakeTransform(target, hitShakeDuration, hitShakeIntensity));
            yield return StartCoroutine(ShakeCamera(hitShakeDuration, cameraShakeIntensity));
        }

        // 안전 정리: 슬롯에 남은 카드 강제 제거
        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot != null && slot.HasCard)
                    slot.ClearCard();
            }
        }

        // 턴 전환
        _isPlayerTurn = !_isPlayerTurn;
        _timer = turnTime;

        UpdateInteraction();

        // 새 턴: 카드 1장 추가
        if (_isPlayerTurn)
            deck.AddOneCard();
        else
            oppDeck.AddOneCard();

        _isTransitioning = false;
    }

    // ─────────────────────────────────────────
    //  슬롯 카드 평가 (점수 + 콤보 이름)
    // ─────────────────────────────────────────
    private float EvaluateSlots(Slot[] slots, out string ruleName)
    {
        ruleName = "";
        if (gameFlow == null || slots == null) return 0f;

        List<int> values = new List<int>();
        foreach (var slot in slots)
        {
            if (slot == null || !slot.HasCard) continue;
            var cv = slot.GetCardValue();
            if (cv != null) values.Add(cv.value);
        }

        if (values.Count == 0) return 0f;
        return gameFlow.EvaluateHand(values.ToArray(), out ruleName);
    }

    // ─────────────────────────────────────────
    //  상호작용 제어
    // ─────────────────────────────────────────
    private void UpdateInteraction()
    {
        // 플레이어 턴에만 슬롯 배치 허용
        if (deck != null)
            deck.canPlaceInSlot = _isPlayerTurn;

        if (endTurnButton != null)
            endTurnButton.interactable = _isPlayerTurn;
    }

    // ─────────────────────────────────────────
    //  쇼케이스: 중간 지점에 카드 나열 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator ArrangeAtShowcase(List<GameObject> cards, Vector3 center)
    {
        int count = cards.Count;

        // 목표 위치: 중앙 기준 균등 배치
        Vector3[] targets = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float offset = (i - (count - 1) * 0.5f) * showcaseSpacing;
            targets[i] = new Vector3(center.x + offset, center.y, 0f);
        }

        // 시작 상태 저장
        Vector3[] startPositions = new Vector3[count];
        Quaternion[] startRotations = new Quaternion[count];
        for (int i = 0; i < count; i++)
        {
            startPositions[i] = cards[i].transform.position;
            startRotations[i] = cards[i].transform.rotation;
        }

        float elapsed = 0f;
        while (elapsed < showcaseMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / showcaseMoveDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            for (int i = 0; i < count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.position = Vector3.Lerp(startPositions[i], targets[i], eased);
                cards[i].transform.rotation = Quaternion.Slerp(startRotations[i], Quaternion.identity, eased);
            }

            yield return null;
        }

        // 최종 위치 보정
        for (int i = 0; i < count; i++)
        {
            if (cards[i] == null) continue;
            cards[i].transform.position = targets[i];
            cards[i].transform.rotation = Quaternion.identity;
        }
    }

    // ─────────────────────────────────────────
    //  카드 날리기: 회전 → 발사 + 타격 연출
    // ─────────────────────────────────────────
    private IEnumerator FlyAndHit(List<GameObject> cards, Vector3 targetWorld)
    {
        // 1) 상대 방향으로 회전 (0.3초)
        float rotateDuration = 0.3f;
        Quaternion[] startRotations = new Quaternion[cards.Count];
        Quaternion[] targetRotations = new Quaternion[cards.Count];

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            startRotations[i] = cards[i].transform.rotation;

            // 180도 회전
            targetRotations[i] = startRotations[i] * Quaternion.Euler(0f, 0f, 45f);
        }

        float elapsed = 0f;
        while (elapsed < rotateDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / rotateDuration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.rotation = Quaternion.Slerp(startRotations[i], targetRotations[i], eased);
            }

            yield return null;
        }

        // 2) 순차적으로 발사
        List<Coroutine> flights = new List<Coroutine>();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            flights.Add(StartCoroutine(FlyOneCard(cards[i], targetWorld)));
            if (i < cards.Count - 1)
                yield return new WaitForSeconds(attackStagger);
        }

        // 마지막 카드 도착 대기
        if (flights.Count > 0)
            yield return flights[flights.Count - 1];
    }

    private IEnumerator FlyOneCard(GameObject card, Vector3 targetWorld)
    {
        Vector3 startPos = card.transform.position;
        Vector3 startScale = card.transform.localScale;
        Quaternion startRot = card.transform.rotation;

        // 모든 SpriteRenderer 수집
        var renderers = card.GetComponentsInChildren<SpriteRenderer>();
        Color[] startColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            startColors[i] = renderers[i].color;

        float elapsed = 0f;

        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / attackDuration);

            // ease-in (가속)
            float eased = t * t;

            card.transform.position = Vector3.Lerp(startPos, targetWorld, eased);
            card.transform.rotation = startRot; // 회전 유지
            card.transform.localScale = Vector3.Lerp(startScale, startScale * 0.3f, eased);

            // 페이드 아웃: 후반부(40%~100%)에서 자연스럽게
            float fadeT = Mathf.Clamp01((t - 0.4f) / 0.6f);
            float alpha = 1f - fadeT;
            for (int i = 0; i < renderers.Length; i++)
            {
                Color c = startColors[i];
                c.a = startColors[i].a * alpha;
                renderers[i].color = c;
            }

            yield return null;
        }

        Destroy(card);
    }

    // ─────────────────────────────────────────
    //  피격 연출: 덱 흔들림
    // ─────────────────────────────────────────
    private IEnumerator ShakeTransform(Transform target, float duration, float intensity)
    {
        Vector3 originalPos = target.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration); // 감쇠
            float offsetX = Random.Range(-1f, 1f) * intensity * t;
            float offsetY = Random.Range(-1f, 1f) * intensity * t;
            target.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);
            yield return null;
        }

        target.localPosition = originalPos;
    }

    // ─────────────────────────────────────────
    //  피격 연출: 카메라 흔들림
    // ─────────────────────────────────────────
    private IEnumerator ShakeCamera(float duration, float intensity)
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 originalPos = cam.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration); // 감쇠
            float offsetX = Random.Range(-1f, 1f) * intensity * t;
            float offsetY = Random.Range(-1f, 1f) * intensity * t;
            cam.transform.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);
            yield return null;
        }

        cam.transform.localPosition = originalPos;
    }
}
