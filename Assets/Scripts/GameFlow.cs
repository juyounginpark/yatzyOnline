using System.Collections.Generic;
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

    [Tooltip("슬롯에 넣었을 때 최고점이 나오는 덱 카드에 표시")]
    public Sprite effectBestPick;

    [Header("─ 참조 ─")]
    public Deck deck;

    // ─── 이펙트 오브젝트 ───
    private GameObject _hoverEffect;
    private readonly List<GameObject> _bestPickEffects = new List<GameObject>();
    private readonly List<GameObject> _slotEffects = new List<GameObject>();
    private string _lastBestCombo = "";
    private GameObject _lastEffectTarget;

    // ─── 외부 참조용 현재 상태 ───
    public float CurrentBestScore { get; private set; }
    public string CurrentBestRule { get; private set; } = "";

    void Start()
    {
        if (deck == null)
            deck = FindObjectOfType<Deck>();

        for (int i = 0; i < slots.Length; i++)
        {
            var fx = CreateEffectObject($"SlotEffect_{i}", effectContribute);
            fx.SetActive(false);
            _slotEffects.Add(fx);
        }

        _hoverEffect = CreateEffectObject("HoverEffect", effectHover);
        _hoverEffect.SetActive(false);

        for (int i = 0; i < 10; i++)
        {
            var fx = CreateEffectObject($"BestPickEffect_{i}", effectBestPick);
            fx.SetActive(false);
            _bestPickEffects.Add(fx);
        }
    }

    void Update()
    {
        UpdateHoverEffect();
        UpdateSlotEffects();
        UpdateBestPickEffect();
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
    //  추천 카드 이펙트 (덱에서 넣으면 최고점인 카드)
    // ─────────────────────────────────────────
    private void UpdateBestPickEffect()
    {
        if (deck == null || effectBestPick == null || deck.IsAnimating)
        {
            foreach (var fx in _bestPickEffects) if (fx != null) fx.SetActive(false);
            return;
        }

        var cards = deck.SpawnedCards;

        int slotCount = slots.Length;
        int[] allValues = new int[slotCount + cards.Count];

        for (int i = 0; i < slotCount; i++)
        {
            if (slots[i] != null && slots[i].HasCard)
            {
                var cv = slots[i].GetCardValue();
                allValues[i] = cv != null ? cv.value : 0;
            }
        }

        for (int i = 0; i < cards.Count; i++)
        {
            var cv = cards[i] != null ? cards[i].GetComponent<CardValue>() : null;
            allValues[slotCount + i] = cv != null ? cv.value : 0;
        }

        string dummy;
        float dummyScore;
        bool[] contributing = FindContributingIndices(allValues, out dummy, out dummyScore);

        for (int i = 0; i < _bestPickEffects.Count; i++)
        {
            int idx = slotCount + i;
            if (i < cards.Count && cards[i] != null && idx < contributing.Length && contributing[idx])
            {
                _bestPickEffects[i].SetActive(true);
                var hover = cards[i].GetComponent<CardHover>();
                int sortOrder = hover != null ? hover.baseSortingOrder + 1 : 101;
                AttachEffect(_bestPickEffects[i], cards[i], effectBestPick, sortOrder);
            }
            else
            {
                _bestPickEffects[i].SetActive(false);
            }
        }
    }

    // ─────────────────────────────────────────
    //  슬롯 조합 기여 이펙트
    // ─────────────────────────────────────────
    private void UpdateSlotEffects()
    {
        string currentBest;
        float currentScore;
        bool[] contributing = GetContributingSlots(out currentBest, out currentScore);

        CurrentBestRule = currentBest;
        CurrentBestScore = currentScore;

        if (currentBest != _lastBestCombo)
        {
            _lastBestCombo = currentBest;
            if (!string.IsNullOrEmpty(currentBest))
                EvaluateAndLog();
        }

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
    //  슬롯 조합 기여 계산 (래퍼)
    // ─────────────────────────────────────────
    private bool[] GetContributingSlots(out string bestComboName, out float bestComboScore)
    {
        int[] slotValues = new int[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != null && slots[i].HasCard)
            {
                var cv = slots[i].GetCardValue();
                slotValues[i] = cv != null ? cv.value : 0;
            }
        }
        return FindContributingIndices(slotValues, out bestComboName, out bestComboScore);
    }

    // ─────────────────────────────────────────
    //  공통: 값 배열에서 최고 조합 기여 인덱스 계산
    // ─────────────────────────────────────────
    private bool[] FindContributingIndices(int[] values, out string bestComboName, out float bestComboScore)
    {
        bestComboName = "";
        bestComboScore = 0f;
        bool[] result = new bool[values.Length];

        List<int> filledValues = new List<int>();
        for (int i = 0; i < values.Length; i++)
            if (values[i] > 0) filledValues.Add(values[i]);

        if (filledValues.Count == 0) return result;

        int[] dice = filledValues.ToArray();
        bestComboScore = EvaluateHand(dice, out bestComboName);
        if (bestComboScore <= 0f) return result;

        int[] counts = CountDice(dice);

        switch (bestComboName)
        {
            case "파이브카드":
            {
                int val = 0;
                for (int i = 6; i >= 1; i--) if (counts[i] >= 5) { val = i; break; }
                result = MarkSlots(values, v => v == val, 5);
                break;
            }
            case "포카드":
            {
                int val = 0;
                for (int i = 6; i >= 1; i--) if (counts[i] >= 4) { val = i; break; }
                result = MarkSlots(values, v => v == val, 4);
                break;
            }
            case "풀하우스":
            {
                int tripleVal = 0, pairVal = 0;
                for (int i = 6; i >= 1; i--)
                {
                    if (counts[i] >= 3 && tripleVal == 0) tripleVal = i;
                    else if (counts[i] >= 2 && pairVal == 0) pairVal = i;
                }
                bool[] marks = new bool[values.Length];
                int ct = 0, cp = 0;
                for (int j = 0; j < values.Length; j++)
                {
                    if (values[j] == tripleVal && ct < 3) { marks[j] = true; ct++; }
                    else if (values[j] == pairVal && cp < 2) { marks[j] = true; cp++; }
                }
                result = marks;
                break;
            }
            case "스트레이트(하이)":
                result = MarkSlotsInSet(values, new HashSet<int> { 2, 3, 4, 5, 6 });
                break;
            case "스트레이트(로우)":
                result = MarkSlotsInSet(values, new HashSet<int> { 1, 2, 3, 4, 5 });
                break;
            case "트리플":
            {
                int val = 0;
                for (int i = 6; i >= 1; i--) if (counts[i] >= 3) { val = i; break; }
                result = MarkSlots(values, v => v == val, 3);
                break;
            }
            case "투페어":
            {
                List<int> pairs = new List<int>();
                for (int i = 6; i >= 1; i--) if (counts[i] >= 2) pairs.Add(i);
                if (pairs.Count >= 2)
                {
                    int bigP = pairs[0], smallP = pairs[1];
                    bool[] marks = new bool[values.Length];
                    int c1 = 0, c2 = 0;
                    for (int j = 0; j < values.Length; j++)
                    {
                        if (values[j] == bigP && c1 < 2) { marks[j] = true; c1++; }
                        else if (values[j] == smallP && c2 < 2) { marks[j] = true; c2++; }
                    }
                    result = marks;
                }
                break;
            }
            case "원페어":
            {
                int val = 0;
                for (int i = 6; i >= 1; i--) if (counts[i] >= 2) { val = i; break; }
                result = MarkSlots(values, v => v == val, 2);
                break;
            }
            case "하이카드":
                result = MarkAllFilled(values);
                break;
        }

        return result;
    }

    // ─── 기여 슬롯 마킹 헬퍼 ───

    private bool[] MarkSlots(int[] slotValues, System.Func<int, bool> predicate, int maxCount = int.MaxValue)
    {
        bool[] marks = new bool[slotValues.Length];
        int count = 0;
        for (int i = 0; i < slotValues.Length; i++)
        {
            if (slotValues[i] > 0 && predicate(slotValues[i]) && count < maxCount)
            {
                marks[i] = true;
                count++;
            }
        }
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

    // ─────────────────────────────────────────
    //  디버그 로그
    // ─────────────────────────────────────────
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
        string ruleName;
        float score = EvaluateHand(dice, out ruleName);

        string diceStr = string.Join(", ", dice);
        Debug.Log($"[GameFlow] 카드: [{diceStr}] → 최고: {ruleName} ({score:F1}점)");
    }

    // ─────────────────────────────────────────
    //  핸드 평가 (우선순위 기반)
    // ─────────────────────────────────────────
    private float EvaluateHand(int[] dice, out string ruleName)
    {
        ruleName = "";
        if (dice.Length == 0) return 0f;

        int[] sorted = (int[])dice.Clone();
        System.Array.Sort(sorted);
        int[] counts = CountDice(sorted);

        float score;

        score = ScoreFiveOfAKind(counts);
        if (score > 0f) { ruleName = "파이브카드"; return score; }

        score = ScoreFourOfAKind(counts);
        if (score > 0f) { ruleName = "포카드"; return score; }

        score = ScoreFullHouse(counts);
        if (score > 0f) { ruleName = "풀하우스"; return score; }

        score = ScoreStraightHigh(sorted);
        if (score > 0f) { ruleName = "스트레이트(하이)"; return score; }

        score = ScoreStraightLow(sorted);
        if (score > 0f) { ruleName = "스트레이트(로우)"; return score; }

        score = ScoreTriple(counts);
        if (score > 0f) { ruleName = "트리플"; return score; }

        score = ScoreTwoPair(counts);
        if (score > 0f) { ruleName = "투페어"; return score; }

        score = ScoreOnePair(counts);
        if (score > 0f) { ruleName = "원페어"; return score; }

        score = ScoreHighCard(sorted);
        if (score > 0f) { ruleName = "하이카드"; return score; }

        return 0f;
    }

    // ─────────────────────────────────────────
    //  스코어링 함수들
    // ─────────────────────────────────────────

    private int[] CountDice(int[] dice)
    {
        int[] counts = new int[7];
        foreach (int d in dice)
            if (d >= 1 && d <= 6) counts[d]++;
        return counts;
    }

    // 파이브카드: 95 + num × 0.5
    private float ScoreFiveOfAKind(int[] counts)
    {
        for (int i = 6; i >= 1; i--)
            if (counts[i] >= 5) return 95f + i * 0.5f;
        return 0f;
    }

    // 포카드: 80 + fourVal × 1 + kicker × 0.1
    private float ScoreFourOfAKind(int[] counts)
    {
        int fourVal = 0;
        for (int i = 6; i >= 1; i--)
            if (counts[i] >= 4) { fourVal = i; break; }
        if (fourVal == 0) return 0f;

        int kicker = 0;
        for (int i = 6; i >= 1; i--)
            if (i != fourVal && counts[i] > 0) { kicker = i; break; }

        return 80f + fourVal * 1f + kicker * 0.1f;
    }

    // 풀하우스: 65 + tripleVal × 1 + pairVal × 0.1
    private float ScoreFullHouse(int[] counts)
    {
        int tripleVal = 0, pairVal = 0;
        for (int i = 6; i >= 1; i--)
        {
            if (counts[i] >= 3 && tripleVal == 0)
                tripleVal = i;
            else if (counts[i] >= 2 && pairVal == 0)
                pairVal = i;
        }
        return (tripleVal > 0 && pairVal > 0) ? 65f + tripleVal * 1f + pairVal * 0.1f : 0f;
    }

    // 스트레이트(하이): 2,3,4,5,6 → 58
    private float ScoreStraightHigh(int[] sorted)
    {
        HashSet<int> unique = new HashSet<int>(sorted);
        if (unique.Contains(2) && unique.Contains(3) && unique.Contains(4)
            && unique.Contains(5) && unique.Contains(6))
            return 58f;
        return 0f;
    }

    // 스트레이트(로우): 1,2,3,4,5 → 55
    private float ScoreStraightLow(int[] sorted)
    {
        HashSet<int> unique = new HashSet<int>(sorted);
        if (unique.Contains(1) && unique.Contains(2) && unique.Contains(3)
            && unique.Contains(4) && unique.Contains(5))
            return 55f;
        return 0f;
    }

    // 트리플: 40 + tripleVal × 2 + bigKicker × 0.3 + smallKicker × 0.1
    private float ScoreTriple(int[] counts)
    {
        int tripleVal = 0;
        for (int i = 6; i >= 1; i--)
            if (counts[i] >= 3) { tripleVal = i; break; }
        if (tripleVal == 0) return 0f;

        List<int> kickers = new List<int>();
        for (int i = 6; i >= 1; i--)
        {
            if (i == tripleVal) continue;
            for (int j = 0; j < counts[i]; j++)
                kickers.Add(i);
        }

        float bigKicker = kickers.Count > 0 ? kickers[0] : 0;
        float smallKicker = kickers.Count > 1 ? kickers[1] : 0;

        return 40f + tripleVal * 2f + bigKicker * 0.3f + smallKicker * 0.1f;
    }

    // 투페어: 25 + bigPair × 2 + smallPair × 0.3 + kicker × 0.1
    private float ScoreTwoPair(int[] counts)
    {
        List<int> pairs = new List<int>();
        for (int i = 6; i >= 1; i--)
            if (counts[i] >= 2) pairs.Add(i);

        if (pairs.Count < 2) return 0f;

        int bigPair = pairs[0];
        int smallPair = pairs[1];

        int kicker = 0;
        int[] tempCounts = (int[])counts.Clone();
        tempCounts[bigPair] -= 2;
        tempCounts[smallPair] -= 2;
        for (int i = 6; i >= 1; i--)
            if (tempCounts[i] > 0) { kicker = i; break; }

        return 25f + bigPair * 2f + smallPair * 0.3f + kicker * 0.1f;
    }

    // 원페어: 10 + pairVal × 2 + bigKicker × 0.5 + midKicker × 0.2 + smallKicker × 0.1
    private float ScoreOnePair(int[] counts)
    {
        int pairVal = 0;
        for (int i = 6; i >= 1; i--)
            if (counts[i] >= 2) { pairVal = i; break; }
        if (pairVal == 0) return 0f;

        List<int> kickers = new List<int>();
        int[] tempCounts = (int[])counts.Clone();
        tempCounts[pairVal] -= 2;
        for (int i = 6; i >= 1; i--)
            for (int j = 0; j < tempCounts[i]; j++)
                kickers.Add(i);

        float bigKicker = kickers.Count > 0 ? kickers[0] : 0;
        float midKicker = kickers.Count > 1 ? kickers[1] : 0;
        float smallKicker = kickers.Count > 2 ? kickers[2] : 0;

        return 10f + pairVal * 2f + bigKicker * 0.5f + midKicker * 0.2f + smallKicker * 0.1f;
    }

    // 하이카드: biggest × 2 + second × 0.8 + third × 0.3 + fourth × 0.1
    private float ScoreHighCard(int[] sorted)
    {
        if (sorted.Length == 0) return 0f;

        List<int> desc = new List<int>(sorted);
        desc.Sort((a, b) => b.CompareTo(a));

        float biggest = desc.Count > 0 ? desc[0] : 0;
        float second  = desc.Count > 1 ? desc[1] : 0;
        float third   = desc.Count > 2 ? desc[2] : 0;
        float fourth  = desc.Count > 3 ? desc[3] : 0;

        return biggest * 2f + second * 0.8f + third * 0.3f + fourth * 0.1f;
    }
}
