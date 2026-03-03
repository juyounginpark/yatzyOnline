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
    public Button sortByNumButton;
    public Button sortByTypeButton;
    public Button scoreUIButton;

    [Header("─ UI 캔버스 ─")]
    [Tooltip("버튼 UI가 카드 위에 표시되도록 Canvas 설정")]
    public Canvas uiCanvas;

    [Header("─ 참조 (점수 표시용) ─")]
    public GameFlow gameFlow;
    public GameUI gameUI;
    public HP hp;
    public Exp exp;
    public Roulette roulette;

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
    private bool _scoreSkipped;

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

        if (scoreUIButton != null)
            scoreUIButton.onClick.AddListener(() => _scoreSkipped = true);

        if (sortByNumButton != null)
            sortByNumButton.onClick.AddListener(() => { if (deck != null && !_isTransitioning) deck.SortByNumber(); });

        if (sortByTypeButton != null)
            sortByTypeButton.onClick.AddListener(() => { if (deck != null && !_isTransitioning) deck.SortByType(); });

        // UI Canvas가 카드 아래에 표시되도록 설정
        if (uiCanvas == null && endTurnButton != null)
            uiCanvas = endTurnButton.GetComponentInParent<Canvas>();
        if (uiCanvas != null)
        {
            uiCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            uiCanvas.worldCamera = Camera.main;
            uiCanvas.sortingOrder = 0;
        }

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

                // 클릭으로 스킵 가능한 대기
                _scoreSkipped = false;
                float waited = 0f;
                while (waited < 1f && !_scoreSkipped)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                // 버튼 상태 초기화 (pressed 색상 고정 방지)
                if (scoreUIButton != null)
                {
                    scoreUIButton.OnDeselect(null);
                    scoreUIButton.interactable = false;
                    scoreUIButton.interactable = true;
                }

                gameUI.scoreText.gameObject.SetActive(false);
                // isScoreOverridden는 전환 완료 시까지 유지 → 공격 후 잠깐 뜨는 문제 방지
            }

            // (EXP 추가는 공격 연출 이후로 이동됨)
        }

        // 조커 해석 값을 카드에 할당 (정렬용)
        int[] resolvedValues = null;
        if (slotsToRelease != null && gameFlow != null)
        {
            string dummyName;
            float dummyScore;
            gameFlow.GetBestCombo(slotsToRelease, out dummyName, out dummyScore, out resolvedValues);

            for (int i = 0; i < slotsToRelease.Length; i++)
            {
                if (slotsToRelease[i] == null || !slotsToRelease[i].HasCard) continue;
                var cv = slotsToRelease[i].GetCardValue();
                if (cv != null && cv.isJoker && resolvedValues != null && i < resolvedValues.Length)
                    cv.value = resolvedValues[i];
            }
        }

        // 슬롯에서 카드 수거 (Attack / Critical / Heal 분리, 뒷면·revealedOnly 카드는 제외)
        List<GameObject> attackCards = new List<GameObject>();
        List<GameObject> criticalCards = new List<GameObject>();
        List<GameObject> healCards = new List<GameObject>();

        // 슬롯에서 카드 수거 (Attack / Critical / Heal 분리)
        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot == null || !slot.HasCard) continue;
                if (!slot.HasVisibleCard) continue;
                var cv = slot.GetCardValue();
                CardType type = cv != null ? cv.cardType : CardType.Attack;
                var card = slot.ReleaseCard();
                if (card != null)
                {
                    card.transform.localScale = Vector3.one; // 일단 기본 크기로
                    foreach (var r in card.GetComponentsInChildren<Renderer>())
                        r.sortingOrder = (type == CardType.Heal) ? 400 : 500;
                    if (type == CardType.Heal)
                        healCards.Add(card);
                    else if (type == CardType.Critical)
                        criticalCards.Add(card);
                    else
                        attackCards.Add(card);
                }
            }
        }

        // 카드 크기 통일: playerSlots[0] 콜라이더 크기에 맞춤 (모든 카드 동일)
        List<GameObject> allReleasedCards = new List<GameObject>();
        allReleasedCards.AddRange(attackCards);
        allReleasedCards.AddRange(criticalCards);
        allReleasedCards.AddRange(healCards);

        Vector3 uniformCardScale = Vector3.one;
        if (playerSlots != null && playerSlots.Length > 0 && playerSlots[0] != null)
        {
            var refCol = playerSlots[0].GetComponent<Collider2D>();
            var deckRefScale = FindObjectOfType<Deck>();
            if (refCol != null && deckRefScale != null && deckRefScale.cardBackPrefab != null)
            {
                var backSr = deckRefScale.cardBackPrefab.GetComponent<SpriteRenderer>();
                if (backSr != null && backSr.sprite != null)
                {
                    Vector2 spriteSize = backSr.sprite.bounds.size;
                    Vector2 slotSize = refCol.bounds.size;
                    float s = Mathf.Min(slotSize.x / spriteSize.x, slotSize.y / spriteSize.y);
                    uniformCardScale = new Vector3(s, s, 1f);
                }
            }
        }
        foreach (var card in allReleasedCards)
        {
            if (card != null) card.transform.localScale = uniformCardScale;
        }

        List<GameObject> allCards = new List<GameObject>();
        allCards.AddRange(attackCards);
        allCards.AddRange(criticalCards);
        allCards.AddRange(healCards);

        int originalAttackCardCount = allCards.Count;

        // 쇼케이스 중심점
        Vector3 showcaseCenter = (mySpawn.position + target.position) * 0.5f;
        showcaseCenter.z = 0f;

        if (allCards.Count > 0)
        {
            Vector3 attackTarget = target.position;
            Vector3 healTarget = mySpawn.position;
            Transform shakeTarget = target;

            // Attack/Critical 카드 → 공격 대상, Heal 카드 → 힐 대상
            List<GameObject> allAttackCards = new List<GameObject>();
            allAttackCards.AddRange(attackCards);
            allAttackCards.AddRange(criticalCards);

            yield return StartCoroutine(GatherToPoint(allCards, showcaseCenter));
            yield return new WaitForSeconds(0.3f);

            // 공격 카드 먼저 날리기
            if (allAttackCards.Count > 0)
                yield return StartCoroutine(FlyAndHit(allAttackCards, attackTarget));

            // HP 처리 (공격)
            if (hp != null && turnScore > 0f)
            {
                int totalCount = attackCards.Count + criticalCards.Count + healCards.Count;
                float attackRatio = (float)attackCards.Count / totalCount;
                float criticalRatio = (float)criticalCards.Count / totalCount;
                float healRatio = (float)healCards.Count / totalCount;

                float attackScore = turnScore * attackRatio;
                float criticalScore = turnScore * criticalRatio * 2f; // 크리티컬 2배
                float totalAttackScore = attackScore + criticalScore;
                float healScore = turnScore * healRatio;

                // 피격 연출
                if (allAttackCards.Count > 0)
                {
                    float shakeMult = Mathf.Max(1f, Mathf.Floor(totalAttackScore / 10f));
                    StartCoroutine(ShakeTransform(shakeTarget, hitShakeDuration, hitShakeIntensity * shakeMult));
                    yield return StartCoroutine(ShakeCamera(hitShakeDuration, cameraShakeIntensity * shakeMult));
                }

                if (totalAttackScore > 0f)
                {
                    if (_isPlayerTurn)
                        hp.DamageOpp(totalAttackScore);
                    else
                        hp.DamagePlayer(totalAttackScore);
                }

                // 공격 완료 + 진동 후 0.5초 대기 → 힐 카드 날리기
                if (healCards.Count > 0)
                {
                    yield return new WaitForSeconds(0.5f);
                    yield return StartCoroutine(FlyAndHit(healCards, healTarget));
                }

                if (healScore > 0f)
                {
                    // 힐 연출
                    IReadOnlyList<GameObject> healDeckCards = _isPlayerTurn ? deck.SpawnedCards : oppDeck.SpawnedCards;
                    yield return StartCoroutine(HealGreenWave(healDeckCards, healScore));

                    if (_isPlayerTurn)
                        hp.HealPlayer(healScore);
                    else
                        hp.HealOpp(healScore);
                }
            }

            // 공격 후: 상대 슬롯에 남아있던 카드 제거 (뒷면 카드는 유지)
            Slot[] victimSlots = _isPlayerTurn ? oppSlots : playerSlots;
            if (victimSlots != null)
            {
                foreach (var slot in victimSlots)
                {
                    if (slot != null && slot.HasCard)
                        slot.ClearCard();
                }
            }

        }

        // ── 공격 후 EXP 추가 + 레벨업 시 룰렛 ──
        if (_isPlayerTurn && exp != null && turnScore > 0f)
        {
            int levelsGained = exp.AddExp(Mathf.RoundToInt(turnScore));
            Debug.Log($"[MainFlow] EXP +{Mathf.RoundToInt(turnScore)}, 레벨업 {levelsGained}회, roulette={roulette}");
            if (levelsGained > 0 && roulette != null)
            {
                for (int i = 0; i < levelsGained; i++)
                {
                    Debug.Log($"[MainFlow] 룰렛 시작 ({i + 1}/{levelsGained})");
                    yield return StartCoroutine(roulette.SpinAndReward());
                }
            }
        }

        // 안전 정리: 슬롯에 남은 앞면 카드 → 덱으로 복귀
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

        // ── 드로우 수 계산: 낸 카드 수 - 1 (최소 1) ──
        if (originalAttackCardCount > 0)
        {
            int attackerDraw = Mathf.Max(originalAttackCardCount - 1, 1);
            if (_isPlayerTurn)
                _playerNextDraw = attackerDraw;
            else
                _oppNextDraw = attackerDraw;
        }

        // 턴 전환
        _isPlayerTurn = !_isPlayerTurn;
        _timer = turnTime;

        // 상태 플래그 해제 (턴 전환 후, 모든 GameUI)
        foreach (var gui in FindObjectsOfType<GameUI>())
        {
            gui.isScoreOverridden = false;
        }

        // 턴 전환 직후 드로우 (양쪽 모두)
        for (int i = 0; i < _playerNextDraw; i++)
            deck.AddOneCard();
        _playerNextDraw = 1;

        for (int i = 0; i < _oppNextDraw; i++)
            oppDeck.AddOneCard();
        _oppNextDraw = 1;

        UpdateInteraction();

        // 카드 크기 전환 애니메이션
        yield return StartCoroutine(AnimateTurnScale());

        // 전환 종료 직전에 오버라이드 해제
        if (gameUI != null)
            gameUI.isScoreOverridden = false;

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

        // GameFlow의 GetContributingSlots와 동일하게 조커 최적 해석 사용
        string comboName;
        float comboScore;
        gameFlow.GetBestCombo(slots, out comboName, out comboScore);
        ruleName = comboName;
        return comboScore;
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
    //  카드를 중앙 한 점으로 모으기
    // ─────────────────────────────────────────
    private IEnumerator GatherToPoint(List<GameObject> cards, Vector3 point)
    {
        int count = cards.Count;
        Vector3[] startPositions = new Vector3[count];
        for (int i = 0; i < count; i++)
            startPositions[i] = cards[i] != null ? cards[i].transform.position : point;

        float elapsed = 0f;
        while (elapsed < showcaseMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / showcaseMoveDuration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.position = Vector3.Lerp(startPositions[i], point, eased);
            }
            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (cards[i] != null)
                cards[i].transform.position = point;
        }
    }

    // ─────────────────────────────────────────
    //  쇼케이스: 두 줄 (위: 공격, 아래: 방어)
    // ─────────────────────────────────────────
    private IEnumerator ArrangeAtShowcaseTwoRows(
        List<GameObject> topCards, List<GameObject> bottomCards,
        Vector3 center, float rowOffset)
    {
        int topCount = topCards.Count;
        int bottomCount = bottomCards.Count;
        int totalCount = topCount + bottomCount;

        Vector3 topCenter = center + new Vector3(0f, rowOffset, 0f);
        Vector3 bottomCenter = center - new Vector3(0f, rowOffset, 0f);

        // 목표 위치 계산
        Vector3[] topTargets = new Vector3[topCount];
        for (int i = 0; i < topCount; i++)
        {
            float offset = (i - (topCount - 1) * 0.5f) * showcaseSpacing;
            topTargets[i] = new Vector3(topCenter.x + offset, topCenter.y, 0f);
        }

        Vector3[] bottomTargets = new Vector3[bottomCount];
        for (int i = 0; i < bottomCount; i++)
        {
            float offset = (i - (bottomCount - 1) * 0.5f) * showcaseSpacing;
            bottomTargets[i] = new Vector3(bottomCenter.x + offset, bottomCenter.y, 0f);
        }

        // 시작 상태 저장
        Vector3[] topStarts = new Vector3[topCount];
        Quaternion[] topStartRots = new Quaternion[topCount];
        for (int i = 0; i < topCount; i++)
        {
            topStarts[i] = topCards[i].transform.position;
            topStartRots[i] = topCards[i].transform.rotation;
        }

        Vector3[] bottomStarts = new Vector3[bottomCount];
        Quaternion[] bottomStartRots = new Quaternion[bottomCount];
        for (int i = 0; i < bottomCount; i++)
        {
            bottomStarts[i] = bottomCards[i].transform.position;
            bottomStartRots[i] = bottomCards[i].transform.rotation;
        }

        float elapsed = 0f;
        while (elapsed < showcaseMoveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / showcaseMoveDuration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < topCount; i++)
            {
                if (topCards[i] == null) continue;
                topCards[i].transform.position = Vector3.Lerp(topStarts[i], topTargets[i], eased);
                topCards[i].transform.rotation = Quaternion.Slerp(topStartRots[i], Quaternion.identity, eased);
            }

            for (int i = 0; i < bottomCount; i++)
            {
                if (bottomCards[i] == null) continue;
                bottomCards[i].transform.position = Vector3.Lerp(bottomStarts[i], bottomTargets[i], eased);
                bottomCards[i].transform.rotation = Quaternion.Slerp(bottomStartRots[i], Quaternion.identity, eased);
            }

            yield return null;
        }

        // 최종 위치 보정
        for (int i = 0; i < topCount; i++)
        {
            if (topCards[i] == null) continue;
            topCards[i].transform.position = topTargets[i];
            topCards[i].transform.rotation = Quaternion.identity;
        }
        for (int i = 0; i < bottomCount; i++)
        {
            if (bottomCards[i] == null) continue;
            bottomCards[i].transform.position = bottomTargets[i];
            bottomCards[i].transform.rotation = Quaternion.identity;
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

    // ── 정렬 비교 함수: 타입별 (Attack → Critical → Heal) ──
    private static int SortByType(GameObject a, GameObject b)
    {
        var cva = a != null ? a.GetComponent<CardValue>() : null;
        var cvb = b != null ? b.GetComponent<CardValue>() : null;
        int ta = cva != null ? (int)cva.cardType : 0;
        int tb = cvb != null ? (int)cvb.cardType : 0;
        return ta.CompareTo(tb);
    }

    // ── 정렬 비교 함수: 숫자별 (타입 무관) ──
    private static int SortByTypeAndValue(GameObject a, GameObject b)
    {
        var cva = a != null ? a.GetComponent<CardValue>() : null;
        var cvb = b != null ? b.GetComponent<CardValue>() : null;
        int va = cva != null ? cva.value : 0;
        int vb = cvb != null ? cvb.value : 0;
        return va.CompareTo(vb);
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
    //  힐 연출: 위→아래 초록빛 웨이브
    // ─────────────────────────────────────────
    private IEnumerator HealGreenWave(IReadOnlyList<GameObject> cards, float healAmount)
    {
        if (cards == null || cards.Count == 0) yield break;

        float intensity = Mathf.Clamp01(healAmount / 30f); // 힐량 비례 (30이면 최대)
        Color greenTint = new Color(0f, 1f, 0.3f, intensity * 0.7f);
        float duration = 0.4f + intensity * 0.3f; // 0.4~0.7초

        // 각 카드의 SpriteRenderer와 원래 색상 저장
        var renderers = new List<List<SpriteRenderer>>();
        var originalColors = new List<List<Color>>();
        var cardBounds = new List<float>(); // 각 카드의 상단 y (로컬)

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) { renderers.Add(null); originalColors.Add(null); cardBounds.Add(0f); continue; }
            var srs = new List<SpriteRenderer>(cards[i].GetComponentsInChildren<SpriteRenderer>());
            var cols = new List<Color>();
            foreach (var sr in srs) cols.Add(sr.color);
            renderers.Add(srs);
            originalColors.Add(cols);

            var mainSr = cards[i].GetComponentInChildren<SpriteRenderer>();
            cardBounds.Add(mainSr != null && mainSr.sprite != null
                ? mainSr.sprite.bounds.extents.y * mainSr.transform.lossyScale.y
                : 0.5f);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration); // 0→1 (위→아래)

            for (int i = 0; i < cards.Count; i++)
            {
                if (renderers[i] == null) continue;
                for (int j = 0; j < renderers[i].Count; j++)
                {
                    if (renderers[i][j] == null) continue;

                    // 스프라이트 로컬 y 기준으로 위→아래 sweep
                    float spriteY = renderers[i][j].transform.localPosition.y;
                    float normalizedY = Mathf.Clamp01((cardBounds[i] - spriteY) / (cardBounds[i] * 2f));
                    float wave = Mathf.Clamp01(1f - Mathf.Abs(progress - normalizedY) * 4f);

                    Color c = originalColors[i][j];
                    renderers[i][j].color = Color.Lerp(c, new Color(c.r * 0.5f, 1f, c.g * 0.5f + 0.3f, c.a), wave * intensity);
                }
            }
            yield return null;
        }

        // 원래 색상 복원
        for (int i = 0; i < cards.Count; i++)
        {
            if (renderers[i] == null) continue;
            for (int j = 0; j < renderers[i].Count; j++)
            {
                if (renderers[i][j] != null)
                    renderers[i][j].color = originalColors[i][j];
            }
        }
    }





    // ─────────────────────────────────────────
    //  피격 연출: 덱 흔들림
    // ─────────────────────────────────────────
    private IEnumerator ShakeTransform(Transform target, float duration, float intensity)
    {
        Vector3 originalPos = target.localPosition;
        
        // 덱 이미지 스프라이트 찾아서 빨갛게 번쩍이는 효과 추가
        SpriteRenderer sr = target.GetComponentInChildren<SpriteRenderer>();
        Color originalColor = sr != null ? sr.color : Color.white;
        Color redTint = new Color(1f, 0.3f, 0.3f, 1f);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration); // 감쇠
            float offsetX = Random.Range(-1f, 1f) * intensity * t;
            float offsetY = Random.Range(-1f, 1f) * intensity * t;
            target.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);

            if (sr != null)
            {
                // 매우 빠르게 빨간색 <-> 원래색 번쩍임
                float flash = Mathf.PingPong(elapsed * 15f, 1f) * t; 
                sr.color = Color.Lerp(originalColor, redTint, flash);
            }

            yield return null;
        }

        target.localPosition = originalPos;
        if (sr != null) sr.color = originalColor;
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

    // ─────────────────────────────────────────
    //  카드 그룹을 목표 위치로 이동 (파괴 없음)
    // ─────────────────────────────────────────
    private IEnumerator FlyCardsTo(List<GameObject> cards, Vector3 targetPos, float duration = 0.4f)
    {
        Vector3[] startPositions = new Vector3[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                startPositions[i] = cards[i].transform.position;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.position = Vector3.Lerp(startPositions[i], targetPos, eased);
            }
            yield return null;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                cards[i].transform.position = targetPos;
        }
    }

    // ─────────────────────────────────────────
    //  카드들을 각각의 목표 위치로 퍼뜨리며 이동
    // ─────────────────────────────────────────
    private IEnumerator SpreadToTargets(List<GameObject> cards, List<Vector3> targets, float duration = 0.3f)
    {
        if (targets.Count == 0) yield break;

        Vector3[] startPositions = new Vector3[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                startPositions[i] = cards[i].transform.position;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                Vector3 dest = targets[i % targets.Count];
                cards[i].transform.position = Vector3.Lerp(startPositions[i], dest, eased);
            }
            yield return null;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                cards[i].transform.position = targets[i % targets.Count];
        }
    }

    // ─────────────────────────────────────────
    //  동점 무효화: 전체 카드 서서히 페이드 아웃 + 파괴
    // ─────────────────────────────────────────
    // ─────────────────────────────────────────
    //  승패 시각화: 이긴 카드 확대, 진 카드 축소
    // ─────────────────────────────────────────
    private IEnumerator ScaleCards(List<GameObject> winCards, float winScale,
        List<GameObject> loseCards, float loseScale, float duration)
    {
        Vector3[] winStarts = new Vector3[winCards.Count];
        Vector3[] loseStarts = new Vector3[loseCards.Count];

        for (int i = 0; i < winCards.Count; i++)
        {
            if (winCards[i] != null) winStarts[i] = winCards[i].transform.localScale;
        }
        for (int i = 0; i < loseCards.Count; i++)
        {
            if (loseCards[i] != null) loseStarts[i] = loseCards[i].transform.localScale;
        }

        // 진 쪽 카드 원래 색 저장
        SpriteRenderer[][] loseRenderers = new SpriteRenderer[loseCards.Count][];
        Color[][] loseOrigColors = new Color[loseCards.Count][];
        for (int i = 0; i < loseCards.Count; i++)
        {
            if (loseCards[i] == null) continue;
            loseRenderers[i] = loseCards[i].GetComponentsInChildren<SpriteRenderer>();
            loseOrigColors[i] = new Color[loseRenderers[i].Length];
            for (int j = 0; j < loseRenderers[i].Length; j++)
                loseOrigColors[i][j] = loseRenderers[i][j].color;
        }

        Color redTint = new Color(1f, 0.3f, 0.3f, 1f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);

            for (int i = 0; i < winCards.Count; i++)
            {
                if (winCards[i] == null) continue;
                winCards[i].transform.localScale = winStarts[i] * Mathf.Lerp(1f, winScale, eased);
            }
            for (int i = 0; i < loseCards.Count; i++)
            {
                if (loseCards[i] == null) continue;
                loseCards[i].transform.localScale = loseStarts[i] * Mathf.Lerp(1f, loseScale, eased);

                // 진 쪽 빨간색 그라데이션
                if (loseRenderers[i] != null)
                {
                    for (int j = 0; j < loseRenderers[i].Length; j++)
                    {
                        if (loseRenderers[i][j] == null) continue;
                        loseRenderers[i][j].color = Color.Lerp(loseOrigColors[i][j], redTint, eased);
                    }
                }
            }
            yield return null;
        }

        // 최종값 보정 (색상은 유지 — 이후 uniformCardScale 복원 시 색도 복원)
        for (int i = 0; i < winCards.Count; i++)
        {
            if (winCards[i] != null)
                winCards[i].transform.localScale = winStarts[i] * winScale;
        }
        for (int i = 0; i < loseCards.Count; i++)
        {
            if (loseCards[i] == null) continue;
            loseCards[i].transform.localScale = loseStarts[i] * loseScale;
            if (loseRenderers[i] != null)
                for (int j = 0; j < loseRenderers[i].Length; j++)
                    if (loseRenderers[i][j] != null)
                        loseRenderers[i][j].color = redTint;
        }
    }

    private IEnumerator FadeOutAndDestroy(List<GameObject> cards, float duration)
    {
        // 원래 스케일·색상 저장
        Vector3[] origScales = new Vector3[cards.Count];
        var renderersList = new List<SpriteRenderer[]>();
        var originalColors = new List<Color[]>();

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) { renderersList.Add(null); originalColors.Add(null); continue; }
            origScales[i] = cards[i].transform.localScale;
            var srs = cards[i].GetComponentsInChildren<SpriteRenderer>();
            renderersList.Add(srs);
            var cols = new Color[srs.Length];
            for (int j = 0; j < srs.Length; j++) cols[j] = srs[j].color;
            originalColors.Add(cols);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;

                // 축소
                cards[i].transform.localScale = origScales[i] * (1f - t * 0.5f);

                // 페이드 아웃
                if (renderersList[i] == null) continue;
                for (int j = 0; j < renderersList[i].Length; j++)
                {
                    if (renderersList[i][j] == null) continue;
                    Color c = originalColors[i][j];
                    c.a = originalColors[i][j].a * (1f - t);
                    renderersList[i][j].color = c;
                }
            }
            yield return null;
        }

        // 파괴
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                Destroy(cards[i]);
        }
    }

    // 방어 관련 코루틴 삭제됨
}
