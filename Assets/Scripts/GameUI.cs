using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("─ UI 참조 ─")]
    public TextMeshProUGUI scoreText;

    [Header("─ 참조 ─")]
    public GameFlow gameFlow;

    // 외부에서 scoreText를 직접 제어할 때 true로 설정
    // → Update()가 scoreText를 덮어쓰지 않음
    [HideInInspector]
    public bool isScoreOverridden;

    void Start()
    {
        if (gameFlow == null)
            gameFlow = FindObjectOfType<GameFlow>();

        scoreText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (gameFlow == null || scoreText == null) return;

        // 외부 오버라이드 중이면 Update에서 건드리지 않음
        if (isScoreOverridden) return;

        bool show = gameFlow.CurrentBestScore > 0;
        scoreText.gameObject.SetActive(show);

        if (show)
            scoreText.text = $"+{gameFlow.CurrentBestScore:F1}\n({gameFlow.CurrentBestRule})";
    }
}
