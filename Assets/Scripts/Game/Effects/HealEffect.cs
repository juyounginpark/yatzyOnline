using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  Heal 카드 효과
//  자신의 덱으로 날아가 HP 회복 + 초록 웨이브 연출
// ─────────────────────────────────────────────
public class HealEffect : CardEffectBase
{
    public override int Order => 20;

    public override IEnumerator Execute(TurnEffectContext ctx)
    {
        var healCards = ctx.GetCards(CardType.Heal);
        if (healCards.Count == 0) yield break;

        if (ctx.Hp == null || ctx.TurnScore <= 0f)
        {
            foreach (var c in healCards)
                Object.Destroy(c);
            yield break;
        }

        float healScore = ctx.TurnScore * ctx.GetRatio(CardType.Heal);

        // 힐 카드 딜레이 후 자기 덱으로 날리기
        yield return new WaitForSeconds(0.5f);
        yield return ctx.Host.StartCoroutine(
            ctx.Animator.FlyAndHit(healCards, ctx.HealTarget));

        if (healScore > 0f)
        {
            // 초록 웨이브 연출
            IReadOnlyList<GameObject> deckCards = ctx.IsPlayerTurn
                ? ctx.Deck.SpawnedCards
                : ctx.OppDeck.SpawnedCards;
            yield return ctx.Host.StartCoroutine(
                ctx.Animator.HealGreenWave(deckCards, healScore));

            // HP 회복
            ctx.HealSelf(healScore);
        }
    }
}
