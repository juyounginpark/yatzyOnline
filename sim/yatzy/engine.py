"""전체 게임 루프 — MainFlow.cs / CardDraft.cs / OppAuto(생존파괴) 규칙을 포팅.

게임 구조 (MainFlow):
- HP 1000 시작, 슬롯 5칸, 손패 최대 8장(초기 3장).
- 라운드 = 2턴(선공/후공). 매 라운드 선공이 교대.
- 각 턴: 드래프트(2장 제시→1장 선택, 1장 상대) → 배치 → Attack/Guard 자세 → 종료.
- 두 턴이 끝나면 ResolutionPhase: 양측 필드 점수 비교 → 본체 데미지 + 카드 수명 처리.

자세(Attack/Guard)는 필드 전체 단위. 카드를 새로 놓으면 Attack 이 되지만(OnPlayerCardPlaced),
플레이어가 다시 Guard 로 토글할 수 있으므로 자세는 에이전트의 자유 선택으로 모델링한다.

상대 카드 기억: 드래프트는 양측에 완전 공개(determinism 계약)이므로,
상대 손패로 들어간 카드를 누적 기록(knowledge)하여 관측에 노출한다.
"""

from __future__ import annotations

import random
from collections import Counter
from dataclasses import dataclass, field
from typing import Callable, List, Optional, Sequence, Tuple

from .cards import Card, CardDealer, CardType, DeckConfig, DEFAULT_DECK
from . import scoring

# ── 규칙 상수 (Unity 인스펙터 기본값) ──
MAX_HP = 300.0            # 시작 HP (요청에 따라 1000 → 300)
NUM_SLOTS = 5
INITIAL_HAND = 3          # Deck.drawCount
MAX_HAND = 8              # Deck.maxCards
JACKPOT_MIN = 200.0       # GameFlow.jokerJackpotMin
JACKPOT_MAX = 300.0       # GameFlow.jokerJackpotMax
DEFAULT_MAX_ROUNDS = 300  # 무한 교착 방지용 캡 (원본엔 없음)


# ─────────────────────────────────────────────
#  행동 (에이전트가 반환)
# ─────────────────────────────────────────────
@dataclass
class DraftAction:
    """제시된 2장 중 어느 쪽을 내 손패로 가져갈지. keep_index ∈ {0, 1}."""

    keep_index: int = 0


@dataclass
class TurnAction:
    """이번 턴 배치 + 자세.

    placements: (hand_index, slot_index) 목록. hand_index 는 관측 시점 my_hand 기준.
    guard: 필드가 카드를 가질 때 최종 자세 (True=Guard 뒷면, False=Attack 앞면).
    """

    placements: List[Tuple[int, int]] = field(default_factory=list)
    guard: bool = False


# ─────────────────────────────────────────────
#  관측 (에이전트에게 전달)
# ─────────────────────────────────────────────
@dataclass
class Observation:
    me: int
    round_index: int
    is_first_this_round: bool       # 이번 라운드 내가 선공인가
    my_hp: float
    opp_hp: float

    my_hand: List[Card]
    my_field: List[Optional[Card]]  # 길이 NUM_SLOTS, 빈칸 None (이미 놓인 잔류 카드 포함)
    my_field_guard: bool
    my_empty_slots: List[int]

    opp_field_count: int
    opp_field_guard: bool                       # 상대가 현재 Guard 중인지 (개수/자세는 공개)
    opp_field_visible: List[Optional[Card]]     # 앞면(Attack)일 때만 값 노출, 아니면 None
    opp_known_drafted: Counter                  # 상대가 드래프트로 가져간 카드 누적 기억

    # 드래프트 단계에서만 채워짐 (배치 단계엔 None)
    draft_offer: Optional[Tuple[Card, Card]] = None

    shared_seed: int = 0
    pool: Optional[List[Card]] = None

    # ── 편의 메서드 ──
    def my_hand_values(self) -> List[int]:
        return [0 if c.is_joker else c.value for c in self.my_hand]

    def my_field_cards(self) -> List[Card]:
        return [c for c in self.my_field if c is not None]


# ─────────────────────────────────────────────
#  플레이어 상태
# ─────────────────────────────────────────────
@dataclass
class PlayerState:
    hp: float = MAX_HP
    hand: List[Card] = field(default_factory=list)
    field: List[Optional[Card]] = field(default_factory=lambda: [None] * NUM_SLOTS)
    field_guard: bool = False

    def field_cards(self) -> List[Card]:
        return [c for c in self.field if c is not None]

    def field_count(self) -> int:
        return sum(1 for c in self.field if c is not None)

    def empty_slots(self) -> List[int]:
        return [i for i, c in enumerate(self.field) if c is None]

    def has_field(self) -> bool:
        return self.field_count() > 0

    def is_guard(self) -> bool:
        # AnyFieldGuard: 카드가 있고 field_guard 일 때만 유효
        return self.field_guard and self.has_field()


# ─────────────────────────────────────────────
#  로그 이벤트 (텍스트 시뮬레이션 출력용)
# ─────────────────────────────────────────────
Logger = Callable[[str], None]


def _noop(_: str) -> None:
    pass


# ─────────────────────────────────────────────
#  게임 본체
# ─────────────────────────────────────────────
class Game:
    def __init__(self, agents: Sequence["object"], deck_config: DeckConfig = DEFAULT_DECK,
                 seed: Optional[int] = None, max_rounds: int = DEFAULT_MAX_ROUNDS,
                 logger: Logger = _noop):
        assert len(agents) == 2, "두 명의 에이전트가 필요합니다."
        self.agents = list(agents)
        self.deck_config = deck_config
        self.rng = random.Random(seed)
        # 공유 시드 (조커 잭팟/판정 RNG). 온라인 MainFlow 의 _sharedSeed 대응.
        self.shared_seed = self.rng.randrange(0, 2 ** 31 - 1) if seed is None else seed
        self.dealer = CardDealer(deck_config, random.Random(self.shared_seed))
        self.max_rounds = max_rounds
        self.log = logger

        self.players = [PlayerState(), PlayerState()]
        # knowledge[p] = p 가 기억하는 "상대가 드래프트로 가져간 카드" 누적
        self.knowledge: List[Counter] = [Counter(), Counter()]
        self.round_index = 0
        self.first_player = 0   # 호스트(=player0) 선공. 라운드마다 교대.

        # 초기 손패 (Deck.DrawCards: drawCount 장). 상대에겐 비공개이므로 knowledge 갱신 안 함.
        for p in self.players:
            for _ in range(INITIAL_HAND):
                p.hand.append(self.dealer.draw())

    # ───────────── 카드 이동 헬퍼 ─────────────
    def _give_card(self, pidx: int, card: Card, via_draft: bool) -> None:
        """카드를 pidx 손패에 추가. 드래프트 경유면 상대(1-pidx)의 기억에 기록(완전 공개)."""
        self.players[pidx].hand.append(card)
        if via_draft:
            key = ("J", card.card_type) if card.is_joker else (card.value, card.card_type)
            self.knowledge[1 - pidx][key] += 1

    # ───────────── 드래프트 ─────────────
    def _do_draft(self, pidx: int) -> None:
        """CardDraft.StartDraft 의 손패 한도 분기 포팅."""
        me = self.players[pidx]
        opp = self.players[1 - pidx]
        me_full = len(me.hand) >= MAX_HAND
        opp_full = len(opp.hand) >= MAX_HAND

        if me_full and opp_full:
            self.log(f"  [드래프트] 양쪽 손패 max — 스킵")
            return

        if me_full or opp_full:
            # 단일 카드: max 가 아닌 쪽으로
            card = self.dealer.draw()
            to_player = not me_full  # me 가 full 이면 상대에게
            target = pidx if to_player else (1 - pidx)
            self._give_card(target, card, via_draft=True)
            self.log(f"  [드래프트] 단일카드 {card} → P{target}")
            return

        # 일반: 2장 제시 → pidx 가 1장 선택
        offer = (self.dealer.draw(), self.dealer.draw())
        obs = self._build_observation(pidx, draft_offer=offer)
        action = self.agents[pidx].choose_draft(obs)
        keep = action.keep_index if action and action.keep_index in (0, 1) else 0
        kept = offer[keep]
        given = offer[1 - keep]
        self._give_card(pidx, kept, via_draft=True)
        self._give_card(1 - pidx, given, via_draft=True)
        self.log(f"  [드래프트] 제시 {offer} → P{pidx} 가 {kept} 선택, {given} → P{1 - pidx}")

    # ───────────── 배치/자세 ─────────────
    def _do_turn_placement(self, pidx: int) -> None:
        me = self.players[pidx]
        obs = self._build_observation(pidx, draft_offer=None)
        action = self.agents[pidx].choose_turn(obs)
        if action is None:
            action = TurnAction()

        # placements 적용 — 인덱스 안정성을 위해 카드 참조를 먼저 확보
        empties = set(me.empty_slots())
        chosen: List[Tuple[Card, int]] = []
        used_hand: set = set()
        used_slot: set = set()
        for hand_idx, slot_idx in action.placements:
            if hand_idx in used_hand or slot_idx in used_slot:
                continue
            if not (0 <= hand_idx < len(me.hand)):
                continue
            if slot_idx not in empties or slot_idx in used_slot:
                continue
            chosen.append((me.hand[hand_idx], slot_idx))
            used_hand.add(hand_idx)
            used_slot.add(slot_idx)

        # 손패에서 제거 (높은 인덱스부터)
        for hand_idx in sorted(used_hand, reverse=True):
            me.hand.pop(hand_idx)
        for card, slot_idx in chosen:
            me.field[slot_idx] = card

        placed_new = len(chosen) > 0

        # 자세 결정:
        #  - 카드를 새로 놓았으면(OnPlayerCardPlaced→Attack) 이후 toggle 가능 → 에이전트 guard 값 채택
        #  - 새로 안 놓았고 잔류 카드가 있으면 기존 자세 유지(에이전트가 toggle 가능하게 guard 값 채택)
        if me.has_field():
            me.field_guard = bool(action.guard)
        else:
            me.field_guard = False

        if placed_new or me.has_field():
            stance = "Guard" if me.is_guard() else "Attack"
            self.log(f"  [P{pidx}] 배치 {[c.label() for c in me.field_cards()]} "
                     f"({stance}), 손패 {len(me.hand)}장")
        else:
            self.log(f"  [P{pidx}] 배치 없음 (손패 {len(me.hand)}장)")

    def _play_turn(self, pidx: int) -> None:
        self.log(f"-- P{pidx} 턴 (HP {self.players[0].hp:.0f} vs {self.players[1].hp:.0f}) --")
        self._do_draft(pidx)
        self._do_turn_placement(pidx)

    # ───────────── 관측 빌드 ─────────────
    def _build_observation(self, pidx: int, draft_offer) -> Observation:
        me = self.players[pidx]
        opp = self.players[1 - pidx]
        opp_visible: List[Optional[Card]] = []
        for c in opp.field:
            if c is not None and not opp.is_guard():
                opp_visible.append(c)   # Attack = 앞면 공개
            else:
                opp_visible.append(None)  # Guard/뒷면 또는 빈칸 → 값 비공개
        return Observation(
            me=pidx,
            round_index=self.round_index,
            is_first_this_round=(self.first_player == pidx),
            my_hp=me.hp,
            opp_hp=opp.hp,
            my_hand=list(me.hand),
            my_field=list(me.field),
            my_field_guard=me.is_guard(),
            my_empty_slots=me.empty_slots(),
            opp_field_count=opp.field_count(),
            opp_field_guard=opp.is_guard(),
            opp_field_visible=opp_visible,
            opp_known_drafted=Counter(self.knowledge[pidx]),
            draft_offer=draft_offer,
            shared_seed=self.shared_seed,
            pool=self.dealer.pool,
        )

    # ───────────── 판정 (ResolutionPhaseRoutine) ─────────────
    def _score(self, pidx: int) -> Tuple[float, str]:
        s, rule, _ = scoring.best_combo(self.players[pidx].field, self.shared_seed,
                                        JACKPOT_MIN, JACKPOT_MAX)
        return s, rule

    def _resolution(self) -> None:
        # 판정 결정적 RNG (MainFlow: _sharedSeed + _roundIndex*999983), 그 후 round_index++
        res_seed = (self.shared_seed + self.round_index * 999983) & 0x7FFFFFFF
        res_rng = random.Random(res_seed)
        self.round_index += 1

        p0, p1 = self.players
        p_score, p_combo = self._score(0)
        o_score, o_combo = self._score(1)
        p_guard = p0.is_guard()
        o_guard = p1.is_guard()
        p_has = p0.has_field()
        o_has = p1.has_field()

        self.log(f"== 판정: P0 {p_score:.1f}({p_combo}) guard={p_guard} | "
                 f"P1 {o_score:.1f}({o_combo}) guard={o_guard} ==")

        # FieldOutcome: None/Fly/Grave/Survive/Stay
        NONE, FLY, GRAVE, SURVIVE, STAY = "None", "Fly", "Grave", "Survive", "Stay"
        p_out = o_out = NONE
        dmg_to_o = 0.0  # P0 -> P1
        dmg_to_p = 0.0  # P1 -> P0

        if p_has and not o_has:
            if not p_guard:
                p_out = FLY; dmg_to_o = p_score
            else:
                p_out = STAY
        elif not p_has and o_has:
            if not o_guard:
                o_out = FLY; dmg_to_p = o_score
            else:
                o_out = STAY
        elif p_has and o_has:
            if not p_guard and not o_guard:
                p_out = FLY; o_out = FLY
                dmg_to_o = p_score; dmg_to_p = o_score
            elif not p_guard and o_guard:
                if o_score >= p_score:           # Guard 승 (동점 Guard 우세)
                    p_out = GRAVE; o_out = SURVIVE
                else:
                    p_out = FLY; dmg_to_o = p_score - o_score
                    o_out = GRAVE
            elif p_guard and not o_guard:
                if p_score >= o_score:
                    o_out = GRAVE; p_out = SURVIVE
                else:
                    o_out = FLY; dmg_to_p = o_score - p_score
                    p_out = GRAVE
            else:  # Guard vs Guard
                if p_score > o_score:
                    o_out = GRAVE; p_out = SURVIVE; dmg_to_o = p_score + o_score
                elif o_score > p_score:
                    p_out = GRAVE; o_out = SURVIVE; dmg_to_p = p_score + o_score
                else:
                    p_out = GRAVE; o_out = GRAVE  # 무승부, 데미지 없음

        # 데미지 적용 (라운드당 1회)
        if dmg_to_o > 0:
            p1.hp = max(0.0, p1.hp - dmg_to_o)
        if dmg_to_p > 0:
            p0.hp = max(0.0, p0.hp - dmg_to_p)
        if dmg_to_o or dmg_to_p:
            self.log(f"   데미지: P1 -{dmg_to_o:.1f}, P0 -{dmg_to_p:.1f} "
                     f"→ HP {p0.hp:.0f} vs {p1.hp:.0f}")

        # 카드 수명 처리
        self._apply_outcome(0, p_out, res_rng)
        self._apply_outcome(1, o_out, res_rng)

    def _apply_outcome(self, pidx: int, outcome: str, res_rng: random.Random) -> None:
        p = self.players[pidx]
        cards = p.field_cards()
        if outcome in ("Fly", "Grave"):
            # 소모/파괴 — 손패로 복귀하지 않음
            p.field = [None] * NUM_SLOTS
            p.field_guard = False
        elif outcome == "Survive":
            destroyed = self._guard_destroy_count(len(cards), res_rng)
            # 무작위 파괴 대상 선택 (룰렛 finalIdx ≈ 무작위)
            order = list(range(len(cards)))
            res_rng.shuffle(order)
            to_destroy = set(order[:destroyed])
            survivors = [c for i, c in enumerate(cards) if i not in to_destroy]
            p.field = [None] * NUM_SLOTS
            p.field_guard = False
            for c in survivors:
                p.hand.append(c)  # ReturnCardToHand (개수 제한 없음)
            self.log(f"   P{pidx} Guard 생존: {len(cards)}장 중 {destroyed}장 파괴, "
                     f"{len(survivors)}장 패 복귀")
        elif outcome == "Stay":
            # 뒷면 그대로 잔류 — 필드 유지, guard 유지
            self.log(f"   P{pidx} Guard 잔류: {len(cards)}장 필드 유지")
        # None: 아무것도 안 함

    @staticmethod
    def _guard_destroy_count(count: int, res_rng: random.Random) -> int:
        """ProcessGuardSurvivors 의 파괴 장수 결정 로직."""
        if count <= 0:
            return 0
        if count == 1:
            return 1 if res_rng.random() < 0.5 else 0
        if count == 2:
            return 1
        aggressive = count - 2      # remain 2
        conservative = count // 2   # remain ≈ 절반
        return aggressive if res_rng.random() < 0.7 else conservative

    # ───────────── 매치 진행 ─────────────
    def play(self) -> "MatchResult":
        """한 매치를 끝까지 진행하고 결과 반환."""
        while True:
            if self.round_index >= self.max_rounds:
                return self._finish(reason="max_rounds")

            self.log(f"\n=== 라운드 {self.round_index} (선공 P{self.first_player}) ===")
            order = [self.first_player, 1 - self.first_player]
            for pidx in order:
                self._play_turn(pidx)

            self._resolution()

            if self.players[0].hp <= 0 or self.players[1].hp <= 0:
                return self._finish(reason="ko")

            # 다음 라운드 선공 교대
            self.first_player = 1 - self.first_player

    def _finish(self, reason: str) -> "MatchResult":
        h0, h1 = self.players[0].hp, self.players[1].hp
        if h0 <= 0 and h1 <= 0:
            winner = -1  # 동시 사망 = 무승부
        elif h1 <= 0:
            winner = 0
        elif h0 <= 0:
            winner = 1
        else:
            winner = 0 if h0 > h1 else (1 if h1 > h0 else -1)
        self.log(f"\n*** 종료({reason}): "
                 f"{'무승부' if winner < 0 else f'P{winner} 승'} "
                 f"(HP {h0:.0f} vs {h1:.0f}, {self.round_index} 라운드) ***")
        return MatchResult(winner=winner, hp=(h0, h1), rounds=self.round_index, reason=reason)


@dataclass
class MatchResult:
    winner: int                 # 0, 1, 또는 -1(무승부)
    hp: Tuple[float, float]
    rounds: int
    reason: str


def play_match(agent0, agent1, deck_config: DeckConfig = DEFAULT_DECK,
               seed: Optional[int] = None, max_rounds: int = DEFAULT_MAX_ROUNDS,
               logger: Logger = _noop) -> MatchResult:
    """두 에이전트로 한 판 대전. (편의 함수)"""
    return Game([agent0, agent1], deck_config, seed, max_rounds, logger).play()
