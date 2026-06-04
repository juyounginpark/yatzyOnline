using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  상대방 덱 매니저 (드로우 로직)
//  - 카드 뒷면 프리팹 1종 통일
//  - 배치·애니메이션 설정은 Deck에서 참조
//  - 위→아래 방향 아치형 배치 (플레이어 아치 반전)
// ─────────────────────────────────────────────
public class OppDeck : MonoBehaviour
{
    [Header("─ 플레이어 덱 참조 ─")]
    [Tooltip("모든 설정(drawCount, maxCards, cardBackPrefab, 아치/딜 옵션)을 이 Deck과 동기화")]
    public Deck deck;

    [Header("─ 스폰 위치 ─")]
    public Transform deckSpawnPoint;

    [Header("─ 디버그 ─")]
    [Tooltip("켜면 상대 카드를 뒷면 대신 앞면(실제 카드)으로 표시")]
    public bool showOpponentCards = false;

    // ─── Deck 동기화 프로퍼티 ───
    public GameObject cardBackPrefab => deck != null ? deck.cardBackPrefab : null;
    public int        drawCount       => deck != null ? deck.drawCount      : 0;
    public int        maxCards        => deck != null ? deck.maxCards       : 0;

    // ─── 내부 상태 ───
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private readonly List<Deck.CardPool> _handSpecs = new List<Deck.CardPool>(); // 각 카드의 실제 정보 (앞/뒷면 전환용)
    private readonly List<Vector3> _targetPositions = new List<Vector3>();
    private readonly List<Quaternion> _targetRotations = new List<Quaternion>();
    private bool _isAnimating;
    private bool _lastShown;
    private float _smoothSpeed = 10f;

    public bool IsAnimating => _isAnimating;
    public IReadOnlyList<GameObject> SpawnedCards => _spawnedCards;

    /// <summary>현재 손패의 카드 스펙 목록 (앞면 프리팹 포함 — 쇼다운 공개용).</summary>
    public List<Deck.CardPool> GetHandSpecs() => new List<Deck.CardPool>(_handSpecs);

    /// <summary>현재 손패의 종류별 장수 (인덱스 0~7 = 값 1~8, 8 = 조커).</summary>
    public int[] GetHandCountsByType()
    {
        int[] counts = new int[9];
        foreach (var s in _handSpecs)
        {
            int idx = s.isJoker ? 8 : (s.value >= 1 && s.value <= 8 ? s.value - 1 : -1);
            if (idx >= 0) counts[idx]++;
        }
        return counts;
    }

    private float CurrentAngleRange =>
        deck.baseAngleRange + Mathf.Max(0, _spawnedCards.Count - drawCount) * deck.anglePerCard;

    private Transform Parent =>
        deckSpawnPoint != null ? deckSpawnPoint : transform;

    void Start()
    {
        if (deck == null)
        {
            deck = FindObjectOfType<Deck>();
            if (deck == null)
                Debug.LogWarning("[OppDeck] Deck을 찾을 수 없습니다.");
        }
        // 초기 분배는 Deck.DrawCards가 공유 덱 풀에서 DealInitialFromPile을 호출해 처리.
        _lastShown = showOpponentCards;
    }

    void Update()
    {
        // 체크박스 토글 시 손패 비주얼을 앞/뒷면으로 다시 생성
        if (showOpponentCards != _lastShown && !_isAnimating)
        {
            _lastShown = showOpponentCards;
            RebuildVisuals();
        }
    }

    void LateUpdate()
    {
        float t = 1f - Mathf.Exp(-_smoothSpeed * Time.deltaTime);

        for (int i = 0; i < _spawnedCards.Count; i++)
        {
            if (_spawnedCards[i] == null) continue;

            var tr = _spawnedCards[i].transform;
            tr.localPosition = Vector3.Lerp(tr.localPosition, _targetPositions[i], t);
            tr.localRotation = Quaternion.Slerp(tr.localRotation, _targetRotations[i], t);
        }
    }

    // ─────────────────────────────────────────
    //  초기 분배: Deck이 공유 덱 풀에서 뽑아 넘긴 카드들을 배치
    //  - 화면엔 뒷면을 띄우되 내부적으로 실제 값/조커/종류를 보유
    // ─────────────────────────────────────────
    public void DealInitialFromPile(List<Deck.CardPool> cards)
    {
        ClearCards();

        if (deck == null) deck = FindObjectOfType<Deck>();
        if (cardBackPrefab == null)
        {
            Debug.LogWarning("[OppDeck] 카드 뒷면 프리팹이 지정되지 않았습니다.");
            return;
        }

        StartCoroutine(DealAnimation(cards));
    }

    // ─────────────────────────────────────────
    //  카드 1장 추가 (공유 덱 풀에서 뽑기)
    // ─────────────────────────────────────────
    public void AddOneCard()
    {
        if (cardBackPrefab == null || deck == null) return;
        if (_spawnedCards.Count >= deck.maxCards) return;

        if (deck.DrawFromPile(out Deck.CardPool c))
        {
            SpawnCard(c);
            UpdateAllTargets();
        }
    }

    // ─────────────────────────────────────────
    //  특정 값 카드 추가 (슬롯 복귀용)
    // ─────────────────────────────────────────
    public void AddCardByValue(int value)
    {
        if (cardBackPrefab == null) return;

        var spec = new Deck.CardPool
        {
            value     = value,
            isJoker   = false,
            poolIndex = 0,
            prefab    = deck != null ? deck.GetFrontPrefab(value, false) : null
        };
        SpawnCard(spec);
        UpdateAllTargets();
    }

    // ─────────────────────────────────────────
    //  카드 초기화
    // ─────────────────────────────────────────
    [ContextMenu("카드 초기화")]
    public void ClearCards()
    {
        StopAllCoroutines();
        _isAnimating = false;

        foreach (GameObject card in _spawnedCards)
        {
            if (card == null) continue;
            if (Application.isPlaying)
                Destroy(card);
            else
                DestroyImmediate(card);
        }
        _spawnedCards.Clear();
        _handSpecs.Clear();
        _targetPositions.Clear();
        _targetRotations.Clear();
    }

    // ─────────────────────────────────────────
    //  외부에서 카드 제거
    // ─────────────────────────────────────────
    public void RemoveCard(GameObject card)
    {
        int idx = _spawnedCards.IndexOf(card);
        if (idx < 0) return;

        _spawnedCards.RemoveAt(idx);
        if (idx < _handSpecs.Count) _handSpecs.RemoveAt(idx);
        _targetPositions.RemoveAt(idx);
        _targetRotations.RemoveAt(idx);
        UpdateAllTargets();
    }

    // ─────────────────────────────────────────
    //  카드 1장 제거 (손패 개수 동기화용 — 마지막 카드 제거)
    // ─────────────────────────────────────────
    public void RemoveOneCard()
    {
        if (_spawnedCards.Count == 0) return;
        int idx = _spawnedCards.Count - 1;

        GameObject card = _spawnedCards[idx];
        _spawnedCards.RemoveAt(idx);
        if (idx < _handSpecs.Count) _handSpecs.RemoveAt(idx);
        if (idx < _targetPositions.Count) _targetPositions.RemoveAt(idx);
        if (idx < _targetRotations.Count) _targetRotations.RemoveAt(idx);
        if (card != null) Destroy(card);
        UpdateAllTargets();
    }

    // ─────────────────────────────────────────
    //  내부: 카드 생성
    //  - showOpponentCards가 켜져 있고 앞면 프리팹이 있으면 앞면, 아니면 뒷면
    // ─────────────────────────────────────────
    private GameObject SpawnCard(Deck.CardPool spec)
    {
        GameObject prefab = (showOpponentCards && spec.prefab != null) ? spec.prefab : cardBackPrefab;

        GameObject card = Instantiate(prefab, Parent);
        card.transform.localPosition = deck.spawnOffset * -1f;
        card.transform.localRotation = Quaternion.identity;

        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value     = spec.value;
        cv.isJoker   = spec.isJoker;
        cv.poolIndex = spec.poolIndex;

        // 상대 카드는 상호작용 불가
        var hover = card.GetComponent<CardHover>();
        if (hover != null) hover.enabled = false;
        foreach (var col in card.GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        _spawnedCards.Add(card);
        _handSpecs.Add(spec);
        _targetPositions.Add(card.transform.localPosition);
        _targetRotations.Add(card.transform.localRotation);

        return card;
    }

    // ─────────────────────────────────────────
    //  현재 손패를 앞/뒷면 설정에 맞게 다시 생성
    // ─────────────────────────────────────────
    private void RebuildVisuals()
    {
        if (_handSpecs.Count == 0) return;

        var specs = new List<Deck.CardPool>(_handSpecs);

        foreach (GameObject card in _spawnedCards)
            if (card != null) Destroy(card);

        _spawnedCards.Clear();
        _handSpecs.Clear();
        _targetPositions.Clear();
        _targetRotations.Clear();

        foreach (var spec in specs)
            SpawnCard(spec);

        UpdateAllTargets();
    }

    // ─────────────────────────────────────────
    //  딜 애니메이션: 한 장씩 생성
    // ─────────────────────────────────────────
    private IEnumerator DealAnimation(List<Deck.CardPool> cards)
    {
        _isAnimating = true;

        foreach (var c in cards)
        {
            SpawnCard(c);
            UpdateAllTargets();

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

            yield return new WaitForSeconds(deck.dealDelay);
        }

        _isAnimating = false;
    }

    // ─────────────────────────────────────────
    //  모든 카드 타겟 위치 갱신
    // ─────────────────────────────────────────
    private void UpdateAllTargets()
    {
        int count = _spawnedCards.Count;
        for (int i = 0; i < count; i++)
        {
            if (_spawnedCards[i] == null) continue;

            GetArchTarget(i, count, out Vector3 pos, out Quaternion rot);
            _targetPositions[i] = pos;
            _targetRotations[i] = rot;

            foreach (var sr in _spawnedCards[i].GetComponentsInChildren<SpriteRenderer>())
                sr.sortingOrder = deck.baseSortingOrder + i * 2;
        }
    }

    // ─────────────────────────────────────────
    //  아치형 목표 위치 + 회전 (플레이어 아치 반전)
    // ─────────────────────────────────────────
    private void GetArchTarget(int index, int total, out Vector3 pos, out Quaternion rot)
    {
        float angleRange = CurrentAngleRange;
        float t = total == 1 ? 0f : (float)index / (total - 1) - 0.5f;

        float angleDeg = t * angleRange;
        float angleRad = angleDeg * Mathf.Deg2Rad;

        pos = new Vector3(
            Mathf.Sin(angleRad),
            -(Mathf.Cos(angleRad) - 1f),
            0f
        ) * deck.archRadius;

        rot = Quaternion.Euler(0f, 0f, angleDeg);
    }
}
