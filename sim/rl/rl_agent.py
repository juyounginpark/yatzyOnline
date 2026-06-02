"""정책을 감싼 에이전트 — engine 의 Agent 인터페이스와 호환.

training=True 이면 의사결정을 trajectory 에 기록(자가대전 학습용).
모델 경로가 주어지면 로드, 아니면 새 정책(랜덤 가중치)을 만든다.
"""

from __future__ import annotations

from typing import List, Optional

import numpy as np

from yatzy.engine import Observation, DraftAction, TurnAction
from yatzy.agents import Agent
from . import features as F
from .policy import Policy, Step


class RLAgent(Agent):
    name = "rl"

    def __init__(self, policy: Optional[Policy] = None, model_path: Optional[str] = None,
                 seed: Optional[int] = None, training: bool = False, greedy: bool = False):
        if policy is not None:
            self.policy = policy
        elif model_path:
            self.policy = Policy.load(model_path)
        else:
            self.policy = Policy(seed=seed)
        self.rng = np.random.default_rng(seed)
        self.training = training
        self.greedy = greedy
        self.trajectory: List[Step] = []

    def reset_trajectory(self):
        self.trajectory = []

    def choose_draft(self, obs: Observation) -> DraftAction:
        x = F.draft_features(obs)
        action, step = self.policy.act("draft", x, None, self.rng, greedy=self.greedy)
        if self.training:
            self.trajectory.append(step)
        return DraftAction(keep_index=action)

    def choose_turn(self, obs: Observation) -> TurnAction:
        x = F.turn_features(obs)
        mask = F.turn_action_mask(obs)
        macro, step = self.policy.act("turn", x, mask, self.rng, greedy=self.greedy)
        if self.training:
            self.trajectory.append(step)
        return F.macro_to_action(obs, macro)
