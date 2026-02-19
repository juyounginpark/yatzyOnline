using UnityEngine;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("─ UI 참조 ─")]
    public TextMeshProUGUI scoreText;

    [Header("─ 참조 ─")]
    public GameFlow gameFlow;

    void Start()
    {
        if (gameFlow == null)
            gameFlow = FindObjectOfType<GameFlow>();

        scoreText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (gameFlow == null || scoreText == null) return;

        bool show = gameFlow.CurrentBestScore > 0;
        scoreText.gameObject.SetActive(show);

        if (show)
            scoreText.text = $"+{gameFlow.CurrentBestScore:F1}\n({gameFlow.CurrentBestRule})";
    }
}
