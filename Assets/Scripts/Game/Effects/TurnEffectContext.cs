using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  턴 효과 실행에 필요한 공유 컨텍스트
//  모든 CardEffect가 이 객체를 통해 게임 상태에 접근
// ─────────────────────────────────────────────
public class TurnEffectContext
{
    // ── 턴 기본 정보 ──
    public bool   IsPlayerTurn;
    public float  TurnScore;
    public string ComboName;

    // ── 슬롯 ──
    public Slot[] PlayerSlots;
    public Slot[] OppSlots;
    public Slot[] SourceSlots => IsPlayerTurn ? PlayerSlots : OppSlots;
    public Slot[] TargetSlots => IsPlayerTurn ? OppSlots   : PlayerSlots;

    // ── 위치 ──
    public Vector3   AttackTarget;
    public Vector3   HealTarget;
    public Transform ShakeTarget;

    // ── 시스템 참조 ──
    public HP             Hp;
    public TurnAnimator   Animator;
    public MonoBehaviour  Host;
    public Deck           Deck;
    public OppDeck        OppDeck;

    // ── 체인 전용 ──
    public GameObject     ChainLockPrefab;
    public List<Slot>     ChainTargetSlots  = new List<Slot>();
    public HashSet<Slot>  ChainMatchedSlots = new HashSet<Slot>();
    public List<int>      PresetChainTargetIndices;  // 온라인 수신 시 사용

    // ── 해제된 카드 그룹 (타입별) ──
    public Dictionary<CardType, List<GameObject>> CardGroups
        = new Dictionary<CardType, List<GameObject>>();
    public int TotalCardCount;

    // ─── 유틸 ───

    public float GetRatio(CardType type)
    {
        if (TotalCardCount <= 0) return 0f;
        return CardGroups.TryGetValue(type, out var list)
            ? (float)list.Count / TotalCardCount : 0f;
    }

    public List<GameObject> GetCards(CardType type)
    {
        return CardGroups.TryGetValue(type, out var list)
            ? list : new List<GameObject>();
    }

    public List<GameObject> AllCards()
    {
        var all = new List<GameObject>();
        foreach (var kv in CardGroups)
            all.AddRange(kv.Value);
        return all;
    }

    public void DealDamage(float amount)
    {
        if (Hp == null) return;
        if (IsPlayerTurn) Hp.DamageOpp(amount);
        else              Hp.DamagePlayer(amount);
    }

    public void HealSelf(float amount)
    {
        if (Hp == null) return;
        if (IsPlayerTurn) Hp.HealPlayer(amount);
        else              Hp.HealOpp(amount);
    }
}
