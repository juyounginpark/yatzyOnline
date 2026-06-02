using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("─ UI 참조 ─")]
    public TextMeshProUGUI scoreText;

    [Header("─ 참조 ─")]
    public GameFlow gameFlow;
    public MainFlow mainFlow;
    public Slot[] oppSlots;

    // 외부에서 scoreText를 직접 제어할 때 true로 설정
    // → Update()가 scoreText를 덮어쓰지 않음
    [HideInInspector]
    public bool isScoreOverridden;

    private Color _originalColor;
    private float _oppScoreDelay;
    private bool _oppScoreReady;

    void Start()
    {
        if (gameFlow == null)
            gameFlow = FindObjectOfType<GameFlow>();

        if (mainFlow == null)
            mainFlow = FindObjectOfType<MainFlow>();

        if (oppSlots == null && mainFlow != null)
            oppSlots = mainFlow.oppSlots;

        if (scoreText != null)
            _originalColor = scoreText.color;

        scoreText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (gameFlow == null || scoreText == null) return;

        // 외부 오버라이드, 또는 턴 전환 중이면 Update에서 건드리지 않음
        if (isScoreOverridden) return;
        if (mainFlow != null && mainFlow.IsTransitioning) return;

        bool isPlayerTurn = mainFlow == null || mainFlow.IsPlayerTurn;

        if (!isPlayerTurn)
        {
            // 상대가 방어(Guard) 상태면 점수를 숨김 — 뒷면 카드의 점수가 노출되지 않도록
            bool oppGuarding = false;
            if (oppSlots != null)
                foreach (var s in oppSlots)
                    if (s != null && s.HasCard && s.IsGuard) { oppGuarding = true; break; }

            // 상대 턴: 상대 슬롯 조합 점수 표시 (최소 1초 딜레이)
            if (oppSlots != null && !oppGuarding)
            {
                string oppRule;
                float oppScore;
                gameFlow.GetBestCombo(oppSlots, out oppRule, out oppScore);

                if (oppScore > 0)
                {
                    if (!_oppScoreReady)
                    {
                        _oppScoreDelay += Time.deltaTime;
                        if (_oppScoreDelay >= 1f)
                            _oppScoreReady = true;
                    }

                    if (_oppScoreReady)
                    {
                        scoreText.gameObject.SetActive(true);
                        scoreText.color = _originalColor;
                        scoreText.text = $"+{oppScore:F1}\n({oppRule})";
                    }
                    else
                    {
                        scoreText.gameObject.SetActive(false);
                    }
                }
                else
                {
                    scoreText.gameObject.SetActive(false);
                    _oppScoreDelay = 0f;
                    _oppScoreReady = false;
                }
            }
            else
            {
                // oppSlots 없음 또는 상대 방어 상태 → 숨김
                scoreText.gameObject.SetActive(false);
                _oppScoreDelay = 0f;
                _oppScoreReady = false;
            }
        }
        else
        {
            _oppScoreDelay = 0f;
            _oppScoreReady = false;
            // 플레이어 턴: 스코어 표시
            bool show = gameFlow.CurrentBestScore > 0;
            scoreText.gameObject.SetActive(show);
            scoreText.color = _originalColor;

            if (show)
                scoreText.text = $"+{gameFlow.CurrentBestScore:F1}\n({gameFlow.CurrentBestRule})";
        }
    }
}
