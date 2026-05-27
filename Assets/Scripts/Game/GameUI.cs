using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("─ UI 참조 ─")]
    public TextMeshProUGUI scoreText;

    [Header("─ 참조 ─")]
    public GameFlow gameFlow;
    public MainFlow mainFlow;

    // 외부에서 scoreText를 직접 제어할 때 true로 설정
    // → Update()가 scoreText를 덮어쓰지 않음
    [HideInInspector]
    public bool isScoreOverridden;

    private Color _originalColor;

    void Start()
    {
        if (gameFlow == null)
            gameFlow = FindObjectOfType<GameFlow>();

        if (mainFlow == null)
            mainFlow = FindObjectOfType<MainFlow>();

        if (scoreText != null)
            _originalColor = scoreText.color;

        if (scoreText != null)
            scoreText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (gameFlow == null || scoreText == null) return;

        // 외부 오버라이드, 또는 전환 중이면 Update에서 건드리지 않음
        if (isScoreOverridden) return;
        if (mainFlow != null && mainFlow.IsTransitioning) return;

        bool show = gameFlow.CurrentBestScore > 0;
        scoreText.gameObject.SetActive(show);
        scoreText.color = _originalColor;

        if (show)
            scoreText.text = $"+{gameFlow.CurrentBestScore:F1}\n({gameFlow.CurrentBestRule})";
    }
}
