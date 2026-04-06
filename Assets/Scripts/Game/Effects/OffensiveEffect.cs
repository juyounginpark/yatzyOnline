using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  Attack + Critical 카드 효과
//  Attack: 기본 공격 (1×), Critical: 강화 공격 (2×)
//  두 타입은 같은 애니메이션 그룹으로 함께 날아감
// ─────────────────────────────────────────────
public class OffensiveEffect : CardEffectBase
{
    public override int Order => 0;

    public override IEnumerator Execute(TurnEffectContext ctx)
    {
        var attackCards   = ctx.GetCards(CardType.Attack);
        var criticalCards = ctx.GetCards(CardType.Critical);

        var allOffensive = new List<GameObject>();
        allOffensive.AddRange(attackCards);
        allOffensive.AddRange(criticalCards);

        if (allOffensive.Count == 0) yield break;

        // 공격 카드 날리기
        yield return ctx.Host.StartCoroutine(
            ctx.Animator.FlyAndHit(allOffensive, ctx.AttackTarget));

        if (ctx.Hp == null || ctx.TurnScore <= 0f) yield break;

        // 점수 배분: 카드 수 비율 × 배율
        float attackScore   = ctx.TurnScore * ctx.GetRatio(CardType.Attack);
        float criticalScore = ctx.TurnScore * ctx.GetRatio(CardType.Critical) * 2f;
        float totalDamage   = attackScore + criticalScore;

        // 피격 연출
        float shakeMult = Mathf.Max(1f, Mathf.Floor(totalDamage / 10f));
        ctx.Host.StartCoroutine(ctx.Animator.ShakeTransform(
            ctx.ShakeTarget, ctx.Animator.hitShakeDuration,
            ctx.Animator.hitShakeIntensity * shakeMult));
        yield return ctx.Host.StartCoroutine(
            ctx.Animator.ShakeCamera(
                ctx.Animator.hitShakeDuration,
                ctx.Animator.cameraShakeIntensity * shakeMult));

        // 데미지 적용
        if (totalDamage > 0f)
            ctx.DealDamage(totalDamage);
    }
}
