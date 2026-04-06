using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  Chain 카드 효과
//  같은 값 2장 이상 매칭 → 상대 슬롯 잠금 + DOT 데미지
//  매칭 안 된 체인 카드는 Attack으로 분류됨 (Pipeline에서 처리)
// ─────────────────────────────────────────────
public class ChainEffect : CardEffectBase
{
    public override int Order => 10;

    // ─────────────────────────────────────────
    //  Prepare: 체인 매칭 분석 + 잠금 대상 결정
    //  카드가 아직 슬롯에 있는 상태에서 호출됨
    // ─────────────────────────────────────────
    public override void Prepare(TurnEffectContext ctx)
    {
        var sourceSlots = ctx.SourceSlots;
        if (sourceSlots == null || ctx.ChainLockPrefab == null) return;

        // 같은 값의 체인 카드 수 집계
        var chainValueCount = new Dictionary<int, int>();
        foreach (var slot in sourceSlots)
        {
            if (slot == null || !slot.HasCard || slot.IsChainLocked) continue;
            var cv = slot.GetCardValue();
            if (cv != null && cv.cardType == CardType.Chain)
            {
                int v = cv.value;
                if (!chainValueCount.ContainsKey(v)) chainValueCount[v] = 0;
                chainValueCount[v]++;
            }
        }

        // 2장 이상 매칭된 총 체인 카드 수
        int chainLockCount = 0;
        foreach (var kvp in chainValueCount)
            if (kvp.Value >= 2) chainLockCount += kvp.Value;

        if (chainLockCount == 0) return;

        // 매칭된 체인 카드 슬롯 마킹
        foreach (var slot in sourceSlots)
        {
            if (slot == null || !slot.HasCard || slot.IsChainLocked) continue;
            var cv = slot.GetCardValue();
            if (cv != null && cv.cardType == CardType.Chain
                && chainValueCount.ContainsKey(cv.value)
                && chainValueCount[cv.value] >= 2)
                ctx.ChainMatchedSlots.Add(slot);
        }

        // 잠금 대상 슬롯 결정
        // 네트워크에서 받은 인덱스가 있으면 우선 사용, 없으면 소스 슬롯 인덱스 = 타겟 인덱스
        var targetSlots = ctx.TargetSlots;
        if (targetSlots == null) return;

        ctx.ChainTargetSlots = new List<Slot>();

        if (ctx.PresetChainTargetIndices != null && ctx.PresetChainTargetIndices.Count > 0)
        {
            // 온라인: 상대가 보낸 체인 잠금 인덱스 사용
            foreach (int idx in ctx.PresetChainTargetIndices)
            {
                if (idx >= 0 && idx < targetSlots.Length
                    && targetSlots[idx] != null && !targetSlots[idx].IsChainLocked)
                    ctx.ChainTargetSlots.Add(targetSlots[idx]);
            }
        }
        else
        {
            // 로컬: 공격자 체인 카드 슬롯 인덱스 → 같은 위치의 상대 슬롯 잠금
            for (int i = 0; i < sourceSlots.Length; i++)
            {
                if (!ctx.ChainMatchedSlots.Contains(sourceSlots[i])) continue;
                if (i < targetSlots.Length
                    && targetSlots[i] != null && !targetSlots[i].IsChainLocked)
                    ctx.ChainTargetSlots.Add(targetSlots[i]);
            }
        }
    }

    // ─────────────────────────────────────────
    //  Execute: 체인 카드 → 상대 슬롯으로 날리기 + 잠금 + DOT
    // ─────────────────────────────────────────
    public override IEnumerator Execute(TurnEffectContext ctx)
    {
        var chainCards = ctx.GetCards(CardType.Chain);

        if (chainCards.Count == 0 || ctx.ChainTargetSlots.Count == 0)
        {
            foreach (var c in chainCards)
                Object.Destroy(c);
            yield break;
        }

        // 공격 카드가 있었으면 딜레이
        if (ctx.GetCards(CardType.Attack).Count > 0
            || ctx.GetCards(CardType.Critical).Count > 0)
            yield return new WaitForSeconds(0.3f);

        // 체인 카드 날리기 + 잠금 애니메이션
        yield return ctx.Host.StartCoroutine(
            ctx.Animator.FlyChainCardsAndLock(
                chainCards, ctx.ChainTargetSlots, ctx.ChainLockPrefab));

        // DOT는 잠금 대상의 다음 턴 시작 시 활성화됨 (MainFlow에서 처리)
    }
}
