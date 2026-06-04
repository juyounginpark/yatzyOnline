using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  BettingAI — 상대(AI) 베팅 판단 (규칙 기반)
//  - 자기 홀카드 + 공개된 커뮤니티로 핸드 강도 추정 (조커 제외 best-5)
//  - 강도/팟오즈/약간의 랜덤으로 체크·벳·콜·레이즈·폴드 결정
//  - 미공개 커뮤니티는 양쪽 모두 모르므로 평가에 넣지 않음
// ─────────────────────────────────────────────
public class BettingAI : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public GameFlow gameFlow;

    [Header("─ 성향 ─")]
    [Range(0f, 1f), Tooltip("약한 패로 벳/레이즈하는 블러프 빈도")]
    public float bluffChance = 0.12f;

    [Range(0f, 1f), Tooltip("강한 패로 레이즈(vs 콜)하는 공격성")]
    public float aggression = 0.5f;

    [Tooltip("벳/레이즈 기본 사이즈 (이 단위로)")]
    public int betUnit = 50;

    [Header("─ 강도 임계값 (best-5 점수) ─")]
    public float strongScore = 55f;   // 풀하우스+ 근방
    public float mediumScore  = 30f;   // 트리플/투페어/스트레이트 근방
    public float weakScore    = 14f;   // 원페어 근방

    void Awake()
    {
        if (gameFlow == null) gameFlow = FindObjectOfType<GameFlow>();
    }

    /// <summary>
    /// AI 액션 결정.
    ///  myHole          : AI 홀카드 값/조커
    ///  community       : 중앙 슬롯 (공개된 것만 평가에 사용)
    ///  toCall          : 콜에 필요한 금액
    ///  stack           : AI 보유 LP
    ///  pot             : 현재 팟
    ///  canRaise        : 레이즈 가능 여부 (스택이 콜보다 큰가 등 호출 측 판단)
    /// 반환: action, out sizeAmount (Bet=벳 사이즈, Raise=콜 위 추가 사이즈)
    /// </summary>
    public BetAction Decide(IEnumerable<Deck.CardPool> myHole, Slot[] community,
                            int toCall, int stack, int pot, bool canRaise, out int sizeAmount)
    {
        sizeAmount = 0;
        float score = EstimateStrength(myHole, community);
        bool facingBet = toCall > 0;

        // 사이즈 후보 (팟의 절반 ~ betUnit 중 큰 값, 스택 한도)
        int baseSize = Mathf.Max(betUnit, RoundToUnit(pot / 2));

        if (!facingBet)
        {
            // 체크 또는 벳
            if (score >= strongScore || (score >= mediumScore && Random.value < aggression))
            {
                sizeAmount = ClampBet(baseSize, stack);
                return sizeAmount > 0 ? BetAction.Bet : BetAction.Check;
            }
            if (score < weakScore && Random.value < bluffChance)
            {
                sizeAmount = ClampBet(baseSize, stack);
                return sizeAmount > 0 ? BetAction.Bet : BetAction.Check;
            }
            return BetAction.Check;
        }

        // 베팅에 직면 — 폴드/콜/레이즈
        // 올인을 콜해야 하는데 스택이 모자라면 콜=올인 처리(호출 측에서 클램프)
        float potOdds = (pot + toCall) > 0 ? (float)toCall / (pot + toCall) : 1f;

        if (score >= strongScore)
        {
            if (canRaise && Random.value < aggression)
            {
                sizeAmount = ClampBet(baseSize, stack - toCall);
                if (sizeAmount > 0) return BetAction.Raise;
            }
            return BetAction.Call;
        }

        if (score >= mediumScore)
        {
            // 팟오즈가 좋으면 콜, 가끔 레이즈
            if (canRaise && Random.value < aggression * 0.4f)
            {
                sizeAmount = ClampBet(baseSize, stack - toCall);
                if (sizeAmount > 0) return BetAction.Raise;
            }
            return BetAction.Call;
        }

        if (score >= weakScore)
        {
            // 콜 비용이 팟 대비 작으면 콜
            return potOdds < 0.33f ? BetAction.Call : BetAction.Fold;
        }

        // 쓰레기 패 — 가끔 블러프 레이즈, 보통 폴드
        if (canRaise && Random.value < bluffChance)
        {
            sizeAmount = ClampBet(baseSize, stack - toCall);
            if (sizeAmount > 0) return BetAction.Raise;
        }
        return potOdds < 0.15f ? BetAction.Call : BetAction.Fold;
    }

    // ─────────────────────────────────────────
    //  핸드 강도 추정 (조커 제외 best-5)
    // ─────────────────────────────────────────
    private float EstimateStrength(IEnumerable<Deck.CardPool> myHole, Slot[] community)
    {
        if (gameFlow == null) gameFlow = FindObjectOfType<GameFlow>();
        if (gameFlow == null) return 0f;

        var nums = new List<int>();

        if (myHole != null)
            foreach (var c in myHole)
                if (!c.isJoker && c.value >= 1 && c.value <= 8) nums.Add(c.value);

        if (community != null)
            foreach (var s in community)
                if (s != null && s.HasCard && !s.IsGuard)   // 공개된 커뮤니티만
                {
                    var cv = s.GetCardValue();
                    if (cv != null && !cv.isJoker && cv.value >= 1 && cv.value <= 8)
                        nums.Add(cv.value);
                }

        return gameFlow.EvaluateBestOfSeven(nums.ToArray(), out _);
    }

    private int RoundToUnit(int v) => betUnit > 1 ? Mathf.Max(betUnit, (v / betUnit) * betUnit) : Mathf.Max(1, v);

    private int ClampBet(int size, int max)
    {
        if (max <= 0) return 0;
        return Mathf.Clamp(size, 0, max);
    }
}
