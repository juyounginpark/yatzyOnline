using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

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
    public TMP_Text endTurnButtonText;

    [Header("─ 참조 (점수 표시용) ─")]
    public GameFlow gameFlow;
    public GameUI gameUI;
    public HP hp;

    [Header("─ 턴 설정 ─")]
    public float turnTime = 30f;

    [Header("─ 쇼케이스 설정 ─")]
    [Tooltip("중간 전시 시간")]
    public float showcaseTime = 1f;

    [Tooltip("쇼케이스 위치로 이동 시간")]
    public float showcaseMoveDuration = 0.5f;

    [Tooltip("정렬 애니메이션 시간")]
    public float showcaseSortDuration = 0.4f;

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

    [Header("─ 턴 카드 크기 연출 ─")]
    [Tooltip("활성 턴 카드 스케일")]
    public float activeScale = 1.2f;

    [Tooltip("비활성 턴 카드 스케일")]
    public float inactiveScale = 0.9f;

    [Tooltip("스케일 전환 시간")]
    public float scaleDuration = 0.3f;

    // ─── 상태 ───
    private bool _isPlayerTurn = true;
    private float _timer;
    private bool _isTransitioning;
    private int _playerNextDraw = 1;
    private int _oppNextDraw = 1;

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

        // 초기 스케일: 플레이어 턴이므로 플레이어 확대, 상대 축소
        SetDeckScale(deck, activeScale);
        SetDeckScale(oppDeck, inactiveScale);
    }

    void Update()
    {
        if (_isTransitioning) return;

        // 상대 턴이고 AI가 카드 애니메이션 중이면 타이머 일시정지
        bool oppAnimating = !_isPlayerTurn && _oppAuto != null && _oppAuto.IsAnimating;

        if (!oppAnimating)
            _timer -= Time.deltaTime;

        // 버튼 텍스트에 남은 시간 표시 (전환 중이 아닐 때만)
        if (endTurnButtonText != null && !_isTransitioning)
            endTurnButtonText.text = Mathf.CeilToInt(Mathf.Max(0f, _timer)).ToString();

        if (_timer <= 0f && !oppAnimating)
        {
            EndTurn();
        }
    }

    // ─────────────────────────────────────────
    //  턴 넘기기 (버튼 onClick에 연결)
    // ─────────────────────────────────────────
    public void EndTurn()
    {
        if (_isTransitioning) return;
        if (endTurnButtonText != null)
            endTurnButtonText.text = "...";
        if (endTurnButton != null)
            endTurnButton.gameObject.SetActive(false);
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

        // ── 슬롯 카드 점수 계산 + UI 표시 + HP 차감 ──
        Slot[] slotsToRelease = _isPlayerTurn ? playerSlots : oppSlots;
        float turnScore = 0f;
        string turnRule = "";
        if (slotsToRelease != null)
        {
            turnScore = EvaluateSlots(slotsToRelease, out turnRule);

            if (turnScore > 0f && gameUI != null && gameUI.scoreText != null)
            {
                gameUI.isScoreOverridden = true;
                gameUI.scoreText.gameObject.SetActive(true);
                gameUI.scoreText.text = $"+{turnScore:F1}\n({turnRule})";
                yield return new WaitForSeconds(1f);
                gameUI.scoreText.gameObject.SetActive(false);
                gameUI.isScoreOverridden = false;
            }
        }

        // 슬롯에서 카드 수거 (Attack / Heal 분리)
        List<GameObject> attackCards = new List<GameObject>();
        List<GameObject> healCards = new List<GameObject>();

        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot == null || !slot.HasCard) continue;
                var cv = slot.GetCardValue();
                CardType type = cv != null ? cv.cardType : CardType.Attack;
                var card = slot.ReleaseCard();
                if (card != null)
                {
                    foreach (var r in card.GetComponentsInChildren<Renderer>())
                        r.sortingOrder = 500;
                    if (type == CardType.Heal)
                        healCards.Add(card);
                    else
                        attackCards.Add(card);
                }
            }
        }

        List<GameObject> allCards = new List<GameObject>();
        allCards.AddRange(attackCards);
        allCards.AddRange(healCards);

        if (allCards.Count > 0)
        {
            // 쇼케이스: 중간 지점에 전체 카드 나열
            Vector3 showcaseCenter = (mySpawn.position + target.position) * 0.5f;
            showcaseCenter.z = 0f;

            yield return StartCoroutine(ArrangeAtShowcase(allCards, showcaseCenter));

            if (allCards.Count > 1)
            {
                yield return new WaitForSeconds(0.7f);

                // 1단계: 타입별 정렬 (Attack → Heal)
                yield return StartCoroutine(SortShowcaseBy(allCards, showcaseCenter, SortByType));

                // 카드 숫자가 전부 동일하지 않을 때만 2단계 정렬
                bool allSameIndex = true;
                int firstIndex = allCards[0] != null ? allCards[0].GetComponent<CardValue>()?.poolIndex ?? 0 : 0;
                for (int i = 1; i < allCards.Count; i++)
                {
                    int idx = allCards[i] != null ? allCards[i].GetComponent<CardValue>()?.poolIndex ?? 0 : 0;
                    if (idx != firstIndex) { allSameIndex = false; break; }
                }

                if (!allSameIndex)
                {
                    yield return new WaitForSeconds(0.7f);

                    // 2단계: 숫자 정렬 (왼쪽 작은 수 → 오른쪽 큰 수)
                    yield return StartCoroutine(SortShowcaseBy(allCards, showcaseCenter, SortByTypeAndValue));
                }
            }

            yield return new WaitForSeconds(showcaseTime);

            // Attack 카드 → 상대 덱으로, Heal 카드 → 자기 덱으로
            Coroutine attackFly = null, healFly = null;
            if (attackCards.Count > 0)
                attackFly = StartCoroutine(FlyAndHit(attackCards, target.position));
            if (healCards.Count > 0)
                healFly = StartCoroutine(FlyAndHit(healCards, mySpawn.position));

            if (attackFly != null) yield return attackFly;
            if (healFly != null) yield return healFly;

            // 피격 연출 (Attack 카드가 있을 때만)
            if (attackCards.Count > 0)
            {
                StartCoroutine(ShakeTransform(target, hitShakeDuration, hitShakeIntensity));
                yield return StartCoroutine(ShakeCamera(hitShakeDuration, cameraShakeIntensity));
            }

            // HP 처리: 카드 비율에 따라 데미지 / 회복 분배
            if (hp != null && turnScore > 0f)
            {
                int totalCount = attackCards.Count + healCards.Count;
                float attackRatio = (float)attackCards.Count / totalCount;
                float healRatio = (float)healCards.Count / totalCount;

                float attackScore = turnScore * attackRatio;
                float healScore = turnScore * healRatio * 0.5f; // 힐 계수 1/2

                if (attackScore > 0f)
                {
                    if (_isPlayerTurn)
                        hp.DamageOpp(attackScore);
                    else
                        hp.DamagePlayer(attackScore);
                }
                if (healScore > 0f)
                {
                    if (_isPlayerTurn)
                        hp.HealPlayer(healScore);
                    else
                        hp.HealOpp(healScore);
                }
            }

            // 콤보(원페어 이상)일 때만 카드 수만큼 다음 드로우
            bool isCombo = turnRule != "" && turnRule != "하이카드";
            if (isCombo && allCards.Count > 0)
            {
                if (_isPlayerTurn)
                    _playerNextDraw = allCards.Count;
                else
                    _oppNextDraw = allCards.Count;
            }

            // 점수 비례 보너스 카드 (0~2장): 40점 미만 0장, 40~79점 1장, 80점 이상 2장
            if (turnScore > 0f)
            {
                int bonusCards = Mathf.Clamp(Mathf.FloorToInt(turnScore / 40f), 0, 2);
                if (_isPlayerTurn)
                    _playerNextDraw += bonusCards;
                else
                    _oppNextDraw += bonusCards;
            }
        }

        // 안전 정리: 슬롯에 남은 카드 → 덱으로 복귀
        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot == null || !slot.HasCard) continue;

                var cv = slot.GetCardValue();
                int value = cv != null ? cv.value : 0;
                bool isJoker = cv != null && cv.isJoker;
                CardType type = cv != null ? cv.cardType : CardType.Attack;

                slot.ClearCard();

                if (_isPlayerTurn)
                {
                    if (isJoker)
                        deck.AddJokerCard(type);
                    else if (value > 0)
                        deck.AddCardByValue(value, type);
                }
                else
                {
                    if (value > 0)
                        oppDeck.AddCardByValue(value, type);
                }
            }
        }

        // 턴 전환
        _isPlayerTurn = !_isPlayerTurn;
        _timer = turnTime;

        UpdateInteraction();

        // 새 턴: 이전 콤보 카드 수만큼 드로우
        if (_isPlayerTurn)
        {
            for (int i = 0; i < _playerNextDraw; i++)
                deck.AddOneCard();
            _playerNextDraw = 1;
        }
        else
        {
            for (int i = 0; i < _oppNextDraw; i++)
                oppDeck.AddOneCard();
            _oppNextDraw = 1;
        }

        // 카드 크기 전환 애니메이션
        yield return StartCoroutine(AnimateTurnScale());

        _isTransitioning = false;
    }

    // ─────────────────────────────────────────
    //  턴 전환 스케일 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator AnimateTurnScale()
    {
        // 활성 턴 → 확대, 비활성 턴 → 축소
        MonoBehaviour activeDeck = _isPlayerTurn ? (MonoBehaviour)deck : (MonoBehaviour)oppDeck;
        MonoBehaviour inactiveDeck = _isPlayerTurn ? (MonoBehaviour)oppDeck : (MonoBehaviour)deck;

        Transform activeT = GetDeckTransform(activeDeck);
        Transform inactiveT = GetDeckTransform(inactiveDeck);

        if (activeT == null && inactiveT == null) yield break;

        Vector3 activeStart = activeT != null ? activeT.localScale : Vector3.one;
        Vector3 inactiveStart = inactiveT != null ? inactiveT.localScale : Vector3.one;
        Vector3 activeTarget = Vector3.one * activeScale;
        Vector3 inactiveTarget = Vector3.one * inactiveScale;

        float elapsed = 0f;
        while (elapsed < scaleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scaleDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            if (activeT != null)
                activeT.localScale = Vector3.Lerp(activeStart, activeTarget, eased);
            if (inactiveT != null)
                inactiveT.localScale = Vector3.Lerp(inactiveStart, inactiveTarget, eased);

            yield return null;
        }

        if (activeT != null) activeT.localScale = activeTarget;
        if (inactiveT != null) inactiveT.localScale = inactiveTarget;
    }

    private void SetDeckScale(MonoBehaviour deckComp, float scale)
    {
        Transform t = GetDeckTransform(deckComp);
        if (t != null) t.localScale = Vector3.one * scale;
    }

    private Transform GetDeckTransform(MonoBehaviour deckComp)
    {
        if (deckComp == null) return null;
        if (deckComp is Deck d && d.deckSpawnPoint != null) return d.deckSpawnPoint;
        if (deckComp is OppDeck od && od.deckSpawnPoint != null) return od.deckSpawnPoint;
        return deckComp.transform;
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
        {
            endTurnButton.gameObject.SetActive(true);
            endTurnButton.interactable = _isPlayerTurn;
        }
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
    //  쇼케이스 정렬 (범용)
    // ─────────────────────────────────────────
    private IEnumerator SortShowcaseBy(List<GameObject> cards, Vector3 center,
        System.Comparison<GameObject> comparison)
    {
        int count = cards.Count;

        int[] indices = new int[count];
        for (int i = 0; i < count; i++) indices[i] = i;
        System.Array.Sort(indices, (a, b) => comparison(cards[a], cards[b]));

        Vector3[] sortedTargets = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            float offset = (i - (count - 1) * 0.5f) * showcaseSpacing;
            sortedTargets[i] = new Vector3(center.x + offset, center.y, 0f);
        }

        Vector3[] startPositions = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            if (cards[indices[i]] != null)
                startPositions[i] = cards[indices[i]].transform.position;
        }

        float elapsed = 0f;
        while (elapsed < showcaseSortDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / showcaseSortDuration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < count; i++)
            {
                if (cards[indices[i]] == null) continue;
                cards[indices[i]].transform.position = Vector3.Lerp(startPositions[i], sortedTargets[i], eased);
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (cards[indices[i]] == null) continue;
            cards[indices[i]].transform.position = sortedTargets[i];
            foreach (var r in cards[indices[i]].GetComponentsInChildren<Renderer>())
                r.sortingOrder = 500 + i;
        }
    }

    // ── 정렬 비교 함수: 타입별 (Attack → Heal) ──
    private static int SortByType(GameObject a, GameObject b)
    {
        var cva = a != null ? a.GetComponent<CardValue>() : null;
        var cvb = b != null ? b.GetComponent<CardValue>() : null;
        int ta = cva != null ? (int)cva.cardType : 0;
        int tb = cvb != null ? (int)cvb.cardType : 0;
        return ta.CompareTo(tb);
    }

    // ── 정렬 비교 함수: 타입별 → 덱 인덱스별 ──
    private static int SortByTypeAndValue(GameObject a, GameObject b)
    {
        var cva = a != null ? a.GetComponent<CardValue>() : null;
        var cvb = b != null ? b.GetComponent<CardValue>() : null;
        int ta = cva != null ? (int)cva.cardType : 0;
        int tb = cvb != null ? (int)cvb.cardType : 0;
        if (ta != tb) return ta.CompareTo(tb);
        int pa = cva != null ? cva.poolIndex : 0;
        int pb = cvb != null ? cvb.poolIndex : 0;
        return pa.CompareTo(pb);
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
