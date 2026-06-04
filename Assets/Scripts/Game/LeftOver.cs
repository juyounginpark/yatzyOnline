using System.Text;
using UnityEngine;
using TMPro;

// ─────────────────────────────────────────────
//  LeftOver
//  - 아직 공개되지 않은(플레이어가 모르는) 카드 수를 TMP로 실시간 표시
//  - 잔여 = 덱 풀 + 상대 손패(뒷면) + RandomSlot 뒷면 카드
//    → 앞면으로 공개된 카드만 소진으로 계산 (뒷면·상대 카드는 잔여에 포함)
//  - 형식:
//      잔여카드
//      1: n장
//      ...
//      8: n장
//      J: n장
// ─────────────────────────────────────────────
public class LeftOver : MonoBehaviour
{
    [Header("─ 참조 (비우면 자동 탐색) ─")]
    [Tooltip("덱 풀 잔여")]
    public Deck deck;

    [Tooltip("상대 손패 (뒷면이라 잔여에 포함)")]
    public OppDeck oppDeck;

    [Tooltip("RandomSlot 뒷면 카드 (공개되면 제외)")]
    public RandomSlot randomSlot;

    [Tooltip("홀 슬롯 뒷면 카드 포함용 (앞면으로 확인 전까지 잔여에 포함)")]
    public RoundDirector director;

    [Tooltip("표시할 TMP 텍스트")]
    public TMP_Text label;

    private int _lastTotal = -1;
    private readonly StringBuilder _sb = new StringBuilder(96);

    void Start()
    {
        if (deck == null)       deck = FindObjectOfType<Deck>();
        if (oppDeck == null)    oppDeck = FindObjectOfType<OppDeck>();
        if (randomSlot == null) randomSlot = FindObjectOfType<RandomSlot>();
        if (director == null)   director = FindObjectOfType<RoundDirector>();
        Refresh();
    }

    void Update()
    {
        // 잔여 총합이 바뀔 때만 갱신 (카드가 앞면으로 공개되거나 플레이어가 뽑을 때)
        if (CurrentTotal() != _lastTotal) Refresh();
    }

    // 덱 풀 + 상대 손패 + RandomSlot 뒷면 = 미공개 카드 총합
    private int CurrentTotal()
    {
        int total = deck != null ? deck.RemainingInPile : 0;
        if (oppDeck != null)    total += oppDeck.SpawnedCards.Count;
        if (randomSlot != null) total += randomSlot.FaceDownCount;
        if (director != null)   total += director.FaceDownHoleCount;   // 홀 슬롯 뒷면(미확인)
        return total;
    }

    public void Refresh()
    {
        if (label == null) return;

        int[] counts = deck != null ? deck.GetRemainingCountsByType() : new int[9];

        if (oppDeck != null)
        {
            int[] opp = oppDeck.GetHandCountsByType();
            for (int i = 0; i < 9; i++) counts[i] += opp[i];
        }
        if (randomSlot != null)
        {
            int[] hidden = randomSlot.GetFaceDownCountsByType();
            for (int i = 0; i < 9; i++) counts[i] += hidden[i];
        }
        if (director != null)
        {
            int[] hole = director.GetFaceDownHoleCountsByType();   // 앞면 확인 전엔 잔여로 포함
            for (int i = 0; i < 9; i++) counts[i] += hole[i];
        }

        _lastTotal = CurrentTotal();

        _sb.Clear();
        _sb.Append("잔여카드");
        for (int i = 0; i < 8; i++)
            _sb.Append('\n').Append(i + 1).Append(": ").Append(counts[i]).Append('장');
        _sb.Append('\n').Append("J: ").Append(counts[8]).Append('장');

        label.text = _sb.ToString();
    }
}
