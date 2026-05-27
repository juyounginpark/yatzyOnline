using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  메인 턴 관리 (오케스트레이터)
//
//  실제 로직은 서브시스템에 위임:
//    TurnAnimator       — 애니메이션 코루틴
//    CardEffectPipeline — 카드 효과 (Attack/Critical/Chain/Heal)
//    ChainDotSystem     — 체인 DOT 틱
//    SlotRerollHandler  — 슬롯 리롤
// ─────────────────────────────────────────────
public class MainFlow : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public Deck    deck;
    public OppDeck oppDeck;
    public Slot[]  playerSlots;
    public Slot[]  oppSlots;

    [Header("─ UI ─")]
    public Button   endTurnButton;
    public TMP_Text endTurnButtonText;
    public Button   sortByNumButton;
    public Button   sortByTypeButton;
    public Button   scoreUIButton;

    [Header("─ UI 캔버스 ─")]
    public Canvas uiCanvas;

    [Header("─ 참조 (점수 표시용) ─")]
    public GameFlow  gameFlow;
    public GameUI    gameUI;
    public HP        hp;
    public Exp       exp;
    public Roulette  roulette;

    [Header("─ 슬롯 리롤 ─")]
    public GameObject rerollImagePrefab;
    public GameObject rerollCountObject;
    public int        maxSlotRerolls = 2;

    [Header("─ 카드 드래프트 ─")]
    public CardDraft cardDraft;

    [Header("─ 턴 설정 ─")]
    public float turnTime = 30f;

    [Header("─ 쇼케이스 설정 ─")]
    public float showcaseTime         = 1f;
    public float showcaseMoveDuration = 0.5f;
    public float showcaseSortDuration = 0.4f;
    public float showcaseSpacing      = 1.2f;

    [Header("─ 공격 애니메이션 ─")]
    public float attackDuration = 0.4f;
    public float attackStagger  = 0.06f;

    [Header("─ 피격 연출 ─")]
    public float hitShakeDuration     = 0.35f;
    public float hitShakeIntensity    = 0.15f;
    public float cameraShakeIntensity = 0.08f;

    [Header("─ 온라인 모드 ─")]
    public bool           isOnlineMode = false;
    public OnlineOpponent onlineOpponent;

    [Header("─ 체인 카드 ─")]
    public GameObject chainLockPrefab;

    [Header("─ 턴 카드 크기 연출 ─")]
    public float activeScale   = 1.2f;
    public float inactiveScale = 0.9f;
    public float scaleDuration = 0.3f;

    // ─── 상태 ───
    private bool  _isPlayerTurn = true;
    private float _timer;
    private bool  _isTransitioning;
    private bool  _scoreSkipped;

    private OppAuto _oppAuto;
    private int[]   _pendingChainLockIndices;  // 온라인 수신 체인 잠금 인덱스

    // ─── 온라인 Sync ───
    private const float SyncInterval = 1f;
    private float _syncTimer;

    // ─── 서브시스템 ───
    private TurnAnimator      _animator;
    private ChainDotSystem    _chainDot;
    private SlotRerollHandler _reroll;

    // ─── 공개 프로퍼티 ───
    public bool  IsPlayerTurn    => _isPlayerTurn;
    public float TimeRemaining   => Mathf.Max(0f, _timer);
    public bool  IsTransitioning => _isTransitioning;
    public bool  IsRouletteActive => roulette != null && roulette.IsSpinning;

    // ─────────────────────────────────────────
    //  초기화
    // ─────────────────────────────────────────
    void Start()
    {
        _timer        = turnTime;
        _isPlayerTurn = true;

        // 서브시스템 초기화
        _animator = new TurnAnimator(this)
        {
            attackDuration       = attackDuration,
            attackStagger        = attackStagger,
            hitShakeDuration     = hitShakeDuration,
            hitShakeIntensity    = hitShakeIntensity,
            cameraShakeIntensity = cameraShakeIntensity,
            showcaseMoveDuration = showcaseMoveDuration,
        };
        _chainDot = new ChainDotSystem(this, hp, _animator);
        _reroll   = new SlotRerollHandler(this, playerSlots, deck, gameFlow,
            rerollImagePrefab, maxSlotRerolls);

        // AI/온라인 상대
        _oppAuto = FindObjectOfType<OppAuto>();

        if (NetworkManager.Instance != null
            && NetworkManager.Instance.State == NetState.InGame)
            isOnlineMode = true;

        if (isOnlineMode)
        {
            if (_oppAuto != null) _oppAuto.enabled = false;

            if (onlineOpponent == null)
                onlineOpponent = FindObjectOfType<OnlineOpponent>();
            if (onlineOpponent == null)
            {
                var go = new GameObject("OnlineOpponent");
                onlineOpponent = go.AddComponent<OnlineOpponent>();
                Debug.Log("[MainFlow] OnlineOpponent 자동 생성");
            }
            onlineOpponent.Init(this, oppDeck);

            if (NetworkManager.Instance != null)
                _isPlayerTurn = NetworkManager.Instance.IGoFirst;
        }

        // UI 바인딩
        if (endTurnButton != null)
            endTurnButton.onClick.AddListener(EndTurn);
        if (scoreUIButton != null)
            scoreUIButton.onClick.AddListener(() => _scoreSkipped = true);
        if (sortByNumButton != null)
            sortByNumButton.onClick.AddListener(() =>
            {
                if (deck != null && !_isTransitioning) deck.SortByNumber();
            });
        if (sortByTypeButton != null)
            sortByTypeButton.onClick.AddListener(() =>
            {
                if (deck != null && !_isTransitioning) deck.SortByType();
            });

        if (uiCanvas == null && endTurnButton != null)
            uiCanvas = endTurnButton.GetComponentInParent<Canvas>();
        if (uiCanvas != null)
        {
            uiCanvas.renderMode   = RenderMode.ScreenSpaceCamera;
            uiCanvas.worldCamera  = Camera.main;
            uiCanvas.sortingOrder = 0;
        }

        UpdateInteraction();
        SetDeckScale(deck, activeScale);
        SetDeckScale(oppDeck, inactiveScale);

        // 첫 턴 카드 드래프트 시작 (딜링 애니메이션 후 1.5초 대기)
        if (cardDraft != null) StartCoroutine(InitialDraftRoutine());
    }

    private IEnumerator InitialDraftRoutine()
    {
        _isTransitioning = true; // 턴 행동 및 타이머 차단

        if (deck != null)
            while (deck.IsAnimating) yield return null;
        if (oppDeck != null)
            while (oppDeck.IsAnimating) yield return null;

        yield return new WaitForSeconds(1.5f);

        _isTransitioning = false;
        if (cardDraft != null) cardDraft.StartDraft();
    }

    // ─────────────────────────────────────────
    //  매 프레임
    // ─────────────────────────────────────────
    void Update()
    {
        // ── 타이머 ──
        // 온라인: 내 턴일 때만 로컬 카운트다운, 상대 턴 타이머는 Sync 패킷으로 수신
        // 오프라인: 기존 로직 유지
        bool isDrafting = cardDraft != null && cardDraft.IsDrafting;
        bool pauseTimer = _isTransitioning || isDrafting;
        if (!isOnlineMode)
            pauseTimer = pauseTimer || (!_isPlayerTurn && _oppAuto != null && _oppAuto.IsAnimating);
        else
            pauseTimer = pauseTimer || !_isPlayerTurn;  // 온라인: 상대 턴이면 로컬 카운트다운 정지

        if (!pauseTimer) _timer -= Time.deltaTime;

        if (endTurnButtonText != null)
            endTurnButtonText.text = Mathf.CeilToInt(Mathf.Max(0f, _timer)).ToString();

        // 엔드턴 버튼 상태 (드래프트 중 또는 전환 중 비활성화, 끝나면 복원)
        if (endTurnButton != null)
            endTurnButton.interactable = _isPlayerTurn && !isDrafting && !_isTransitioning;

        // ── 체인 DOT 틱 ──
        if (_chainDot != null && _chainDot.IsActive)
            _chainDot.Tick(Time.deltaTime);

        // ── 온라인 Sync 송수신 ──
        if (isOnlineMode) UpdateOnlineSync();

        if (_isTransitioning) return;

        // 시간 초과 → 자동 턴 종료
        if (_timer <= 0f && !pauseTimer && !IsRouletteActive)
        {
            if (!isOnlineMode || _isPlayerTurn)
                EndTurn();
        }

        // 온라인 폴링
        PollOnlinePackets();

        // 슬롯 리롤 (드래프트 중 차단)
        if (_isPlayerTurn && !_isTransitioning && !_reroll.IsRerolling && !isDrafting)
            _reroll.Tick();
    }

    // ─────────────────────────────────────────
    //  온라인 실시간 Sync (타이머 + HP)
    // ─────────────────────────────────────────
    private void UpdateOnlineSync()
    {
        if (NetworkManager.Instance == null) return;
        var nm = NetworkManager.Instance;

        // 내 턴이면 주기적으로 Sync 전송
        if (_isPlayerTurn && !_isTransitioning)
        {
            _syncTimer -= Time.deltaTime;
            if (_syncTimer <= 0f)
            {
                _syncTimer = SyncInterval;
                float myHp  = hp != null ? hp.PlayerHP : 0f;
                float oppHp = hp != null ? hp.OppHP    : 0f;
                nm.SendSync(_timer, myHp, oppHp);
            }
        }

        // 상대 Sync 수신 → 타이머 + HP 반영 (애니메이션 포함)
        if (nm.HasIncomingSync)
        {
            _timer = nm.IncomingSyncTimer;
            if (hp != null)
            {
                ApplyHpSync(nm.IncomingSyncSenderHp, isOpp: true);
                ApplyHpSync(nm.IncomingSyncReceiverHp, isOpp: false);
            }
            nm.ConsumeIncomingSync();
        }
    }

    /// <summary>Sync HP를 애니메이션 포함으로 반영 (차이가 1 이상일 때만)</summary>
    private void ApplyHpSync(float syncValue, bool isOpp)
    {
        if (hp == null || syncValue < 0f) return;

        float current = isOpp ? hp.OppHP : hp.PlayerHP;
        float diff = syncValue - current;

        if (Mathf.Abs(diff) < 1f) return;  // 변화 없음

        if (diff < 0f)
        {
            // 데미지
            if (isOpp) hp.DamageOpp(-diff);
            else       hp.DamagePlayer(-diff);
        }
        else
        {
            // 회복
            if (isOpp) hp.HealOpp(diff);
            else       hp.HealPlayer(diff);
        }
    }

    // ─────────────────────────────────────────
    //  온라인 패킷 폴링
    // ─────────────────────────────────────────
    private void PollOnlinePackets()
    {
        if (!isOnlineMode || NetworkManager.Instance == null) return;
        var nm = NetworkManager.Instance;

        while (nm.IncomingCardPlaces.Count > 0)
        {
            var cp = nm.IncomingCardPlaces.Dequeue();
            if (onlineOpponent != null)
                onlineOpponent.HandleCardPlaced(
                    cp.slotIndex, cp.value, cp.cardType, cp.isJoker);
        }

        while (nm.IncomingCardReturns.Count > 0)
        {
            int si = nm.IncomingCardReturns.Dequeue();
            if (onlineOpponent != null)
                onlineOpponent.HandleCardReturned(si);
        }

        if (!_isPlayerTurn && !_isTransitioning && nm.IncomingTurnEnd != null)
        {
            var data = nm.IncomingTurnEnd;
            _pendingChainLockIndices = nm.IncomingChainLockIndices;

            // HP 동기화: 상대가 보낸 senderHp = 상대의 HP, receiverHp = 나의 HP
            ApplyHpSync(nm.IncomingSenderHp, isOpp: true);
            ApplyHpSync(nm.IncomingReceiverHp, isOpp: false);

            nm.ConsumeIncomingTurnEnd();
            Debug.Log($"[MainFlow] 상대 TurnEnd 처리 (체인잠금: {_pendingChainLockIndices?.Length ?? 0}개)");
            if (onlineOpponent != null)
                onlineOpponent.HandleTurnEnd(data);
            else
                EndTurn();
        }
    }

    // ─────────────────────────────────────────
    //  턴 종��� (버튼 / 타이머 / OnlineOpponent에서 호출)
    // ─────────────────────────────────────────
    public void EndTurn()
    {
        if (_isTransitioning || IsRouletteActive) return;
        // 드래프트 미완료 시 턴 종료 불가
        if (cardDraft != null && cardDraft.IsDrafting) return;
        if (endTurnButtonText != null) endTurnButtonText.text = "...";
        if (endTurnButton != null)     endTurnButton.gameObject.SetActive(false);
        StartCoroutine(DoEndTurn());
    }

    // ─────────────────────────────────────────
    //  턴 전환 시퀀스
    // ─────────────────────────────────────────
    private IEnumerator DoEndTurn()
    {
        _isTransitioning = true;
        _chainDot.Deactivate();  // 이전 턴 DOT 중단
        Slot[] sourceSlots = _isPlayerTurn ? playerSlots : oppSlots;

        // ── 1. 점수 계산 ──
        string comboName;
        float turnScore = EvaluateSlots(sourceSlots, out comboName);

        // ── 2. 점수 UI 표시 ──
        if (turnScore > 0f)
            yield return StartCoroutine(ShowScoreUI(turnScore, comboName));

        // ── 3. 조커 최적 해석 ──
        ResolveJokers(sourceSlots);

        // ── 4. 온라인 패킷 전송 ──
        SendTurnEndPacket(sourceSlots);

        // ── 5. 효과 컨텍스트 생성 ──
        var ctx = BuildEffectContext(turnScore, comboName);

        // ── 6. 사전 처리 + 카드 해제 + 분류 ──
        CardEffectPipeline.PrepareAndRelease(ctx);
        var allCards = ctx.AllCards();
        NormalizeCardScales(allCards);

        // ── 7. 효과 실행 ──
        if (allCards.Count > 0)
        {
            Vector3 showcaseCenter =
                (ctx.HealTarget + ctx.AttackTarget) * 0.5f;
            showcaseCenter.z = 0f;

            yield return StartCoroutine(_animator.GatherToPoint(allCards, showcaseCenter));
            yield return new WaitForSeconds(0.3f);

            yield return StartCoroutine(CardEffectPipeline.ExecuteAll(ctx));
        }

        // 상대 슬롯 카드백은 유지 (PlaceCard 안전 체크가 새 배치 시 자동 파괴)

        // ── 8. 턴 전환 준비 ──
        bool wasPlayerTurn = _isPlayerTurn;
        int savedExp = (wasPlayerTurn && exp != null && turnScore > 0f)
            ? Mathf.RoundToInt(turnScore) : 0;

        // ── 9. 체인 잠금 해제 ──
        Slot[] chainedSlots = _isPlayerTurn ? playerSlots : oppSlots;
        yield return StartCoroutine(_chainDot.UnlockAll(chainedSlots));

        // ── 10. 턴 전환 ──
        _isPlayerTurn = !_isPlayerTurn;
        _timer = turnTime;

        foreach (var gui in FindObjectsOfType<GameUI>())
            gui.isScoreOverridden = false;

        UpdateInteraction();

        yield return StartCoroutine(AnimateTurnScale());

        if (gameUI != null) gameUI.isScoreOverridden = false;

        // ── 11. EXP + 룰렛 ──
        if (savedExp > 0)
        {
            System.Func<IEnumerator> levelUpCb = roulette != null
                ? () => roulette.SpinAndReward()
                : (System.Func<IEnumerator>)null;
            yield return StartCoroutine(exp.AddExpAnimated(savedExp, levelUpCb));
            Debug.Log($"[MainFlow] EXP +{savedExp} 완료");
        }

        // ── 12. 체인 DOT 틱 시작 ──
        // 온라인: 내 턴일 때만 DOT 실행 (상대는 Sync로 HP 수신)
        if (!isOnlineMode || _isPlayerTurn)
        {
            Slot[] activeSlots = _isPlayerTurn ? playerSlots : oppSlots;
            _chainDot.Activate(activeSlots, !_isPlayerTurn);
        }

        // ── 13. 타이머 리셋 + 전환 완료 (모든 작업 끝난 후) ──
        _timer = turnTime;
        _syncTimer = 0f;  // 즉시 첫 Sync 전송
        _isTransitioning = false;

        // ── 14. 새 턴 카드 드래프트 ──
        if (cardDraft != null) cardDraft.StartDraft();
    }

    // ═════════════════════════════════════════
    //  헬퍼 메서드
    // ═════════════════════════════════════════

    private TurnEffectContext BuildEffectContext(float turnScore, string comboName)
    {
        Transform mySpawn = _isPlayerTurn
            ? (deck.deckSpawnPoint != null ? deck.deckSpawnPoint : deck.transform)
            : (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint : oppDeck.transform);

        Transform target = _isPlayerTurn
            ? (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint : oppDeck.transform)
            : (deck.deckSpawnPoint != null ? deck.deckSpawnPoint : deck.transform);

        // 온라인 수신 체인 잠금 인덱스 소비
        List<int> chainIndices = null;
        if (_pendingChainLockIndices != null)
        {
            chainIndices = new List<int>(_pendingChainLockIndices);
            _pendingChainLockIndices = null;
        }

        return new TurnEffectContext
        {
            IsPlayerTurn    = _isPlayerTurn,
            TurnScore       = turnScore,
            ComboName       = comboName,
            PlayerSlots     = playerSlots,
            OppSlots        = oppSlots,
            AttackTarget    = target.position,
            HealTarget      = mySpawn.position,
            ShakeTarget     = target,
            Hp              = hp,
            Animator        = _animator,
            Host            = this,
            Deck            = deck,
            OppDeck         = oppDeck,
            ChainLockPrefab = chainLockPrefab,
            PresetChainTargetIndices = chainIndices,
        };
    }

    private float EvaluateSlots(Slot[] slots, out string ruleName)
    {
        ruleName = "";
        if (gameFlow == null || slots == null) return 0f;
        string comboName;
        float comboScore;
        gameFlow.GetBestCombo(slots, out comboName, out comboScore);
        ruleName = comboName;
        return comboScore;
    }

    private IEnumerator ShowScoreUI(float turnScore, string comboName)
    {
        if (turnScore <= 0f || gameUI == null || gameUI.scoreText == null)
            yield break;

        gameUI.isScoreOverridden = true;
        gameUI.scoreText.gameObject.SetActive(true);
        gameUI.scoreText.text = $"+{turnScore:F1}\n({comboName})";

        _scoreSkipped = false;
        float waited = 0f;
        while (waited < 1f && !_scoreSkipped)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (scoreUIButton != null)
        {
            scoreUIButton.OnDeselect(null);
            scoreUIButton.interactable = false;
            scoreUIButton.interactable = true;
        }

        gameUI.scoreText.gameObject.SetActive(false);
    }

    private void ResolveJokers(Slot[] slots)
    {
        if (slots == null || gameFlow == null) return;
        string dummyName;
        float dummyScore;
        int[] resolvedValues;
        gameFlow.GetBestCombo(slots, out dummyName, out dummyScore, out resolvedValues);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || !slots[i].HasCard) continue;
            var cv = slots[i].GetCardValue();
            if (cv != null && cv.isJoker
                && resolvedValues != null && i < resolvedValues.Length)
                cv.value = resolvedValues[i];
        }
    }

    private void SendTurnEndPacket(Slot[] sourceSlots)
    {
        if (!isOnlineMode || !_isPlayerTurn || sourceSlots == null
            || NetworkManager.Instance == null) return;

        var packets = new SlotCardData[sourceSlots.Length];
        for (int i = 0; i < sourceSlots.Length; i++)
        {
            if (sourceSlots[i] == null || !sourceSlots[i].HasCard)
            {
                packets[i] = SlotCardData.Empty(i);
                continue;
            }
            var cv = sourceSlots[i].GetCardValue();
            packets[i] = new SlotCardData
            {
                slotIndex = i,
                value     = cv != null ? cv.value       : 0,
                cardType  = cv != null ? (int)cv.cardType : 0,
                isJoker   = cv != null && cv.isJoker,
            };
        }

        // 체인 잠금 대상 인덱스 계산 (공격자 체인 카드 슬롯 = 잠금 위치)
        int[] chainIndices = ComputeChainLockIndices(sourceSlots);

        // HP 동기화: 보내는 쪽(플레이어) HP + 상대 HP
        float myHp  = hp != null ? hp.PlayerHP : -1f;
        float oppHp = hp != null ? hp.OppHP    : -1f;
        NetworkManager.Instance.SendTurnEnd(packets, chainIndices, myHp, oppHp);
    }

    /// <summary>매칭된 체인 카드의 슬롯 인덱스를 반환</summary>
    private int[] ComputeChainLockIndices(Slot[] slots)
    {
        if (slots == null) return null;

        // 같은 값의 체인 카드 수 집계
        var chainValueCount = new System.Collections.Generic.Dictionary<int, int>();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || !slots[i].HasCard || slots[i].IsChainLocked) continue;
            var cv = slots[i].GetCardValue();
            if (cv != null && cv.cardType == CardType.Chain)
            {
                int v = cv.value;
                if (!chainValueCount.ContainsKey(v)) chainValueCount[v] = 0;
                chainValueCount[v]++;
            }
        }

        // 2장 이상 매칭된 체인 카드 슬롯 인덱스 수집
        var indices = new System.Collections.Generic.List<int>();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || !slots[i].HasCard || slots[i].IsChainLocked) continue;
            var cv = slots[i].GetCardValue();
            if (cv != null && cv.cardType == CardType.Chain
                && chainValueCount.ContainsKey(cv.value)
                && chainValueCount[cv.value] >= 2)
                indices.Add(i);
        }

        return indices.Count > 0 ? indices.ToArray() : null;
    }

    private void NormalizeCardScales(List<GameObject> cards)
    {
        if (playerSlots == null || playerSlots.Length == 0
            || playerSlots[0] == null) return;

        var refCol     = playerSlots[0].GetComponent<Collider2D>();
        var deckRef    = FindObjectOfType<Deck>();
        if (refCol == null || deckRef == null
            || deckRef.cardBackPrefab == null) return;

        var backSr = deckRef.cardBackPrefab.GetComponent<SpriteRenderer>();
        if (backSr == null || backSr.sprite == null) return;

        Vector2 spriteSize = backSr.sprite.bounds.size;
        Vector2 slotSize   = refCol.bounds.size;
        float s = Mathf.Min(slotSize.x / spriteSize.x, slotSize.y / spriteSize.y);
        Vector3 scale = new Vector3(s, s, 1f);

        foreach (var card in cards)
            if (card != null) card.transform.localScale = scale;
    }

    private void UpdateInteraction()
    {
        if (deck != null) deck.canPlaceInSlot = true;

        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(true);
            endTurnButton.interactable = _isPlayerTurn;
        }

        if (_isPlayerTurn)
            _reroll.OnNewTurn();
    }

    // ─────────────────────────────────────────
    //  덱 스케일 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator AnimateTurnScale()
    {
        MonoBehaviour activeDeck   = _isPlayerTurn ? (MonoBehaviour)deck : (MonoBehaviour)oppDeck;
        MonoBehaviour inactiveDeck = _isPlayerTurn ? (MonoBehaviour)oppDeck : (MonoBehaviour)deck;

        Transform activeT   = GetDeckTransform(activeDeck);
        Transform inactiveT = GetDeckTransform(inactiveDeck);

        if (activeT == null && inactiveT == null) yield break;

        Vector3 activeStart   = activeT   != null ? activeT.localScale   : Vector3.one;
        Vector3 inactiveStart = inactiveT != null ? inactiveT.localScale : Vector3.one;
        Vector3 activeTarget   = Vector3.one * activeScale;
        Vector3 inactiveTarget = Vector3.one * inactiveScale;

        float elapsed = 0f;
        while (elapsed < scaleDuration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / scaleDuration);
            float eased = t * t * (3f - 2f * t);

            if (activeT   != null) activeT.localScale   = Vector3.Lerp(activeStart,   activeTarget,   eased);
            if (inactiveT != null) inactiveT.localScale = Vector3.Lerp(inactiveStart, inactiveTarget, eased);

            yield return null;
        }

        if (activeT   != null) activeT.localScale   = activeTarget;
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
}
