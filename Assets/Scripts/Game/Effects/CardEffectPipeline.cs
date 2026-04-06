using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  카드 효과 파이프라인
//  효과를 등록하고, 턴 종료 시 순서대로 실행
//
//  사용법:
//    CardEffectPipeline.Register(new MyNewEffect());
//    CardEffectPipeline.PrepareAndRelease(ctx);
//    yield return CardEffectPipeline.ExecuteAll(ctx);
// ─────────────────────────────────────────────
public static class CardEffectPipeline
{
    private static readonly List<CardEffectBase> _effects = new List<CardEffectBase>();
    private static bool _initialized;

    // ─── 기본 효과 등록 ───
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;

        Register(new OffensiveEffect());
        Register(new ChainEffect());
        Register(new HealEffect());
    }

    /// <summary>새로운 효과를 파이프라인에 추가 (Order 기준 자동 정렬)</summary>
    public static void Register(CardEffectBase effect)
    {
        _effects.Add(effect);
        _effects.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    // ─────────────────────────────────────────
    //  사전 처리 + 슬롯에서 카드 해제 → 타입별 분류
    // ─────────────────────────────────────────
    public static void PrepareAndRelease(TurnEffectContext ctx)
    {
        EnsureInitialized();

        // 1) 각 효과의 Prepare 호출 (체인 매칭 등)
        foreach (var effect in _effects)
            effect.Prepare(ctx);

        // 2) 슬롯에서 카드 해제 및 타입별 분류
        var sourceSlots = ctx.SourceSlots;
        if (sourceSlots == null) return;

        ctx.TotalCardCount = 0;

        foreach (var slot in sourceSlots)
        {
            if (slot == null || !slot.HasCard) continue;
            if (slot.IsChainLocked || !slot.HasVisibleCard) continue;

            var cv = slot.GetCardValue();
            CardType type = cv != null ? cv.cardType : CardType.Attack;
            var card = slot.ReleaseCard();
            if (card == null) continue;

            card.transform.localScale = Vector3.one;

            // 매칭되지 않은 체인 카드 → Attack으로 분류
            if (type == CardType.Chain && !ctx.ChainMatchedSlots.Contains(slot))
                type = CardType.Attack;

            if (!ctx.CardGroups.ContainsKey(type))
                ctx.CardGroups[type] = new List<GameObject>();
            ctx.CardGroups[type].Add(card);
            ctx.TotalCardCount++;

            // 소팅 오더 설정
            foreach (var r in card.GetComponentsInChildren<Renderer>())
                r.sortingOrder = (type == CardType.Heal) ? 400 : 500;
        }
    }

    // ─────────────────────────────────────────
    //  모든 효과를 순서대로 실행
    // ─────────────────────────────────────────
    public static IEnumerator ExecuteAll(TurnEffectContext ctx)
    {
        EnsureInitialized();

        foreach (var effect in _effects)
            yield return ctx.Host.StartCoroutine(effect.Execute(ctx));
    }
}
