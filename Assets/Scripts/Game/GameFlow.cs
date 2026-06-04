using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  핸드 판정 (콤보 점수 로직)
//  - 게임 진행/이펙트 로직은 제거됨. 순수 점수 계산 API 만 제공.
//  - 조커는 1~6 전부 시도해 최고 점수 채택. 조커 5장은 특수 콤보(잭팟).
//  - 잭팟 점수 결정용 시드는 sharedSeed 로 외부에서 주입(온라인 동기화 시).
// ─────────────────────────────────────────────
public class GameFlow : MonoBehaviour
{
    [Header("─ 조커 잭팟 (조커 5장 특수 콤보) ─")]
    public string jokerComboName = "???";
    public float  jokerJackpotMin = 200f;
    public float  jokerJackpotMax = 300f;

    [Tooltip("잭팟 점수 결정용 공유 시드 (온라인 동기화 시 양쪽 동일 값 주입)")]
    public int sharedSeed = 0;

    // ─────────────────────────────────────────
    //  외부용: 슬롯 배열에서 조커 해석 포함 최고 점수
    // ─────────────────────────────────────────
    public void GetBestCombo(Slot[] targetSlots, out string bestComboName, out float bestComboScore)
    {
        int[] dummy;
        GetBestCombo(targetSlots, out bestComboName, out bestComboScore, out dummy);
    }

    public void GetBestCombo(Slot[] targetSlots, out string bestComboName, out float bestComboScore, out int[] resolvedValues)
    {
        bestComboName = "";
        bestComboScore = 0f;

        int[] slotValues = new int[targetSlots.Length];
        bool[] jokerFlags = new bool[targetSlots.Length];
        for (int i = 0; i < targetSlots.Length; i++)
        {
            if (targetSlots[i] != null && targetSlots[i].HasVisibleCard
                && !targetSlots[i].IsChainLocked)
            {
                var cv = targetSlots[i].GetCardValue();
                if (cv != null)
                {
                    jokerFlags[i] = cv.isJoker;
                    slotValues[i] = cv.isJoker ? 0 : cv.value;
                }
            }
        }
        resolvedValues = ResolveJokersOptimal(slotValues, jokerFlags);

        // 조커 5장 특수 콤보
        if (TryJokerJackpot(targetSlots, jokerFlags, out bestComboName, out bestComboScore))
            return;

        List<int> filled = new List<int>();
        for (int i = 0; i < resolvedValues.Length; i++)
            if (resolvedValues[i] > 0) filled.Add(resolvedValues[i]);

        if (filled.Count > 0)
            bestComboScore = EvaluateHand(filled.ToArray(), out bestComboName);
    }

    // ─────────────────────────────────────────
    //  조커 5장 특수 콤보(잭팟) — 필드별 1회 추첨 후 캐시
    // ─────────────────────────────────────────
    private readonly Dictionary<object, float> _jokerJackpot = new Dictionary<object, float>();

    private bool TryJokerJackpot(Slot[] s, bool[] jokerFlags, out string name, out float score)
    {
        name = "";
        score = 0f;

        int joker = 0, placed = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != null && s[i].HasVisibleCard && !s[i].IsChainLocked
                && s[i].GetCardValue() != null)
            {
                placed++;
                if (jokerFlags[i]) joker++;
            }
        }

        if (joker == 5 && placed == 5)
        {
            name  = jokerComboName;
            score = GetJokerJackpotScore(s);
            return true;
        }

        ClearJokerJackpot(s);
        return false;
    }

    private object JackpotKey(Slot[] s)
        => (s != null && s.Length > 0 && s[0] != null) ? (object)s[0] : this;

    private float GetJokerJackpotScore(Slot[] s)
    {
        object key = JackpotKey(s);
        if (!_jokerJackpot.TryGetValue(key, out float v))
        {
            // 온라인 동기화 시 양쪽 동일 결과를 위해 공유 시드 기반 결정적 난수 사용
            var rng = new System.Random(sharedSeed);
            v = rng.Next(Mathf.RoundToInt(jokerJackpotMin),
                         Mathf.RoundToInt(jokerJackpotMax) + 1);
            _jokerJackpot[key] = v;
        }
        return v;
    }

    private void ClearJokerJackpot(Slot[] s) => _jokerJackpot.Remove(JackpotKey(s));

    // ─────────────────────────────────────────
    //  조커 최적 값 해석 (1~6 전부 시도, 최고 점수 채택)
    // ─────────────────────────────────────────
    public int[] ResolveJokers(int[] values, bool[] jokerFlags)
    {
        return ResolveJokersOptimal(values, jokerFlags);
    }

    private int[] ResolveJokersOptimal(int[] values, bool[] jokerFlags)
    {
        List<int> jokerIndices = new List<int>();
        for (int i = 0; i < jokerFlags.Length; i++)
            if (jokerFlags[i]) jokerIndices.Add(i);

        if (jokerIndices.Count == 0) return values;

        int[] bestValues = (int[])values.Clone();
        float bestScore = -1f;

        int totalCombos = 1;
        for (int j = 0; j < jokerIndices.Count; j++) totalCombos *= 8;

        int[] trial = new int[values.Length];
        for (int combo = 0; combo < totalCombos; combo++)
        {
            System.Array.Copy(values, trial, values.Length);

            int c = combo;
            for (int j = 0; j < jokerIndices.Count; j++)
            {
                trial[jokerIndices[j]] = (c % 8) + 1;
                c /= 8;
            }

            List<int> filled = new List<int>();
            for (int i = 0; i < trial.Length; i++)
                if (trial[i] > 0) filled.Add(trial[i]);

            if (filled.Count == 0) continue;

            string rule;
            float score = EvaluateHand(filled.ToArray(), out rule);
            if (score > bestScore)
            {
                bestScore = score;
                bestValues = (int[])trial.Clone();
            }
        }

        return bestValues;
    }

    // ─────────────────────────────────────────
    //  외부용: 값/조커 배열에서 조커 해석 포함 점수
    // ─────────────────────────────────────────
    public float EvaluateValues(int[] values, bool[] jokerFlags, out string ruleName)
    {
        ruleName = "";
        int[] resolved = ResolveJokersOptimal(values, jokerFlags);
        List<int> filled = new List<int>();
        for (int i = 0; i < resolved.Length; i++)
            if (resolved[i] > 0) filled.Add(resolved[i]);
        if (filled.Count == 0) return 0f;
        return EvaluateHand(filled.ToArray(), out ruleName);
    }

    // ─────────────────────────────────────────
    //  블러드 베팅 룰: 7장(홀2+중앙5) 중 숫자카드 best-5 조합
    //  - 조커는 쓰레기(평가 제외) — 호출 전 numberValues에서 조커를 빼고 넘길 것
    //  - 숫자카드가 5장 이하면 그대로 평가, 6장 이상이면 C(n,5) 전부 시도해 최고 채택
    // ─────────────────────────────────────────
    public float EvaluateBestOfSeven(int[] numberValues, out string ruleName)
    {
        ruleName = "";
        if (numberValues == null || numberValues.Length == 0) return 0f;
        if (numberValues.Length <= 5)
            return EvaluateHand(numberValues, out ruleName);

        float best = -1f;
        string bestRule = "";
        int n = numberValues.Length;
        int[] hand = new int[5];

        for (int a = 0;     a < n - 4; a++)
        for (int b = a + 1; b < n - 3; b++)
        for (int c = b + 1; c < n - 2; c++)
        for (int d = c + 1; d < n - 1; d++)
        for (int e = d + 1; e < n;     e++)
        {
            hand[0] = numberValues[a]; hand[1] = numberValues[b]; hand[2] = numberValues[c];
            hand[3] = numberValues[d]; hand[4] = numberValues[e];

            float s = EvaluateHand(hand, out string r);
            if (s > best) { best = s; bestRule = r; }
        }

        ruleName = bestRule;
        return best < 0f ? 0f : best;
    }

    /// <summary>슬롯/카드 목록(조커 포함)에서 조커를 빼고 best-5 평가. 편의 래퍼.</summary>
    public float EvaluateBestOfSeven(IEnumerable<CardValue> cards, out string ruleName)
    {
        var nums = new List<int>();
        if (cards != null)
            foreach (var cv in cards)
                if (cv != null && !cv.isJoker && cv.value >= 1 && cv.value <= 8)
                    nums.Add(cv.value);
        return EvaluateBestOfSeven(nums.ToArray(), out ruleName);
    }

    // ─────────────────────────────────────────
    //  핸드 평가 (우선순위 기반)
    // ─────────────────────────────────────────
    public float EvaluateHand(int[] dice, out string ruleName)
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

        score = ScoreSmallStraight(sorted);
        if (score > 0f) { ruleName = "스몰스트레이트"; return score; }

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
        int[] counts = new int[9];
        foreach (int d in dice)
            if (d >= 1 && d <= 8) counts[d]++;
        return counts;
    }

    private float ScoreFiveOfAKind(int[] counts)
    {
        for (int i = 8; i >= 1; i--)
            if (counts[i] >= 5) return 95f + i * 0.5f;
        return 0f;
    }

    private float ScoreFourOfAKind(int[] counts)
    {
        int fourVal = 0;
        for (int i = 8; i >= 1; i--)
            if (counts[i] >= 4) { fourVal = i; break; }
        if (fourVal == 0) return 0f;

        int kicker = 0;
        for (int i = 8; i >= 1; i--)
            if (i != fourVal && counts[i] > 0) { kicker = i; break; }

        return 80f + fourVal * 1f + kicker * 0.1f;
    }

    private float ScoreFullHouse(int[] counts)
    {
        int tripleVal = 0, pairVal = 0;
        for (int i = 8; i >= 1; i--)
        {
            if (counts[i] >= 3 && tripleVal == 0)
                tripleVal = i;
            else if (counts[i] >= 2 && pairVal == 0)
                pairVal = i;
        }
        return (tripleVal > 0 && pairVal > 0) ? 65f + tripleVal * 1f + pairVal * 0.1f : 0f;
    }

    // 연속 run 보유 여부 (top 포함 아래로 len개)
    private static bool HasRun(HashSet<int> unique, int top, int len)
    {
        for (int v = top; v > top - len; v--)
            if (!unique.Contains(v)) return false;
        return true;
    }

    // 하이 스트레이트: 5연속 중 top이 6 이상 (2-6=70, 3-7=75, 4-8=80)
    private float ScoreStraightHigh(int[] sorted)
    {
        HashSet<int> unique = new HashSet<int>(sorted);
        for (int top = 8; top >= 6; top--)
            if (HasRun(unique, top, 5))
                return 40f + top * 5f;
        return 0f;
    }

    // 로우 스트레이트: 1-5 (=65)
    private float ScoreStraightLow(int[] sorted)
    {
        HashSet<int> unique = new HashSet<int>(sorted);
        if (HasRun(unique, 5, 5))
            return 40f + 5 * 5f;  // 65
        return 0f;
    }

    // 스몰 스트레이트: 4연속 (1-4=58 … 5-8=60), top 높을수록 가점
    private float ScoreSmallStraight(int[] sorted)
    {
        HashSet<int> unique = new HashSet<int>(sorted);
        for (int top = 8; top >= 4; top--)
            if (HasRun(unique, top, 4))
                return 56f + top * 0.5f;
        return 0f;
    }

    private float ScoreTriple(int[] counts)
    {
        int tripleVal = 0;
        for (int i = 8; i >= 1; i--)
            if (counts[i] >= 3) { tripleVal = i; break; }
        if (tripleVal == 0) return 0f;

        List<int> kickers = new List<int>();
        for (int i = 8; i >= 1; i--)
        {
            if (i == tripleVal) continue;
            for (int j = 0; j < counts[i]; j++)
                kickers.Add(i);
        }

        float bigKicker = kickers.Count > 0 ? kickers[0] : 0;
        float smallKicker = kickers.Count > 1 ? kickers[1] : 0;

        return 40f + tripleVal * 2f + bigKicker * 0.3f + smallKicker * 0.1f;
    }

    private float ScoreTwoPair(int[] counts)
    {
        List<int> pairs = new List<int>();
        for (int i = 8; i >= 1; i--)
            if (counts[i] >= 2) pairs.Add(i);

        if (pairs.Count < 2) return 0f;

        int bigPair = pairs[0];
        int smallPair = pairs[1];

        int kicker = 0;
        int[] tempCounts = (int[])counts.Clone();
        tempCounts[bigPair] -= 2;
        tempCounts[smallPair] -= 2;
        for (int i = 8; i >= 1; i--)
            if (tempCounts[i] > 0) { kicker = i; break; }

        return 25f + bigPair * 2f + smallPair * 0.3f + kicker * 0.1f;
    }

    private float ScoreOnePair(int[] counts)
    {
        int pairVal = 0;
        for (int i = 8; i >= 1; i--)
            if (counts[i] >= 2) { pairVal = i; break; }
        if (pairVal == 0) return 0f;

        List<int> kickers = new List<int>();
        int[] tempCounts = (int[])counts.Clone();
        tempCounts[pairVal] -= 2;
        for (int i = 8; i >= 1; i--)
            for (int j = 0; j < tempCounts[i]; j++)
                kickers.Add(i);

        float bigKicker = kickers.Count > 0 ? kickers[0] : 0;
        float midKicker = kickers.Count > 1 ? kickers[1] : 0;
        float smallKicker = kickers.Count > 2 ? kickers[2] : 0;

        return 10f + pairVal * 2f + bigKicker * 0.5f + midKicker * 0.2f + smallKicker * 0.1f;
    }

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
