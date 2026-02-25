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

    [Header("─ UI 캔버스 ─")]
    [Tooltip("버튼 UI가 카드 위에 표시되도록 Canvas 설정")]
    public Canvas uiCanvas;

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

        // ── 방어 카드가 있으면: 점수 UI → defenseScoreText 위치로 애니메이션 ──
        if (_isPlayerTurn && gameFlow != null && gameFlow.HasDefenseCards && gameFlow.CurrentDefenseScore > 0f)
        {
            // scoreText에 방어 점수 표시 후 애니메이션
            if (gameUI != null && gameUI.scoreText != null)
            {
                gameUI.isScoreOverridden = true;
                gameUI.scoreText.gameObject.SetActive(true);
                gameUI.scoreText.text = $"+{gameFlow.CurrentDefenseScore:F1}\n({gameFlow.CurrentDefenseRule})";
            }
            yield return StartCoroutine(AnimateScoreToDefenseUI());
        }
        else if (_isPlayerTurn)
        {
            Debug.Log($"[MainFlow] 방어 애니메이션 스킵: gameFlow={gameFlow != null}, HasDefense={gameFlow?.HasDefenseCards}, DefScore={gameFlow?.CurrentDefenseScore}");
        }

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
                        r.sortingOrder = 500;
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
        int originalAttackerDefenseCardCount = 0; // 공격측이 낸 방어 카드 수
        float originalTurnScore = turnScore;
        string originalTurnRule = turnRule;

        // 쇼케이스 중심점
        Vector3 showcaseCenter = (mySpawn.position + target.position) * 0.5f;
        showcaseCenter.z = 0f;

        // ── 방어 카드 체크 (공격 카드 유무와 무관) ──
        bool reflected = false;
        Slot[] defenderSlots = _isPlayerTurn ? oppSlots : playerSlots;
        float defenderScore = 0f;
        string defenderRule = "";
        int originalDefenseCardCount = 0;
        bool isDrawMatched = false;

        bool hasFaceDown = false;
        if (defenderSlots != null)
        {
            foreach (var slot in defenderSlots)
            {
                if (slot != null && slot.HasCard && slot.IsFaceDown)
                {
                    hasFaceDown = true;
                    break;
                }
            }
        }

        bool attackerHasFaceDown = false;
        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot != null && slot.HasCard && slot.IsFaceDown)
                {
                    attackerHasFaceDown = true;
                    break;
                }
            }
        }

        // ── 양쪽 방어 대치 (공격 카드 유무와 무관) ──
        if (attackerHasFaceDown && hasFaceDown)
        {
            // 공격측 방어 점수 계산
            float atkDefScore = 0f;
            string atkDefRule = "";
            int[] atkDefValues = new int[slotsToRelease.Length];
            bool[] atkDefJokerFlags = new bool[slotsToRelease.Length];
            for (int i = 0; i < slotsToRelease.Length; i++)
            {
                if (slotsToRelease[i] == null || !slotsToRelease[i].HasCard || !slotsToRelease[i].IsFaceDown) continue;
                var cv = slotsToRelease[i].GetCardValue();
                if (cv != null)
                {
                    atkDefJokerFlags[i] = cv.isJoker;
                    atkDefValues[i] = cv.isJoker ? 0 : cv.value;
                }
            }
            if (gameFlow != null)
                atkDefScore = gameFlow.EvaluateValues(atkDefValues, atkDefJokerFlags, out atkDefRule);

            // 수비측 방어 점수 계산
            float defDefScore = 0f;
            string defDefRule = "";
            int[] defDefValues = new int[defenderSlots.Length];
            bool[] defDefJokerFlags = new bool[defenderSlots.Length];
            for (int i = 0; i < defenderSlots.Length; i++)
            {
                if (defenderSlots[i] == null || !defenderSlots[i].HasCard || !defenderSlots[i].IsFaceDown) continue;
                var cv = defenderSlots[i].GetCardValue();
                if (cv != null)
                {
                    defDefJokerFlags[i] = cv.isJoker;
                    defDefValues[i] = cv.isJoker ? 0 : cv.value;
                }
            }
            if (gameFlow != null)
                defDefScore = gameFlow.EvaluateValues(defDefValues, defDefJokerFlags, out defDefRule);

            Debug.Log($"[MainFlow] 양쪽 방어 대치! 공격측: {atkDefRule}({atkDefScore:F1}) vs 수비측: {defDefRule}({defDefScore:F1})");

            // ── 1) 양쪽 방어 카드 릴리스 + 뒷면 스프라이트로 교체 ──
            Sprite backSprite = null;
            var deckRefForBack = FindObjectOfType<Deck>();
            if (deckRefForBack != null && deckRefForBack.cardBackPrefab != null)
            {
                var backSr = deckRefForBack.cardBackPrefab.GetComponent<SpriteRenderer>();
                if (backSr != null) backSprite = backSr.sprite;
            }

            Dictionary<GameObject, Sprite> savedFaceSprites = new Dictionary<GameObject, Sprite>();

            List<GameObject> atkDefCards = new List<GameObject>();
            foreach (var slot in slotsToRelease)
            {
                if (slot == null || !slot.HasCard || !slot.IsFaceDown) continue;
                var card = slot.ReleaseCard();
                if (card != null)
                {
                    card.transform.localScale = uniformCardScale;
                    foreach (var r in card.GetComponentsInChildren<Renderer>())
                        r.sortingOrder = 500;
                    // 앞면 스프라이트 저장 후 뒷면으로 교체
                    var sr = card.GetComponent<SpriteRenderer>();
                    if (sr != null && backSprite != null)
                    {
                        savedFaceSprites[card] = sr.sprite;
                        sr.sprite = backSprite;
                    }
                    atkDefCards.Add(card);
                }
            }
            List<GameObject> defDefCards = new List<GameObject>();
            foreach (var slot in defenderSlots)
            {
                if (slot == null || !slot.HasCard || !slot.IsFaceDown) continue;
                var card = slot.ReleaseCard();
                if (card != null)
                {
                    card.transform.localScale = uniformCardScale;
                    foreach (var r in card.GetComponentsInChildren<Renderer>())
                        r.sortingOrder = 500;
                    var sr = card.GetComponent<SpriteRenderer>();
                    if (sr != null && backSprite != null)
                    {
                        savedFaceSprites[card] = sr.sprite;
                        sr.sprite = backSprite;
                    }
                    defDefCards.Add(card);
                }
            }

            // ── 2) 중앙 두 줄에 뒷면 상태로 대치 배치 ──
            float rowOffset = showcaseSpacing * 0.8f;
            yield return StartCoroutine(ArrangeAtShowcaseTwoRows(defDefCards, atkDefCards, showcaseCenter, rowOffset));

            yield return new WaitForSeconds(0.8f);

            // ── 3) 뒤집기 애니메이션 (뒷면→앞면) ──
            List<GameObject> allDefFlip = new List<GameObject>(atkDefCards);
            allDefFlip.AddRange(defDefCards);

            float flipDur = 0.4f;
            float flipHalf = flipDur * 0.5f;

            // 전반: X 스케일 축소
            float elapsed = 0f;
            while (elapsed < flipHalf)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flipHalf);
                foreach (var card in allDefFlip)
                {
                    Vector3 s = card.transform.localScale;
                    s.x = uniformCardScale.x * (1f - t);
                    card.transform.localScale = s;
                }
                yield return null;
            }

            // 스프라이트 교체: 뒷면 → 앞면
            foreach (var card in allDefFlip)
            {
                var sr = card.GetComponent<SpriteRenderer>();
                if (sr != null && savedFaceSprites.ContainsKey(card))
                    sr.sprite = savedFaceSprites[card];
                Vector3 s = card.transform.localScale;
                s.x = 0f;
                card.transform.localScale = s;
            }

            // 후반: X 스케일 복원
            elapsed = 0f;
            while (elapsed < flipHalf)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flipHalf);
                foreach (var card in allDefFlip)
                {
                    Vector3 s = card.transform.localScale;
                    s.x = uniformCardScale.x * t;
                    card.transform.localScale = s;
                }
                yield return null;
            }

            foreach (var card in allDefFlip)
                card.transform.localScale = uniformCardScale;

            // 플립 후 정렬 (일반 공격과 동일: 타입별 → 숫자별)
            // defDefCards = 위(상대), atkDefCards = 아래(내 쪽)
            if (defDefCards.Count > 1)
            {
                yield return new WaitForSeconds(0.5f);
                Vector3 defRowCenter = showcaseCenter + new Vector3(0f, rowOffset, 0f);
                yield return StartCoroutine(SortShowcaseBy(defDefCards, defRowCenter, SortByType));
                bool defAllSame = true;
                int defFirstVal = defDefCards[0] != null ? defDefCards[0].GetComponent<CardValue>()?.value ?? 0 : 0;
                for (int i = 1; i < defDefCards.Count; i++)
                {
                    int v = defDefCards[i] != null ? defDefCards[i].GetComponent<CardValue>()?.value ?? 0 : 0;
                    if (v != defFirstVal) { defAllSame = false; break; }
                }
                if (!defAllSame)
                {
                    yield return new WaitForSeconds(0.5f);
                    yield return StartCoroutine(SortShowcaseBy(defDefCards, defRowCenter, SortByTypeAndValue));
                }
            }
            if (atkDefCards.Count > 1)
            {
                yield return new WaitForSeconds(0.5f);
                Vector3 atkRowCenter = showcaseCenter - new Vector3(0f, rowOffset, 0f);
                yield return StartCoroutine(SortShowcaseBy(atkDefCards, atkRowCenter, SortByType));
                bool atkAllSame = true;
                int atkFirstVal = atkDefCards[0] != null ? atkDefCards[0].GetComponent<CardValue>()?.value ?? 0 : 0;
                for (int i = 1; i < atkDefCards.Count; i++)
                {
                    int v = atkDefCards[i] != null ? atkDefCards[i].GetComponent<CardValue>()?.value ?? 0 : 0;
                    if (v != atkFirstVal) { atkAllSame = false; break; }
                }
                if (!atkAllSame)
                {
                    yield return new WaitForSeconds(0.5f);
                    yield return StartCoroutine(SortShowcaseBy(atkDefCards, atkRowCenter, SortByTypeAndValue));
                }
            }

            yield return new WaitForSeconds(showcaseTime);

            // ── 4) 승패 판정 ──
            bool defMolDraw = Mathf.Approximately(atkDefScore, defDefScore);

            if (defMolDraw)
            {
                // 동점: 양쪽 방어 카드 전부 페이드 아웃
                Debug.Log("[MainFlow] 방어 대치 → 동점! 양쪽 소멸");
                yield return StartCoroutine(FadeOutAndDestroy(allDefFlip, 0.8f));
            }
            else
            {
                bool atkDefWins = atkDefScore > defDefScore;
                List<GameObject> winnerCards = atkDefWins ? atkDefCards : defDefCards;
                List<GameObject> loserCards = atkDefWins ? defDefCards : atkDefCards;
                float winnerScore = atkDefWins ? atkDefScore : defDefScore;

                // ── 5) 승패 크기 연출: 승자 확대 + 패자 축소/빨간색 ──
                yield return StartCoroutine(ScaleCards(winnerCards, 1.15f, loserCards, 0.85f, 0.4f));
                yield return new WaitForSeconds(1f);

                // 전체 카드 크기/색 복원
                List<GameObject> allDefCards = new List<GameObject>(winnerCards);
                allDefCards.AddRange(loserCards);
                foreach (var c in allDefCards)
                {
                    if (c != null)
                    {
                        c.transform.localScale = uniformCardScale;
                        foreach (var sr in c.GetComponentsInChildren<SpriteRenderer>())
                            sr.color = Color.white;
                    }
                }

                // ── 6) 카드 타입별 분리 (Attack/Critical/Heal) ──
                List<GameObject> defAttackCards = new List<GameObject>();
                List<GameObject> defCriticalCards = new List<GameObject>();
                List<GameObject> defHealCards = new List<GameObject>();

                foreach (var card in allDefCards)
                {
                    var cv = card.GetComponent<CardValue>();
                    CardType cType = cv != null ? cv.cardType : CardType.Attack;
                    if (cType == CardType.Heal)
                        defHealCards.Add(card);
                    else if (cType == CardType.Critical)
                        defCriticalCards.Add(card);
                    else
                        defAttackCards.Add(card);
                }

                // 공격 방향 결정
                Vector3 defHitTarget = atkDefWins ? target.position : mySpawn.position;
                Vector3 defHealTarget = atkDefWins ? mySpawn.position : target.position;
                Transform defShakeT = atkDefWins ? target : mySpawn;

                // Attack/Critical 카드 모으기
                List<GameObject> defAllAttack = new List<GameObject>(defAttackCards);
                defAllAttack.AddRange(defCriticalCards);

                // 전체 카드 한 점으로 모으기
                yield return StartCoroutine(GatherToPoint(allDefCards, showcaseCenter));
                yield return new WaitForSeconds(0.2f);

                // ── 7) 공격 카드 먼저 날리기 ──
                if (defAllAttack.Count > 0)
                    yield return StartCoroutine(FlyAndHit(defAllAttack, defHitTarget));

                // HP 처리 (Attack/Critical/Heal 비율 분리)
                if (hp != null && winnerScore > 0f)
                {
                    int totalCount = defAttackCards.Count + defCriticalCards.Count + defHealCards.Count;
                    float atkRatio = (float)defAttackCards.Count / totalCount;
                    float critRatio = (float)defCriticalCards.Count / totalCount;
                    float healRatio = (float)defHealCards.Count / totalCount;

                    float atkScore = winnerScore * atkRatio;
                    float critScore = winnerScore * critRatio * 2f;
                    float totalAtkScore = atkScore + critScore;
                    float healScore = winnerScore * healRatio;

                    // 피격 연출
                    if (defAllAttack.Count > 0 && totalAtkScore > 0f)
                    {
                        float shakeMult = Mathf.Max(1f, Mathf.Floor(totalAtkScore / 10f));
                        StartCoroutine(ShakeTransform(defShakeT, hitShakeDuration, hitShakeIntensity * shakeMult));
                        yield return StartCoroutine(ShakeCamera(hitShakeDuration, cameraShakeIntensity * shakeMult));
                    }

                    if (totalAtkScore > 0f)
                    {
                        if (atkDefWins)
                        {
                            if (_isPlayerTurn)
                                hp.DamageOpp(totalAtkScore);
                            else
                                hp.DamagePlayer(totalAtkScore);
                        }
                        else
                        {
                            if (_isPlayerTurn)
                                hp.DamagePlayer(totalAtkScore);
                            else
                                hp.DamageOpp(totalAtkScore);
                        }
                    }

                    // ── 8) 힐 카드 날리기 ──
                    if (defHealCards.Count > 0)
                    {
                        yield return new WaitForSeconds(0.5f);
                        yield return StartCoroutine(FlyAndHit(defHealCards, defHealTarget));
                    }

                    if (healScore > 0f)
                    {
                        // 힐 연출
                        IReadOnlyList<GameObject> healDeckCards;
                        if (atkDefWins)
                            healDeckCards = _isPlayerTurn ? deck.SpawnedCards : oppDeck.SpawnedCards;
                        else
                            healDeckCards = _isPlayerTurn ? oppDeck.SpawnedCards : deck.SpawnedCards;
                        yield return StartCoroutine(HealGreenWave(healDeckCards, healScore));

                        if (atkDefWins)
                        {
                            if (_isPlayerTurn)
                                hp.HealPlayer(healScore);
                            else
                                hp.HealOpp(healScore);
                        }
                        else
                        {
                            if (_isPlayerTurn)
                                hp.HealOpp(healScore);
                            else
                                hp.HealPlayer(healScore);
                        }
                    }
                }

                Debug.Log($"[MainFlow] 방어 대치 → {(atkDefWins ? "공격측" : "수비측")} 승! 몰빵 ({winnerScore:F1} 데미지)");
            }

            // 방어 대치 처리 완료: 양쪽 방어 카드 모두 소비됨
            originalAttackerDefenseCardCount = atkDefCards.Count;
            originalDefenseCardCount = defDefCards.Count;
            hasFaceDown = false;
        }

        if (allCards.Count > 0)
        {
            yield return StartCoroutine(ArrangeAtShowcase(allCards, showcaseCenter));

            if (allCards.Count > 1)
            {
                yield return new WaitForSeconds(0.7f);

                // 1단계: 타입별 정렬 (Attack → Heal)
                yield return StartCoroutine(SortShowcaseBy(allCards, showcaseCenter, SortByType));

                // 카드 숫자가 전부 동일하지 않을 때만 2단계 정렬
                bool allSameValue = true;
                int firstValue = allCards[0] != null ? allCards[0].GetComponent<CardValue>()?.value ?? 0 : 0;
                for (int i = 1; i < allCards.Count; i++)
                {
                    int val = allCards[i] != null ? allCards[i].GetComponent<CardValue>()?.value ?? 0 : 0;
                    if (val != firstValue) { allSameValue = false; break; }
                }

                if (!allSameValue)
                {
                    yield return new WaitForSeconds(0.7f);

                    // 2단계: 숫자 정렬 (왼쪽 작은 수 → 오른쪽 큰 수)
                    yield return StartCoroutine(SortShowcaseBy(allCards, showcaseCenter, SortByTypeAndValue));
                }
            }

            yield return new WaitForSeconds(showcaseTime);

            if (hasFaceDown)
            {
                // 방어 점수 계산
                int[] defValues = new int[defenderSlots.Length];
                bool[] defJokerFlags = new bool[defenderSlots.Length];
                for (int i = 0; i < defenderSlots.Length; i++)
                {
                    if (defenderSlots[i] == null || !defenderSlots[i].HasCard || !defenderSlots[i].IsFaceDown) continue;
                    var cv = defenderSlots[i].GetCardValue();
                    if (cv != null)
                    {
                        defJokerFlags[i] = cv.isJoker;
                        defValues[i] = cv.isJoker ? 0 : cv.value;
                    }
                }
                if (gameFlow != null)
                    defenderScore = gameFlow.EvaluateValues(defValues, defJokerFlags, out defenderRule);

                // 조커 몰빵 체크: 모든 방어 카드가 조커이고 방어 점수 > 공격 점수
                bool allJokers = true;
                int defCardCheckCount = 0;
                foreach (var slot in defenderSlots)
                {
                    if (slot == null || !slot.HasCard || !slot.IsFaceDown) continue;
                    defCardCheckCount++;
                    var cv = slot.GetCardValue();
                    if (cv == null || !cv.isJoker) { allJokers = false; break; }
                }
                if (defCardCheckCount == 0) allJokers = false;
                bool isJokerReflect = allJokers && defenderScore > turnScore;

                // 방어 카드 공개 (앞면 전환)
                Coroutine lastReveal = null;
                foreach (var slot in defenderSlots)
                {
                    if (slot != null && slot.HasCard && slot.IsFaceDown)
                        lastReveal = slot.RevealCard();
                }
                if (lastReveal != null)
                    yield return lastReveal;

                // 방어 카드 수 기록
                foreach (var slot in defenderSlots)
                {
                    if (slot == null || !slot.HasCard) continue;
                    if (slot.HasVisibleCard || slot.IsFaceDown) continue;
                    originalDefenseCardCount++;
                }

                yield return new WaitForSeconds(0.5f);

                if (isJokerReflect)
                {
                    // ── 조커 몰빵: 공격 반사 ──
                    Debug.Log($"[MainFlow] 조커 몰빵! 공격: {turnRule}({turnScore:F1}) vs 방어: {defenderRule}({defenderScore:F1})");

                    // 방어 카드 릴리스
                    List<GameObject> defenseCards = new List<GameObject>();
                    foreach (var slot in defenderSlots)
                    {
                        if (slot == null || !slot.HasCard) continue;
                        if (slot.HasVisibleCard || slot.IsFaceDown) continue;
                        var card = slot.ReleaseCard();
                        if (card != null)
                        {
                            card.transform.localScale = uniformCardScale;
                            foreach (var r in card.GetComponentsInChildren<Renderer>())
                                r.sortingOrder = 500;
                            defenseCards.Add(card);
                        }
                    }

                    // 조커 해석 값 할당
                    if (defenseCards.Count > 0 && gameFlow != null)
                    {
                        int[] defResolved = gameFlow.ResolveJokers(defValues, defJokerFlags);
                        if (defResolved != null)
                        {
                            int defIdx = 0;
                            for (int i = 0; i < defenderSlots.Length && defIdx < defenseCards.Count; i++)
                            {
                                if (defValues[i] == 0 && !defJokerFlags[i]) continue;
                                var cv = defenseCards[defIdx].GetComponent<CardValue>();
                                if (cv != null && cv.isJoker && i < defResolved.Length)
                                    cv.value = defResolved[i];
                                defIdx++;
                            }
                        }
                    }

                    // 두 줄 쇼케이스: 공격(위) vs 방어(아래)
                    allCards.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
                    float rowOffset = showcaseSpacing * 0.8f;
                    yield return StartCoroutine(ArrangeAtShowcaseTwoRows(allCards, defenseCards, showcaseCenter, rowOffset));

                    yield return new WaitForSeconds(0.5f);
                    if (gameUI != null && gameUI.scoreText != null)
                    {
                        gameUI.isScoreOverridden = true;
                        gameUI.scoreText.gameObject.SetActive(false);
                    }
                    yield return new WaitForSeconds(1f);

                    // 승패 시각화
                    List<GameObject> winCards = defenseCards;
                    List<GameObject> loseCards = allCards;
                    yield return StartCoroutine(ScaleCards(winCards, 1.15f, loseCards, 0.85f, 0.4f));
                    yield return new WaitForSeconds(2f);

                    foreach (var c in allCards) { if (c != null) { c.transform.localScale = uniformCardScale; foreach (var sr in c.GetComponentsInChildren<SpriteRenderer>()) sr.color = Color.white; } }
                    foreach (var c in defenseCards) { if (c != null) { c.transform.localScale = uniformCardScale; foreach (var sr in c.GetComponentsInChildren<SpriteRenderer>()) sr.color = Color.white; } }

                    if (gameUI != null && gameUI.scoreText != null)
                    {
                        gameUI.scoreText.gameObject.SetActive(false);
                        gameUI.isScoreOverridden = false;
                    }

                    // 반사 처리
                    reflected = true;
                    turnScore = defenderScore;
                    turnRule = defenderRule;

                    allCards.AddRange(defenseCards);
                    foreach (var dc in defenseCards)
                    {
                        var cv = dc.GetComponent<CardValue>();
                        CardType dcType = cv != null ? cv.cardType : CardType.Attack;
                        if (dcType == CardType.Heal) healCards.Add(dc);
                        else if (dcType == CardType.Critical) criticalCards.Add(dc);
                        else attackCards.Add(dc);
                    }

                    yield return StartCoroutine(GatherToPoint(allCards, showcaseCenter));
                    yield return new WaitForSeconds(0.3f);
                }
                else
                {
                    // ── 방어 = 공격 점수 감소 (최소 0) ──
                    float reducedScore = Mathf.Max(0f, turnScore - defenderScore);
                    Debug.Log($"[MainFlow] 방어! 공격: {turnRule}({turnScore:F1}) - 방어: {defenderRule}({defenderScore:F1}) = {reducedScore:F1}");

                    if (reducedScore <= 0f)
                    {
                        // ── 완전 방어: 동점과 같은 연출 (전체 카드 페이드 아웃) ──
                        Debug.Log("[MainFlow] 완전 방어! 공격 무효화");
                        isDrawMatched = true;

                        // 방어 카드 릴리스
                        List<GameObject> defenseCards = new List<GameObject>();
                        foreach (var slot in defenderSlots)
                        {
                            if (slot == null || !slot.HasCard) continue;
                            if (slot.HasVisibleCard || slot.IsFaceDown) continue;
                            var card = slot.ReleaseCard();
                            if (card != null)
                            {
                                card.transform.localScale = uniformCardScale;
                                foreach (var r in card.GetComponentsInChildren<Renderer>())
                                    r.sortingOrder = 500;
                                defenseCards.Add(card);
                            }
                        }

                        // 공격 + 방어 전체 카드 페이드 아웃
                        List<GameObject> allBattleCards = new List<GameObject>(allCards);
                        allBattleCards.AddRange(defenseCards);
                        yield return StartCoroutine(FadeOutAndDestroy(allBattleCards, 0.8f));

                        allCards.Clear();
                        attackCards.Clear();
                        criticalCards.Clear();
                        healCards.Clear();
                        turnScore = 0f;
                    }
                    else
                    {
                        // ── 부분 방어: 방어 카드 소멸 연출 후 감소된 점수로 공격 ──
                        Debug.Log($"[MainFlow] 부분 방어! 감소된 공격: {reducedScore:F1}");

                        // 방어 카드 소멸 연출 (흔들림 + 붉은 플래시 + 축소)
                        yield return StartCoroutine(BreakDefenseCards(defenderSlots));

                        // 감소된 점수로 공격 진행
                        turnScore = reducedScore;
                    }
                }
            }

            // 반사 시 방향 전환: 공격 → 상대에게, 힐 → 나에게
            Vector3 attackTarget = reflected ? mySpawn.position : target.position;
            Vector3 healTarget = reflected ? target.position : mySpawn.position;
            Transform shakeTarget = reflected ? mySpawn : target;

            // Attack/Critical 카드 → 공격 대상, Heal 카드 → 힐 대상
            List<GameObject> allAttackCards = new List<GameObject>();
            allAttackCards.AddRange(attackCards);
            allAttackCards.AddRange(criticalCards);

            // 전체 카드 한 점으로 모으기 (조커 몰빵은 이미 처리됨)
            if (!reflected && allCards.Count > 0)
            {
                yield return StartCoroutine(GatherToPoint(allCards, showcaseCenter));
                yield return new WaitForSeconds(0.3f);
            }

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
                    if (reflected)
                    {
                        if (_isPlayerTurn)
                            hp.DamagePlayer(totalAttackScore);
                        else
                            hp.DamageOpp(totalAttackScore);
                    }
                    else
                    {
                        if (_isPlayerTurn)
                            hp.DamageOpp(totalAttackScore);
                        else
                            hp.DamagePlayer(totalAttackScore);
                    }
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
                    IReadOnlyList<GameObject> healDeckCards;
                    if (reflected)
                        healDeckCards = _isPlayerTurn ? oppDeck.SpawnedCards : deck.SpawnedCards;
                    else
                        healDeckCards = _isPlayerTurn ? deck.SpawnedCards : oppDeck.SpawnedCards;
                    yield return StartCoroutine(HealGreenWave(healDeckCards, healScore));

                    if (reflected)
                    {
                        // 반사: 방어자에게 힐
                        if (_isPlayerTurn)
                            hp.HealOpp(healScore);
                        else
                            hp.HealPlayer(healScore);
                    }
                    else
                    {
                        if (_isPlayerTurn)
                            hp.HealPlayer(healScore);
                        else
                            hp.HealOpp(healScore);
                    }
                }
            }

            // 공격 후: 상대 슬롯에 남아있던 카드 제거 (뒷면 카드는 유지)
            Slot[] victimSlots = _isPlayerTurn ? oppSlots : playerSlots;
            if (victimSlots != null)
            {
                foreach (var slot in victimSlots)
                {
                    if (slot != null && slot.HasCard && !slot.IsFaceDown)
                        slot.ClearCard();
                }
            }

            // 반사 발생 시 방어자 슬롯의 revealedOnly 카드도 제거
            if (reflected && defenderSlots != null)
            {
                foreach (var slot in defenderSlots)
                {
                    if (slot != null && slot.HasCard)
                        slot.ClearCard();
                }
            }

        }

        // 안전 정리: 슬롯에 남은 앞면 카드 → 덱으로 복귀 (뒷면·revealedOnly 카드는 유지)
        if (slotsToRelease != null)
        {
            foreach (var slot in slotsToRelease)
            {
                if (slot == null || !slot.HasCard) continue;
                if (!slot.HasVisibleCard) continue; // 뒷면·revealedOnly 카드는 남겨둠

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
        // 공격자: 공격 카드 + 방어 카드 합산
        int totalAttackerCards = originalAttackCardCount + originalAttackerDefenseCardCount;
        if (totalAttackerCards > 0 && !isDrawMatched)
        {
            int attackerDraw = Mathf.Max(totalAttackerCards - 1, 1);
            if (_isPlayerTurn)
                _playerNextDraw = attackerDraw;
            else
                _oppNextDraw = attackerDraw;
        }

        // 방어자: 방어 카드 수
        if (originalDefenseCardCount > 0 && !isDrawMatched)
        {
            int defenderDraw = Mathf.Max(originalDefenseCardCount - 1, 1);
            if (!_isPlayerTurn)
                _playerNextDraw = defenderDraw;
            else
                _oppNextDraw = defenderDraw;
        }

        // 턴 전환
        _isPlayerTurn = !_isPlayerTurn;
        _timer = turnTime;

        // 방어 애니메이션 플래그 해제 (턴 전환 후, 모든 GameUI)
        foreach (var gui in FindObjectsOfType<GameUI>())
        {
            gui.isDefenseAnimating = false;
            gui.isScoreOverridden = false;
        }

        // 턴 전환 직후 드로우 (양쪽 모두)
        for (int i = 0; i < _playerNextDraw; i++)
            deck.AddOneCard();
        _playerNextDraw = 1;

        for (int i = 0; i < _oppNextDraw; i++)
            oppDeck.AddOneCard();
        _oppNextDraw = 1;

        // 상대 턴 시작: 내 방어(뒷면) 카드 앞면으로 보여줌 (애니메이션)
        if (!_isPlayerTurn && playerSlots != null)
        {
            Coroutine lastPeek = null;
            foreach (var slot in playerSlots)
            {
                if (slot != null && slot.HasCard && slot.IsFaceDown)
                    lastPeek = slot.PeekDefenseCard();
            }
            if (lastPeek != null)
                yield return lastPeek;
        }

        UpdateInteraction();

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

        // GameFlow의 GetContributingSlots와 동일하게 조커 최적 해석 사용
        string comboName;
        float comboScore;
        gameFlow.GetBestCombo(slots, out comboName, out comboScore);
        ruleName = comboName;
        return comboScore;
    }

    // ─────────────────────────────────────────
    //  revealedOnly 슬롯 카드 점수 계산 (뒷면이었던 카드만)
    // ─────────────────────────────────────────
    private float EvaluateRevealedSlots(Slot[] slots, out string ruleName)
    {
        ruleName = "";
        if (gameFlow == null || slots == null) return 0f;

        // revealedOnly 카드만 수집 (HasCard && !HasVisibleCard && !IsFaceDown)
        bool[] jokerFlags = new bool[slots.Length];
        int[] slotValues = new int[slots.Length];

        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || !slots[i].HasCard) continue;
            if (slots[i].HasVisibleCard || slots[i].IsFaceDown) continue;

            var cv = slots[i].GetCardValue();
            if (cv != null)
            {
                jokerFlags[i] = cv.isJoker;
                slotValues[i] = cv.isJoker ? 0 : cv.value;
            }
        }

        return gameFlow.EvaluateValues(slotValues, jokerFlags, out ruleName);
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
    //  반사 연출: 카드 흔들림 + 붉은 플래시 + 카메라 흔들림
    // ─────────────────────────────────────────
    private IEnumerator ReflectEffect(List<GameObject> cards, Vector3 center)
    {
        // 1) 카드들 일시정지 흔들림 (0.4초)
        float shakeDuration = 0.4f;
        float shakeIntensity = 0.15f;
        Vector3[] origPositions = new Vector3[cards.Count];
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                origPositions[i] = cards[i].transform.position;
        }

        float elapsed = 0f;
        while (elapsed < shakeDuration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / shakeDuration);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                float ox = Random.Range(-1f, 1f) * shakeIntensity * t;
                float oy = Random.Range(-1f, 1f) * shakeIntensity * t;
                cards[i].transform.position = origPositions[i] + new Vector3(ox, oy, 0f);
            }
            yield return null;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] != null)
                cards[i].transform.position = origPositions[i];
        }

        // 2) 붉은 플래시 (0.3초)
        var renderersList = new List<List<SpriteRenderer>>();
        var originalColors = new List<List<Color>>();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) { renderersList.Add(null); originalColors.Add(null); continue; }
            var srs = new List<SpriteRenderer>(cards[i].GetComponentsInChildren<SpriteRenderer>());
            var cols = new List<Color>();
            foreach (var sr in srs) cols.Add(sr.color);
            renderersList.Add(srs);
            originalColors.Add(cols);
        }

        Color flashColor = new Color(1f, 0.2f, 0.2f, 1f);
        float flashDuration = 0.3f;

        // 플래시 인
        elapsed = 0f;
        while (elapsed < flashDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (flashDuration * 0.5f));
            for (int i = 0; i < cards.Count; i++)
            {
                if (renderersList[i] == null) continue;
                for (int j = 0; j < renderersList[i].Count; j++)
                {
                    if (renderersList[i][j] != null)
                        renderersList[i][j].color = Color.Lerp(originalColors[i][j], flashColor, t);
                }
            }
            yield return null;
        }

        // 플래시 아웃
        elapsed = 0f;
        while (elapsed < flashDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / (flashDuration * 0.5f));
            for (int i = 0; i < cards.Count; i++)
            {
                if (renderersList[i] == null) continue;
                for (int j = 0; j < renderersList[i].Count; j++)
                {
                    if (renderersList[i][j] != null)
                        renderersList[i][j].color = Color.Lerp(flashColor, originalColors[i][j], t);
                }
            }
            yield return null;
        }

        // 색상 복원
        for (int i = 0; i < cards.Count; i++)
        {
            if (renderersList[i] == null) continue;
            for (int j = 0; j < renderersList[i].Count; j++)
            {
                if (renderersList[i][j] != null)
                    renderersList[i][j].color = originalColors[i][j];
            }
        }

        // 3) 카메라 흔들림
        yield return StartCoroutine(ShakeCamera(0.2f, 0.1f));
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

    // ─────────────────────────────────────────
    //  방어 카드 파괴 연출 (흔들림 + 붉은 플래시 + 축소 + 페이드)
    // ─────────────────────────────────────────
    private IEnumerator BreakDefenseCards(Slot[] slots)
    {
        List<Slot> breakSlots = new List<Slot>();
        List<GameObject> breakCards = new List<GameObject>();
        foreach (var slot in slots)
        {
            if (slot == null || !slot.HasCard) continue;
            // revealedOnly 카드 (공개된 방어 카드)
            if (!slot.HasVisibleCard && !slot.IsFaceDown)
            {
                var card = slot.GetPlacedCard();
                if (card != null)
                {
                    breakSlots.Add(slot);
                    breakCards.Add(card);
                }
            }
        }

        if (breakCards.Count == 0) yield break;

        float duration = 0.5f;

        Vector3[] origPositions = new Vector3[breakCards.Count];
        Vector3[] origScales = new Vector3[breakCards.Count];
        var renderersList = new List<SpriteRenderer[]>();
        var originalColors = new List<Color[]>();

        for (int i = 0; i < breakCards.Count; i++)
        {
            origPositions[i] = breakCards[i].transform.position;
            origScales[i] = breakCards[i].transform.localScale;
            var srs = breakCards[i].GetComponentsInChildren<SpriteRenderer>();
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

            for (int i = 0; i < breakCards.Count; i++)
            {
                if (breakCards[i] == null) continue;

                // 흔들림 (감쇠)
                float shakeT = 1f - t;
                float ox = Random.Range(-1f, 1f) * 0.1f * shakeT;
                float oy = Random.Range(-1f, 1f) * 0.1f * shakeT;
                breakCards[i].transform.position = origPositions[i] + new Vector3(ox, oy, 0f);

                // 축소
                breakCards[i].transform.localScale = origScales[i] * (1f - t);

                // 붉은 색 + 페이드 아웃
                for (int j = 0; j < renderersList[i].Length; j++)
                {
                    if (renderersList[i][j] == null) continue;
                    Color c = Color.Lerp(originalColors[i][j], new Color(1f, 0.2f, 0.2f, 0f), t);
                    renderersList[i][j].color = c;
                }
            }
            yield return null;
        }

        // 방어 슬롯 정리
        foreach (var slot in breakSlots)
        {
            if (slot != null)
                slot.ClearCard();
        }
    }

    // ─────────────────────────────────────────
    //  점수 UI → 방어 점수 UI 위치/크기로 이동 애니메이션
    //  → 애니메이션 완료 후 scoreText 숨기고 원래 설정 복귀
    // ─────────────────────────────────────────
    private IEnumerator AnimateScoreToDefenseUI()
    {
        if (gameUI == null || gameUI.scoreText == null)
        {
            yield break;
        }

        if (gameUI.defenseScoreText == null)
        {
            // 씬에서 "DefenseScore" 이름 포함하는 TMP 자동 탐색
            foreach (var tmp in FindObjectsOfType<TMPro.TextMeshProUGUI>(true))
            {
                if (tmp.gameObject.name.IndexOf("defense", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || tmp.gameObject.name.IndexOf("Defense", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    gameUI.defenseScoreText = tmp;
                    Debug.Log($"[MainFlow] defenseScoreText 자동 발견: {tmp.gameObject.name}");
                    break;
                }
            }
        }

        if (gameUI.defenseScoreText == null)
        {
            Debug.LogWarning("[MainFlow] defenseScoreText를 찾을 수 없음!");
            // defenseScoreText 없어도 scoreText 유지 후 숨기기
            gameUI.isScoreOverridden = true;
            gameUI.isDefenseAnimating = true;
            gameUI.scoreText.gameObject.SetActive(true);
            yield return new WaitForSeconds(gameUI.defenseAnimDelay + gameUI.defenseAnimDuration);
            gameUI.scoreText.gameObject.SetActive(false);
            gameUI.isScoreOverridden = false;
            gameUI.isDefenseAnimating = false;
            yield break;
        }

        Debug.Log($"[MainFlow] AnimateScoreToDefenseUI 시작! scoreText={gameUI.scoreText.gameObject.activeSelf}, defenseScoreText={gameUI.defenseScoreText.gameObject.name}");

        // ── 플래그 설정 + 모든 GameUI의 Update 차단 ──
        gameUI.isScoreOverridden = true;
        gameUI.isDefenseAnimating = true;

        // 모든 GameUI 인스턴스의 Update 차단 (다른 인스턴스가 scoreText 덮어쓰기 방지)
        var allGameUIs = FindObjectsOfType<GameUI>();
        foreach (var gui in allGameUIs)
        {
            gui.isScoreOverridden = true;
            gui.isDefenseAnimating = true;
        }

        // defenseScoreText 숨김 (겹침 방지)
        gameUI.defenseScoreText.gameObject.SetActive(false);

        // scoreText 검은색 + 활성
        Color origScoreColor = gameUI.scoreText.color;
        gameUI.scoreText.color = Color.black;
        gameUI.scoreText.gameObject.SetActive(true);

        RectTransform scoreRT = gameUI.scoreText.rectTransform;
        RectTransform defRT = gameUI.defenseScoreText.rectTransform;

        // 원래 값 저장
        Vector2 origAnchoredPos = scoreRT.anchoredPosition;
        Vector2 origSizeDelta = scoreRT.sizeDelta;
        Vector3 origScale = scoreRT.localScale;
        float origFontSize = gameUI.scoreText.fontSize;

        // 목표 값
        Vector2 targetAnchoredPos = defRT.anchoredPosition;
        Vector3 targetScale = defRT.localScale;
        float targetFontSize = gameUI.defenseScoreText.fontSize;

        // ── 1단계: 대기 (점수 텍스트가 보이는 상태 유지) ──
        yield return new WaitForSeconds(gameUI.defenseAnimDelay);

        // ── 2단계: 텍스트 변환 (score UI → defense UI 양식) ── 0.7초
        string defScoreStr = $"{gameFlow.CurrentDefenseScore:F1}";
        gameUI.scoreText.text = defScoreStr;
        gameUI.scoreText.ForceMeshUpdate();

        float elapsed = 0f;
        while (elapsed < 0.7f)
        {
            elapsed += Time.deltaTime;
            // 매 프레임 강제 적용 + 덮어쓰기 감지
            if (gameUI.scoreText.text != defScoreStr)
            {
                Debug.LogWarning($"[MainFlow] scoreText 덮어쓰기 감지! 현재='{gameUI.scoreText.text}', 예상='{defScoreStr}', flags: override={gameUI.isScoreOverridden}, anim={gameUI.isDefenseAnimating}");
                gameUI.scoreText.text = defScoreStr;
            }
            yield return null;
        }

        // defenseScoreText 텍스트 설정 (비활성 유지)
        gameUI.defenseScoreText.text = defScoreStr;
        gameUI.defenseScoreText.gameObject.SetActive(false);

        // ── 3단계: scoreText 이동 + 폰트 크기 변환 + 페이드 아웃 (동시) ──
        float moveDuration = gameUI.defenseAnimDuration;
        elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            float eased = t * t * (3f - 2f * t);

            scoreRT.anchoredPosition = Vector2.Lerp(origAnchoredPos, targetAnchoredPos, eased);
            scoreRT.localScale = Vector3.Lerp(origScale, targetScale, eased);
            gameUI.scoreText.fontSize = Mathf.Lerp(origFontSize, targetFontSize, eased);
            gameUI.scoreText.color = new Color(0f, 0f, 0f, 1f - eased);

            yield return null;
        }

        // 최종 상태 확정 (루프 끝에 정확히 t=1 아닐 수 있음)
        scoreRT.anchoredPosition = targetAnchoredPos;
        gameUI.scoreText.color = new Color(0f, 0f, 0f, 0f);

        // scoreText 비활성화 후 원래 설정 복귀
        gameUI.scoreText.gameObject.SetActive(false);
        gameUI.scoreText.color = origScoreColor;
        scoreRT.anchoredPosition = origAnchoredPos;
        scoreRT.sizeDelta = origSizeDelta;
        scoreRT.localScale = origScale;
        gameUI.scoreText.fontSize = origFontSize;

        // ── 4단계: defenseScoreText 페이드 인 ── 0.3초
        Color origDefColor = gameUI.defenseScoreText.color;
        gameUI.defenseScoreText.color = new Color(origDefColor.r, origDefColor.g, origDefColor.b, 0f);
        gameUI.defenseScoreText.gameObject.SetActive(true);

        float fadeInDuration = 0.3f;
        elapsed = 0f;
        while (elapsed < fadeInDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeInDuration);
            gameUI.defenseScoreText.color = new Color(origDefColor.r, origDefColor.g, origDefColor.b, t);
            yield return null;
        }
        gameUI.defenseScoreText.color = origDefColor;

        // ── 5단계: 탄력 바운스 ── 0.6초
        Vector3 origDefScale = defRT.localScale;
        float bounceDuration = 0.6f;
        elapsed = 0f;
        while (elapsed < bounceDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / bounceDuration);

            float elastic = 1f + Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * 0.35f;
            defRT.localScale = origDefScale * elastic;

            yield return null;
        }
        defRT.localScale = origDefScale;

        // isScoreOverridden 해제 (모든 GameUI 인스턴스)
        // isDefenseAnimating는 DoEndTurn 턴 전환 후 해제
        foreach (var gui in allGameUIs)
            gui.isScoreOverridden = false;
        gameUI.isScoreOverridden = false;
    }
}
