using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameFlow : MonoBehaviour
{
    [Header("─ 슬롯 (5개) ─")]
    public Slot[] slots = new Slot[5];

    private int _lastFilledCount = -1;

    void Update()
    {
        int filled = CountFilledSlots();
        if (filled != _lastFilledCount)
        {
            _lastFilledCount = filled;
            if (filled > 0)
                EvaluateAndLog();
        }
    }

    private int CountFilledSlots()
    {
        int count = 0;
        foreach (var slot in slots)
            if (slot != null && slot.HasCard) count++;
        return count;
    }

    private List<int> GetSlotValues()
    {
        List<int> values = new List<int>();
        foreach (var slot in slots)
        {
            if (slot == null || !slot.HasCard) continue;
            var cv = slot.GetCardValue();
            if (cv != null)
                values.Add(cv.value);
        }
        return values;
    }

    // ─────────────────────────────────────────
    //  모든 룰 평가 → 최고 점수 룰만 표시
    // ─────────────────────────────────────────
    private void EvaluateAndLog()
    {
        List<int> values = GetSlotValues();
        if (values.Count == 0) return;

        int[] dice = values.ToArray();
        System.Array.Sort(dice);

        string bestName = "없음";
        int bestScore = 0;

        CheckRule("Ones",             SumOf(dice, 1),      ref bestName, ref bestScore);
        CheckRule("Twos",             SumOf(dice, 2),      ref bestName, ref bestScore);
        CheckRule("Threes",           SumOf(dice, 3),      ref bestName, ref bestScore);
        CheckRule("Fours",            SumOf(dice, 4),      ref bestName, ref bestScore);
        CheckRule("Fives",            SumOf(dice, 5),      ref bestName, ref bestScore);
        CheckRule("Sixes",            SumOf(dice, 6),      ref bestName, ref bestScore);
        CheckRule("Three of a Kind",  ThreeOfAKind(dice),  ref bestName, ref bestScore);
        CheckRule("Four of a Kind",   FourOfAKind(dice),   ref bestName, ref bestScore);
        CheckRule("Full House",       FullHouse(dice),     ref bestName, ref bestScore);
        CheckRule("Small Straight",   SmallStraight(dice), ref bestName, ref bestScore);
        CheckRule("Large Straight",   LargeStraight(dice), ref bestName, ref bestScore);
        CheckRule("Yacht",            Yacht(dice),         ref bestName, ref bestScore);

        string diceStr = string.Join(", ", dice);
        Debug.Log($"[GameFlow] 카드: [{diceStr}] → 최고: {bestName} ({bestScore}점)");
    }

    private void CheckRule(string name, int score, ref string bestName, ref int bestScore)
    {
        if (score > bestScore)
        {
            bestScore = score;
            bestName = name;
        }
    }

    // ─────────────────────────────────────────
    //  스코어링 함수들
    // ─────────────────────────────────────────

    private int SumOf(int[] dice, int num)
    {
        int sum = 0;
        foreach (int d in dice)
            if (d == num) sum += d;
        return sum;
    }

    private int[] CountDice(int[] dice)
    {
        int[] counts = new int[7];
        foreach (int d in dice)
            counts[d]++;
        return counts;
    }

    private int ThreeOfAKind(int[] dice)
    {
        int[] counts = CountDice(dice);
        foreach (int c in counts)
            if (c >= 3) return dice.Sum();
        return 0;
    }

    private int FourOfAKind(int[] dice)
    {
        int[] counts = CountDice(dice);
        foreach (int c in counts)
            if (c >= 4) return dice.Sum();
        return 0;
    }

    private int FullHouse(int[] dice)
    {
        int[] counts = CountDice(dice);
        bool hasThree = false, hasTwo = false;
        foreach (int c in counts)
        {
            if (c == 3) hasThree = true;
            if (c == 2) hasTwo = true;
        }
        return (hasThree && hasTwo) ? 25 : 0;
    }

    private int SmallStraight(int[] dice)
    {
        HashSet<int> unique = new HashSet<int>(dice);
        if (unique.Contains(1) && unique.Contains(2) && unique.Contains(3) && unique.Contains(4)) return 30;
        if (unique.Contains(2) && unique.Contains(3) && unique.Contains(4) && unique.Contains(5)) return 30;
        if (unique.Contains(3) && unique.Contains(4) && unique.Contains(5) && unique.Contains(6)) return 30;
        return 0;
    }

    private int LargeStraight(int[] dice)
    {
        if (dice.Length < 5) return 0;
        HashSet<int> unique = new HashSet<int>(dice);
        if (unique.Count >= 5)
        {
            if (unique.Contains(1) && unique.Contains(2) && unique.Contains(3) && unique.Contains(4) && unique.Contains(5)) return 40;
            if (unique.Contains(2) && unique.Contains(3) && unique.Contains(4) && unique.Contains(5) && unique.Contains(6)) return 40;
        }
        return 0;
    }

    private int Yacht(int[] dice)
    {
        if (dice.Length < 5) return 0;
        int[] counts = CountDice(dice);
        foreach (int c in counts)
            if (c >= 5) return 50;
        return 0;
    }
}
