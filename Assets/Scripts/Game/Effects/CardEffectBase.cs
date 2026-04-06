using System.Collections;

// ─────────────────────────────────────────────
//  카드 효과 기본 클래스
//
//  새로운 카드 타입을 추가하려면:
//  1. CardType enum에 값 추가
//  2. 이 클래스를 상속하는 효과 클래스 작성
//  3. CardEffectPipeline.Register()로 등록
// ─────────────────────────────────────────────
public abstract class CardEffectBase
{
    /// <summary>실행 순서 (낮을수록 먼저). Offensive=0, Chain=10, Heal=20</summary>
    public abstract int Order { get; }

    /// <summary>
    /// 카드 해제 전 사전 처리 (체인 매칭 분석 등).
    /// 슬롯에 카드가 아직 남아있는 상태에서 호출됨.
    /// </summary>
    public virtual void Prepare(TurnEffectContext ctx) { }

    /// <summary>효과 실행 (애니메이션 + 게임 로직)</summary>
    public abstract IEnumerator Execute(TurnEffectContext ctx);
}
