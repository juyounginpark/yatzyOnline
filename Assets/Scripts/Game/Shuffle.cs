using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  Shuffle — 쌓아둔 카드(레이어/ y축 오프셋)로 리플 셔플 연출
//  - cards: 스택 순서대로 쌓아둔 카드 Transform들 (각자의 localPosition/rotation을 home으로 캡처)
//  - 위/아래 절반을 좌우로 가른 뒤(split), 번갈아 호를 그리며 제자리로 합침(merge=리플)
//  - 게임 시작 + 덱 초기화(5라운드 주기) 때 RoundDirector가 PlayAndWait()로 재생
//  - Time.unscaledDeltaTime 사용 → 히트스톱(타임스케일) 영향 없음
// ─────────────────────────────────────────────
public class Shuffle : MonoBehaviour
{
    [Header("─ 카드 (스택 순서대로) ─")]
    [Tooltip("레이어/y축으로 쌓아둔 카드들. 인덱스 순서 = 아래→위 스택")]
    public Transform[] cards;

    [Header("─ 동작 ─")]
    [Tooltip("켜면 Start에서 자동 재생 (보통은 RoundDirector가 구동하므로 꺼둠)")]
    public bool playOnStart = false;

    [Tooltip("리플(가르고 합치기) 반복 횟수")]
    public int riffles = 1;

    [Tooltip("카드의 기울기(예: 45도)에 맞춰 이동/호 방향을 카드 자기 축 기준으로 (권장)")]
    public bool alignToCardTilt = true;

    [Header("─ 가르기(split) ─")]
    [Tooltip("두 더미가 (카드 오른쪽 축으로) 벌어지는 거리")]
    public float splitOffsetX = 0.3f;

    [Tooltip("가를 때 살짝 드는 높이 (카드 위쪽 축)")]
    public float liftY = 0.04f;

    [Tooltip("가르는 시간 (크게=느긋)")]
    public float splitDuration = 0.45f;

    [Header("─ 합치기(merge=리플) ─")]
    [Tooltip("카드가 번갈아 끼워지는 간격(초). 크게=또박또박 차분")]
    public float interleaveStagger = 0.05f;

    [Tooltip("끼워질 때 호 높이 (0이면 호 없이 깔끔하게 슬라이드)")]
    public float arcHeight = 0f;

    [Tooltip("카드 한 장이 제자리로 돌아오는 시간 (크게=느긋)")]
    public float mergeCardDuration = 0.22f;

    [Header("─ 사운드 ─")]
    public bool sound = true;

    private Vector3[]    _homePos;
    private Quaternion[] _homeRot;
    private bool _busy;

    public bool IsShuffling => _busy;

    void Awake() => CaptureHome();

    void Start() { if (playOnStart) Play(); }

    /// <summary>현재 카드 배치를 '제자리(home)'로 캡처. 카드 배치를 바꿨으면 다시 호출.</summary>
    public void CaptureHome()
    {
        if (cards == null) return;
        _homePos = new Vector3[cards.Length];
        _homeRot = new Quaternion[cards.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            _homePos[i] = cards[i].localPosition;
            _homeRot[i] = cards[i].localRotation;
        }
    }

    [ContextMenu("Shuffle")]
    public void Play()
    {
        if (!_busy) StartCoroutine(PlayAndWait());
    }

    /// <summary>셔플 연출 (RoundDirector가 yield).</summary>
    public IEnumerator PlayAndWait()
    {
        if (_busy || cards == null || cards.Length == 0) yield break;
        if (_homePos == null || _homePos.Length != cards.Length) CaptureHome();
        _busy = true;

        for (int r = 0; r < Mathf.Max(1, riffles); r++)
        {
            yield return StartCoroutine(SplitRoutine());
            yield return StartCoroutine(MergeRoutine());
        }

        SnapHome();
        _busy = false;
    }

    // 위/아래 절반을 좌우로 가르며 살짝 든다
    private IEnumerator SplitRoutine()
    {
        if (sound && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw, 0.6f);

        int half = cards.Length / 2;
        var start   = CurrentPositions();
        var targets = new Vector3[cards.Length];
        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            float dir = i < half ? -1f : 1f;          // 아래 절반=왼쪽, 위 절반=오른쪽
            // 카드 기울기(45도 등)에 맞춘 오른쪽/위 축으로 이동
            targets[i] = _homePos[i]
                       + AxisRight(i) * (dir * splitOffsetX)
                       + AxisUp(i)    * liftY;
        }

        float t = 0f;
        while (t < splitDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / splitDuration));
            for (int i = 0; i < cards.Length; i++)
                if (cards[i] != null) cards[i].localPosition = Vector3.Lerp(start[i], targets[i], k);
            yield return null;
        }
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null) cards[i].localPosition = targets[i];
    }

    // 양쪽 절반을 번갈아 호를 그리며 제자리로 합친다 (리플)
    private IEnumerator MergeRoutine()
    {
        int half = cards.Length / 2;
        int left = 0, right = half;
        bool takeRight = true;
        var running = new List<Coroutine>();

        while (left < half || right < cards.Length)
        {
            int idx = -1;
            if (takeRight && right < cards.Length) idx = right++;
            else if (!takeRight && left < half)    idx = left++;
            else if (right < cards.Length)         idx = right++;
            else if (left < half)                  idx = left++;
            takeRight = !takeRight;

            if (idx >= 0 && cards[idx] != null)
            {
                running.Add(StartCoroutine(MergeOne(idx)));
                if (sound && SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.cardPlace, 0.3f);
            }

            if (interleaveStagger > 0f) yield return new WaitForSeconds(interleaveStagger);
        }

        foreach (var c in running) yield return c;
    }

    private IEnumerator MergeOne(int i)
    {
        Vector3 from = cards[i].localPosition;
        Vector3 to   = _homePos[i];
        Quaternion rFrom = cards[i].localRotation;
        Quaternion rTo   = _homeRot[i];

        float t = 0f;
        while (t < mergeCardDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / mergeCardDuration));
            // 카드 위쪽 축으로 작은 호 (기울기 반영)
            Vector3 p = Vector3.Lerp(from, to, k) + AxisUp(i) * (Mathf.Sin(k * Mathf.PI) * arcHeight);
            cards[i].localPosition = p;
            cards[i].localRotation = Quaternion.Slerp(rFrom, rTo, k);
            yield return null;
        }
        cards[i].localPosition = to;
        cards[i].localRotation = rTo;
    }

    // 카드 기울기(home 회전)를 반영한 오른쪽/위 축 (parent 로컬 공간)
    private Vector3 AxisRight(int i) =>
        alignToCardTilt ? _homeRot[i] * Vector3.right : Vector3.right;
    private Vector3 AxisUp(int i) =>
        alignToCardTilt ? _homeRot[i] * Vector3.up : Vector3.up;

    private Vector3[] CurrentPositions()
    {
        var arr = new Vector3[cards.Length];
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null) arr[i] = cards[i].localPosition;
        return arr;
    }

    private void SnapHome()
    {
        for (int i = 0; i < cards.Length; i++)
            if (cards[i] != null)
            {
                cards[i].localPosition = _homePos[i];
                cards[i].localRotation = _homeRot[i];
            }
    }
}
