"""yatzyOnline 규칙 시뮬레이터.

Unity 프로젝트(Assets/Scripts/Game)의 전투 규칙을 파이썬으로 정확히 옮긴 패키지.

- cards     : 카드/덱 구성
- scoring   : 핸드 판정 (GameFlow.cs 포팅)
- engine    : 전체 게임 루프 (MainFlow.cs 포팅)
- agents    : 의사결정 에이전트 (Random / Heuristic = OppAuto 포팅)
"""

from . import cards, scoring, engine, agents  # noqa: F401
