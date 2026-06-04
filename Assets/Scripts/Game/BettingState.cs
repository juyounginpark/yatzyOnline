using UnityEngine;

// ─────────────────────────────────────────────
//  베팅 액션 / 스트리트(한 카드 오픈 단위) 결과
// ─────────────────────────────────────────────
public enum BetAction
{
    Check,   // 콜 금액 0일 때만
    Bet,     // 콜 금액 0에서 새 베팅
    Call,    // 콜 금액 매칭 → 스트리트 종료
    Raise,   // 콜 + 추가 베팅
    Fold,    // 기권
    AllIn,   // 남은 LP 전부
}

public enum StreetResult
{
    Continue,        // 아직 진행 중
    BetCallMatched,  // 벳-콜 일치 → 카드 1장 공개
    CheckCheck,      // 양쪽 체크 → (옵션에 따라) 쇼다운 강제
    PlayerFold,      // 플레이어 기권
    OppFold,         // 상대 기권
    AllInShowdown,   // 올인 콜 성립 → 남은 카드 전부 공개 후 쇼다운
}

// ─────────────────────────────────────────────
//  BettingState — 한 스트리트의 베팅 상태(콜 금액 계산용)
//  - 실제 LP 차감/팟 적립은 RoundDirector가 담당
//  - 여기선 "이번 스트리트에 각자 얼마를 넣었는지"만 추적
// ─────────────────────────────────────────────
public class BettingState
{
    public int  playerCommitted;   // 이번 스트리트에 플레이어가 넣은 칩
    public int  oppCommitted;      // 이번 스트리트에 상대가 넣은 칩
    public int  currentBet;        // 이번 스트리트 최대 베팅액
    public int  minBet;            // 최소 벳/레이즈 단위
    public bool playerAllIn;
    public bool oppAllIn;

    public void ResetStreet(int minBetUnit)
    {
        playerCommitted = 0;
        oppCommitted    = 0;
        currentBet      = 0;
        minBet          = Mathf.Max(1, minBetUnit);
        // all-in 상태는 스트리트가 바뀌어도 유지 (한 번 올인이면 끝까지)
    }

    public int Committed(bool isPlayer) => isPlayer ? playerCommitted : oppCommitted;

    /// <summary>해당 플레이어가 콜하려면 더 넣어야 하는 금액.</summary>
    public int ToCall(bool isPlayer) => Mathf.Max(0, currentBet - Committed(isPlayer));

    /// <summary>지금 베팅(콜할 금액)에 직면해 있는가.</summary>
    public bool FacingBet(bool isPlayer) => ToCall(isPlayer) > 0;

    public bool IsAllIn(bool isPlayer) => isPlayer ? playerAllIn : oppAllIn;

    /// <summary>칩 투입 기록 (LP 차감/팟 적립은 호출 측에서).</summary>
    public void Commit(bool isPlayer, int amount, bool allIn)
    {
        if (isPlayer) { playerCommitted += amount; if (allIn) playerAllIn = true; }
        else          { oppCommitted    += amount; if (allIn) oppAllIn    = true; }

        int mine = Committed(isPlayer);
        if (mine > currentBet) currentBet = mine;
    }
}
