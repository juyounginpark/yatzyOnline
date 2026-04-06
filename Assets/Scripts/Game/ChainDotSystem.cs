using System.Collections;
using UnityEngine;

// ─────────────────────────────────────────────
//  체인 DOT 시스템 (틱 기반)
//  활성화 중 일정 간격으로 잠금 슬롯 1개씩 흔들며 10데미지
// ─────────────────────────────────────────────
public class ChainDotSystem
{
    private readonly MonoBehaviour _host;
    private readonly HP _hp;
    private readonly TurnAnimator _animator;

    private const float DamagePerSlot = 10f;
    private const float TickInterval  = 2f;   // 틱 간격(초)

    private bool   _active;
    private Slot[] _slots;
    private bool   _damageIsToOpp;
    private float  _tickTimer;
    private int    _shakeIndex;      // 현재 흔들 슬롯 (연출용 순환)
    private float  _shakeTimer;
    private const float ShakeStagger = 0.3f;  // 슬롯 간 흔들림 간격

    private bool   _shaking;         // 한 틱의 순차 흔들림 진행 중
    private int    _shakeStep;       // 몇 번째 잠금 슬롯까지 흔들었는지

    public bool IsActive => _active;

    public ChainDotSystem(MonoBehaviour host, HP hp, TurnAnimator animator)
    {
        _host     = host;
        _hp       = hp;
        _animator = animator;
    }

    /// <summary>턴 전환 후 호출 — 잠금 슬롯이 있으면 틱 시작</summary>
    public void Activate(Slot[] slots, bool damageIsToOpp)
    {
        _active = false;
        _shaking = false;
        _slots  = slots;
        _damageIsToOpp = damageIsToOpp;
        _tickTimer     = TickInterval;

        if (slots == null) return;
        foreach (var s in slots)
            if (s != null && s.IsChainLocked) { _active = true; break; }
    }

    /// <summary>턴 종료 시 호출 — 틱 중단</summary>
    public void Deactivate()
    {
        _active  = false;
        _shaking = false;
        _slots   = null;
    }

    /// <summary>MainFlow.Update에서 매 프레임 호출</summary>
    public void Tick(float deltaTime)
    {
        if (!_active || _slots == null || _hp == null) return;

        // 순차 흔들림 진행 중이면 다음 슬롯 처리
        if (_shaking)
        {
            _shakeTimer -= deltaTime;
            if (_shakeTimer <= 0f)
                ProcessNextShake();
            return;
        }

        // 다음 틱 대기
        _tickTimer -= deltaTime;
        if (_tickTimer > 0f) return;
        _tickTimer = TickInterval;

        // 잠금 슬롯 순차 흔들림 시작
        _shaking   = true;
        _shakeStep = 0;
        _shakeTimer = 0f;  // 즉시 첫 슬롯 처리
    }

    private void ProcessNextShake()
    {
        int len = _slots.Length;
        // 다음 잠금 슬롯 찾기
        while (_shakeStep < len)
        {
            var slot = _slots[_shakeStep];
            _shakeStep++;

            if (slot != null && slot.IsChainLocked)
            {
                // 흔들림 연출
                _host.StartCoroutine(
                    _animator.ShakeTransform(slot.transform, 0.35f, 0.15f));

                // 10 데미지
                if (_damageIsToOpp) _hp.DamageOpp(DamagePerSlot);
                else                _hp.DamagePlayer(DamagePerSlot);

                _shakeTimer = ShakeStagger;
                return;
            }
        }

        // 모든 슬롯 처리 완료
        _shaking = false;

        // 잠금 슬롯이 하나도 없으면 비활성화
        bool anyLocked = false;
        foreach (var s in _slots)
            if (s != null && s.IsChainLocked) { anyLocked = true; break; }
        if (!anyLocked) _active = false;
    }

    /// <summary>체인 잠금 해제</summary>
    public IEnumerator UnlockAll(Slot[] chainedSlots)
    {
        if (chainedSlots == null) yield break;

        foreach (var slot in chainedSlots)
        {
            if (slot != null && slot.IsChainLocked)
                yield return _host.StartCoroutine(slot.UnlockChain());
        }
    }
}
