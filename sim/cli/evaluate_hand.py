"""핸드 평가기 — 카드 값을 텍스트로 넣으면 콤보/점수를 출력.

사용 예 (sim/ 디렉터리에서):
    python -m cli.evaluate_hand 6 6 6 6 6
    python -m cli.evaluate_hand "5 5 3 3 6"
    python -m cli.evaluate_hand J 4 4 4        # J = 조커
    python -m cli.evaluate_hand                # 인자 없으면 대화형(여러 줄 입력)

토큰: 1~6 = 숫자 카드, J/j/0 = 조커. 최대 5장(슬롯 수)을 권장하나 임의 개수 허용.
"""

from __future__ import annotations

import sys
from typing import List

from yatzy.cards import Card
from yatzy import scoring


def parse_tokens(text: str) -> List[Card]:
    cards: List[Card] = []
    for tok in text.replace(",", " ").split():
        t = tok.strip().lower()
        if t in ("j", "0", "joker"):
            cards.append(Card(value=0, is_joker=True))
        elif t.isdigit() and 1 <= int(t) <= 6:
            cards.append(Card(value=int(t)))
        else:
            raise ValueError(f"알 수 없는 토큰: '{tok}' (1~6 또는 J)")
    return cards


def describe(cards: List[Card], shared_seed: int = 0) -> str:
    if not cards:
        return "카드 없음"
    score, rule, resolved = scoring.best_combo(cards, shared_seed)
    labels = [c.label() for c in cards]
    resolved_str = " ".join(str(v) for v in resolved if v > 0)
    out = [f"입력 : [{' '.join(labels)}]"]
    if any(c.is_joker for c in cards):
        out.append(f"조커해석: [{resolved_str}]")
    out.append(f"콤보 : {rule or '없음'}")
    out.append(f"점수 : {score:.1f}")
    return "\n".join(out)


def main(argv: List[str]) -> int:
    if argv:
        try:
            cards = parse_tokens(" ".join(argv))
        except ValueError as e:
            print(e)
            return 1
        print(describe(cards))
        return 0

    print("핸드 평가기 — 카드 값을 입력하세요 (예: 6 6 6 6 6 / J 4 4 4). 빈 줄이면 종료.")
    while True:
        try:
            line = input("> ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            break
        if not line:
            break
        try:
            print(describe(parse_tokens(line)))
        except ValueError as e:
            print(e)
        print()
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
