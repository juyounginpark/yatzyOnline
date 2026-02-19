using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameFlow : MonoBehaviour
{
    [Header("─ 슬롯 (5개) ─")]
    public Slot[] slots = new Slot[5];

    [Header("─ 이펙트 이미지 ─")]
    [Tooltip("호버/홀드 중인 카드에 표시")]
    public Sprite effectHover;

    [Tooltip("조합에 기여하는 슬롯 카드에 표시")]
    public Sprite effectContribute;

    [Header("─ 참조 ─")]
    public Deck deck;

    // ─── 이펙트 오브젝트 ───
    private GameObject _hoverEffect;
    private readonly List<GameObject> _slotEffects = new List<GameObject>();
    private int _lastFilledCount = -1;
    private GameObject _lastEffectTarget;

    void Start()
    {
        if (deck == null)
            deck = FindObjectOfType<Deck>();

        // 슬롯 이펙트 오브젝트 미리 생성
        for (int i = 0; i < slots.Length; i++)
        {
            var fx = CreateEffectObject($"SlotEffect_{i}", effectContribute);
            fx.SetActive(false);
            _slotEffects.Add(fx);
        }

        // 호버 이펙트 오브젝트
        _hoverEffect = CreateEffectObject("HoverEffect", effectHover);
        _hoverEffect.SetActive(false);
    }

    void Update()
    {
        UpdateHoverEffect();
        UpdateSlotEffects();
    }

    // ─────────────────────────────────────────
    //  호버/홀드 이펙트 (덱 카드)
    // ─────────────────────────────────────────
    private void UpdateHoverEffect()
    {
        if (deck == null || effectHover == null)
        {
            if (_hoverEffect != null) _hoverEffect.SetActive(false);
            return;
        }

        // 드래그 중이면 드래그 카드, 아니면 호버 카드
        GameObject target = deck.DraggedCard ?? deck.HoveredCard;

        if (target != null)
        {
            _hoverEffect.SetActive(true);
            AttachEffect(_hoverEffect, target, effectHover, 200);
        }
        else
        {
            _hoverEffect.SetActive(false);
        }

        _lastEffectTarget = target;
    }

    // ─────────────────────────────────────────
    //  슬롯 조합 기여 이펙트
    // ─────────────────────────────────────────
    private void UpdateSlotEffects()
    {
        int filled = CountFilledSlots();

        if (filled != _lastFilledCount)
        {
            _lastFilledCount = filled;
            if (filled > 0)
                EvaluateAndLog();
        }

        // 기여 카드 계산
        bool[] contributing = GetContributingSlots();

        for (int i = 0; i < slots.Length; i++)
        {
            if (i >= _slotEffects.Count) break;

            if (contributing[i] && slots[i] != null && slots[i].HasCard)
            {
                _slotEffects[i].SetActive(true);
                GameObject slotCard = slots[i].GetPlacedCard();
                if (slotCard != null)
                    AttachEffect(_slotEffects[i], slotCard, effectContribute, 50);
            }
            else
            {
                _slotEffects[i].SetActive(false);
            }
        }
    }

    // ─────────────────────────────────────────
    //  이펙트를 카드 위에 맞추기
    // ─────────────────────────────────────────
    private void AttachEffect(GameObject fx, GameObject card, Sprite sprite, int sortOrder)
    {
        fx.transform.position = card.transform.position;
        fx.transform.rotation = card.transform.rotation;

        var cardSr = card.GetComponentInChildren<SpriteRenderer>();
        if (cardSr == null || cardSr.sprite == null) return;

        var fxSr = fx.GetComponent<SpriteRenderer>();
        fxSr.sortingOrder = sortOrder;

        // 카드의 실제 크기 (회전 무관하게 sprite 원본 크기 × lossyScale)
        Vector2 cardSpriteSize = cardSr.sprite.bounds.size;
        Vector3 cardScale = cardSr.transform.lossyScale;
        float cardW = cardSpriteSize.x * Mathf.Abs(cardScale.x);
        float cardH = cardSpriteSize.y * Mathf.Abs(cardScale.y);

        Vector2 effectSize = sprite.bounds.size;
        fx.transform.localScale = new Vector3(cardW / effectSize.x, cardH / effectSize.y, 1f);
    }

    // ─────────────────────────────────────────
    //  이펙트 오브젝트 생성
    // ─────────────────────────────────────────
    private GameObject CreateEffectObject(string name, Sprite sprite)
    {
        var go = new GameObject(name);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(1f, 1f, 1f, 0.8f);
        return go;
    }

    // ─────────────────────────────────────────
    //  조합 기여 슬롯 계산
    // ─────────────────────────────────────────
    private bool[] GetContributingSlots()
    {
        bool[] result = new bool[slots.Length];

        // 슬롯별 값 수집 (빈 슬롯은 0)
        int[] slotValues = new int[slots.Length];
        List<int> filledValues = new List<int>();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].HasCard)
            {
                var cv = slots[i].GetCardValue();
                slotValues[i] = cv != null ? cv.value : 0;
                filledValues.Add(slotValues[i]);
            }
        }

        if (filledValues.Count == 0) return result;

        int[] dice = filledValues.ToArray();
        System.Array.Sort(dice);

        // 최고 룰 찾기
        string bestName = "";
        int bestScore = 0;
        bool[] bestContrib = new bool[slots.Length];

        // 숫자별 (Ones ~ Sixes)
        for (int num = 1; num <= 6; num++)
        {
            int score = SumOf(dice, num);
            if (score > bestScore)
            {
                bestScore = score;
                bestName = num.ToString();
                bestContrib = MarkSlots(slotValues, v => v == num);
            }
        }

        // Three of a Kind
        {
            int score = ThreeOfAKind(dice);
            if (score > bestScore)
            {
                int kindVal = FindKindValue(dice, 3);
                bestScore = score;
                bestName = "Three of a Kind";
                bestContrib = MarkSlots(slotValues, v => v == kindVal);
            }
        }

        // Four of a Kind
        {
            int score = FourOfAKind(dice);
            if (score > bestScore)
            {
                int kindVal = FindKindValue(dice, 4);
                bestScore = score;
                bestName = "Four of a Kind";
                bestContrib = MarkSlots(slotValues, v => v == kindVal);
            }
        }

        // Full House
        {
            int score = FullHouse(dice);
            if (score > bestScore)
            {
                bestScore = score;
                bestName = "Full House";
                bestContrib = MarkAllFilled(slotValues);
            }
        }

        // Small Straight
        {
            int score = SmallStraight(dice);
            if (score > bestScore)
            {
                HashSet<int> straight = FindSmallStraightSet(dice);
                bestScore = score;
                bestName = "Small Straight";
                bestContrib = MarkSlotsInSet(slotValues, straight);
            }
        }

        // Large Straight
        {
            int score = LargeStraight(dice);
            if (score > bestScore)
            {
                bestScore = score;
                bestName = "Large Straight";
                bestContrib = MarkAllFilled(slotValues);
            }
        }

        // Yacht
        {
            int score = Yacht(dice);
            if (score > bestScore)
            {
                bestScore = score;
                bestName = "Yacht";
                bestContrib = MarkAllFilled(slotValues);
            }
        }

        return bestContrib;
    }

    // ─── 기여 슬롯 마킹 헬퍼 ───

    private bool[] MarkSlots(int[] slotValues, System.Func<int, bool> predicate)
    {
        bool[] marks = new bool[slotValues.Length];
        for (int i = 0; i < slotValues.Length; i++)
            if (slotValues[i] > 0 && predicate(slotValues[i]))
                marks[i] = true;
        return marks;
    }

    private bool[] MarkAllFilled(int[] slotValues)
    {
        bool[] marks = new bool[slotValues.Length];
        for (int i = 0; i < slotValues.Length; i++)
            if (slotValues[i] > 0) marks[i] = true;
        return marks;
    }

    private bool[] MarkSlotsInSet(int[] slotValues, HashSet<int> set)
    {
        bool[] marks = new bool[slotValues.Length];
        HashSet<int> used = new HashSet<int>();
        for (int i = 0; i < slotValues.Length; i++)
        {
            if (slotValues[i] > 0 && set.Contains(slotValues[i]) && !used.Contains(slotValues[i]))
            {
                marks[i] = true;
                used.Add(slotValues[i]);
            }
        }
        return marks;
    }

    private int FindKindValue(int[] dice, int minCount)
    {
        int[] counts = CountDice(dice);
        for (int i = 1; i <= 6; i++)
            if (counts[i] >= minCount) return i;
        return 0;
    }

    private HashSet<int> FindSmallStraightSet(int[] dice)
    {
        HashSet<int> unique = new HashSet<int>(dice);
        if (unique.Contains(3) && unique.Contains(4) && unique.Contains(5) && unique.Contains(6))
            return new HashSet<int> { 3, 4, 5, 6 };
        if (unique.Contains(2) && unique.Contains(3) && unique.Contains(4) && unique.Contains(5))
            return new HashSet<int> { 2, 3, 4, 5 };
        if (unique.Contains(1) && unique.Contains(2) && unique.Contains(3) && unique.Contains(4))
            return new HashSet<int> { 1, 2, 3, 4 };
        return new HashSet<int>();
    }

    // ─────────────────────────────────────────
    //  디버그 로그
    // ─────────────────────────────────────────

    private int CountFilledSlots()
    {
        int count = 0;
        foreach (var slot in slots)
            if (slot != null && slot.HasCard) count++;
        return count;
    }

    private void EvaluateAndLog()
    {
        List<int> values = new List<int>();
        foreach (var slot in slots)
        {
            if (slot == null || !slot.HasCard) continue;
            var cv = slot.GetCardValue();
            if (cv != null) values.Add(cv.value);
        }
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
