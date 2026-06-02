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
    public ResultUI  resultUI;
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

    [Header("─ 턴 경고 ─")]
    public float warningTime      = 5f;          // 이 시간 이하부터 경고 (시계 소리 + 빨간 텍스트)
    public Color normalTimeColor  = Color.black; // 평상시 타이머 텍스트 색
    public Color warningTimeColor = Color.red;   // 시간 0 근처 타이머 텍스트 색

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

    [Header("─ 전투 대치 연출 ─")]
    [Tooltip("대치 기준점 (월드 좌표). 기본 원점 0,0")]
    public Vector2 standoffCenter = Vector2.zero;
    [Tooltip("원점에서 각 진영 행까지의 상하 거리")]
    public float standoffGap = 0.8f;
    [Tooltip("대치 시 카드 간 가로 간격")]
    public float standoffSpacing = 1.0f;

    [Header("─ 방어 룰렛 파괴 연출 ─")]
    [Tooltip("룰렛 시작 시 칸당 이동 간격(초)")]
    public float rouletteStartInterval = 0.04f;
    [Tooltip("막바지에 칸당 추가되는 감속량(초)")]
    public float rouletteSlowdown = 0.035f;

    [Header("─ 턴 카드 크기 연출 ─")]
    public float activeScale   = 1.2f;
    public float inactiveScale = 0.9f;
    public float scaleDuration = 0.3f;

    // ─── 상태 ───
    private bool  _isPlayerTurn = true;
    private float _timer;
    private bool  _isTransitioning;
    private bool  _scoreSkipped;

    // ─── 라운드/전투 판정 제어 ───
    private bool _firstPlayerInRound = true; // 현재 라운드의 선공 플레이어 (true: 로컬, false: 상대)
    private bool _isRoundSecondTurn = false; // 현재 라운드의 두 번째 턴 진행 중인가?

    // ─── 필드 단위 Guard 상태 ───
    // 플레이어 필드는 통째로 Attack(앞면) 또는 Guard(뒷면) 중 하나. 한 칸을 뒤집으면 전체가 함께 뒤집힌다.
    private bool _playerFieldGuard;
    public bool PlayerFieldGuard => _playerFieldGuard;


    private OppAuto _oppAuto;

    // ─── 온라인 Sync ───
    private const float SyncInterval = 1f;
    private float _syncTimer;
    private float _syncTargetTimer = -1f;  // 상대가 보낸 타이머 목표값 (보간용)

    // ─── 판정 결정적 RNG (양 클라이언트 동일 결과 보장) ───
    private int _sharedSeed;               // 초기 seed 공유값 (온라인: 호스트 생성 / 게스트 수신)
    private int _roundIndex;               // 라운드마다 증가 → 라운드별 다른 시드
    private System.Random _resolutionRng;  // 방어 생존 파괴 등 '상태에 영향을 주는' 난수 전용

    // ─── 서브시스템 ───
    private TurnAnimator      _animator;
    private ChainDotSystem    _chainDot;
    private SlotRerollHandler _reroll;

    // ─── 공개 프로퍼티 ───
    public bool  IsPlayerTurn    => _isPlayerTurn;
    public float TimeRemaining   => Mathf.Max(0f, _timer);
    public bool  IsTransitioning => _isTransitioning;

    // 현재 라운드의 공격 턴 주인 (라운드 선공자). 두 번째 턴이면 현재 행동자의 반대편이 선공이었음.
    public bool  IsPlayerAttackTurn => _isRoundSecondTurn ? !_isPlayerTurn : _isPlayerTurn;
    public bool  IsRouletteActive => roulette != null && roulette.IsSpinning;

    // 온라인 양 클라이언트가 공유하는 시드 (조커 잭팟 등 결정적 난수용)
    public int   SharedSeed => _sharedSeed;

    // ─────────────────────────────────────────
    //  초기화
    // ─────────────────────────────────────────
    void Start()
    {
        _timer        = turnTime;
        _isPlayerTurn = true;
        // 오프라인 기본 시드 (온라인은 InitialDraftRoutine에서 동기화된 seed로 교체)
        _sharedSeed   = UnityEngine.Random.Range(0, int.MaxValue);

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
            {
                _isPlayerTurn = NetworkManager.Instance.IGoFirst;
                // 라운드 선공자도 IGoFirst로 초기화해야 2라운드부터 양 클라이언트의 턴 순서가 일치한다.
                // (기본값 true로 두면 게스트가 라운드 교대 계산을 틀려 양측 모두 "상대 선공"으로 판단 → 데드락)
                _firstPlayerInRound = NetworkManager.Instance.IGoFirst;
            }
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

        // 온라인: Deck/OppDeck의 Start()가 이미 로컬 RNG로 카드를 뽑았으므로,
        // seed 동기화 후 다시 뽑아야 한다 → InitialDraftRoutine에서 처리
        if (cardDraft != null) StartCoroutine(InitialDraftRoutine());
    }

    private IEnumerator InitialDraftRoutine()
    {
        _isTransitioning = true; // 턴 행동 및 타이머 차단

        if (isOnlineMode && NetworkManager.Instance != null)
        {
            var nm = NetworkManager.Instance;

            if (nm.IsHost)
            {
                // 호스트: seed 생성 → 전송 → 로컬 적용
                int seed = UnityEngine.Random.Range(0, int.MaxValue);
                Debug.Log($"[MainFlow] 호스트 seed 생성: {seed}");
                nm.SendSeed(seed);
                _sharedSeed = seed;  // 판정 결정적 RNG에도 동일 seed 사용

                // seed 기반으로 카드 재뽑기
                if (deck != null)    deck.DrawCards(seed);
                if (oppDeck != null) oppDeck.DrawCards(seed);
            }
            else
            {
                // 게스트: seed 수신 대기
                Debug.Log("[MainFlow] 게스트 seed 수신 대기...");
                float seedTimeout = 5f;
                while (!nm.HasIncomingSeed && seedTimeout > 0f)
                {
                    seedTimeout -= Time.deltaTime;
                    yield return null;
                }

                if (nm.HasIncomingSeed)
                {
                    int seed = nm.IncomingSeed;
                    nm.ConsumeIncomingSeed();
                    Debug.Log($"[MainFlow] 게스트 seed 수신: {seed}");
                    _sharedSeed = seed;  // 판정 결정적 RNG에도 동일 seed 사용

                    // seed 기반으로 카드 재뽑기
                    if (deck != null)    deck.DrawCards(seed);
                    if (oppDeck != null) oppDeck.DrawCards(seed);
                }
                else
                {
                    Debug.LogWarning("[MainFlow] seed 수신 타임아웃 — 로컬 RNG 유지");
                }
            }
        }

        // 딜링 애니메이션 완료 대기
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
        {
            endTurnButtonText.text = Mathf.CeilToInt(Mathf.Max(0f, _timer)).ToString();

            // 시간이 0에 가까워질수록 텍스트가 빨개짐
            float t = warningTime > 0f ? Mathf.Clamp01(_timer / warningTime) : 1f;
            endTurnButtonText.color = Color.Lerp(warningTimeColor, normalTimeColor, t);
        }

        // 시계 소리: 내 턴에 타이머가 실제로 흐를 때만 루프 재생, 그 외엔 정지
        if (SoundManager.Instance != null)
        {
            if (_isPlayerTurn && !pauseTimer && _timer > 0f)
                SoundManager.Instance.StartClock();
            else
                SoundManager.Instance.StopClock();
        }

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

        // 슬롯 리롤 시스템 비활성화 (요청 사항)
        // if (_isPlayerTurn && !_isTransitioning && !_reroll.IsRerolling && !isDrafting)
        //     _reroll.Tick();
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

        // 상대 Sync 수신 → 목표 타이머/HP 저장 (즉시 덮어쓰지 않음, 보간으로 따라감)
        if (nm.HasIncomingSync)
        {
            _syncTargetTimer = nm.IncomingSyncTimer;
            if (hp != null)
            {
                ApplyHpSync(nm.IncomingSyncSenderHp, isOpp: true);
                ApplyHpSync(nm.IncomingSyncReceiverHp, isOpp: false);
            }
            nm.ConsumeIncomingSync();
        }

        // 상대 턴(옵저버): 목표값을 자연 감소시키고 _timer를 부드럽게 추종 → 1초 점프 제거
        if (!_isPlayerTurn && !_isTransitioning && _syncTargetTimer >= 0f)
        {
            _syncTargetTimer = Mathf.Max(0f, _syncTargetTimer - Time.deltaTime);
            float smoothing  = 1f - Mathf.Exp(-4f * Time.deltaTime);  // 프레임레이트 독립
            _timer = Mathf.Lerp(_timer, _syncTargetTimer, smoothing);
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

        // 드래프트 결과 수신 → 양쪽 패에 반영 (상대 턴 시작 시 1회). 카드 배치보다 먼저 처리.
        if (nm.HasIncomingDraft && cardDraft != null && cardDraft.IsAwaitingRemoteDraft)
        {
            var draft = nm.IncomingDraft;
            nm.ConsumeIncomingDraft();
            cardDraft.ApplyRemoteDraft(draft);
        }

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

        // 원격 드래프트 리플레이가 진행 중이면 상대 TurnEnd를 미뤄 둔다.
        // (리플레이 중 EndTurn은 IsDrafting 가드로 무시되는데, IncomingTurnEnd는 이미 소비되어 드롭 → 데드락)
        bool draftBusy = cardDraft != null && cardDraft.IsDrafting;
        if (!_isPlayerTurn && !_isTransitioning && !draftBusy && nm.IncomingTurnEnd != null)
        {
            var data = nm.IncomingTurnEnd;
            bool oppFieldGuard = nm.IncomingFieldGuard;

            // HP 동기화: 상대가 보낸 senderHp = 상대의 HP, receiverHp = 나의 HP
            ApplyHpSync(nm.IncomingSenderHp, isOpp: true);
            ApplyHpSync(nm.IncomingReceiverHp, isOpp: false);

            nm.ConsumeIncomingTurnEnd();
            Debug.Log($"[MainFlow] 상대 TurnEnd 처리 (Guard:{oppFieldGuard})");
            if (onlineOpponent != null)
                onlineOpponent.HandleTurnEnd(data, oppFieldGuard);
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
        if (SoundManager.Instance != null) SoundManager.Instance.StopClock();
        if (endTurnButtonText != null) endTurnButtonText.text = "...";
        if (endTurnButton != null)     endTurnButton.gameObject.SetActive(false);
        StartCoroutine(DoEndTurn());
    }

    // ─────────────────────────────────────────
    //  턴 전환 시퀀스
    // ─────────────────────────────────────────
    private IEnumerator DoEndTurn()
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.turnEnd);

        _isTransitioning = true;
        _chainDot.Deactivate();  // 이전 턴 DOT 중단

        Slot[] sourceSlots = _isPlayerTurn ? playerSlots : oppSlots;

        // 조커 최적 해석 (미리 해둠)
        ResolveJokers(sourceSlots);

        // 온라인 패킷 전송 (내 턴이었다면 상대에게 턴 넘김을 알림)
        SendTurnEndPacket(sourceSlots);

        if (!_isRoundSecondTurn)
        {
            // 라운드의 첫 번째 플레이어 턴 종료 -> 상대에게 턴을 넘김
            _isRoundSecondTurn = true;
            _isPlayerTurn = !_isPlayerTurn;
            _timer = turnTime;

            UpdateInteraction();
            yield return StartCoroutine(AnimateTurnScale());

            // 체인 잠금 해제 (이전 턴의 잠금 유지)
            Slot[] chainedSlots = _isPlayerTurn ? playerSlots : oppSlots;
            yield return StartCoroutine(_chainDot.UnlockAll(chainedSlots));

            _timer = turnTime;
            _syncTimer = 0f;
            _isTransitioning = false;

            // 다음 플레이어 턴 시작
            if (cardDraft != null) cardDraft.StartDraft();
            
            // 엔드턴 버튼 다시 활성화
            if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
        }
        else
        {
            // 라운드의 두 번째 플레이어 턴 종료 -> 전투 판정(Resolution Phase) 진입
            yield return StartCoroutine(ResolutionPhaseRoutine());
        }
    }

    // 필드 단위 전투 결과
    private enum FieldOutcome
    {
        None,     // 카드 없음
        Fly,      // 공격 발사 → 본체 데미지, 카드 소모
        Grave,    // 파괴(무덤) — 패로 복귀하지 않음
        Survive,  // Guard 생존 — 0~3장 랜덤 파괴, 나머지 패 복귀
        Stay      // Guard 잔류 — 뒷면 그대로 다음 라운드까지 유지
    }

    private IEnumerator ResolutionPhaseRoutine()
    {
        _isTransitioning = true;

        // 방어 생존 파괴 등 '상태에 영향을 주는' 난수를 양 클라이언트가 동일하게 뽑도록 결정적 RNG 초기화.
        // 점수·자세·카드수가 이미 동기화돼 호출 순서가 같으므로, 같은 시드 → 같은 파괴 결과.
        _resolutionRng = new System.Random(unchecked(_sharedSeed + _roundIndex * 999983));
        _roundIndex++;

        // 1. 점수 계산 (필드 전체 = 1콤보, 각 측 점수 1개)
        string pCombo, oCombo;
        float pScore = EvaluateSlots(playerSlots, out pCombo);
        float oScore = EvaluateSlots(oppSlots, out oCombo);

        // 2. 전투 참가 슬롯 + 필드 상태 (필드는 통째로 Attack 또는 Guard)
        List<Slot> pSlots = GatherFieldSlots(playerSlots);
        List<Slot> oSlots = GatherFieldSlots(oppSlots);
        bool pGuard = AnyFieldGuard(playerSlots);
        bool oGuard = AnyFieldGuard(oppSlots);
        bool pHas = pSlots.Count > 0;
        bool oHas = oSlots.Count > 0;

        Debug.Log($"[Resolution] pScore={pScore}({pCombo}) oScore={oScore}({oCombo}) pGuard={pGuard} oGuard={oGuard} pCards={pSlots.Count} oCards={oSlots.Count}");

        // 3. 연출 순서 결정: 전투 상황이면 양측 카드를 모두 줌+패닝 연출 (앞/뒷면 무관)
        if (pHas && oHas)
        {
            // 전투: 플레이어 카드 훑기 -> 상대 카드 훑기 -> 점수 UI
            yield return StartCoroutine(PanAndRevealSlots(pSlots, isPlayer: true, pCombo, pScore));

            // 상대편 보러 가기 전 내 결과 페이드아웃 (최소 플로팅 시간 보장 후)
            if (resultUI != null) yield return StartCoroutine(resultUI.HideResult());

            yield return StartCoroutine(PanAndRevealSlots(oSlots, isPlayer: false, oCombo, oScore));

            // 점수 UI로 넘어가기 전 상대 결과 페이드아웃
            if (resultUI != null) yield return StartCoroutine(resultUI.HideResult());

            // 모든 카드 확인 후 점수 표시
            yield return StartCoroutine(ShowResolutionScores(pScore, pCombo, pHas, oScore, oCombo, oHas));
        }
        else
        {
            // 비전투: 점수 먼저 표시
            yield return StartCoroutine(ShowResolutionScores(pScore, pCombo, pHas, oScore, oCombo, oHas));
        }

        // 5. 결과 판정 — 본체 데미지는 라운드당 1회만 적용
        FieldOutcome pOut = FieldOutcome.None, oOut = FieldOutcome.None;
        float dmgToOpp = 0f, dmgToPlayer = 0f;

        if (pHas && !oHas)
        {
            // 카드 vs 빈 필드
            if (!pGuard) { pOut = FieldOutcome.Fly; dmgToOpp = pScore; }
            else          pOut = FieldOutcome.Stay;            // Guard는 잔류 (심리전)
        }
        else if (!pHas && oHas)
        {
            if (!oGuard) { oOut = FieldOutcome.Fly; dmgToPlayer = oScore; }
            else          oOut = FieldOutcome.Stay;
        }
        else if (pHas && oHas)
        {
            if (!pGuard && !oGuard)
            {
                // 공격 vs 공격 — 양쪽 본체 데미지, 양쪽 소모
                pOut = FieldOutcome.Fly; oOut = FieldOutcome.Fly;
                dmgToOpp = pScore; dmgToPlayer = oScore;
            }
            else if (!pGuard && oGuard)
            {
                // 공격(나) vs Guard(상대)
                if (oScore >= pScore)   // Guard 승리 (동점은 Guard 우세)
                {
                    pOut = FieldOutcome.Grave;    // 내 공격 파괴(차단)
                    oOut = FieldOutcome.Survive;  // 상대 Guard 0~3 랜덤
                }
                else                    // 공격 승리 → 점수 차이만큼 데미지
                {
                    pOut = FieldOutcome.Fly; dmgToOpp = pScore - oScore;
                    oOut = FieldOutcome.Grave;    // Guard 전부 파괴
                }
            }
            else if (pGuard && !oGuard)
            {
                // Guard(나) vs 공격(상대)
                if (pScore >= oScore)
                {
                    oOut = FieldOutcome.Grave;
                    pOut = FieldOutcome.Survive;
                }
                else
                {
                    oOut = FieldOutcome.Fly; dmgToPlayer = oScore - pScore;
                    pOut = FieldOutcome.Grave;
                }
            }
            else
            {
                // Guard vs Guard (올인 대치) — 낮은 쪽 파괴 + 두 점수 합 데미지, 높은 쪽 0~3 랜덤
                Debug.Log($"[Resolution] Guard vs Guard! pScore={pScore}, oScore={oScore}, pGuard={pGuard}, oGuard={oGuard}");
                if (pScore > oScore)
                {
                    oOut = FieldOutcome.Grave; pOut = FieldOutcome.Survive;
                    dmgToOpp = pScore + oScore;
                    Debug.Log($"[Resolution] Player wins! dmgToOpp={dmgToOpp}");
                }
                else if (oScore > pScore)
                {
                    pOut = FieldOutcome.Grave; oOut = FieldOutcome.Survive;
                    dmgToPlayer = pScore + oScore;
                    Debug.Log($"[Resolution] Opp wins! dmgToPlayer={dmgToPlayer}");
                }
                else
                {
                    // 무승부: 양쪽 모두 파괴, 데미지 없음
                    pOut = FieldOutcome.Grave; oOut = FieldOutcome.Grave;
                    Debug.Log("[Resolution] Guard vs Guard DRAW — no damage");
                }
            }
        }

        // 6. 연출 + 데미지 + 카드 처리
        Debug.Log($"[Resolution] OUTCOME pOut={pOut} oOut={oOut} dmgToOpp={dmgToOpp} dmgToPlayer={dmgToPlayer}");
        yield return StartCoroutine(ResolveOutcome(pSlots, oSlots, pOut, oOut, dmgToOpp, dmgToPlayer, pGuard, oGuard));

        // 7. 체인 잠금 해제
        yield return StartCoroutine(_chainDot.UnlockAll(playerSlots));
        yield return StartCoroutine(_chainDot.UnlockAll(oppSlots));

        // EXP 보상 (로컬 플레이어만)
        int savedExp = (exp != null && pScore > 0f) ? Mathf.RoundToInt(pScore) : 0;
        if (savedExp > 0)
        {
            System.Func<IEnumerator> levelUpCb = roulette != null ? () => roulette.SpinAndReward() : (System.Func<IEnumerator>)null;
            yield return StartCoroutine(exp.AddExpAnimated(savedExp, levelUpCb));
        }

        // 8. 다음 라운드 준비 (선공 교대)
        _firstPlayerInRound = !_firstPlayerInRound;
        _isPlayerTurn = _firstPlayerInRound;
        _isRoundSecondTurn = false;

        foreach (var gui in FindObjectsOfType<GameUI>()) gui.isScoreOverridden = false;
        UpdateInteraction();
        yield return StartCoroutine(AnimateTurnScale());

        _timer = turnTime;
        _syncTimer = 0f;
        _isTransitioning = false;

        if (cardDraft != null) cardDraft.StartDraft();
        if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
    }

    // ─────────────────────────────────────────
    //  판정 연출 + 데미지 + 카드 정리
    // ─────────────────────────────────────────
    private IEnumerator ResolveOutcome(List<Slot> pSlots, List<Slot> oSlots,
        FieldOutcome pOut, FieldOutcome oOut, float dmgToOpp, float dmgToPlayer, bool pGuard, bool oGuard)
    {
        Transform oppBodyT    = oppDeck != null
            ? (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint : oppDeck.transform) : null;
        Transform playerBodyT = deck != null
            ? (deck.deckSpawnPoint != null ? deck.deckSpawnPoint : deck.transform) : null;

        bool combat = pSlots.Count > 0 && oSlots.Count > 0;

        // ─── 전투가 아니면 (카드 vs 빈 필드): 직선 공격 또는 잔류 ───
        if (!combat)
        {
            if (pOut == FieldOutcome.Fly && oppBodyT != null)
            {
                yield return StartCoroutine(_animator.FlyAndHit(GetCards(pSlots), oppBodyT.position));
                foreach (var s in pSlots) s.ForgetCard();
            }
            if (oOut == FieldOutcome.Fly && playerBodyT != null)
            {
                yield return StartCoroutine(_animator.FlyAndHit(GetCards(oSlots), playerBodyT.position));
                foreach (var s in oSlots) s.ForgetCard();
            }
            if (dmgToOpp > 0f)    yield return StartCoroutine(ApplyBodyDamage(true,  dmgToOpp,    oppBodyT));
            if (dmgToPlayer > 0f) yield return StartCoroutine(ApplyBodyDamage(false, dmgToPlayer, playerBodyT));
            yield break; // Stay 등은 그대로 잔류
        }

        // ─── 전투: 중앙으로 모여 대치 ───
        yield return StartCoroutine(GatherBothToCenter(pSlots, oSlots));
        yield return new WaitForSeconds(0.45f);

        // 공격 vs 공격 — 양쪽이 충돌, 양쪽 본체 피해, 양쪽 소모
        if (dmgToOpp > 0f && dmgToPlayer > 0f)
        {
            yield return StartCoroutine(_animator.MutualAttackClash(
                GetCards(pSlots), 
                GetCards(oSlots), 
                oppBodyT != null ? oppBodyT.position : Vector3.zero, 
                playerBodyT != null ? playerBodyT.position : Vector3.zero,
                () => { StartCoroutine(ApplyBodyDamage(true, dmgToOpp, oppBodyT)); },
                () => { StartCoroutine(ApplyBodyDamage(false, dmgToPlayer, playerBodyT)); }
            ));

            foreach (var s in pSlots) s.ForgetCard();
            foreach (var s in oSlots) s.ForgetCard();
            yield break;
        }

        // 한쪽만 데미지 — 차이(공격 승리) 또는 합(방어 승리)
        if (dmgToOpp > 0f || dmgToPlayer > 0f)
        {
            bool         playerWins   = dmgToOpp > 0f;
            List<Slot>   winnerSlots  = playerWins ? pSlots : oSlots;
            List<Slot>   loserSlots   = playerWins ? oSlots : pSlots;
            FieldOutcome winnerOut    = playerWins ? pOut : oOut;
            Transform    loserBody    = playerWins ? oppBodyT : playerBodyT;
            bool         loserIsPlayer = !playerWins;
            float        dmg          = playerWins ? dmgToOpp : dmgToPlayer;

            bool winnerIsGuard = playerWins ? pGuard : oGuard;
            bool loserIsGuard  = playerWins ? oGuard : pGuard;

            System.Action onBodyHit = () => {
                StartCoroutine(ApplyBodyDamage(playerWins, dmg, loserBody));
            };

            yield return StartCoroutine(_animator.UnifiedCombatStrike(
                GetCards(winnerSlots), 
                GetCards(loserSlots), 
                loserBody != null ? loserBody.position : Vector3.zero, 
                winnerIsGuard, 
                loserIsGuard,
                onBodyHit
            ));

            if (!winnerIsGuard) foreach (var s in winnerSlots) s.ForgetCard();
            foreach (var s in loserSlots) s.ForgetCard();

            // 패자 잔여 파괴 (이미 ForgetCard 되었다면 무시됨)
            yield return StartCoroutine(GraveyardSlots(loserSlots, loserIsPlayer));
            
            // 승자가 생존(Guard)이면 룰렛으로 일부 파괴 후 복귀
            if (winnerOut == FieldOutcome.Survive)
                yield return StartCoroutine(ProcessGuardSurvivors(winnerSlots, playerWins));
            yield break;
        }

        // 데미지 없음 — 방어가 공격 완전 차단 / 무승부
        if (dmgToOpp == 0f && dmgToPlayer == 0f)
        {
            if (pGuard != oGuard)
            {
                // 한쪽은 Attack, 한쪽은 Guard (Guard 승리 / Attack 차단됨)
                bool playerWins = pGuard; // Guard 측이 승자(차단 성공)
                List<Slot> atkSlots = playerWins ? oSlots : pSlots;
                List<Slot> defSlots = playerWins ? pSlots : oSlots;
                
                // Attack 카드가 Guard 카드에 부딪히고 산산조각(파괴) 나는 연출
                yield return StartCoroutine(_animator.BlockAndShatter(GetCards(atkSlots), GetCards(defSlots)));
                foreach (var s in atkSlots) s.ForgetCard(); // 공격 카드는 연출에서 파괴됨
            }

            // 남은 잔여 처리 (무승부면 양쪽 다 Grave, 차단이면 공격은 위에서 파괴+Forget, 수비는 Survive 룰렛)
            if (pOut == FieldOutcome.Grave && oOut == FieldOutcome.Grave)
            {
                // 양쪽 다 Attack이고 무승부면 중앙 격돌 파괴 연출
                if (!pGuard && !oGuard)
                {
                    yield return StartCoroutine(_animator.MutualDestructionClash(GetCards(pSlots), GetCards(oSlots)));
                    foreach (var s in pSlots) s.ForgetCard();
                    foreach (var s in oSlots) s.ForgetCard();
                }
                else
                {
                    yield return StartCoroutine(GraveyardSlots(pSlots, true));
                    yield return StartCoroutine(GraveyardSlots(oSlots, false));
                }
            }
            else
            {
                if (pOut == FieldOutcome.Grave) yield return StartCoroutine(GraveyardSlots(pSlots, true));
                if (oOut == FieldOutcome.Grave) yield return StartCoroutine(GraveyardSlots(oSlots, false));
            }

            if (pOut == FieldOutcome.Survive) yield return StartCoroutine(ProcessGuardSurvivors(pSlots, true));
            if (oOut == FieldOutcome.Survive) yield return StartCoroutine(ProcessGuardSurvivors(oSlots, false));
        }
    }

    /// <summary>본체 데미지 적용 + 피격 흔들림</summary>
    private IEnumerator ApplyBodyDamage(bool toOpp, float dmg, Transform bodyT)
    {
        if (hp == null || dmg <= 0f) yield break;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.damageTaken);

        if (toOpp) hp.DamageOpp(dmg); else hp.DamagePlayer(dmg);
        if (bodyT != null)
            StartCoroutine(_animator.ShakeTransform(bodyT, hitShakeDuration, hitShakeIntensity));
        yield return StartCoroutine(_animator.ShakeCamera(hitShakeDuration, cameraShakeIntensity));
    }

    /// <summary>양 진영 카드를 원점(standoffCenter) 기준 위·아래 두 행으로 나열해 대치시킨다</summary>
    private IEnumerator GatherBothToCenter(List<Slot> pSlots, List<Slot> oSlots)
    {
        // 각 진영을 자기 쪽(원점 기준 위/아래)으로 배치
        float pSign = Centroid(pSlots).y >= Centroid(oSlots).y ? 1f : -1f;
        Vector3 pRow = new Vector3(standoffCenter.x, standoffCenter.y + pSign * standoffGap, 0f);
        Vector3 oRow = new Vector3(standoffCenter.x, standoffCenter.y - pSign * standoffGap, 0f);

        Coroutine g1 = StartCoroutine(_animator.LineUpAt(GetCards(pSlots), pRow, standoffSpacing));
        Coroutine g2 = StartCoroutine(_animator.LineUpAt(GetCards(oSlots), oRow, standoffSpacing));
        if (g1 != null) yield return g1;
        if (g2 != null) yield return g2;
    }

    private Vector3 Centroid(List<Slot> slots)
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        foreach (var s in slots)
        {
            var c = s.GetPlacedCard();
            if (c != null) { sum += c.transform.position; n++; }
        }
        return n > 0 ? sum / n : Vector3.zero;
    }

    /// <summary>중앙으로 모였던 카드를 각자 슬롯 위치로 되돌린다</summary>
    private IEnumerator ReturnCardsToSlots(List<Slot> slots, float dur = 0.2f)
    {
        var cards = new List<GameObject>();
        var from  = new List<Vector3>();
        var to    = new List<Vector3>();
        foreach (var s in slots)
        {
            var c = s.GetPlacedCard();
            if (c == null) continue;
            cards.Add(c);
            from.Add(c.transform.position);
            to.Add(s.transform.position);
        }
        if (cards.Count == 0) yield break;

        float e = 0f;
        while (e < dur)
        {
            e += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
            for (int i = 0; i < cards.Count; i++)
                if (cards[i] != null) cards[i].transform.position = Vector3.Lerp(from[i], to[i], t);
            yield return null;
        }
        for (int i = 0; i < cards.Count; i++)
            if (cards[i] != null) cards[i].transform.position = to[i];
    }

    /// <summary>패자 카드의 일부(1~장수-1)를 중앙 대치 중 먼저 파괴해 덱으로 보낸다</summary>
    private IEnumerator PartialDestroyAtCenter(List<Slot> loserSlots, bool loserIsPlayer)
    {
        var alive = new List<Slot>();
        foreach (var s in loserSlots)
            if (s.GetPlacedCard() != null) alive.Add(s);
        if (alive.Count <= 1) yield break; // 1장 이하면 본 파괴 단계에서 처리

        int partial = ResRandRange(1, alive.Count); // 1 ~ 장수-1 (결정적 — 현재 미사용이나 동기화 대비)
        Vector3 anchor = DeckAnchor(loserIsPlayer);
        Coroutine last = null;
        for (int i = 0; i < partial; i++)
        {
            var card = alive[i].GetPlacedCard();
            alive[i].ForgetCard();
            if (card != null)
            {
                card.transform.SetParent(null);
                last = StartCoroutine(_animator.FlyCardTo(card, anchor));
            }
            if (i < partial - 1) yield return new WaitForSeconds(0.06f);
        }
        if (last != null) yield return last;
    }

    /// <summary>카드가 있고 잠기지 않은 슬롯 목록</summary>
    private List<Slot> GatherFieldSlots(Slot[] slots)
    {
        var list = new List<Slot>();
        if (slots == null) return list;
        foreach (var s in slots)
            if (s != null && s.HasCard && !s.IsChainLocked) list.Add(s);
        return list;
    }

    private List<GameObject> GetCards(List<Slot> slots)
    {
        var list = new List<GameObject>();
        foreach (var s in slots)
        {
            var c = s.GetPlacedCard();
            if (c != null) list.Add(c);
        }
        return list;
    }

    /// <summary>카드를 한 번 줌 인 후 쭉 이동하며 순차 조명. Guard면 공개. 마지막 카드는 셰이크+여운.</summary>
    private IEnumerator PanAndRevealSlots(List<Slot> slots, bool isPlayer, string comboName, float score)
    {
        if (slots == null || slots.Count == 0) yield break;

        Camera cam = Camera.main;
        if (cam == null) yield break;

        float origSize = cam.orthographicSize;
        Vector3 origPos = cam.transform.position;
        float zoomSize = origSize * 0.45f;
        float panDuration = 0.25f;         // 카드 간 이동 시간
        float slowFlipDuration = 1.2f;     // 마지막 카드 극적 뒤집기

        // UI 캔버스 숨기기
        if (uiCanvas != null) uiCanvas.enabled = false;

        // ── 1) 첫 카드 위치로 줌 인 ──
        GameObject firstCard = slots[0].GetPlacedCard();
        if (firstCard != null)
        {
            Vector3 firstTarget = new Vector3(firstCard.transform.position.x, firstCard.transform.position.y, origPos.z);
            yield return StartCoroutine(SmoothCameraMove(cam, firstTarget, zoomSize, 0.35f));
        }

        // ── 2) 줌 유지한 채로 카드를 순차 공개 ──
        for (int i = 0; i < slots.Count; i++)
        {
            Slot s = slots[i];
            GameObject card = s.GetPlacedCard();
            if (card == null) continue;

            bool isLast = (i == slots.Count - 1);

            // 첫 카드가 아니면 줌 유지한 채로 다음 카드 위치로 패닝
            if (i > 0)
            {
                Vector3 cardPos = new Vector3(card.transform.position.x, card.transform.position.y, origPos.z);
                yield return StartCoroutine(SmoothCameraMove(cam, cardPos, zoomSize, panDuration));
            }

            // 살짝 대기
            yield return new WaitForSeconds(isLast ? 0.3f : 0.1f);

            if (s.IsGuard)
            {
                // ── 카드 뒤집기 (Guard) ──
                if (isLast)
                {
                    // 마지막 카드: 미세한 셰이크로 긴장감 고조 + 느린 뒤집기
                    Coroutine shakeRoutine = StartCoroutine(TensionShake(cam, slowFlipDuration));

                    // 뒤집기와 타이밍을 맞춰 결과(콤보명+점수) 페이드인 (병렬)
                    if (resultUI != null)
                        StartCoroutine(resultUI.ShowResult(comboName, score, slowFlipDuration));

                    if (isPlayer)
                        yield return StartCoroutine(s.ForceRevealSlow(slowFlipDuration));
                    else if (_oppAuto != null)
                        yield return StartCoroutine(_oppAuto.RevealGuardCardRoutine(s, slowFlipDuration));
                    else if (isOnlineMode && onlineOpponent != null)
                        yield return StartCoroutine(onlineOpponent.RevealGuardCardRoutine(s));
                    else
                        yield return StartCoroutine(s.ForceRevealSlow(slowFlipDuration));

                    // 셰이크 자연 종료 대기 후 보여주기
                    yield return new WaitForSeconds(0.5f);
                }
                else
                {
                    // 일반 속도 뒤집기
                    if (isPlayer)
                        yield return StartCoroutine(s.ForceReveal());
                    else if (_oppAuto != null)
                        yield return StartCoroutine(_oppAuto.RevealGuardCardRoutine(s));
                    else if (isOnlineMode && onlineOpponent != null)
                        yield return StartCoroutine(onlineOpponent.RevealGuardCardRoutine(s));
                    else
                        yield return StartCoroutine(s.ForceReveal());

                    yield return new WaitForSeconds(0.2f);
                }
            }
            else
            {
                // ── 앞면 (Attack) 유지 ──
                if (isLast)
                {
                    // 뒤집지는 않지만 긴장감을 위해 셰이크와 여운 대기 + 결과 페이드인 (병렬)
                    if (resultUI != null)
                        StartCoroutine(resultUI.ShowResult(comboName, score, 0.5f));

                    yield return StartCoroutine(TensionShake(cam, 0.4f));
                    yield return new WaitForSeconds(0.5f);
                }
                else
                {
                    // 눈에 담을 시간
                    yield return new WaitForSeconds(0.2f);
                }
            }
        }

        // ── 3) 카메라 줌 아웃 + 원위치 복구 ──
        yield return StartCoroutine(SmoothCameraMove(cam, origPos, origSize, 0.4f));

        // UI 캔버스 복구
        if (uiCanvas != null) uiCanvas.enabled = true;
    }

    /// <summary>마지막 카드 뒤집는 동안 점점 강해지는 미세 셰이크 (긴장감)</summary>
    private IEnumerator TensionShake(Camera cam, float duration)
    {
        if (cam == null) yield break;
        Vector3 anchor = cam.transform.position;
        float elapsed = 0f;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionShake);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration; // 0 → 1
            // 진폭: 처음 미세하게 → 끝에 살짝 강하게
            float intensity = Mathf.Lerp(0.01f, 0.06f, progress * progress);
            float offsetX = Random.Range(-1f, 1f) * intensity;
            float offsetY = Random.Range(-1f, 1f) * intensity;
            cam.transform.position = anchor + new Vector3(offsetX, offsetY, 0f);
            yield return null;
        }

        cam.transform.position = anchor;
    }

    /// <summary>카메라를 부드럽게 이동 + 줌 (Orthographic)</summary>
    private IEnumerator SmoothCameraMove(Camera cam, Vector3 targetPos, float targetSize, float duration)
    {
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cameraPan);

        Vector3 startPos = cam.transform.position;
        float startSize = cam.orthographicSize;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            cam.transform.position = Vector3.Lerp(startPos, targetPos, t);
            cam.orthographicSize = Mathf.Lerp(startSize, targetSize, t);
            yield return null;
        }

        cam.transform.position = targetPos;
        cam.orthographicSize = targetSize;
    }

    /// <summary>슬롯 카드들을 덱 앵커로 하나씩 날려 보낸 뒤(소멸) 슬롯을 비운다</summary>
    private IEnumerator GraveyardSlots(List<Slot> slots, bool isPlayer)
    {
        Vector3 anchor = DeckAnchor(isPlayer);
        Coroutine last = null;
        for (int i = 0; i < slots.Count; i++)
        {
            var card = slots[i].GetPlacedCard();
            slots[i].ForgetCard();
            if (card != null)
            {
                card.transform.SetParent(null);
                last = StartCoroutine(_animator.FlyCardTo(card, anchor));
            }
            if (i < slots.Count - 1)
                yield return new WaitForSeconds(0.08f);
        }
        if (last != null) yield return last;
    }

    /// <summary>
    /// 생존한 Guard 카드 중 0~3장(최대 3)을 룰렛으로 무작위 파괴, 나머지는 패로 복귀.
    /// 테두리가 후보 카드를 왔다갔다 하며 점점 느려지다 멈춘 카드를 파괴한다.
    /// </summary>
    // 판정용 결정적 난수 (온라인 양 클라이언트 동일). _resolutionRng 미설정 시 일반 난수로 폴백.
    private double ResRandValue()
        => _resolutionRng != null ? _resolutionRng.NextDouble() : UnityEngine.Random.value;

    private int ResRandRange(int minInclusive, int maxExclusive)
        => _resolutionRng != null ? _resolutionRng.Next(minInclusive, maxExclusive)
                                  : UnityEngine.Random.Range(minInclusive, maxExclusive);

    private IEnumerator ProcessGuardSurvivors(List<Slot> slots, bool isPlayer)
    {
        var candidates = new List<Slot>();
        foreach (var s in slots)
            if (s != null && s.GetPlacedCard() != null) candidates.Add(s);
        if (candidates.Count == 0) yield break;

        // 중앙 대치로 모였던 카드를 각자 슬롯으로 되돌려 룰렛 테두리가 칸별로 보이게 함
        yield return StartCoroutine(ReturnCardsToSlots(candidates));

        // 삭제 장수 결정
        // 1장: 50% 확률로 파괴 (0 또는 1)
        // 2장: 1장 파괴 → 1장 생존
        // 3장 이상: 70% 확률로 "2장만 남기기"(공격적), 30% 확률로 절반 생존(보수적)
        int count = candidates.Count;
        int destroyCount;
        if (count == 1)
        {
            destroyCount = ResRandValue() < 0.5 ? 1 : 0;
        }
        else if (count == 2)
        {
            destroyCount = 1;
        }
        else
        {
            int aggressive   = count - 2;       // remain = 2
            int conservative = count / 2;       // remain ≈ 절반
            destroyCount = (ResRandValue() < 0.7) ? aggressive : conservative;
        }

        // 삭제된 카드가 날아갈 덱 앵커
        Vector3 anchor = DeckAnchor(isPlayer);

        // 삭제할 카드 수만큼 룰렛 반복
        for (int round = 0; round < destroyCount; round++)
        {
            var alive = candidates.FindAll(s => s.GetPlacedCard() != null);
            if (alive.Count == 0) break;

            Slot chosen;
            if (alive.Count == 1)
            {
                chosen = alive[0];
                if (gameFlow != null) gameFlow.ShowRouletteEffect(chosen);

                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.rouletteSelect);

                yield return new WaitForSeconds(0.4f);
            }
            else
            {
                yield return StartCoroutine(SpinRoulette(alive));
                chosen = _rouletteResult;
            }

            if (chosen != null)
                yield return StartCoroutine(DestroyOneSlot(chosen, anchor));
        }

        if (gameFlow != null) gameFlow.HideRouletteEffect();

        // 확실히 삭제된 결과를 본 뒤 잠시 대기 → 그 다음 패로 복귀
        if (destroyCount > 0) yield return new WaitForSeconds(0.6f);

        foreach (var s in candidates)
            ReturnCardToHand(s, isPlayer);
    }

    private Slot _rouletteResult;

    /// <summary>룰렛 테두리가 후보를 돌다 점점 느려지며 한 칸에 멈춘다</summary>
    private IEnumerator SpinRoulette(List<Slot> alive)
    {
        _rouletteResult = null;

        int finalIdx   = ResRandRange(0, alive.Count);    // 파괴 대상 (결정적 — 양 클라이언트 동일)
        int loops      = Random.Range(2, 4);              // 2~3바퀴 (연출용 — 비결정적 무방)
        int totalSteps = loops * alive.Count + finalIdx;

        float interval = rouletteStartInterval;
        for (int step = 0; step <= totalSteps; step++)
        {
            if (gameFlow != null) gameFlow.ShowRouletteEffect(alive[step % alive.Count]);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.rouletteSpin);

            yield return new WaitForSeconds(interval);

            // 마지막 1.5바퀴부터 점점 감속
            if (totalSteps - step <= alive.Count * 1.5f)
                interval += rouletteSlowdown;
        }

        if (gameFlow != null) gameFlow.ShowRouletteEffect(alive[finalIdx]);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.rouletteSelect);

        _rouletteResult = alive[finalIdx];
        yield return new WaitForSeconds(0.35f); // 당첨 강조 유지
    }

    /// <summary>선택된 카드를 덱 앵커로 날려 보낸 뒤 소멸시키고 슬롯을 비운다</summary>
    private IEnumerator DestroyOneSlot(Slot slot, Vector3 anchor)
    {
        var card = slot.GetPlacedCard();
        if (card == null) yield break;

        // 선택 확정 → 테두리 유지한 채 카메라 셰이크 한 번
        yield return StartCoroutine(_animator.ShakeCamera(hitShakeDuration, cameraShakeIntensity));

        // 테두리만 떼고(카드와 함께 파괴되지 않도록) 룰렛 상태는 유지
        if (gameFlow != null) gameFlow.ClearRouletteBorder();

        // 슬롯에서 분리 후 덱 앵커로 날아가 사라짐
        slot.ForgetCard();
        card.transform.SetParent(null);
        yield return StartCoroutine(_animator.FlyCardTo(card, anchor));
    }

    /// <summary>파괴 카드가 날아갈 덱 앵커 월드 좌표 (플레이어는 CardDraft 덱 앵커 우선)</summary>
    private Vector3 DeckAnchor(bool isPlayer)
    {
        if (isPlayer)
        {
            if (cardDraft != null && cardDraft.deckAnchor != null)
                return cardDraft.deckAnchor.position;
            return deck != null
                ? (deck.deckSpawnPoint != null ? deck.deckSpawnPoint.position : deck.transform.position)
                : Vector3.zero;
        }
        // 상대 — CardDraft의 상대 덱 앵커 우선
        if (cardDraft != null && cardDraft.oppDeckAnchor != null)
            return cardDraft.oppDeckAnchor.position;
        return oppDeck != null
            ? (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint.position : oppDeck.transform.position)
            : Vector3.zero;
    }

    /// <summary>슬롯 카드를 패로 복귀시키고 필드에서 제거</summary>
    private void ReturnCardToHand(Slot slot, bool isPlayer)
    {
        var card = slot != null ? slot.GetPlacedCard() : null;
        if (card == null) return;

        var cv = card.GetComponent<CardValue>();
        Vector3 pos = card.transform.position;
        if (cv != null)
        {
            if (isPlayer)
            {
                if (cv.isJoker) deck.AddJokerCard(cv.cardType, pos);
                else            deck.AddCardByValue(cv.value, cv.cardType, pos);
            }
            else
            {
                oppDeck.AddCardByValue(cv.value, cv.cardType);
            }
        }
        Destroy(card);
        slot.ForgetCard();
    }

    /// <summary>판정 시 양측 점수를 함께 표시</summary>
    private IEnumerator ShowResolutionScores(float pScore, string pCombo, bool pHas,
        float oScore, string oCombo, bool oHas)
    {
        if (gameUI == null || gameUI.scoreText == null) yield break;
        // 한쪽이라도 카드를 내지 않았으면 집계 점수 UI를 띄우지 않음 (양측 모두 카드가 있을 때만 표시)
        if (!pHas || !oHas) yield break;

        gameUI.isScoreOverridden = true;
        gameUI.scoreText.gameObject.SetActive(true);

        string pLine = (pHas && pScore > 0f) ? $"나 +{pScore:F1} ({pCombo})"    : "나 -";
        string oLine = (oHas && oScore > 0f) ? $"상대 +{oScore:F1} ({oCombo})" : "상대 -";
        gameUI.scoreText.text = pLine + "\n" + oLine;

        _scoreSkipped = false;
        float waited = 0f;
        while (waited < 1.2f && !_scoreSkipped)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        gameUI.scoreText.gameObject.SetActive(false);
    }

    // ═════════════════════════════════════════
    //  헬퍼 메서드
    // ═════════════════════════════════════════

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

        // HP 동기화: 보내는 쪽(플레이어) HP + 상대 HP
        float myHp  = hp != null ? hp.PlayerHP : -1f;
        float oppHp = hp != null ? hp.OppHP    : -1f;
        NetworkManager.Instance.SendTurnEnd(packets, myHp, oppHp, _playerFieldGuard);
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
        {
            _reroll.OnNewTurn();
            // 새 플레이어 턴 시작 시 필드 Guard 상태를 현재 필드에 맞춘다
            // (이전 라운드에서 잔류한 Guard 카드가 있으면 Guard 모드로 시작)
            _playerFieldGuard = AnyFieldGuard(playerSlots);
        }
    }

    // ─────────────────────────────────────────
    //  필드 단위 Guard (Attack ↔ Guard 전체 뒤집기)
    // ─────────────────────────────────────────
    /// <summary>플레이어 슬롯 중 카드가 있고 잠기지 않은 칸이 하나라도 Guard(뒷면)인지</summary>
    private bool AnyFieldGuard(Slot[] slots)
    {
        if (slots == null) return false;
        foreach (var s in slots)
            if (s != null && s.HasCard && !s.IsChainLocked && s.IsGuard) return true;
        return false;
    }

    /// <summary>플레이어 필드 전체를 Attack ↔ Guard로 토글 (Slot 좌클릭에서 호출)</summary>
    public void ToggleFieldGuard()
    {
        if (_isTransitioning || !_isPlayerTurn) return;
        if (cardDraft != null && cardDraft.IsDrafting) return;
        if (playerSlots == null) return;

        _playerFieldGuard = !_playerFieldGuard;

        for (int i = 0; i < playerSlots.Length; i++)
        {
            var s = playerSlots[i];
            if (s == null || !s.HasCard || s.IsChainLocked) continue;

            // 콤보 하이라이트 등 슬롯 이펙트 분리 (뒤집기 중 파괴 방지)
            if (gameFlow != null) gameFlow.DetachSlotEffect(i);

            s.StartCoroutine(s.FlipTo(_playerFieldGuard));
        }
    }

    /// <summary>새 카드가 놓이면 무조건 모든 카드를 앞면(Attack)으로 전환한다</summary>
    public void OnPlayerCardPlaced(Slot slot)
    {
        if (_playerFieldGuard)
        {
            _playerFieldGuard = false;
            for (int i = 0; i < playerSlots.Length; i++)
            {
                var s = playerSlots[i];
                if (s != null && s.HasCard && !s.IsChainLocked && s.IsGuard)
                    s.StartCoroutine(s.FlipTo(false));
            }
        }
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
