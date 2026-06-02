"""자가대전 강화학습 (REINFORCE, numpy 구현).

- features  : 관측 → 피처 벡터, 매크로 행동 정의
- policy    : numpy MLP 정책 + REINFORCE/Adam
- rl_agent  : 정책을 감싼 에이전트 (engine 과 호환)
- selfplay  : 자가대전 학습 루프

numpy 가 필요하다: pip install -r requirements.txt
"""
