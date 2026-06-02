"""numpy MLP 정책 + REINFORCE(Adam).

두 개의 분리된 정책망:
- draft_net : 드래프트 피처 → 2 logits
- turn_net  : 턴 피처 → 5 logits (매크로)

각 망은 은닉 1층(tanh) MLP. 행동은 softmax 확률로 샘플링하고,
에피소드 종료 리워드(승=+1/패=-1/무=0)를 어드밴티지로 정책경사 업데이트한다.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import List, Optional, Tuple

import numpy as np

from . import features as F


def _softmax(z: np.ndarray) -> np.ndarray:
    z = z - np.max(z)
    e = np.exp(z)
    return e / np.sum(e)


@dataclass
class Step:
    """한 번의 의사결정 기록 (학습용)."""

    net: str            # "draft" 또는 "turn"
    x: np.ndarray       # 입력 피처
    action: int
    probs: np.ndarray   # 샘플 시점 확률 (마스킹 반영)


class MLP:
    def __init__(self, n_in: int, n_hidden: int, n_out: int, rng: np.random.Generator):
        # He/Xavier 절충 초기화
        self.W1 = (rng.standard_normal((n_in, n_hidden)) * np.sqrt(1.0 / n_in)).astype(np.float32)
        self.b1 = np.zeros(n_hidden, dtype=np.float32)
        self.W2 = (rng.standard_normal((n_hidden, n_out)) * np.sqrt(1.0 / n_hidden)).astype(np.float32)
        self.b2 = np.zeros(n_out, dtype=np.float32)
        self._init_adam()

    def _init_adam(self):
        self._m = {k: np.zeros_like(v) for k, v in self.params().items()}
        self._v = {k: np.zeros_like(v) for k, v in self.params().items()}
        self._t = 0

    def params(self):
        return {"W1": self.W1, "b1": self.b1, "W2": self.W2, "b2": self.b2}

    def forward(self, x: np.ndarray):
        h_pre = x @ self.W1 + self.b1
        h = np.tanh(h_pre)
        z = h @ self.W2 + self.b2
        cache = (x, h_pre, h)
        return z, cache

    def logits(self, x: np.ndarray) -> np.ndarray:
        return self.forward(x)[0]

    def grads_for(self, x: np.ndarray, action: int, probs: np.ndarray, advantage: float):
        """-A * log p_a 에 대한 파라미터 그래디언트 누적값 반환."""
        _, cache = self.forward(x)
        _, h_pre, h = cache
        # 손실 L = -A * log p_a 를 최소화. dL/dz_i = -A * (1{i=a} - p_i)
        onehot = np.zeros_like(probs)
        onehot[action] = 1.0
        dz = (-advantage) * (onehot - probs)
        dW2 = np.outer(h, dz)
        db2 = dz
        dh = self.W2 @ dz
        dh_pre = dh * (1.0 - h * h)  # tanh'
        dW1 = np.outer(x, dh_pre)
        db1 = dh_pre
        return {"W1": dW1, "b1": db1, "W2": dW2, "b2": db2}

    def apply_grads(self, grads, lr: float, beta1=0.9, beta2=0.999, eps=1e-8,
                    clip: float = 5.0):
        self._t += 1
        p = self.params()
        for k in p:
            g = grads[k]
            # 그래디언트 클리핑
            norm = np.linalg.norm(g)
            if norm > clip:
                g = g * (clip / (norm + 1e-12))
            self._m[k] = beta1 * self._m[k] + (1 - beta1) * g
            self._v[k] = beta2 * self._v[k] + (1 - beta2) * (g * g)
            mhat = self._m[k] / (1 - beta1 ** self._t)
            vhat = self._v[k] / (1 - beta2 ** self._t)
            p[k] -= lr * mhat / (np.sqrt(vhat) + eps)


class Policy:
    def __init__(self, hidden: int = 64, seed: Optional[int] = None):
        rng = np.random.default_rng(seed)
        self.turn_net = MLP(F.TURN_FEATURE_DIM, hidden, F.NUM_TURN_ACTIONS, rng)
        self.draft_net = MLP(F.DRAFT_FEATURE_DIM, hidden, F.NUM_DRAFT_ACTIONS, rng)
        self.hidden = hidden

    # ── 행동 선택 ──
    def act(self, net_name: str, x: np.ndarray, mask: Optional[np.ndarray],
            rng: np.random.Generator, greedy: bool = False) -> Tuple[int, Step]:
        net = self.turn_net if net_name == "turn" else self.draft_net
        z = net.logits(x)
        if mask is not None:
            z = np.where(mask > 0, z, -1e9)
        probs = _softmax(z)
        if greedy:
            action = int(np.argmax(probs))
        else:
            action = int(rng.choice(len(probs), p=probs))
        return action, Step(net=net_name, x=x, action=action, probs=probs)

    # ── REINFORCE 업데이트 ──
    def update(self, batch: List[Tuple[Step, float]], lr: float = 1e-3):
        """batch: (Step, advantage) 목록. 망별로 그래디언트 누적 후 1 step."""
        accum = {"turn": None, "draft": None}
        counts = {"turn": 0, "draft": 0}
        for step, adv in batch:
            net = self.turn_net if step.net == "turn" else self.draft_net
            g = net.grads_for(step.x, step.action, step.probs, adv)
            if accum[step.net] is None:
                accum[step.net] = g
            else:
                for k in g:
                    accum[step.net][k] += g[k]
            counts[step.net] += 1
        for name, net in (("turn", self.turn_net), ("draft", self.draft_net)):
            if accum[name] is not None and counts[name] > 0:
                avg = {k: v / counts[name] for k, v in accum[name].items()}
                net.apply_grads(avg, lr)

    # ── 저장/로드 ──
    def save(self, path: str):
        data = {}
        for prefix, net in (("turn", self.turn_net), ("draft", self.draft_net)):
            for k, v in net.params().items():
                data[f"{prefix}_{k}"] = v
        data["hidden"] = np.array([self.hidden])
        np.savez(path, **data)

    @classmethod
    def load(cls, path: str) -> "Policy":
        d = np.load(path)
        hidden = int(d["hidden"][0])
        pol = cls(hidden=hidden, seed=0)
        for prefix, net in (("turn", pol.turn_net), ("draft", pol.draft_net)):
            net.W1 = d[f"{prefix}_W1"]; net.b1 = d[f"{prefix}_b1"]
            net.W2 = d[f"{prefix}_W2"]; net.b2 = d[f"{prefix}_b2"]
            net._init_adam()
        return pol
