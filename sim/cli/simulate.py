"""매치 시뮬레이터 — 두 에이전트가 대전하는 과정을 텍스트 로그로 본다.

사용 예 (sim/ 디렉터리에서):
    python -m cli.simulate                                  # heuristic vs heuristic, 로그 출력
    python -m cli.simulate --p0 heuristic --p1 random --seed 7
    python -m cli.simulate --matches 500 --quiet            # 통계만 (승률)
    python -m cli.simulate --p0 rl --model rl/model.npz     # 학습된 RL 정책 사용

에이전트 종류: random, heuristic, rl
"""

from __future__ import annotations

import argparse
import sys
from typing import Optional

from yatzy import engine
from yatzy.agents import RandomAgent, HeuristicAgent, Agent


def make_agent(kind: str, seed: Optional[int], model: Optional[str]) -> Agent:
    kind = kind.lower()
    if kind == "random":
        return RandomAgent(seed)
    if kind == "heuristic":
        return HeuristicAgent(seed)
    if kind == "rl":
        try:
            from rl.rl_agent import RLAgent
        except Exception as e:  # numpy 미설치 또는 모델 없음
            print(f"RL 에이전트 로드 실패: {e}", file=sys.stderr)
            raise SystemExit(2)
        return RLAgent(model_path=model, seed=seed)
    raise SystemExit(f"알 수 없는 에이전트: {kind}")


def run_single(p0_kind, p1_kind, seed, rounds, model0, model1):
    a0 = make_agent(p0_kind, seed, model0)
    a1 = make_agent(p1_kind, None if seed is None else seed + 1, model1)
    print(f"=== P0={p0_kind} vs P1={p1_kind} (seed={seed}) ===")
    result = engine.play_match(a0, a1, seed=seed, max_rounds=rounds, logger=print)
    return result


def run_batch(p0_kind, p1_kind, n, rounds, model0, model1, base_seed):
    wins = [0, 0, 0]  # P0, P1, 무승부
    total_rounds = 0
    for i in range(n):
        seed = (base_seed + i) if base_seed is not None else None
        a0 = make_agent(p0_kind, seed, model0)
        a1 = make_agent(p1_kind, None if seed is None else seed + 100000, model1)
        r = engine.play_match(a0, a1, seed=seed, max_rounds=rounds)
        wins[r.winner if r.winner >= 0 else 2] += 1
        total_rounds += r.rounds
    print(f"=== {n} 매치: P0={p0_kind} vs P1={p1_kind} ===")
    print(f"P0 승: {wins[0]} ({wins[0]/n*100:.1f}%)")
    print(f"P1 승: {wins[1]} ({wins[1]/n*100:.1f}%)")
    print(f"무승부: {wins[2]} ({wins[2]/n*100:.1f}%)")
    print(f"평균 라운드: {total_rounds/n:.1f}")


def main(argv) -> int:
    ap = argparse.ArgumentParser(description="yatzyOnline 규칙 매치 시뮬레이터")
    ap.add_argument("--p0", default="heuristic", help="P0 에이전트 (random/heuristic/rl)")
    ap.add_argument("--p1", default="heuristic", help="P1 에이전트 (random/heuristic/rl)")
    ap.add_argument("--seed", type=int, default=None, help="랜덤 시드")
    ap.add_argument("--rounds", type=int, default=engine.DEFAULT_MAX_ROUNDS,
                    help="최대 라운드 (교착 방지 캡)")
    ap.add_argument("--matches", type=int, default=1, help="반복 매치 수 (>1 이면 통계 모드)")
    ap.add_argument("--quiet", action="store_true", help="단일 매치 로그 끄기")
    ap.add_argument("--model0", default=None, help="P0 가 rl 일 때 모델 경로")
    ap.add_argument("--model1", default=None, help="P1 가 rl 일 때 모델 경로")
    ap.add_argument("--model", default=None, help="model0/model1 공통 기본값")
    args = ap.parse_args(argv)

    model0 = args.model0 or args.model
    model1 = args.model1 or args.model

    if args.matches > 1:
        run_batch(args.p0, args.p1, args.matches, args.rounds, model0, model1, args.seed)
    elif args.quiet:
        a0 = make_agent(args.p0, args.seed, model0)
        a1 = make_agent(args.p1, None if args.seed is None else args.seed + 1, model1)
        r = engine.play_match(a0, a1, seed=args.seed, max_rounds=args.rounds)
        winner = "무승부" if r.winner < 0 else f"P{r.winner} 승"
        print(f"{winner} | HP {r.hp[0]:.0f} vs {r.hp[1]:.0f} | {r.rounds} 라운드")
    else:
        run_single(args.p0, args.p1, args.seed, args.rounds, model0, model1)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
