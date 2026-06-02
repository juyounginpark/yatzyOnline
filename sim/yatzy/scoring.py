"""핸드 판정 — GameFlow.cs 의 점수 규칙을 1:1 로 포팅.

원본(Assets/Scripts/Game/GameFlow.cs) 대응 함수:
- EvaluateHand               -> evaluate_hand
- ScoreFiveOfAKind 등 개별   -> _score_* (내부)
- ResolveJokersOptimal       -> resolve_jokers_optimal
- TryJokerJackpot / 잭팟 점수 -> joker_jackpot_score
- GetBestCombo               -> best_combo

콤보 우선순위(EvaluateHand 의 평가 순서)와 점수 공식은 원본과 정확히 동일하다.
검증값(테스트)도 원본 공식에서 직접 계산한 값을 사용한다.
"""

from __future__ import annotations

import random
from typing import List, Optional, Sequence, Tuple

from .cards import Card

# ── 콤보 이름 (GameFlow 의 ruleName 문자열 그대로) ──
FIVE = "파이브카드"
FOUR = "포카드"
FULL_HOUSE = "풀하우스"
STRAIGHT_HIGH = "스트레이트(하이)"
STRAIGHT_LOW = "스트레이트(로우)"
SMALL_STRAIGHT = "스몰스트레이트"
TRIPLE = "트리플"
TWO_PAIR = "투페어"
ONE_PAIR = "원페어"
HIGH_CARD = "하이카드"
JOKER_JACKPOT = "???"  # GameFlow.jokerComboName 기본값

# 콤보 등급 (OppAuto.ComboRank). 높을수록 강함. 하이카드/없음 = 0.
COMBO_RANK = {
    FIVE: 9,
    FOUR: 8,
    FULL_HOUSE: 7,
    STRAIGHT_HIGH: 6,
    STRAIGHT_LOW: 5,
    SMALL_STRAIGHT: 4,
    TRIPLE: 3,
    TWO_PAIR: 2,
    ONE_PAIR: 1,
}


def _count_dice(dice: Sequence[int]) -> List[int]:
    """index 1..6 에 개수. (GameFlow.CountDice)"""
    counts = [0] * 7
    for d in dice:
        if 1 <= d <= 6:
            counts[d] += 1
    return counts


def _score_five(counts: List[int]) -> float:
    for i in range(6, 0, -1):
        if counts[i] >= 5:
            return 95.0 + i * 0.5
    return 0.0


def _score_four(counts: List[int]) -> float:
    four_val = 0
    for i in range(6, 0, -1):
        if counts[i] >= 4:
            four_val = i
            break
    if four_val == 0:
        return 0.0
    kicker = 0
    for i in range(6, 0, -1):
        if i != four_val and counts[i] > 0:
            kicker = i
            break
    return 80.0 + four_val * 1.0 + kicker * 0.1


def _score_full_house(counts: List[int]) -> float:
    triple_val = 0
    pair_val = 0
    for i in range(6, 0, -1):
        if counts[i] >= 3 and triple_val == 0:
            triple_val = i
        elif counts[i] >= 2 and pair_val == 0:
            pair_val = i
    if triple_val > 0 and pair_val > 0:
        return 65.0 + triple_val * 1.0 + pair_val * 0.1
    return 0.0


def _score_straight_high(unique: set) -> float:
    if {2, 3, 4, 5, 6} <= unique:
        return 70.0
    return 0.0


def _score_straight_low(unique: set) -> float:
    if {1, 2, 3, 4, 5} <= unique:
        return 65.0
    return 0.0


def _score_small_straight(unique: set) -> float:
    # 높은 쪽부터 (3456 -> 2345 -> 1234)
    if {3, 4, 5, 6} <= unique:
        return 56.0 + 6 * 0.5  # 59
    if {2, 3, 4, 5} <= unique:
        return 56.0 + 5 * 0.5  # 58.5
    if {1, 2, 3, 4} <= unique:
        return 56.0 + 4 * 0.5  # 58
    return 0.0


def _score_triple(counts: List[int]) -> float:
    triple_val = 0
    for i in range(6, 0, -1):
        if counts[i] >= 3:
            triple_val = i
            break
    if triple_val == 0:
        return 0.0
    kickers: List[int] = []
    for i in range(6, 0, -1):
        if i == triple_val:
            continue
        kickers.extend([i] * counts[i])
    big = kickers[0] if len(kickers) > 0 else 0
    small = kickers[1] if len(kickers) > 1 else 0
    return 40.0 + triple_val * 2.0 + big * 0.3 + small * 0.1


def _score_two_pair(counts: List[int]) -> float:
    pairs: List[int] = []
    for i in range(6, 0, -1):
        if counts[i] >= 2:
            pairs.append(i)
    if len(pairs) < 2:
        return 0.0
    big_pair, small_pair = pairs[0], pairs[1]
    temp = list(counts)
    temp[big_pair] -= 2
    temp[small_pair] -= 2
    kicker = 0
    for i in range(6, 0, -1):
        if temp[i] > 0:
            kicker = i
            break
    return 25.0 + big_pair * 2.0 + small_pair * 0.3 + kicker * 0.1


def _score_one_pair(counts: List[int]) -> float:
    pair_val = 0
    for i in range(6, 0, -1):
        if counts[i] >= 2:
            pair_val = i
            break
    if pair_val == 0:
        return 0.0
    temp = list(counts)
    temp[pair_val] -= 2
    kickers: List[int] = []
    for i in range(6, 0, -1):
        kickers.extend([i] * temp[i])
    big = kickers[0] if len(kickers) > 0 else 0
    mid = kickers[1] if len(kickers) > 1 else 0
    small = kickers[2] if len(kickers) > 2 else 0
    return 10.0 + pair_val * 2.0 + big * 0.5 + mid * 0.2 + small * 0.1


def _score_high_card(dice: Sequence[int]) -> float:
    if not dice:
        return 0.0
    desc = sorted(dice, reverse=True)
    biggest = desc[0] if len(desc) > 0 else 0
    second = desc[1] if len(desc) > 1 else 0
    third = desc[2] if len(desc) > 2 else 0
    fourth = desc[3] if len(desc) > 3 else 0
    return biggest * 2.0 + second * 0.8 + third * 0.3 + fourth * 0.1


def evaluate_hand(dice: Sequence[int]) -> Tuple[float, str]:
    """카드 값 배열을 받아 (점수, 콤보명) 반환. GameFlow.EvaluateHand 와 동일 순서.

    dice 는 1..6 값의 리스트(조커는 이미 값으로 해석된 상태). 빈 배열이면 (0, "").
    """
    if not dice:
        return 0.0, ""

    counts = _count_dice(dice)
    unique = set(d for d in dice if 1 <= d <= 6)

    s = _score_five(counts)
    if s > 0.0:
        return s, FIVE
    s = _score_four(counts)
    if s > 0.0:
        return s, FOUR
    s = _score_full_house(counts)
    if s > 0.0:
        return s, FULL_HOUSE
    s = _score_straight_high(unique)
    if s > 0.0:
        return s, STRAIGHT_HIGH
    s = _score_straight_low(unique)
    if s > 0.0:
        return s, STRAIGHT_LOW
    s = _score_small_straight(unique)
    if s > 0.0:
        return s, SMALL_STRAIGHT
    s = _score_triple(counts)
    if s > 0.0:
        return s, TRIPLE
    s = _score_two_pair(counts)
    if s > 0.0:
        return s, TWO_PAIR
    s = _score_one_pair(counts)
    if s > 0.0:
        return s, ONE_PAIR
    s = _score_high_card(dice)
    if s > 0.0:
        return s, HIGH_CARD
    return 0.0, ""


def resolve_jokers_optimal(values: Sequence[int], joker_flags: Sequence[bool]) -> List[int]:
    """조커를 1~6 전부 대입해 최고 점수가 되는 값 배열 반환. GameFlow.ResolveJokersOptimal.

    values: 슬롯 값 배열 (빈칸/조커는 0). joker_flags: 같은 길이의 조커 표시.
    """
    values = list(values)
    joker_indices = [i for i, f in enumerate(joker_flags) if f]
    if not joker_indices:
        return values

    best_values = list(values)
    best_score = -1.0
    total = 6 ** len(joker_indices)

    for combo in range(total):
        trial = list(values)
        c = combo
        for j in joker_indices:
            trial[j] = (c % 6) + 1
            c //= 6
        filled = [v for v in trial if v > 0]
        if not filled:
            continue
        score, _ = evaluate_hand(filled)
        if score > best_score:
            best_score = score
            best_values = list(trial)

    return best_values


def joker_jackpot_score(shared_seed: int, jackpot_min: float = 200.0,
                        jackpot_max: float = 300.0) -> float:
    """조커 5장 잭팟 점수. GameFlow.GetJokerJackpotScore 와 동일하게 공유 시드 기반.

    원본: new System.Random(seed).Next(round(min), round(max)+1)
    System.Random.Next(a, b) == [a, b) 정수. round(max)+1 이므로 max 포함.
    """
    rng = random.Random(shared_seed)
    lo = int(round(jackpot_min))
    hi = int(round(jackpot_max)) + 1
    # random.Random.randrange(lo, hi) == [lo, hi) — System.Random.Next 와 동일 반열림 구간
    return float(rng.randrange(lo, hi))


def best_combo(cards: Sequence[Optional[Card]], shared_seed: int = 0,
               jackpot_min: float = 200.0, jackpot_max: float = 300.0
               ) -> Tuple[float, str, List[int]]:
    """슬롯에 놓인 카드들로 (최고점, 콤보명, 조커해석값배열) 계산. GameFlow.GetBestCombo.

    cards: 슬롯 카드 리스트(빈 슬롯은 None). 길이 = 슬롯 수.
    조커 5장 전부면 잭팟.
    """
    n = len(cards)
    values = [0] * n
    joker_flags = [False] * n
    placed = 0
    joker = 0
    for i, c in enumerate(cards):
        if c is None:
            continue
        placed += 1
        joker_flags[i] = c.is_joker
        values[i] = 0 if c.is_joker else c.value
        if c.is_joker:
            joker += 1

    resolved = resolve_jokers_optimal(values, joker_flags)

    # 조커 5장 특수 콤보 (TryJokerJackpot: joker==5 and placed==5)
    if joker == 5 and placed == 5:
        return (joker_jackpot_score(shared_seed, jackpot_min, jackpot_max),
                JOKER_JACKPOT, resolved)

    filled = [v for v in resolved if v > 0]
    if not filled:
        return 0.0, "", resolved
    score, rule = evaluate_hand(filled)
    return score, rule, resolved
