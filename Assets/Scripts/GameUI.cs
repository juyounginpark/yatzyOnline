using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("─ UI 참조 ─")]
    public TextMeshProUGUI scoreText;

    [Tooltip("방어 중일 때 방어 점수(숫자만) 표시")]
    public TextMeshProUGUI defenseScoreText;

    [Header("─ 방어 점수 애니메이션 설정 ─")]
    [Tooltip("애니메이션 시작 전 대기 시간")]
    public float defenseAnimDelay = 0.5f;

    [Tooltip("점수 UI가 방어 점수 UI 위치로 이동하는 시간")]
    public float defenseAnimDuration = 0.5f;

    [Header("─ 참조 ─")]
    public GameFlow gameFlow;
    public MainFlow mainFlow;
    public Slot[] oppSlots;

    // 외부에서 scoreText를 직접 제어할 때 true로 설정
    // → Update()가 scoreText를 덮어쓰지 않음
    [HideInInspector]
    public bool isScoreOverridden;

    // 방어 점수 애니메이션 중에는 Update가 scoreText를 건드리지 않음
    [HideInInspector]
    public bool isDefenseAnimating;

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

        if (defenseScoreText != null)
            defenseScoreText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (gameFlow == null || scoreText == null) return;

        UpdateDefenseScoreText();

        // 외부 오버라이드, 방어 애니메이션, 또는 턴 전환 중이면 Update에서 건드리지 않음
        if (isScoreOverridden || isDefenseAnimating) return;
        if (mainFlow != null && mainFlow.IsTransitioning) return;

        bool isPlayerTurn = mainFlow == null || mainFlow.IsPlayerTurn;

        if (!isPlayerTurn)
        {
            // 상대 턴: 상대 슬롯 조합 점수 표시 (최소 1초 딜레이)
            if (oppSlots != null)
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
                scoreText.gameObject.SetActive(false);
            }
        }
        else
        {
            _oppScoreDelay = 0f;
            _oppScoreReady = false;
            // 플레이어 턴: 방어 모드 또는 공격 점수 표시
            if (gameFlow.HasDefenseCards)
            {
                bool show = gameFlow.CurrentDefenseScore > 0;
                scoreText.gameObject.SetActive(show);
                scoreText.color = Color.black;

                if (show)
                    scoreText.text = $"+{gameFlow.CurrentDefenseScore:F1}\n({gameFlow.CurrentDefenseRule})";
            }
            else
            {
                bool show = gameFlow.CurrentBestScore > 0;
                scoreText.gameObject.SetActive(show);
                scoreText.color = _originalColor;

                if (show)
                    scoreText.text = $"+{gameFlow.CurrentBestScore:F1}\n({gameFlow.CurrentBestRule})";
            }
        }
    }

    private void UpdateDefenseScoreText()
    {
        if (defenseScoreText == null) return;
        if (isDefenseAnimating) return;
        if (mainFlow != null && mainFlow.IsTransitioning) return;

        bool isPlayerTurn = mainFlow == null || mainFlow.IsPlayerTurn;

        // 플레이어 턴에는 defenseScoreText 숨김 (scoreText가 방어 점수 표시)
        if (isPlayerTurn)
        {
            defenseScoreText.gameObject.SetActive(false);
            return;
        }

        bool show = gameFlow.HasDefenseCards && gameFlow.CurrentDefenseScore > 0;
        defenseScoreText.gameObject.SetActive(show);
        if (show)
            defenseScoreText.text = $"{gameFlow.CurrentDefenseScore:F1}";
    }
}
