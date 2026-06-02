# yatzyOnline 시뮬레이터 + 강화학습 (sim/)

Unity 게임(`Assets/Scripts/Game`)의 **전투 규칙을 파이썬으로 정확히 옮긴** 텍스트 시뮬레이터와,
그 규칙 위에서 **자가대전 강화학습(self-play RL)** 으로 플레이 전략을 학습하는 도구 모음.

규칙 출처(포팅 대상):
- 점수 판정: `GameFlow.cs` (`EvaluateHand`, `ResolveJokersOptimal`, 조커 잭팟)
- 게임 루프/판정: `MainFlow.cs` (`ResolutionPhaseRoutine`), 드래프트 `CardDraft.cs`
- 휴리스틱 AI: `OppAuto.cs` (그리디 콤보 배치 + 강제수비/스킵)

---

## 0. 준비 — Python 설치

> **이 PC에는 아직 Python이 설치돼 있지 않습니다.** (`python.exe` 는 Microsoft Store 안내 스텁)
> https://www.python.org/downloads/ 에서 Python 3.10+ 를 설치하고 "Add to PATH" 를 체크하세요.
> 설치 후 새 터미널에서 `python --version` 으로 확인.

핵심 기능(판정/엔진/CLI/테스트)은 **표준 라이브러리만으로** 동작합니다. RL 만 numpy 가 필요합니다.

```powershell
cd C:\Unity\yatzyOnline\sim
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt   # RL 용 numpy
```

---

## 1. 핸드 평가기 — 텍스트 값 → 콤보/점수

```powershell
python -m cli.evaluate_hand 6 6 6 6 6      # 파이브카드 98.0
python -m cli.evaluate_hand 5 5 3 3 6      # 투페어 36.5
python -m cli.evaluate_hand J 4 4 4        # 조커→4, 포카드 84.0
python -m cli.evaluate_hand                # 인자 없으면 대화형
```

토큰: `1`~`6` 숫자 카드, `J`(또는 `0`) 조커.

## 2. 매치 시뮬레이터 — 전체 대전을 텍스트로 보기

```powershell
python -m cli.simulate                                   # heuristic vs heuristic, 라운드별 로그
python -m cli.simulate --p0 heuristic --p1 random --seed 7
python -m cli.simulate --matches 500 --quiet             # 통계만(승률/평균 라운드)
python -m cli.simulate --p0 rl --model rl/model.npz --p1 heuristic
```

로그에는 드래프트(제시 2장/선택), 배치+자세, 판정(점수·데미지·HP), Guard 생존 파괴까지 출력됩니다.

## 3. 자가대전 강화학습

```powershell
# 빠른 동작 확인(스모크 테스트): 진행 표시 자주, 평가 일찍
python -m rl.selfplay --episodes 1000 --opponent mix --log-every 50 --eval-every 500

# 본 학습 (휴리스틱 격파가 목표면 mix 권장)
python -m rl.selfplay --episodes 100000 --opponent mix --eval-every 5000 --log-every 1000 --save rl/model.npz
```

`--opponent` 상대: `self`(자기자신), `heuristic`(휴리스틱 직접), `random`, `mix`(self 40%/heuristic 40%/random 20%, **권장**).
좌석(P0/P1)은 매 게임 무작위 배정해 자리 편향을 줄입니다.

- 시작하면 배너가 찍히고, `--log-every`(기본 200) 마다 진행 하트비트(`...ep N/총 | ep/s | 최근 P0% | 라운드`)가 출력됩니다.
- `--eval-every`(기본 2000) 마다 그리디 정책의 vs Heuristic / vs Random 승률을 출력합니다.
- 매 턴 콤보 탐색이 무거워 수천 판은 수 분 이상 걸릴 수 있습니다. 처음엔 작은 `--episodes` 로 확인하세요.

학습된 모델로 대전/평가:

```powershell
python -m cli.simulate --p0 rl --model rl/model.npz --p1 heuristic --matches 200 --quiet
```

## 4. 학습된 정책을 Unity 게임에 이식

오프라인 AI(상대)의 **턴 판단 + 드래프트**를 학습된 정책으로 교체합니다. (온라인 대전은 실제 상대라 영향 없음)

```powershell
# (1) 학습된 가중치를 Unity 가 읽을 JSON 으로 내보내기 → Assets/Resources/rl_model.json
python -m rl.export_unity --model rl/model.npz
```

```
# (2) Unity 에디터를 열면 Assets/Resources/rl_model.json 이 TextAsset 으로 자동 임포트됨.
#     플레이하면 OppAuto/CardDraft 가 RLBrain.Load() 로 모델을 읽어 RL 로 플레이.
#     모델 파일이 없으면 자동으로 기존 휴리스틱(OppAuto/ChooseBestForOpp) 으로 폴백.
```

관련 C# 파일:
- `Assets/Scripts/Game/RLBrain.cs`    — JSON 로드 + MLP 추론(greedy)
- `Assets/Scripts/Game/RLFeatures.cs` — `rl/features.py` 와 동일한 피처/매크로 재현
- `OppAuto.cs`(`RLTurnRoutine`), `CardDraft.cs`(`ChooseBestForOpp`), `MainFlow.cs`(드래프트 기억) 에 연결

> ⚠️ 재학습 후에는 `export_unity` 를 다시 돌려 JSON 을 갱신해야 게임에 반영됩니다.
> 피처는 파이썬과 1:1 로 맞췄지만(정규화/순서), float(C#) vs double(파이썬) 미세차가 있을 수 있어
> 동작이 이상하면 먼저 `--matches` 통계로 파이썬 쪽 실력을 확인하세요.

## 5. 테스트 (numpy 불필요)

```powershell
python -m unittest discover -s tests
```

---

## 게임 규칙 요약 (구현된 범위)

- HP 300 시작(`engine.MAX_HP`), 슬롯 5칸, 손패 최대 8장(초기 3장).
- **라운드 = 2턴**(선공/후공, 매 라운드 선공 교대) → 판정.
- 각 턴: **드래프트**(2장 제시→1장 선택·1장 상대) → **배치**(빈 슬롯에) → **Attack/Guard 자세**(필드 전체).
- **판정**(`ResolutionPhaseRoutine`):
  | 상황 | 결과 |
  |---|---|
  | 공격 vs 빈필드 | 점수만큼 데미지, 카드 소모 |
  | 방어 vs 빈필드 | 잔류(데미지 없음) |
  | 공격 vs 공격 | 양쪽 점수만큼 상호 데미지 |
  | 공격 vs 방어 | 방어점수 ≥ 공격 → 차단(공격 파괴, 방어 0~3장 룰렛 파괴 후 복귀) / 아니면 차이만큼 데미지 |
  | 방어 vs 방어 | 높은 쪽 승리·두 점수 **합** 데미지, 무승부면 양쪽 파괴 |
- **조커**: 1~6 중 최고 점수로 자동 해석. **조커 5장** = 잭팟(공유 시드 기반 200~300).
- **상대 카드 기억**: 드래프트는 양측 완전 공개 → 상대 손패로 간 카드를 누적 기록하여 관측에 노출.

### 점수 공식 (`GameFlow.EvaluateHand`)
| 콤보 | 점수 |
|---|---|
| 파이브카드 | `95 + n·0.5` |
| 포카드 | `80 + n + kicker·0.1` |
| 풀하우스 | `65 + triple + pair·0.1` |
| 스트레이트(하이, 2~6) | `70` |
| 스트레이트(로우, 1~5) | `65` |
| 스몰스트레이트(4연속) | `56 + 최고·0.5` |
| 트리플 | `40 + n·2 + bigK·0.3 + smallK·0.1` |
| 투페어 | `25 + bigP·2 + smallP·0.3 + kicker·0.1` |
| 원페어 | `10 + n·2 + bigK·0.5 + midK·0.2 + smallK·0.1` |
| 하이카드 | `최고·2 + 2nd·0.8 + 3rd·0.3 + 4th·0.1` |

---

## 구조

```
sim/
  yatzy/                # 규칙 엔진 (표준 라이브러리만)
    cards.py            # 카드/덱 구성/딜러
    scoring.py          # 핸드 판정 (GameFlow 포팅)
    engine.py           # 게임 루프/드래프트/판정/카드수명/상대카드 기억
    agents.py           # Random / Heuristic(OppAuto) + 배치 탐색 유틸
  cli/
    evaluate_hand.py    # 텍스트 값 → 콤보/점수
    simulate.py         # 매치 시뮬레이터 (로그/통계)
  rl/                   # 강화학습 (numpy)
    features.py         # 관측→피처, 매크로 행동(5종) + 드래프트(2종)
    policy.py           # numpy MLP 정책 + REINFORCE/Adam
    rl_agent.py         # 정책 래퍼 (engine 호환)
    selfplay.py         # 자가대전 학습 루프
  tests/                # unittest (numpy 불필요)
```

## RL 설계 메모

- **행동 추상화**: 가변적인 배치 공간을 5개의 **매크로 전략**(SKIP / 최고콤보·Attack / 최고콤보·Guard /
  높은카드 채우기·Attack / 2장이상·Guard)으로 축약 → 학습 가능한 작은 이산 공간.
  드래프트는 제시 2장 중 선택(2종).
- **관측**: 내 손패/필드 값 분포, 내 최고 콤보 점수, 자세, 상대 필드(공격이면 값 공개·방어면 개수/자세만),
  **상대 드래프트 기억**, 양측 HP, 선공 여부, 라운드.
- **학습**: 두 에이전트가 같은 정책으로 자가대전, 승=+1/패=-1/무=0 리워드로 REINFORCE(배치 평균 베이스라인, Adam).

## 범위 밖(미구현)

전투 결과에 영향이 없거나 연출/메타에 해당하는 요소는 제외했습니다:
- 카드 타입별 특수효과(Critical/Heal/Chain)는 현재 `ResolutionPhase` 가 순수 점수로만 판정하므로 메타데이터로만 보존.
- EXP/레벨업 룰렛 보상, 애니메이션/사운드/타이머, 온라인 네트워킹.
실제 덱 구성(프리팹 풀)은 `yatzy.cards.DeckConfig` 로 교체 가능(기본: Attack 1그룹, 값 1~6+조커 균등).
```
