using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  상대방 덱 매니저
//  - 카드 뒷면 프리팹 1종 통일
//  - 배치·애니메이션 설정은 Deck에서 참조
//  - 위→아래 방향 아치형 배치 (플레이어 아치 반전)
//  - 플레이어 상호작용 없음
// ─────────────────────────────────────────────
public class OppDeck : MonoBehaviour
{
    [Header("─ 플레이어 덱 참조 ─")]
    public Deck deck;

    [Header("─ 카드 뒷면 프리팹 ─")]
    public GameObject cardBackPrefab;

    [Header("─ 드로우 설정 ─")]
    public int drawCount = 5;

    [Header("─ 스폰 위치 ─")]
    public Transform deckSpawnPoint;

    // ─── 내부 상태 ───
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private readonly List<Vector3> _targetPositions = new List<Vector3>();
    private readonly List<Quaternion> _targetRotations = new List<Quaternion>();
    private bool _isAnimating;
    private float _smoothSpeed = 10f;

    // [버그 수정] OppAuto 참조 - 배치 중인 카드는 LateUpdate 회전 차단
    private OppAuto _oppAuto;
    public bool IsAnimating => _isAnimating;
    public IReadOnlyList<GameObject> SpawnedCards => _spawnedCards;

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
            {
                Debug.LogWarning("[OppDeck] Deck을 찾을 수 없습니다.");
                return;
            }
        }

        // OppAuto 참조 캐시 (LateUpdate 회전 차단용)
        _oppAuto = GetComponentInParent<OppAuto>();
        if (_oppAuto == null)
            _oppAuto = FindObjectOfType<OppAuto>();

        DrawCards();
    }

    void LateUpdate()
    {
        float t = 1f - Mathf.Exp(-_smoothSpeed * Time.deltaTime);

        for (int i = 0; i < _spawnedCards.Count; i++)
        {
            if (_spawnedCards[i] == null) continue;

            // [버그 수정] AnimatePlace 중인 카드는 아치 위치/회전 덮어쓰지 않음
            if (_oppAuto != null && _oppAuto.IsCardPlacing(_spawnedCards[i])) continue;

            var tr = _spawnedCards[i].transform;
            tr.localPosition = Vector3.Lerp(tr.localPosition, _targetPositions[i], t);
            tr.localRotation = Quaternion.Slerp(tr.localRotation, _targetRotations[i], t);
        }
    }

    // ─────────────────────────────────────────
    //  카드 뽑기 (drawCount장)
    // ─────────────────────────────────────────
    [ContextMenu("카드 뽑기")]
    public void DrawCards()
    {
        ClearCards();

        if (cardBackPrefab == null)
        {
            Debug.LogWarning("[OppDeck] 카드 뒷면 프리팹이 지정되지 않았습니다.");
            return;
        }

        StartCoroutine(DealAnimation());
    }

    // ─────────────────────────────────────────
    //  카드 1장 추가
    // ─────────────────────────────────────────
    public void AddOneCard()
    {
        if (cardBackPrefab == null) return;
        if (_spawnedCards.Count >= deck.maxCards) return;

        // 플레이어 덱 풀에서 랜덤 타입 선택
        CardType type = GetRandomType();
        int value = Random.Range(1, 7);
        SpawnCard(value, type);
        UpdateAllTargets();
    }

    // ─────────────────────────────────────────
    //  특정 값 카드 추가 (슬롯 복귀용)
    // ─────────────────────────────────────────
    public void AddCardByValue(int value, CardType cardType = CardType.Attack)
    {
        if (cardBackPrefab == null) return;
        SpawnCard(value, cardType);
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
        _targetPositions.Clear();
        _targetRotations.Clear();
    }

    // ─────────────────────────────────────────
    //  외부에서 카드 제거 (AI 배치 시 사용)
    // ─────────────────────────────────────────
    public void RemoveCard(GameObject card)
    {
        int idx = _spawnedCards.IndexOf(card);
        if (idx < 0) return;

        _spawnedCards.RemoveAt(idx);
        _targetPositions.RemoveAt(idx);
        _targetRotations.RemoveAt(idx);
        UpdateAllTargets();
    }

    // ─────────────────────────────────────────
    //  내부: 카드 생성 (상호작용 컴포넌트 없음)
    // ─────────────────────────────────────────
    private GameObject SpawnCard(int value, CardType cardType = CardType.Attack)
    {
        GameObject card = Instantiate(cardBackPrefab, Parent);
        card.transform.localPosition = deck.spawnOffset * -1f;
        card.transform.localRotation = Quaternion.identity;

        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value = value;
        cv.cardType = cardType;

        _spawnedCards.Add(card);
        _targetPositions.Add(card.transform.localPosition);
        _targetRotations.Add(card.transform.localRotation);

        return card;
    }

    // ─────────────────────────────────────────
    //  딜 애니메이션: 한 장씩 생성
    // ─────────────────────────────────────────
    private IEnumerator DealAnimation()
    {
        _isAnimating = true;

        for (int i = 0; i < drawCount; i++)
        {
            int value = Random.Range(1, 7);
            SpawnCard(value, GetRandomType());
            UpdateAllTargets();

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

            var sr = _spawnedCards[i].GetComponent<SpriteRenderer>();
            if (sr != null)
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

        // 위→아래: Y 반전 (아치가 아래로 볼록)
        pos = new Vector3(
            Mathf.Sin(angleRad),
            -(Mathf.Cos(angleRad) - 1f),
            0f
        ) * deck.archRadius;

        // 회전도 반전 (카드가 아치 곡선을 따라감)
        rot = Quaternion.Euler(0f, 0f, angleDeg);
    }

    // ─────────────────────────────────────────
    //  덱 그룹에서 랜덤 타입 선택
    // ─────────────────────────────────────────
    private CardType GetRandomType()
    {
        if (deck == null || deck.deckGroups == null || deck.deckGroups.Length == 0)
            return CardType.Attack;
        return deck.deckGroups[Random.Range(0, deck.deckGroups.Length)].groupType;
    }
}
