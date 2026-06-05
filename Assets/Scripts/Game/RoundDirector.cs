using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  RoundDirector — 블러드 베팅 게임 진행 오케스트레이터 (구 MainFlow 대체)
//
//  라운드 흐름:
//    Setup(앤티+홀카드+중앙5장) → InitialOpen(번갈아 2장 공개)
//    → Betting(벳-콜 시 1장씩 공개) → Showdown(7장 중 숫자 best-5) → Cleanup
//  5라운드마다 덱 45장 리셋.  승리 = 상대 LP 0.
//
//  1차 타깃: 로컬 + 베팅 AI (온라인 동기화는 이후 단계).
// ─────────────────────────────────────────────
public class RoundDirector : MonoBehaviour
{
    [Header("─ 참조 (비우면 자동 탐색) ─")]
    public Deck       deck;
    public OppDeck    oppDeck;
    public RandomSlot randomSlot;
    public HP         hp;
    public Pot        pot;
    public GameFlow   gameFlow;
    public BettingUI  bettingUI;
    public BettingAI  bettingAI;
    public ResultUI   resultUI;
    public Shuffle    shuffle;

    [Header("─ 홀카드 슬롯 (비우면 손패로 바로 분배하는 폴백) ─")]
    [Tooltip("플레이어 홀카드가 뒷면으로 놓일 슬롯 (좌클릭하면 앞면으로 손패에 들어감)")]
    public Slot[] playerHoleSlots = new Slot[2];

    [Tooltip("상대 홀카드가 뒷면으로 놓일 슬롯 (자동으로 상대 손패에 들어감)")]
    public Slot[] oppHoleSlots = new Slot[2];

    [Tooltip("상대 홀카드를 손패로 보낼 때 슬롯에서 잠깐 앞면으로 보여줄지 (기본 꺼짐=비공개)")]
    public bool revealOppHoleCards = false;

    [Header("─ 쇼다운 줌 중 숨길 UI ─")]
    [Tooltip("쇼다운 카메라 줌 동안 끌 UI 오브젝트들 (Screen Space-Camera/World UI 깨짐 방지). ResultUI는 넣지 말 것")]
    public GameObject[] hideDuringShowdown;

    [Header("─ 베팅 규칙 ─")]
    [Tooltip("라운드마다 양쪽이 내는 참가비")]
    public int ante = 50;

    [Tooltip("최소 벳/레이즈 단위")]
    public int minBet = 50;

    [Tooltip("커뮤니티 5장 중 셋업 때 번갈아 공개할 장수 (나머지는 베팅으로 공개)")]
    public int initialOpenCount = 2;

    [Tooltip("체크-체크 시 카드 공개 없이 쇼다운으로 강제 진행 (기획 권장 압박 룰)")]
    public bool checkCheckForcesShowdown = true;

    [Tooltip("5라운드마다 덱 리셋")]
    public int roundsPerDeck = 5;

    [Header("─ 연출 ─")]
    [Header("─ 샷클락 (제한시간) ─")]
    [Tooltip("켜면 플레이어 결정에 제한시간 — 끝나면 자동 처리(콜 상황=폴드, 아니면 체크)")]
    public bool enableShotClock = true;

    [Tooltip("결정 제한시간(초)")]
    public float shotClockSeconds = 30f;

    [Header("─ AI ─")]
    [Tooltip("AI 액션 전 '생각하는' 대기 시간(초)")]
    public float aiThinkTime = 0.6f;

    [Tooltip("상대 액션 메시지를 보여주고 멈추는 시간(초) — 플레이어가 읽고 판단하도록")]
    public float aiActionDisplayTime = 0.8f;

    [Tooltip("페이즈 사이 짧은 텀(초)")]
    public float phaseGap = 0.3f;

    [Tooltip("쇼다운에서 각 패 결과를 유지하는 시간(초)")]
    public float resultHoldSeconds = 1.5f;

    [Tooltip("라운드 종료 후 결과 정리 UI를 띄우고 다음 라운드까지 대기하는 시간(초)")]
    public float roundResultSeconds = 2f;

    [Header("─ 진행 ─")]
    public bool autoStart = true;

    // ─── 내부 상태 ───
    private readonly BettingState _bet = new BettingState();
    private int  _roundIndex;
    private bool _firstIsPlayer = true;   // 이번 라운드 선공이 플레이어인가
    private bool _gameOver;

    private int  _playerContrib;          // 이번 라운드 팟에 넣은 총액 (환불 계산용)
    private int  _oppContrib;

    // 홀카드: 슬롯에 둔 채 진행. 판정/쇼다운은 이 스펙으로(슬롯·손패 상태와 무관).
    private List<Deck.CardPool> _playerHoleSpecs = new List<Deck.CardPool>();
    private List<Deck.CardPool> _oppHoleSpecs    = new List<Deck.CardPool>();
    private bool _holeClicksActive;       // 플레이어 홀 슬롯 좌클릭→손패 허용(비차단)

    private int   _lastRoundWinner;       // 0=무승부, 1=플레이어, 2=상대 (결과 정리 UI용)
    private float _lastPotWon;            // 이번 라운드 승자가 가져간 팟

    // 플레이어 액션 수신 (BettingUI → 이 필드)
    private bool      _hasPending;
    private BetAction _pendingAction;
    private int       _pendingAmount;

    // 스트리트/라운드 결과 전달용
    private StreetResult _streetResult;
    private bool _goToShowdown;
    private int  _foldWinner;             // 0=없음, 1=플레이어, 2=상대

    void Start()
    {
        if (deck == null)       deck = FindObjectOfType<Deck>();
        if (oppDeck == null)    oppDeck = FindObjectOfType<OppDeck>();
        if (randomSlot == null) randomSlot = FindObjectOfType<RandomSlot>();
        if (hp == null)         hp = FindObjectOfType<HP>();
        if (pot == null)        pot = FindObjectOfType<Pot>();
        if (gameFlow == null)   gameFlow = FindObjectOfType<GameFlow>();
        if (bettingUI == null)  bettingUI = FindObjectOfType<BettingUI>();
        if (bettingAI == null)  bettingAI = FindObjectOfType<BettingAI>();
        if (resultUI == null)   resultUI = FindObjectOfType<ResultUI>();
        if (shuffle == null)    shuffle = FindObjectOfType<Shuffle>();

        if (bettingUI != null) bettingUI.OnAction += OnPlayerAction;

        // 손패 카드를 슬롯에 드롭하지 못하게 (베팅 게임엔 손패→슬롯 배치 없음)
        if (deck != null) deck.canPlaceInSlot = false;

        if (autoStart) StartCoroutine(GameLoop());
    }

    void OnDestroy()
    {
        if (bettingUI != null) bettingUI.OnAction -= OnPlayerAction;
    }

    void Update()
    {
        // 홀카드는 게임 진행과 무관하게, 언제든 좌클릭으로 앞면→손패 (비차단)
        if (_holeClicksActive && UseHoleSlots() && Input.GetMouseButtonDown(0))
        {
            Slot s = HitFaceDownSlot(playerHoleSlots);
            if (s != null) StartCoroutine(RevealHoleToHand(s, true));
        }
    }

    private void OnPlayerAction(BetAction action, int amount)
    {
        _pendingAction = action;
        _pendingAmount = amount;
        _hasPending    = true;
    }

    // ─────────────────────────────────────────
    //  메인 루프
    // ─────────────────────────────────────────
    public IEnumerator GameLoop()
    {
        // 한 프레임 대기 — 다른 컴포넌트(HP 등)의 초기화가 끝난 뒤 시작
        yield return null;

        if (pot != null) pot.ResetPot();
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlayBGM(SoundManager.Instance.combatBGM, SoundManager.Instance.bgmVolume);

        while (!_gameOver)
        {
            // 시작 전 파산 체크
            if (PlayerStack() <= 0 || OppStack() <= 0) { EndGame(); yield break; }

            yield return StartCoroutine(SetupPhase());
            yield return Gap();

            // 홀카드는 슬롯에 둔 채로 게임 진행. 손패로 가져오는 건 Update에서
            // 언제든(비차단) 좌클릭으로 가능. 판정은 저장된 홀 스펙으로 하므로 타이밍 무관.
            yield return StartCoroutine(InitialOpenPhase());
            yield return Gap();

            yield return StartCoroutine(BettingPhase());
            yield return Gap();

            if (_goToShowdown)
                yield return StartCoroutine(ShowdownPhase());
            else
                SettlePot(_foldWinner);   // 폴드 — 시각 쇼다운 없이 정산

            yield return Gap();

            // 라운드 결과 정리 UI → n초 유지 → 다음 라운드
            yield return StartCoroutine(ShowRoundResult());

            yield return StartCoroutine(CleanupPhase());

            if (PlayerStack() <= 0 || OppStack() <= 0) { EndGame(); yield break; }

            _roundIndex++;
            _firstIsPlayer = !_firstIsPlayer;   // 선공 교대
        }
    }

    private WaitForSeconds Gap() => phaseGap > 0f ? new WaitForSeconds(phaseGap) : null;

    // ─────────────────────────────────────────
    //  [1] Setup — 앤티 + 홀카드 + 중앙 5장
    // ─────────────────────────────────────────
    private IEnumerator SetupPhase()
    {
        _playerContrib = 0;
        _oppContrib    = 0;
        _bet.playerAllIn = false;
        _bet.oppAllIn    = false;

        bool cycleReset = (_roundIndex % Mathf.Max(1, roundsPerDeck) == 0);
        bool holeMode = UseHoleSlots();

        // 게임 시작(라운드0) + 덱 초기화(5라운드 주기) 때 셔플 연출
        if (cycleReset && shuffle != null)
            yield return StartCoroutine(shuffle.PlayAndWait());

        System.Collections.Generic.List<Deck.CardPool> playerHole = null, oppHole = null;

        if (holeMode)
        {
            // 풀만 (재)구성. 홀카드용 숫자를 '먼저' 예약 드로우(커뮤니티가 숫자를 다 가져가지 않게)
            if (cycleReset) deck.BuildPool(Random.Range(int.MinValue, int.MaxValue));
            if (deck != null)    deck.ClearCards();
            if (oppDeck != null) oppDeck.ClearCards();
            ClearHoleSlots();

            playerHole = deck.DrawHoleSpecs(deck.drawCount);
            oppHole    = deck.DrawHoleSpecs(deck.drawCount);

            // 판정/쇼다운은 이 스펙으로 (슬롯·손패 상태와 무관 — 타이밍 자유)
            _playerHoleSpecs = new List<Deck.CardPool>(playerHole);
            _oppHoleSpecs    = new List<Deck.CardPool>(oppHole);
        }
        else
        {
            // 폴백: 손패로 바로 분배 (기존 방식)
            if (cycleReset) deck.DrawCards(Random.Range(int.MinValue, int.MaxValue));
            else            deck.RedealHands();

            if (deck != null)    yield return new WaitWhile(() => deck.IsAnimating);
            if (oppDeck != null) yield return new WaitWhile(() => oppDeck.IsAnimating);
        }

        // 1) 중앙 커뮤니티 5장을 덱에서 한 장씩 (뒷면)
        if (randomSlot != null) yield return StartCoroutine(randomSlot.DealCommunityAnimated());

        // 2) 그 다음 홀카드를 한 장씩 각 슬롯으로 (덱 앵커에서 날아옴, 뒷면)
        if (holeMode)
        {
            yield return StartCoroutine(DealHoleAnimated(playerHoleSlots, playerHole));
            yield return StartCoroutine(DealHoleAnimated(oppHoleSlots, oppHole));
            _holeClicksActive = true;   // 이제부터 언제든 클릭해 손패로 가져올 수 있음
        }

        // 앤티 지불
        PayAnte();
    }

    private bool UseHoleSlots() => HasAnySlot(playerHoleSlots) && HasAnySlot(oppHoleSlots);

    private static bool HasAnySlot(Slot[] arr)
    {
        if (arr == null) return false;
        foreach (var s in arr) if (s != null) return true;
        return false;
    }

    private void SetShowdownUiVisible(bool visible)
    {
        if (hideDuringShowdown == null) return;
        foreach (var go in hideDuringShowdown)
            if (go != null) go.SetActive(visible);
    }

    // ─── 홀 슬롯의 뒷면(미공개) 카드 — LeftOver가 '잔여'에 포함시키기 위해 노출 ───
    public int FaceDownHoleCount =>
        CountFaceDown(playerHoleSlots) + CountFaceDown(oppHoleSlots);

    public int[] GetFaceDownHoleCountsByType()
    {
        int[] counts = new int[9];
        AddFaceDownByType(playerHoleSlots, counts);
        AddFaceDownByType(oppHoleSlots,    counts);
        return counts;
    }

    private void AddFaceDownByType(Slot[] arr, int[] counts)
    {
        if (arr == null) return;
        foreach (var s in arr)
        {
            if (s == null || !s.HasCard || !s.IsGuard) continue;
            var cv = s.GetCardValue();
            if (cv == null) continue;
            int idx = cv.isJoker ? 8 : (cv.value >= 1 && cv.value <= 8 ? cv.value - 1 : -1);
            if (idx >= 0) counts[idx]++;
        }
    }

    // 홀 슬롯(뒷면)을 좌클릭하면 앞면으로 뒤집어 손패로 (비차단 — 게임 진행과 무관)
    private IEnumerator RevealHoleToHand(Slot s, bool isPlayer)
    {
        var cv = s.GetCardValue();
        int value = cv != null ? cv.value : 0;
        Vector3 worldPos = s.transform.position;

        if (isPlayer)
        {
            yield return StartCoroutine(s.FlipTo(false, 0.3f));      // 앞면 공개
            if (phaseGap > 0f) yield return new WaitForSeconds(phaseGap);
            s.ClearCard();                                           // 슬롯 카드 제거
            if (deck != null && value > 0) deck.AddCardByValue(value, worldPos);  // 손패로(앞면, 슬롯에서 날아옴)
        }
        else
        {
            if (revealOppHoleCards)
            {
                yield return StartCoroutine(s.FlipTo(false, 0.3f));
                if (phaseGap > 0f) yield return new WaitForSeconds(phaseGap);
            }
            s.ClearCard();
            if (oppDeck != null && value > 0) oppDeck.AddCardByValue(value);       // 상대 손패로(뒷면)
        }
    }

    // 홀 슬롯: 뒷면 카드 수
    private int CountFaceDown(Slot[] arr)
    {
        int n = 0;
        if (arr != null)
            foreach (var s in arr)
                if (s != null && s.HasCard && s.IsGuard) n++;
        return n;
    }

    // 마우스 위치의 뒷면 홀 슬롯
    private Slot HitFaceDownSlot(Slot[] arr)
    {
        Camera cam = Camera.main;
        if (cam == null || arr == null) return null;

        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
        world.z = 0f;

        Collider2D[] hits = Physics2D.OverlapPointAll(world);
        foreach (var hit in hits)
        {
            var slot = hit.GetComponent<Slot>();
            if (slot == null) continue;
            foreach (var hs in arr)
                if (hs == slot && slot.HasCard && slot.IsGuard) return slot;
        }
        return null;
    }

    // 홀 슬롯에 한 장씩 덱 앵커에서 날아오게 배치 (RandomSlot 딜 모션 재사용)
    private IEnumerator DealHoleAnimated(Slot[] holeSlots, System.Collections.Generic.List<Deck.CardPool> specs)
    {
        if (holeSlots == null || specs == null) yield break;
        int n = Mathf.Min(holeSlots.Length, specs.Count);
        for (int i = 0; i < n; i++)
        {
            if (holeSlots[i] == null || specs[i].prefab == null) continue;
            if (randomSlot != null)
                yield return StartCoroutine(randomSlot.DealCardToSlot(holeSlots[i], specs[i]));
            else
                PlaceOneHoleFaceDown(holeSlots[i], specs[i]);
            yield return new WaitForSeconds(0.08f);
        }
    }

    // 단일 홀 카드 즉시 배치 (RandomSlot 없을 때 폴백)
    private void PlaceOneHoleFaceDown(Slot slot, Deck.CardPool spec)
    {
        if (slot == null || spec.prefab == null) return;
        GameObject card = Instantiate(spec.prefab);
        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value     = spec.value;
        cv.isJoker   = spec.isJoker;
        cv.poolIndex = spec.poolIndex;
        slot.PlaceCardFaceDown(card);
    }

    private void ClearHoleSlots()
    {
        if (playerHoleSlots != null)
            foreach (var s in playerHoleSlots) if (s != null) s.ClearCard();
        if (oppHoleSlots != null)
            foreach (var s in oppHoleSlots) if (s != null) s.ClearCard();
    }

    private void PayAnte()
    {
        int pa = Mathf.Min(ante, PlayerStack());
        int oa = Mathf.Min(ante, OppStack());
        CommitToPot(true,  pa);
        CommitToPot(false, oa);
    }

    // ─────────────────────────────────────────
    //  [2] InitialOpen — 선공부터 번갈아 N장 공개
    // ─────────────────────────────────────────
    private IEnumerator InitialOpenPhase()
    {
        int opens = Mathf.Clamp(initialOpenCount, 0,
            randomSlot != null ? randomSlot.FaceDownCount : 0);

        bool actor = _firstIsPlayer;
        for (int i = 0; i < opens; i++)
        {
            yield return StartCoroutine(OpenOneCommunity(actor));
            actor = !actor;
        }
    }

    private IEnumerator OpenOneCommunity(bool isPlayer)
    {
        if (randomSlot == null || randomSlot.FaceDownCount == 0) yield break;

        if (isPlayer)
        {
            // 플레이어가 뒷면 커뮤니티 슬롯을 클릭 (제한시간 끝나면 자동으로 한 장)
            if (enableShotClock && SoundManager.Instance != null) SoundManager.Instance.StartClock();
            float t = shotClockSeconds;

            while (true)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Slot s = HitHiddenCommunitySlot();
                    if (s != null)
                    {
                        if (SoundManager.Instance != null) SoundManager.Instance.StopClock();
                        yield return StartCoroutine(randomSlot.RevealSlot(s));
                        yield break;
                    }
                }

                if (enableShotClock && shotClockSeconds > 0f)
                {
                    t -= Time.deltaTime;
                    if (t <= 0f)
                    {
                        if (SoundManager.Instance != null) SoundManager.Instance.StopClock();
                        Slot s = RandomHiddenCommunitySlot();
                        if (s != null) yield return StartCoroutine(randomSlot.RevealSlot(s));
                        yield break;
                    }
                }
                yield return null;
            }
        }
        else
        {
            if (aiThinkTime > 0f) yield return new WaitForSeconds(aiThinkTime);
            Slot s = RandomHiddenCommunitySlot();
            if (s != null) yield return StartCoroutine(randomSlot.RevealSlot(s));
        }
    }

    private Slot HitHiddenCommunitySlot()
    {
        Camera cam = Camera.main;
        if (cam == null || randomSlot == null) return null;

        Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
        world.z = 0f;

        Collider2D[] hits = Physics2D.OverlapPointAll(world);
        foreach (var hit in hits)
        {
            var slot = hit.GetComponent<Slot>();
            if (slot == null) continue;
            foreach (var cs in randomSlot.CommunitySlots)
                if (cs == slot && slot.HasCard && slot.IsGuard)
                    return slot;
        }
        return null;
    }

    private Slot RandomHiddenCommunitySlot()
    {
        var hidden = new List<Slot>();
        foreach (var s in randomSlot.CommunitySlots)
            if (s != null && s.HasCard && s.IsGuard) hidden.Add(s);
        return hidden.Count > 0 ? hidden[Random.Range(0, hidden.Count)] : null;
    }

    // ─────────────────────────────────────────
    //  [3] Betting — 벳-콜 일치마다 1장씩 공개, 3장 다 열릴 때까지
    // ─────────────────────────────────────────
    private IEnumerator BettingPhase()
    {
        _goToShowdown = true;
        _foldWinner   = 0;

        while (true)
        {
            yield return StartCoroutine(RunStreet());

            switch (_streetResult)
            {
                case StreetResult.PlayerFold:
                    _goToShowdown = false; _foldWinner = 2; yield break;   // 상대 승
                case StreetResult.OppFold:
                    _goToShowdown = false; _foldWinner = 1; yield break;   // 플레이어 승

                case StreetResult.AllInShowdown:
                    if (randomSlot != null) yield return StartCoroutine(randomSlot.RevealAllHidden());
                    yield break;

                case StreetResult.CheckCheck:
                    if (checkCheckForcesShowdown)
                        yield break;   // 미공개 유지 → 쇼다운에서 일괄 공개
                    // 비강제 모드: 무료 카드 1장 공개 후 계속
                    if (randomSlot != null && randomSlot.FaceDownCount > 0)
                        yield return StartCoroutine(randomSlot.RevealNextHidden());
                    break;

                case StreetResult.BetCallMatched:
                    if (randomSlot != null && randomSlot.FaceDownCount > 0)
                        yield return StartCoroutine(randomSlot.RevealNextHidden());
                    break;
            }

            // 더 열 카드가 없으면 쇼다운으로
            if (randomSlot == null || randomSlot.FaceDownCount == 0) yield break;
            // 양쪽 올인이면 더 베팅 불가 → 남은 카드 공개 후 쇼다운
            if (_bet.playerAllIn && _bet.oppAllIn)
            {
                yield return StartCoroutine(randomSlot.RevealAllHidden());
                yield break;
            }
        }
    }

    // 한 스트리트(카드 1장 오픈 단위)의 베팅
    private IEnumerator RunStreet()
    {
        _bet.ResetStreet(minBet);
        bool actor = _firstIsPlayer;
        int consecutiveChecks = 0;

        while (true)
        {
            // 이미 올인한 액터는 행동 불가
            if (_bet.IsAllIn(actor))
            {
                if (_bet.IsAllIn(!actor) || _bet.ToCall(!actor) <= 0)
                {
                    _streetResult = StreetResult.AllInShowdown;
                    yield break;
                }
                actor = !actor;   // 상대가 콜/폴드해야 함
                continue;
            }

            yield return StartCoroutine(GetAction(actor));
            BetAction act = _pendingAction;
            int size = _pendingAmount;

            int toCall = _bet.ToCall(actor);
            int stack  = Stack(actor);

            switch (act)
            {
                case BetAction.Fold:
                    _streetResult = actor ? StreetResult.PlayerFold : StreetResult.OppFold;
                    yield break;

                case BetAction.Check:
                    consecutiveChecks++;
                    if (consecutiveChecks >= 2) { _streetResult = StreetResult.CheckCheck; yield break; }
                    actor = !actor;
                    break;

                case BetAction.Call:
                {
                    int pay = Mathf.Min(toCall, stack);
                    bool allIn = pay >= stack;
                    CommitChips(actor, pay, allIn);
                    _streetResult = (_bet.playerAllIn || _bet.oppAllIn)
                        ? StreetResult.AllInShowdown : StreetResult.BetCallMatched;
                    yield break;
                }

                case BetAction.Bet:
                {
                    int pay = Mathf.Clamp(size, minBet, stack);
                    bool allIn = pay >= stack;
                    CommitChips(actor, pay, allIn);
                    consecutiveChecks = 0;
                    actor = !actor;
                    break;
                }

                case BetAction.Raise:
                {
                    int pay = Mathf.Min(toCall + Mathf.Max(minBet, size), stack);
                    bool allIn = pay >= stack;
                    CommitChips(actor, pay, allIn);
                    consecutiveChecks = 0;
                    actor = !actor;
                    break;
                }

                case BetAction.AllIn:
                {
                    CommitChips(actor, stack, true);
                    consecutiveChecks = 0;
                    // 상대도 이미 콜 끝났으면 쇼다운
                    if (_bet.IsAllIn(!actor) && _bet.ToCall(!actor) <= 0)
                    {
                        _streetResult = StreetResult.AllInShowdown;
                        yield break;
                    }
                    actor = !actor;
                    break;
                }
            }
        }
    }

    // 액터의 액션을 얻는다 (_pendingAction/_pendingAmount에 채움)
    private IEnumerator GetAction(bool isPlayer)
    {
        int toCall = _bet.ToCall(isPlayer);
        int stack  = Stack(isPlayer);

        if (isPlayer)
        {
            _hasPending = false;
            if (bettingUI != null) bettingUI.Show(toCall, stack, Mathf.CeilToInt(pot != null ? pot.Amount : 0));

            if (enableShotClock && shotClockSeconds > 0f)
            {
                if (SoundManager.Instance != null) SoundManager.Instance.StartClock();

                float t = shotClockSeconds;
                while (!_hasPending && t > 0f)
                {
                    t -= Time.deltaTime;
                    if (bettingUI != null) bettingUI.SetTimer(t, shotClockSeconds);
                    yield return null;
                }

                if (SoundManager.Instance != null) SoundManager.Instance.StopClock();

                if (!_hasPending)
                {
                    // 시간 초과 → 콜 금액 있으면 폴드, 없으면 체크
                    _pendingAction = toCall > 0 ? BetAction.Fold : BetAction.Check;
                    _pendingAmount = 0;
                    if (bettingUI != null) bettingUI.Hide();
                }
            }
            else
            {
                yield return new WaitUntil(() => _hasPending);
            }
            // _pendingAction/_pendingAmount 채워짐
        }
        else
        {
            if (aiThinkTime > 0f) yield return new WaitForSeconds(aiThinkTime);

            bool canRaise = stack > toCall;
            var hole = oppDeck != null ? oppDeck.GetHandSpecs() : new List<Deck.CardPool>();
            int amt;
            BetAction a;
            if (bettingAI != null)
                a = bettingAI.Decide(hole, randomSlot != null ? randomSlot.CommunitySlots : null,
                                     toCall, stack, Mathf.CeilToInt(pot != null ? pot.Amount : 0),
                                     canRaise, out amt);
            else { a = toCall > 0 ? BetAction.Call : BetAction.Check; amt = 0; }

            _pendingAction = a;
            _pendingAmount = amt;

            // 상대 액션을 명시적으로 표시 → 플레이어가 보고 판단
            if (bettingUI != null) bettingUI.ShowMessage("상대  " + DescribeAction(a, amt, toCall, stack));
            if (aiActionDisplayTime > 0f) yield return new WaitForSeconds(aiActionDisplayTime);
        }
    }

    // 액션을 사람이 읽을 문구로 (커밋 시 클램프와 동일한 금액 계산)
    private string DescribeAction(BetAction a, int amt, int toCall, int stack)
    {
        switch (a)
        {
            case BetAction.Check: return "체크";
            case BetAction.Fold:  return "폴드";
            case BetAction.Call:  return $"콜 {Mathf.Min(toCall, stack)}";
            case BetAction.Bet:   return $"벳 {Mathf.Clamp(amt, minBet, stack)}";
            case BetAction.Raise: return $"레이즈 {Mathf.Min(toCall + Mathf.Max(minBet, amt), stack)}";
            case BetAction.AllIn: return $"올인 {stack}";
            default:              return a.ToString();
        }
    }

    // ─────────────────────────────────────────
    //  [4] Showdown — 7장 중 숫자 best-5, 승자 팟 회수
    // ─────────────────────────────────────────
    private IEnumerator ShowdownPhase()
    {
        _holeClicksActive = false;

        // 카드의 현재 위치(손패에 들고 있으면 손패, 아니면 홀 슬롯)를 출발점으로 캡처
        // → 덱에서 새로 가져오지 않고 그 자리에서 쇼다운 슬롯으로 자연스럽게 이동
        var playerStarts = GatherStartPositions(UseHoleSlots() ? playerHoleSlots : null,
                                                deck    != null ? deck.SpawnedCards    : null);
        var oppStarts    = GatherStartPositions(UseHoleSlots() ? oppHoleSlots    : null,
                                                oppDeck != null ? oppDeck.SpawnedCards : null);

        // 홀 슬롯 정리 (쇼다운 슬롯과 중복 방지) — 위치는 이미 캡처함
        ClearHoleSlots();

        // 홀카드는 저장된 스펙 사용 → 손패로 가져왔든 안 가져왔든 판정 동일
        var playerSpecs = UseHoleSlots()
            ? new List<Deck.CardPool>(_playerHoleSpecs)
            : (deck    != null ? deck.GetHandSpecs()    : new List<Deck.CardPool>());
        var oppSpecs    = UseHoleSlots()
            ? new List<Deck.CardPool>(_oppHoleSpecs)
            : (oppDeck != null ? oppDeck.GetHandSpecs() : new List<Deck.CardPool>());

        int[] playerNums = BuildNumberHand(playerSpecs);
        int[] oppNums    = BuildNumberHand(oppSpecs);

        float pScore = gameFlow.EvaluateBestOfSeven(playerNums, out string pRule);
        float oScore = gameFlow.EvaluateBestOfSeven(oppNums,    out string oRule);

        int winner = pScore > oScore ? 1 : (oScore > pScore ? 2 : 0);

        // 줌 동안 깨지는 UI 끄기
        SetShowdownUiVisible(false);

        // 시각 쇼다운 연출 — 손패 제거+슬롯 배치, 커뮤니티 한 장씩 줌,
        // 내 패→상대 패 순으로 마지막 카드에서 결과 fade-in/유지/off
        if (randomSlot != null)
            yield return StartCoroutine(randomSlot.RunShowdown(
                playerSpecs, oppSpecs, resultUI, pRule, pScore, oRule, oScore,
                resultHoldSeconds, deck, oppDeck, playerStarts, oppStarts));

        // 카메라 복귀 후 UI 다시 켜기
        SetShowdownUiVisible(true);

        SettlePot(winner);

        if (resultUI != null)
            yield return StartCoroutine(resultUI.FadeOut());
    }

    // 카드의 현재 월드 위치 수집 (홀 슬롯의 뒷면 카드 + 손패로 가져온 카드) — 쇼다운 출발점
    private List<Vector3> GatherStartPositions(Slot[] holeSlots, IReadOnlyList<GameObject> handCards)
    {
        var pos = new List<Vector3>();
        if (holeSlots != null)
            foreach (var s in holeSlots)
                if (s != null && s.HasCard)
                {
                    var c = s.GetPlacedCard();
                    if (c != null) pos.Add(c.transform.position);
                }
        if (handCards != null)
            foreach (var c in handCards)
                if (c != null) pos.Add(c.transform.position);
        return pos;
    }

    // 홀카드 + 모든 커뮤니티(공개 여부 무관)에서 조커 제외 숫자 목록
    private int[] BuildNumberHand(List<Deck.CardPool> hole)
    {
        var nums = new List<int>();
        if (hole != null)
            foreach (var c in hole)
                if (!c.isJoker && c.value >= 1 && c.value <= 8) nums.Add(c.value);

        if (randomSlot != null)
            foreach (var s in randomSlot.CommunitySlots)
            {
                if (s == null || !s.HasCard) continue;
                var cv = s.GetCardValue();
                if (cv != null && !cv.isJoker && cv.value >= 1 && cv.value <= 8)
                    nums.Add(cv.value);
            }
        return nums.ToArray();
    }

    // ─────────────────────────────────────────
    //  라운드 결과 정리 UI → n초 유지 → 다음 라운드
    // ─────────────────────────────────────────
    private IEnumerator ShowRoundResult()
    {
        if (resultUI != null)
        {
            string label = _lastRoundWinner == 1 ? "YOU WIN"
                         : _lastRoundWinner == 2 ? "YOU LOSE"
                         : "DRAW";
            yield return StartCoroutine(resultUI.ShowResult(label, _lastPotWon));
        }

        if (roundResultSeconds > 0f) yield return new WaitForSeconds(roundResultSeconds);

        if (resultUI != null) yield return StartCoroutine(resultUI.FadeOut());
    }

    // ─────────────────────────────────────────
    //  [5] Cleanup — 손패/커뮤니티/쇼다운 슬롯 정리
    // ─────────────────────────────────────────
    private IEnumerator CleanupPhase()
    {
        _holeClicksActive = false;
        if (deck != null)    deck.ClearCards();
        if (oppDeck != null) oppDeck.ClearCards();
        if (randomSlot != null) randomSlot.ClearShowdownSlots();
        ClearHoleSlots();
        if (bettingUI != null)  bettingUI.Hide();
        yield break;
    }

    // ─────────────────────────────────────────
    //  팟 정산 (환불 + 승자 지급)  winner: 0=분배, 1=플레이어, 2=상대
    // ─────────────────────────────────────────
    private void SettlePot(int winner)
    {
        if (pot == null || hp == null) return;

        // 콜되지 않은 초과분 환불 (한쪽이 더 많이 넣고 상대가 올인으로 다 못 받은 경우)
        int excess = Mathf.Abs(_playerContrib - _oppContrib);
        float total = pot.Take();

        if (excess > 0)
        {
            if (_playerContrib > _oppContrib) hp.HealPlayer(excess);
            else                              hp.HealOpp(excess);
            total -= excess;
        }

        if (winner == 1)      hp.HealPlayer(total);
        else if (winner == 2) hp.HealOpp(total);
        else { hp.HealPlayer(total * 0.5f); hp.HealOpp(total * 0.5f); }

        if (total > 0f && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.heal);

        _lastRoundWinner = winner;
        _lastPotWon      = total;
    }

    // ─────────────────────────────────────────
    //  칩 투입 (LP 차감 + 팟 적립 + 베팅상태/기여 기록)
    // ─────────────────────────────────────────
    private void CommitChips(bool isPlayer, int amount, bool allIn)
    {
        CommitToPot(isPlayer, amount);
        _bet.Commit(isPlayer, amount, allIn);

        if (allIn)
        {
            Juicer.Instance?.AllInImpact();   // 흔들림 + tensionShake 사운드 포함
            if (bettingUI != null) bettingUI.ShowMessage((isPlayer ? "나" : "상대") + " 올인!");
        }
    }

    private void CommitToPot(bool isPlayer, int amount)
    {
        if (amount <= 0) return;
        if (isPlayer) { hp.DamagePlayer(amount); _playerContrib += amount; }
        else          { hp.DamageOpp(amount);    _oppContrib    += amount; }
        if (pot != null) pot.Add(amount);
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardPlace, 0.7f);   // 칩 투입
    }

    // ─────────────────────────────────────────
    //  게임 종료
    // ─────────────────────────────────────────
    private void EndGame()
    {
        _gameOver = true;
        if (bettingUI != null) bettingUI.Hide();

        bool playerWins = PlayerStack() > 0;
        Debug.Log($"[RoundDirector] 게임 종료 — {(playerWins ? "플레이어" : "상대")} 승리");

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(playerWins ? SoundManager.Instance.victory
                                                     : SoundManager.Instance.defeat);

        if (resultUI != null)
            StartCoroutine(resultUI.ShowResult(playerWins ? "YOU WIN" : "YOU LOSE", 0f));
    }

    private int Stack(bool isPlayer) => isPlayer ? PlayerStack() : OppStack();
    private int PlayerStack() => hp != null ? Mathf.FloorToInt(hp.PlayerHP) : 0;
    private int OppStack()    => hp != null ? Mathf.FloorToInt(hp.OppHP)    : 0;
}
