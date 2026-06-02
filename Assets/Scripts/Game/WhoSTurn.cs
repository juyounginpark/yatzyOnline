using UnityEngine;
using TMPro;

// ─────────────────────────────────────────────
//  현재 누구의 공격 턴인지 표시
//  - 공격 턴 = 라운드의 선공 플레이어 (MainFlow.IsPlayerAttackTurn)
//  - 내 공격: "Your Attack Turn" (빨강)
//  - 상대 공격: "Opp's Attack Turn" (파랑)
// ─────────────────────────────────────────────
public class WhoSTurn : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public MainFlow mainFlow;

    [Tooltip("턴을 표시할 TMP 텍스트")]
    public TMP_Text turnText;

    [Header("─ 텍스트 ─")]
    public string myAttackText  = "Your Attack Turn";
    public string oppAttackText = "Opp's Attack Turn";

    [Header("─ 색상 ─")]
    public Color myAttackColor  = Color.red;
    public Color oppAttackColor = Color.blue;

    private bool _initialized;
    private bool _lastIsPlayerAttack;

    void Start()
    {
        if (mainFlow == null) mainFlow = FindObjectOfType<MainFlow>();
        Refresh(force: true);
    }

    void Update()
    {
        Refresh(force: false);
    }

    private void Refresh(bool force)
    {
        if (mainFlow == null || turnText == null) return;

        bool isPlayerAttack = mainFlow.IsPlayerAttackTurn;
        if (!force && _initialized && isPlayerAttack == _lastIsPlayerAttack) return;

        _initialized = true;
        _lastIsPlayerAttack = isPlayerAttack;

        if (isPlayerAttack)
        {
            turnText.text  = myAttackText;
            turnText.color = myAttackColor;
        }
        else
        {
            turnText.text  = oppAttackText;
            turnText.color = oppAttackColor;
        }
    }
}
