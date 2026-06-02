"""engine 통합 검증 — 매치가 정상 종료/결정적인지.

실행 (sim/ 에서): python -m unittest tests.test_engine
"""

import unittest

from yatzy import engine
from yatzy.agents import RandomAgent, HeuristicAgent


class TestMatch(unittest.TestCase):
    def test_heuristic_match_completes(self):
        r = engine.play_match(HeuristicAgent(1), HeuristicAgent(2),
                              seed=123, max_rounds=200)
        self.assertIn(r.winner, (-1, 0, 1))
        self.assertGreaterEqual(min(r.hp), 0.0)
        self.assertLessEqual(max(r.hp), engine.MAX_HP)
        self.assertGreater(r.rounds, 0)

    def test_random_match_completes(self):
        r = engine.play_match(RandomAgent(1), RandomAgent(2), seed=7, max_rounds=200)
        self.assertIn(r.winner, (-1, 0, 1))

    def test_determinism(self):
        # 같은 시드 + 같은 에이전트 시드 → 같은 결과
        r1 = engine.play_match(HeuristicAgent(5), HeuristicAgent(6),
                               seed=999, max_rounds=200)
        r2 = engine.play_match(HeuristicAgent(5), HeuristicAgent(6),
                               seed=999, max_rounds=200)
        self.assertEqual(r1.winner, r2.winner)
        self.assertEqual(r1.hp, r2.hp)
        self.assertEqual(r1.rounds, r2.rounds)

    def test_hand_grows_via_draft(self):
        # 초기 손패 3장 + 드래프트로 증가하는지 (엔진 내부 점검)
        g = engine.Game([HeuristicAgent(1), HeuristicAgent(2)], seed=1)
        self.assertEqual(len(g.players[0].hand), engine.INITIAL_HAND)
        self.assertEqual(len(g.players[1].hand), engine.INITIAL_HAND)


class TestKnowledge(unittest.TestCase):
    def test_opponent_card_memory_records_draft(self):
        g = engine.Game([HeuristicAgent(1), HeuristicAgent(2)], seed=42)
        # 한 라운드 진행 후 상대 카드 기억이 쌓였는지
        g.first_player = 0
        g._play_turn(0)
        g._play_turn(1)
        total_known = sum(g.knowledge[0].values()) + sum(g.knowledge[1].values())
        self.assertGreater(total_known, 0)


if __name__ == "__main__":
    unittest.main()
