"""자가대전 REINFORCE 학습 루프.

두 에이전트가 같은 정책(공유 가중치)으로 대전하며, 매 게임의 승패 리워드로 정책을 갱신.
주기적으로 그리디 정책을 Heuristic/Random 과 평가해 진행 상황을 출력한다.

상대(--opponent):
    self       : 자기 자신과 대전 (양쪽 모두 학습) — 일반적 실력 향상
    heuristic  : 휴리스틱(OppAuto)을 상대로 학습 — baseline 격파에 직접적
    random     : 랜덤을 상대로 학습
    mix        : 매 게임 self/heuristic/random 을 확률적으로 섞음 (권장)
좌석(P0/P1)은 매 게임 무작위로 배정해 자리 편향을 줄인다.

사용 예 (sim/ 디렉터리에서):
    python -m rl.selfplay --episodes 100000 --opponent mix --save rl/model.npz
    python -m rl.selfplay --episodes 50000 --opponent heuristic --batch 32 --lr 5e-4
"""

from __future__ import annotations

import argparse
import random
import sys
from typing import List, Tuple

try:
    import numpy as np
except ImportError:
    print("numpy 가 필요합니다: pip install -r requirements.txt", file=sys.stderr)
    raise SystemExit(1)

from yatzy import engine
from yatzy.agents import HeuristicAgent, RandomAgent
from .policy import Policy, Step
from .rl_agent import RLAgent


_SEED_MASK = 0x5DEECE66

# mix 모드의 상대 분포 (self 강조 + 휴리스틱 격파 + 약간의 랜덤 다양성)
_MIX_WEIGHTS = (("self", 0.4), ("heuristic", 0.4), ("random", 0.2))


def reward(winner: int, pidx: int) -> float:
    if winner < 0:
        return 0.0
    return 1.0 if winner == pidx else -1.0


def _pick_mix(rng: random.Random) -> str:
    r = rng.random()
    acc = 0.0
    for name, w in _MIX_WEIGHTS:
        acc += w
        if r < acc:
            return name
    return _MIX_WEIGHTS[-1][0]


def play_episode(policy: Policy, rng: random.Random, args, mode: str
                 ) -> Tuple[List[Tuple[Step, float]], float, int]:
    """한 게임 진행 후 (학습 기여 목록, 학습자 결과(1/0.5/0), 라운드수) 반환.

    학습자(RL)는 무작위 좌석에 앉힌다. self 모드면 상대도 학습자(양쪽 trajectory 수집).
    """
    m = _pick_mix(rng) if mode == "mix" else mode
    game_seed = rng.randrange(2 ** 31 - 1)
    rl_seat = rng.randrange(2)

    learner = RLAgent(policy=policy, training=True, seed=game_seed)
    if m == "self":
        other = RLAgent(policy=policy, training=True, seed=game_seed ^ _SEED_MASK)
        other_learns = True
    elif m == "heuristic":
        other = HeuristicAgent(game_seed ^ _SEED_MASK)
        other_learns = False
    else:  # random
        other = RandomAgent(game_seed ^ _SEED_MASK)
        other_learns = False

    agents = [None, None]
    agents[rl_seat] = learner
    agents[1 - rl_seat] = other
    result = engine.play_match(agents[0], agents[1], seed=game_seed, max_rounds=args.rounds)

    contrib: List[Tuple[Step, float]] = []
    learner_r = reward(result.winner, rl_seat)
    for st in learner.trajectory:
        contrib.append((st, learner_r))
    if other_learns:
        other_r = reward(result.winner, 1 - rl_seat)
        for st in other.trajectory:
            contrib.append((st, other_r))

    learner_score = 1.0 if learner_r > 0 else (0.5 if learner_r == 0 else 0.0)
    return contrib, learner_score, result.rounds


def evaluate(policy: Policy, opponent_factory, n: int, max_rounds: int,
             base_seed: int) -> float:
    """그리디 RL(P0) vs 상대(P1) 승률 (무승부=0.5)."""
    score = 0.0
    for i in range(n):
        seed = base_seed + i
        rl = RLAgent(policy=policy, training=False, greedy=True, seed=seed)
        opp = opponent_factory(seed + 777)
        r = engine.play_match(rl, opp, seed=seed, max_rounds=max_rounds)
        if r.winner == 0:
            score += 1.0
        elif r.winner < 0:
            score += 0.5
    return score / n


def train(args) -> None:
    import time
    policy = Policy(hidden=args.hidden, seed=args.seed)
    rng = random.Random(args.seed)

    buffer: List[Tuple[Step, float]] = []
    games_in_batch = 0
    recent = []        # 최근 구간의 학습자 결과(1/0.5/0) — 하트비트용
    recent_rounds = [] # 최근 구간의 게임 길이
    t0 = time.time()

    print(f"학습 시작: episodes={args.episodes} opponent={args.opponent} "
          f"batch={args.batch} lr={args.lr} hidden={args.hidden} | "
          f"평가는 {args.eval_every} 마다, 진행표시는 {args.log_every} 마다", flush=True)

    for ep in range(1, args.episodes + 1):
        contrib, learner_score, rounds = play_episode(policy, rng, args, args.opponent)
        buffer.extend(contrib)
        games_in_batch += 1
        recent.append(learner_score)
        recent_rounds.append(rounds)

        if games_in_batch >= args.batch:
            # 배치 평균을 베이스라인으로 사용한 어드밴티지
            if buffer:
                mean_adv = sum(adv for _, adv in buffer) / len(buffer)
                adjusted = [(st, adv - mean_adv) for st, adv in buffer]
                policy.update(adjusted, lr=args.lr)
            buffer.clear()
            games_in_batch = 0

        if ep % args.log_every == 0:
            speed = ep / max(1e-6, time.time() - t0)
            avg_rounds = sum(recent_rounds) / len(recent_rounds) if recent_rounds else 0.0
            lw = sum(recent) / len(recent) if recent else 0.0
            recent.clear()
            recent_rounds.clear()
            print(f"  ...ep {ep:>7}/{args.episodes} | {speed:5.1f} ep/s "
                  f"| 최근 학습자 승률 {lw*100:4.1f}% | 평균 {avg_rounds:.0f}라운드",
                  flush=True)

        if ep % args.eval_every == 0:
            wr_h = evaluate(policy, lambda s: HeuristicAgent(s), args.eval_games,
                            args.rounds, base_seed=ep)
            wr_r = evaluate(policy, lambda s: RandomAgent(s), args.eval_games,
                            args.rounds, base_seed=ep + 10 ** 6)
            print(f"[ep {ep:>7}] vs Heuristic {wr_h*100:5.1f}% | "
                  f"vs Random {wr_r*100:5.1f}%", flush=True)
            if args.save:
                policy.save(args.save)

    if args.save:
        policy.save(args.save)
        print(f"모델 저장: {args.save}")


def main(argv) -> int:
    ap = argparse.ArgumentParser(description="자가대전 강화학습")
    ap.add_argument("--episodes", type=int, default=20000)
    ap.add_argument("--batch", type=int, default=16, help="업데이트당 게임 수")
    ap.add_argument("--lr", type=float, default=1e-3)
    ap.add_argument("--hidden", type=int, default=64)
    ap.add_argument("--rounds", type=int, default=200, help="게임당 최대 라운드")
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--eval-every", type=int, default=2000)
    ap.add_argument("--eval-games", type=int, default=100)
    ap.add_argument("--log-every", type=int, default=200, help="진행 하트비트 주기")
    ap.add_argument("--opponent", choices=["self", "heuristic", "random", "mix"],
                    default="self", help="학습 상대 (mix 권장)")
    ap.add_argument("--save", default="rl/model.npz")
    args = ap.parse_args(argv)
    train(args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
