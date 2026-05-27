using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  싱글플레이 / 타임어택 메인 플로우
//
//  플레이어가 5개 슬롯에 카드를 채워 EndTurn → 점수 평가 → 누적 → 다음 라운드.
//  전체 게임 타이머가 0에 도달하면 종료.
// ─────────────────────────────────────────────
public class MainFlow : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public Deck   deck;
    public Slot[] playerSlots;

    [Header("─ UI ─")]
    public Button   endTurnButton;
    public TMP_Text endTurnButtonText;
    public Button   sortByNumButton;
    public Button   sortByTypeButton;
    public TMP_Text totalScoreText;

    [Header("─ UI 캔버스 ─")]
    public Canvas uiCanvas;

    [Header("─ 참조 (점수 표시용) ─")]
    public GameFlow gameFlow;
    public GameUI   gameUI;
    public Exp      exp;
    public Roulette roulette;

    [Header("─ 슬롯 리롤 ─")]
    public GameObject rerollImagePrefab;
    public GameObject rerollCountObject;
    public int        maxSlotRerolls = 2;

    [Header("─ 타임어택 설정 ─")]
    [Tooltip("전체 게임 제한 시간(초)")]
    public float gameDuration = 120f;

    [Header("─ 라운드 점수 표시 ─")]
    public float scoreDisplayTime = 1f;

    // ─── 상태 ───
    private float _timer;
    private float _totalScore;
    private bool  _isTransitioning;
    private bool  _isGameOver;

    // ─── 서브시스템 ───
    private SlotRerollHandler _reroll;

    // ─── 공개 프로퍼티 ───
    public bool  IsPlayerTurn    => !_isGameOver;
    public float TimeRemaining   => Mathf.Max(0f, _timer);
    public bool  IsTransitioning => _isTransitioning;
    public bool  IsRouletteActive => roulette != null && roulette.IsSpinning;
    public bool  IsGameOver      => _isGameOver;
    public float TotalScore      => _totalScore;

    void Start()
    {
        _timer = gameDuration;
        _totalScore = 0f;

        _reroll = new SlotRerollHandler(this, playerSlots, deck, gameFlow,
            rerollImagePrefab, maxSlotRerolls);

        if (endTurnButton != null)
            endTurnButton.onClick.AddListener(EndTurn);
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

        UpdateTotalScoreText();
        UpdateInteraction();
    }

    void Update()
    {
        if (_isGameOver) return;

        bool pauseTimer = _isTransitioning || IsRouletteActive;
        if (!pauseTimer) _timer -= Time.deltaTime;

        if (endTurnButtonText != null)
            endTurnButtonText.text = Mathf.CeilToInt(Mathf.Max(0f, _timer)).ToString();

        if (_isTransitioning) return;

        if (_timer <= 0f)
        {
            EndGame();
            return;
        }

        if (!_reroll.IsRerolling) _reroll.Tick();
    }

    // ─────────────────────────────────────────
    //  턴 종료 (버튼 클릭)
    // ─────────────────────────────────────────
    public void EndTurn()
    {
        if (_isTransitioning || _isGameOver || IsRouletteActive) return;
        if (endTurnButton != null) endTurnButton.interactable = false;
        StartCoroutine(DoEndTurn());
    }

    private IEnumerator DoEndTurn()
    {
        _isTransitioning = true;

        // 1) 점수 평가
        string comboName;
        float turnScore = EvaluateSlots(playerSlots, out comboName);

        // 2) 점수 UI 표시
        if (turnScore > 0f)
            yield return StartCoroutine(ShowScoreUI(turnScore, comboName));

        // 3) 조커 최적 해석
        ResolveJokers(playerSlots);

        // 4) 누적 점수 반영
        if (turnScore > 0f)
        {
            _totalScore += turnScore;
            UpdateTotalScoreText();
        }

        // 5) 슬롯 비우기 (카드 파괴)
        ClearPlayerSlots();

        // 6) 라운드 리셋
        foreach (var gui in FindObjectsOfType<GameUI>())
            gui.isScoreOverridden = false;

        DrawCards();
        UpdateInteraction();

        // 7) EXP + 룰렛
        int savedExp = (exp != null && turnScore > 0f)
            ? Mathf.RoundToInt(turnScore) : 0;
        if (savedExp > 0)
        {
            System.Func<IEnumerator> levelUpCb = roulette != null
                ? () => roulette.SpinAndReward()
                : (System.Func<IEnumerator>)null;
            yield return StartCoroutine(exp.AddExpAnimated(savedExp, levelUpCb));
        }

        _isTransitioning = false;
    }

    private void EndGame()
    {
        _isGameOver = true;
        _isTransitioning = false;

        if (endTurnButton != null)
        {
            endTurnButton.interactable = false;
            endTurnButton.gameObject.SetActive(false);
        }
        if (endTurnButtonText != null)
            endTurnButtonText.text = "0";

        if (deck != null) deck.canPlaceInSlot = false;

        Debug.Log($"[MainFlow] 게임 종료 — 총점 {_totalScore:F1}");
    }

    // ─────────────────────────────────────────
    //  헬퍼
    // ─────────────────────────────────────────

    private float EvaluateSlots(Slot[] slots, out string ruleName)
    {
        ruleName = "";
        if (gameFlow == null || slots == null) return 0f;
        gameFlow.GetBestCombo(slots, out ruleName, out float comboScore);
        return comboScore;
    }

    private IEnumerator ShowScoreUI(float turnScore, string comboName)
    {
        if (turnScore <= 0f || gameUI == null || gameUI.scoreText == null)
            yield break;

        gameUI.isScoreOverridden = true;
        gameUI.scoreText.gameObject.SetActive(true);
        gameUI.scoreText.text = $"+{turnScore:F1}\n({comboName})";

        float waited = 0f;
        while (waited < scoreDisplayTime)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        gameUI.scoreText.gameObject.SetActive(false);
    }

    private void ResolveJokers(Slot[] slots)
    {
        if (slots == null || gameFlow == null) return;
        gameFlow.GetBestCombo(slots, out _, out _, out int[] resolvedValues);
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null || !slots[i].HasCard) continue;
            var cv = slots[i].GetCardValue();
            if (cv != null && cv.isJoker
                && resolvedValues != null && i < resolvedValues.Length)
                cv.value = resolvedValues[i];
        }
    }

    private void ClearPlayerSlots()
    {
        if (playerSlots == null) return;
        foreach (var slot in playerSlots)
        {
            if (slot != null && slot.HasCard)
                slot.ClearCard();
        }
    }

    private void DrawCards()
    {
        int cardsInPlayerSlots = 0;
        if (playerSlots != null)
            foreach (var s in playerSlots)
                if (s != null && s.HasCard) cardsInPlayerSlots++;

        int playerDraw = Mathf.Max(0, 6 - deck.SpawnedCards.Count - cardsInPlayerSlots);
        for (int i = 0; i < playerDraw; i++)
            deck.AddOneCard();
    }

    private void UpdateInteraction()
    {
        if (deck != null) deck.canPlaceInSlot = !_isGameOver;

        if (endTurnButton != null)
        {
            endTurnButton.gameObject.SetActive(!_isGameOver);
            endTurnButton.interactable = !_isGameOver;
        }

        if (!_isGameOver)
            _reroll.OnNewTurn();
    }

    private void UpdateTotalScoreText()
    {
        if (totalScoreText != null)
            totalScoreText.text = Mathf.RoundToInt(_totalScore).ToString();
    }
}
