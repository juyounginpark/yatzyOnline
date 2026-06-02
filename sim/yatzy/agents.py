"""의사결정 에이전트.

- Agent            : 인터페이스 (choose_draft / choose_turn)
- RandomAgent      : 무작위(유효한 행동만)
- HeuristicAgent   : OppAuto.cs 의 그리디 콤보 배치 + 드래프트 휴리스틱 포팅

배치 탐색 유틸(best_combo_placement / fill_highest)은 RL 매크로 행동에서도 재사용한다.
슬롯 위치는 점수에 영향이 없으므로(점수는 값의 멀티셋으로 결정) 순열 대신 부분집합만 탐색한다.
"""

from __future__ import annotations

import itertools
import random
from typing import List, Optional, Sequence, Tuple

from .cards import Card
from . import scoring
from .engine import DraftAction, TurnAction, Observation


# ─────────────────────────────────────────────
#  배치 탐색 유틸
# ─────────────────────────────────────────────
def _joker_sort_key(c: Card) -> int:
    """정렬용: 조커는 최고값(7)으로 취급."""
    return 7 if c.is_joker else c.value


def best_combo_placement(existing: Sequence[Card], hand: Sequence[Card],
                         empty_slots: int, min_cards: int = 1,
                         reserve: int = 0, combo_only: bool = True
                         ) -> Optional[List[int]]:
    """현재 필드(existing) 위에 hand 에서 카드를 더 놓아 최고 콤보를 만드는 손패 인덱스 목록.

    OppAuto.FindComboPlacement 의 비교 규칙: 등급↑ → 점수↑ → 카드수↓.
    combo_only=True 면 원페어 이상만 채택(하이카드 무시).
    reserve: 패에 남길 최소 장수. 없으면 None 반환.
    """
    n = len(hand)
    max_place = min(n - reserve, empty_slots, n)
    if max_place <= 0:
        return None
    min_place = max(1, min_cards)
    min_place = min(min_place, max_place)

    best_rank = -1
    best_score = -1.0
    best_count = 1 << 30
    best_sel: Optional[List[int]] = None

    existing = list(existing)
    for k in range(min_place, max_place + 1):
        for combo in itertools.combinations(range(n), k):
            cards = existing + [hand[i] for i in combo]
            score, rule, _ = scoring.best_combo(cards)
            rank = scoring.COMBO_RANK.get(rule, 0)
            if combo_only and rank == 0:
                continue
            better = (rank > best_rank
                      or (rank == best_rank and score > best_score)
                      or (rank == best_rank and score == best_score and k < best_count))
            if better:
                best_rank, best_score, best_count = rank, score, k
                best_sel = list(combo)
    return best_sel


def fill_highest(hand: Sequence[Card], count: int) -> List[int]:
    """값이 높은 카드부터 count 장의 손패 인덱스. (콤보 없을 때 채우기용)"""
    order = sorted(range(len(hand)), key=lambda i: _joker_sort_key(hand[i]), reverse=True)
    return order[:max(0, count)]


def assign_to_slots(hand_indices: Sequence[int], empty_slots: Sequence[int]
                    ) -> List[Tuple[int, int]]:
    """선택한 손패 인덱스를 빈 슬롯에 순서대로 매핑 (위치는 점수 무관)."""
    return [(h, empty_slots[i]) for i, h in enumerate(hand_indices)
            if i < len(empty_slots)]


def hand_potential(hand: Sequence[Card]) -> Tuple[float, str]:
    """손패 전체가 만들 수 있는 최고 콤보 (드래프트 평가용, ChooseBestForOpp 근사)."""
    if not hand:
        return 0.0, ""
    score, rule, _ = scoring.best_combo(list(hand))
    return score, rule


# ─────────────────────────────────────────────
#  인터페이스
# ─────────────────────────────────────────────
class Agent:
    name = "agent"

    def choose_draft(self, obs: Observation) -> DraftAction:
        raise NotImplementedError

    def choose_turn(self, obs: Observation) -> TurnAction:
        raise NotImplementedError


# ─────────────────────────────────────────────
#  RandomAgent
# ─────────────────────────────────────────────
class RandomAgent(Agent):
    name = "random"

    def __init__(self, seed: Optional[int] = None):
        self.rng = random.Random(seed)

    def choose_draft(self, obs: Observation) -> DraftAction:
        return DraftAction(self.rng.randrange(2))

    def choose_turn(self, obs: Observation) -> TurnAction:
        empties = list(obs.my_empty_slots)
        hand_n = len(obs.my_hand)
        if not empties or hand_n == 0:
            # 잔류 카드가 있으면 자세만 무작위
            guard = self.rng.random() < 0.5 if obs.my_field_cards() else False
            return TurnAction(placements=[], guard=guard)
        k = self.rng.randint(0, min(len(empties), hand_n))
        hand_idx = self.rng.sample(range(hand_n), k)
        placements = assign_to_slots(hand_idx, empties)
        guard = self.rng.random() < 0.5
        return TurnAction(placements=placements, guard=guard)


# ─────────────────────────────────────────────
#  HeuristicAgent (OppAuto 포팅)
# ─────────────────────────────────────────────
class HeuristicAgent(Agent):
    name = "heuristic"

    def __init__(self, seed: Optional[int] = None, guard_chance: float = 0.35,
                 min_reserve: int = 3, enable_strategic_skip: bool = True):
        self.rng = random.Random(seed)
        self.guard_chance = guard_chance
        self.min_reserve = min_reserve
        self.enable_strategic_skip = enable_strategic_skip

    # ── 드래프트: 손패 잠재 콤보를 가장 올리는 카드 선택 (ChooseBestForOpp) ──
    def choose_draft(self, obs: Observation) -> DraftAction:
        assert obs.draft_offer is not None
        a, b = obs.draft_offer
        sa, _ = hand_potential(list(obs.my_hand) + [a])
        sb, _ = hand_potential(list(obs.my_hand) + [b])
        if abs(sa - sb) < 1e-9:
            return DraftAction(self.rng.randrange(2))
        return DraftAction(0 if sa > sb else 1)

    # ── 배치/자세 ──
    def choose_turn(self, obs: Observation) -> TurnAction:
        existing = obs.my_field_cards()
        empties = list(obs.my_empty_slots)
        hand = list(obs.my_hand)

        # 상대 공격 상황 분석 (보이는 정보만)
        opp_attacking = (not obs.opp_field_guard) and obs.opp_field_count > 0
        opp_visible_cards = [c for c in obs.opp_field_visible if c is not None]
        opp_score = 0.0
        if opp_attacking and opp_visible_cards:
            opp_score, _ = scoring.best_combo(opp_visible_cards)[:2]

        # 1차 콤보 탐색 (reserve 적용)
        sel = best_combo_placement(existing, hand, len(empties),
                                   min_cards=1, reserve=self.min_reserve, combo_only=True)
        my_best_score = 0.0
        if sel is not None:
            my_best_score, _, _ = scoring.best_combo(existing + [hand[i] for i in sel])

        # 전략적 스킵: 상대가 안 치고 내 콤보가 약하고 손패 여유 있으면 패스
        if (self.enable_strategic_skip and not opp_attacking
                and my_best_score <= 15.0 and len(hand) < 8):
            return TurnAction(placements=[], guard=obs.my_field_guard and bool(existing))

        # 강제 방어: 상대 공격을 못 이기면 최소 2장으로 수비
        must_guard = False
        if opp_attacking and my_best_score < opp_score:
            must_guard = True
            sel2 = best_combo_placement(existing, hand, len(empties),
                                        min_cards=2, reserve=self.min_reserve, combo_only=True)
            if sel2 is not None:
                sel = sel2

        # 콤보가 없으면 높은 카드부터 채움 (forceMinCards 만족 / 최소 1장)
        if sel is None:
            force = 2 if must_guard else 1
            cap = min(len(empties), max(0, len(hand) - self.min_reserve))
            force = min(force, cap)
            if force <= 0:
                return TurnAction(placements=[], guard=obs.my_field_guard and bool(existing))
            sel = fill_highest(hand, force)

        placements = assign_to_slots(sel, empties)

        # 자세: 강제수비 / 기존 잔류 Guard / 확률
        has_existing_guard = obs.my_field_guard and bool(existing)
        use_guard = must_guard or has_existing_guard or (self.rng.random() < self.guard_chance)
        return TurnAction(placements=placements, guard=use_guard)
