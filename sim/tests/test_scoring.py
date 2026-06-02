"""scoring 포팅 검증 — 값은 GameFlow.cs 공식에서 직접 계산.

실행 (sim/ 에서): python -m unittest tests.test_scoring
"""

import unittest

from yatzy.cards import Card
from yatzy import scoring


def C(*vals):
    return [Card(value=v) for v in vals]


class TestEvaluateHand(unittest.TestCase):
    def _check(self, dice, exp_score, exp_rule):
        score, rule = scoring.evaluate_hand(dice)
        self.assertEqual(rule, exp_rule, f"{dice} → {rule}")
        self.assertAlmostEqual(score, exp_score, places=4, msg=f"{dice} = {score}")

    def test_five(self):
        self._check([6, 6, 6, 6, 6], 95 + 6 * 0.5, scoring.FIVE)        # 98

    def test_four(self):
        self._check([5, 5, 5, 5, 2], 80 + 5 + 2 * 0.1, scoring.FOUR)    # 85.2

    def test_full_house(self):
        self._check([3, 3, 3, 2, 2], 65 + 3 + 2 * 0.1, scoring.FULL_HOUSE)  # 68.2

    def test_straight_high(self):
        self._check([2, 3, 4, 5, 6], 70, scoring.STRAIGHT_HIGH)

    def test_straight_low(self):
        self._check([1, 2, 3, 4, 5], 65, scoring.STRAIGHT_LOW)

    def test_small_straight(self):
        self._check([3, 4, 5, 6], 56 + 6 * 0.5, scoring.SMALL_STRAIGHT)  # 59
        self._check([2, 3, 4, 5], 56 + 5 * 0.5, scoring.SMALL_STRAIGHT)  # 58.5
        self._check([1, 2, 3, 4], 56 + 4 * 0.5, scoring.SMALL_STRAIGHT)  # 58

    def test_triple(self):
        self._check([4, 4, 4, 6, 1], 40 + 4 * 2 + 6 * 0.3 + 1 * 0.1, scoring.TRIPLE)  # 49.9

    def test_two_pair(self):
        self._check([5, 5, 3, 3, 6], 25 + 5 * 2 + 3 * 0.3 + 6 * 0.1, scoring.TWO_PAIR)  # 36.5

    def test_one_pair(self):
        self._check([6, 6, 4, 2, 1],
                    10 + 6 * 2 + 4 * 0.5 + 2 * 0.2 + 1 * 0.1, scoring.ONE_PAIR)  # 24.5

    def test_high_card(self):
        self._check([6, 4, 3, 2], 6 * 2 + 4 * 0.8 + 3 * 0.3 + 2 * 0.1, scoring.HIGH_CARD)  # 16.3

    def test_empty(self):
        self.assertEqual(scoring.evaluate_hand([]), (0.0, ""))

    def test_priority_straight_over_small(self):
        # 2,3,4,5,6 은 스트레이트(하이) 가 스몰스트레이트보다 우선
        _, rule = scoring.evaluate_hand([2, 3, 4, 5, 6])
        self.assertEqual(rule, scoring.STRAIGHT_HIGH)


class TestJokers(unittest.TestCase):
    def test_joker_makes_four(self):
        cards = [Card(0, is_joker=True)] + C(4, 4, 4)
        score, rule, resolved = scoring.best_combo(cards)
        self.assertEqual(rule, scoring.FOUR)
        self.assertAlmostEqual(score, 80 + 4, places=4)  # [4,4,4,4] kicker 0 → 84

    def test_joker_jackpot(self):
        cards = [Card(0, is_joker=True) for _ in range(5)]
        score, rule, _ = scoring.best_combo(cards, shared_seed=12345)
        self.assertEqual(rule, scoring.JOKER_JACKPOT)
        self.assertTrue(200.0 <= score <= 300.0)

    def test_jackpot_deterministic(self):
        a = scoring.joker_jackpot_score(42)
        b = scoring.joker_jackpot_score(42)
        self.assertEqual(a, b)
        self.assertTrue(200.0 <= a <= 300.0)


class TestBestCombo(unittest.TestCase):
    def test_picks_best_subset_of_field(self):
        # 필드 5칸: 풀하우스가 잡혀야 함
        cards = C(3, 3, 3, 5, 5)
        score, rule, _ = scoring.best_combo(cards)
        self.assertEqual(rule, scoring.FULL_HOUSE)


if __name__ == "__main__":
    unittest.main()
