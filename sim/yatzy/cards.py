"""카드 및 덱 구성.

Unity 측 대응:
- CardValue.cs   : CardType(enum), value/isJoker/cardType
- Deck.cs        : DeckGroup/CardEntry -> BuildPrefabPool (활성 그룹의 카드들을 균등 풀로)
                   7번째(index 6) 카드는 조커. 값은 1..6.
"""

from __future__ import annotations

import enum
import random
from dataclasses import dataclass, field
from typing import List, Optional


class CardType(enum.IntEnum):
    """CardValue.cs 의 enum 순서를 그대로 따른다 (정렬값으로도 쓰임)."""

    ATTACK = 0
    CRITICAL = 1
    HEAL = 2
    CHAIN = 3


@dataclass(frozen=True)
class Card:
    """카드 한 장. 조커면 value 는 와일드(판정 시 1~6 중 최적값으로 해석)."""

    value: int          # 1..6 (조커는 0 으로 두고 is_joker=True)
    card_type: CardType = CardType.ATTACK
    is_joker: bool = False

    def label(self) -> str:
        return "J" if self.is_joker else str(self.value)

    def __repr__(self) -> str:  # 디버그/로그 가독성
        t = self.card_type.name[0]
        return f"J:{t}" if self.is_joker else f"{self.value}:{t}"


@dataclass
class DeckGroup:
    """Deck.cs 의 DeckGroup 대응. cards 는 길이<=7, index 6 이 조커 슬롯."""

    group_type: CardType = CardType.ATTACK
    is_active: bool = True
    # 각 인덱스에 카드가 "존재"하는지 (Deck 인스펙터에서 prefab 지정 여부에 대응)
    present: List[bool] = field(default_factory=lambda: [True] * 7)


@dataclass
class DeckConfig:
    """드래프트/초기 손패가 카드를 뽑는 풀.

    Deck.BuildPrefabPool 과 동일하게, 활성 그룹의 존재하는 카드 엔트리를
    하나의 균등 풀로 합친다. 같은 값이 여러 그룹(타입)에 있으면 그만큼 비중↑.
    """

    groups: List[DeckGroup] = field(
        default_factory=lambda: [DeckGroup(CardType.ATTACK, True, [True] * 7)]
    )

    def build_pool(self) -> List[Card]:
        pool: List[Card] = []
        for g in self.groups:
            if not g.is_active:
                continue
            for i in range(min(7, len(g.present))):
                if not g.present[i]:
                    continue
                if i == 6:
                    pool.append(Card(value=0, card_type=g.group_type, is_joker=True))
                else:
                    pool.append(Card(value=i + 1, card_type=g.group_type))
        return pool


# 기본 덱: Attack 그룹 1개, 값 1~6 + 조커 (각 1/7 확률) — Deck.cs 기본 단일 그룹과 동일.
DEFAULT_DECK = DeckConfig()


class CardDealer:
    """결정적 RNG 로 풀에서 카드를 뽑는다 (Deck.DrawCards(seed) 대응)."""

    def __init__(self, config: DeckConfig, rng: random.Random):
        self._pool = config.build_pool()
        if not self._pool:
            raise ValueError("덱 풀이 비어 있습니다. DeckConfig 를 확인하세요.")
        self._rng = rng

    @property
    def pool(self) -> List[Card]:
        return self._pool

    def draw(self) -> Card:
        # UnityEngine.Random.Range(0, n) / System.Random.Next(n) 와 동일한 균등 추출.
        return self._pool[self._rng.randrange(len(self._pool))]
