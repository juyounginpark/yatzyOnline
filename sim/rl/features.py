"""관측 → 피처 벡터 변환 + 매크로 행동 정의.

턴 의사결정을 다루기 쉽게 5개의 매크로 전략으로 추상화한다(가변 배치 공간 → 고정 이산 공간).
드래프트는 제시 2장 중 선택(2 이산).
"""

from __future__ import annotations

from typing import List, Tuple

import numpy as np

from yatzy import scoring
from yatzy.engine import Observation, TurnAction, MAX_HP
from yatzy.agents import best_combo_placement, fill_highest, assign_to_slots, hand_potential

# ── 매크로 턴 행동 ──
SKIP = 0          # 배치 없음 (잔류 카드 있으면 기존 자세 유지)
ATTACK_BEST = 1   # 최고 콤보 배치 + Attack
GUARD_BEST = 2    # 최고 콤보 배치 + Guard
ATTACK_FILL = 3   # 빈 슬롯을 높은 카드로 채움 + Attack
GUARD_FILL2 = 4   # 2장 이상 배치 + Guard
NUM_TURN_ACTIONS = 5

NUM_DRAFT_ACTIONS = 2

HP_SCALE = MAX_HP   # 피처 정규화 기준 (engine.MAX_HP 추종)


def _value_counts(cards) -> np.ndarray:
    """[c1..c6, joker] 개수 벡터."""
    v = np.zeros(7, dtype=np.float32)
    for c in cards:
        if c is None:
            continue
        if c.is_joker:
            v[6] += 1
        elif 1 <= c.value <= 6:
            v[c.value - 1] += 1
    return v


def turn_features(obs: Observation) -> np.ndarray:
    hand = list(obs.my_hand)
    existing = obs.my_field_cards()
    empties = obs.my_empty_slots

    hand_counts = _value_counts(hand)
    field_counts = _value_counts(existing)

    field_score = scoring.best_combo(existing)[0] if existing else 0.0
    best_sel = best_combo_placement(existing, hand, len(empties),
                                    min_cards=1, reserve=0, combo_only=False)
    best_score = 0.0
    if best_sel is not None:
        best_score = scoring.best_combo(existing + [hand[i] for i in best_sel])[0]

    opp_visible = [c for c in obs.opp_field_visible if c is not None]
    opp_attacking = (not obs.opp_field_guard) and obs.opp_field_count > 0
    opp_score = scoring.best_combo(opp_visible)[0] if (opp_attacking and opp_visible) else 0.0

    known = np.zeros(7, dtype=np.float32)
    for (val, _t), cnt in obs.opp_known_drafted.items():
        idx = 6 if val == "J" else (val - 1)
        if 0 <= idx <= 6:
            known[idx] += cnt

    feat = np.concatenate([
        hand_counts / 8.0,
        np.array([len(hand) / 8.0], dtype=np.float32),
        field_counts / 5.0,
        np.array([
            field_score / 100.0,
            best_score / 100.0,
            1.0 if obs.my_field_guard else 0.0,
            len(existing) / 5.0,
            obs.opp_field_count / 5.0,
            1.0 if obs.opp_field_guard else 0.0,
            opp_score / 100.0,
        ], dtype=np.float32),
        known / 8.0,
        np.array([
            obs.my_hp / HP_SCALE,
            obs.opp_hp / HP_SCALE,
            (obs.my_hp - obs.opp_hp) / HP_SCALE,
            1.0 if obs.is_first_this_round else 0.0,
            min(obs.round_index, 50) / 50.0,
        ], dtype=np.float32),
    ])
    return feat.astype(np.float32)


def draft_features(obs: Observation) -> np.ndarray:
    """드래프트: 전역 상태 + 제시 2장 각각의 손패 잠재점수/값 one-hot."""
    assert obs.draft_offer is not None
    a, b = obs.draft_offer
    hand = list(obs.my_hand)
    base = hand_potential(hand)[0]
    sa = hand_potential(hand + [a])[0]
    sb = hand_potential(hand + [b])[0]

    def card_onehot(c) -> np.ndarray:
        v = np.zeros(7, dtype=np.float32)
        v[6 if c.is_joker else (c.value - 1)] = 1.0
        return v

    hand_counts = _value_counts(hand)
    feat = np.concatenate([
        hand_counts / 8.0,
        np.array([len(hand) / 8.0, base / 100.0], dtype=np.float32),
        card_onehot(a), np.array([(sa - base) / 100.0], dtype=np.float32),
        card_onehot(b), np.array([(sb - base) / 100.0], dtype=np.float32),
        np.array([
            obs.my_hp / HP_SCALE,
            obs.opp_hp / HP_SCALE,
            1.0 if obs.is_first_this_round else 0.0,
        ], dtype=np.float32),
    ])
    return feat.astype(np.float32)


def turn_action_mask(obs: Observation) -> np.ndarray:
    """유효한 매크로만 1. SKIP 은 항상 유효."""
    mask = np.zeros(NUM_TURN_ACTIONS, dtype=np.float32)
    mask[SKIP] = 1.0
    can_place = len(obs.my_empty_slots) > 0 and len(obs.my_hand) > 0
    if can_place:
        mask[ATTACK_BEST] = 1.0
        mask[GUARD_BEST] = 1.0
        mask[ATTACK_FILL] = 1.0
        mask[GUARD_FILL2] = 1.0
    return mask


def macro_to_action(obs: Observation, macro: int) -> TurnAction:
    """매크로 → 구체 TurnAction."""
    hand = list(obs.my_hand)
    existing = obs.my_field_cards()
    empties = list(obs.my_empty_slots)

    if macro == SKIP or not empties or not hand:
        return TurnAction(placements=[], guard=obs.my_field_guard and bool(existing))

    if macro in (ATTACK_BEST, GUARD_BEST):
        sel = best_combo_placement(existing, hand, len(empties),
                                   min_cards=1, reserve=0, combo_only=False)
        if sel is None:
            sel = fill_highest(hand, 1)
        guard = (macro == GUARD_BEST)
    elif macro == ATTACK_FILL:
        sel = fill_highest(hand, min(len(empties), len(hand)))
        guard = False
    else:  # GUARD_FILL2
        sel = best_combo_placement(existing, hand, len(empties),
                                   min_cards=2, reserve=0, combo_only=False)
        if sel is None:
            sel = fill_highest(hand, min(2, len(empties), len(hand)))
        guard = True

    placements = assign_to_slots(sel, empties)
    return TurnAction(placements=placements, guard=guard)


# 피처 차원 (네트워크 입력 크기 결정용) — 더미 관측으로 계산하지 않고 상수로 고정.
TURN_FEATURE_DIM = 7 + 1 + 7 + 7 + 7 + 5   # = 34
DRAFT_FEATURE_DIM = 7 + 2 + 7 + 1 + 7 + 1 + 3  # = 28
