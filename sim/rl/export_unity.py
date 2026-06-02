"""학습된 정책(model.npz) → Unity(JsonUtility) 호환 JSON 으로 변환.

Unity 의 JsonUtility 는 중첩 2차원 배열을 못 읽으므로, 가중치를 1차원(row-major)으로
펴고 shape 를 따로 담는다. C# 의 RLBrain.cs 가 같은 스키마로 역직렬화한다.

사용 예 (sim/ 디렉터리에서):
    python -m rl.export_unity --model rl/model.npz
    python -m rl.export_unity --model rl/model.npz --out ../Assets/Resources/rl_model.json
"""

from __future__ import annotations

import argparse
import json
import os
import sys

try:
    import numpy as np
except ImportError:
    print("numpy 가 필요합니다: pip install -r requirements.txt", file=sys.stderr)
    raise SystemExit(1)

from .policy import Policy
from . import features as F
from yatzy.engine import MAX_HP


def _mat(arr: "np.ndarray") -> dict:
    a = np.asarray(arr, dtype=np.float32)
    shape = list(a.shape) if a.ndim > 0 else [a.shape[0] if a.ndim else len(a)]
    return {"shape": [int(s) for s in a.shape], "data": [float(x) for x in a.reshape(-1)]}


def _net(net) -> dict:
    return {
        "W1": _mat(net.W1), "b1": _mat(net.b1),
        "W2": _mat(net.W2), "b2": _mat(net.b2),
    }


def export(model_path: str, out_path: str) -> None:
    policy = Policy.load(model_path)
    data = {
        "hp_scale": float(MAX_HP),
        "turn_feature_dim": int(F.TURN_FEATURE_DIM),
        "draft_feature_dim": int(F.DRAFT_FEATURE_DIM),
        "num_turn_actions": int(F.NUM_TURN_ACTIONS),
        "num_draft_actions": int(F.NUM_DRAFT_ACTIONS),
        "hidden": int(policy.hidden),
        "turn": _net(policy.turn_net),
        "draft": _net(policy.draft_net),
    }
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(data, f)
    print(f"내보냄: {out_path}  (hidden={policy.hidden}, hp_scale={MAX_HP})")


def main(argv) -> int:
    # 기본 출력 경로: 리포의 Assets/Resources/rl_model.json
    here = os.path.dirname(os.path.abspath(__file__))          # sim/rl
    repo = os.path.abspath(os.path.join(here, "..", ".."))     # repo root
    default_out = os.path.join(repo, "Assets", "Resources", "rl_model.json")

    ap = argparse.ArgumentParser(description="model.npz → Unity JSON")
    ap.add_argument("--model", default="rl/model.npz")
    ap.add_argument("--out", default=default_out)
    args = ap.parse_args(argv)
    export(args.model, args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
