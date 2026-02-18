using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  카드 한 장의 정보 (프리팹)
// ─────────────────────────────────────────────
[Serializable]
public class CardEntry
{
    public string cardName = "Card";
    public GameObject prefab;
}

// ─────────────────────────────────────────────
//  이름을 붙일 수 있는 덱 그룹 (최대 6개 카드)
// ─────────────────────────────────────────────
[Serializable]
public class DeckGroup
{
    public string groupName = "New Group";

    [Tooltip("그룹에 넣을 프리팹 목록 (최대 6개)")]
    public CardEntry[] cards = new CardEntry[6];
}

// ─────────────────────────────────────────────
//  덱 매니저
// ─────────────────────────────────────────────
public class Deck : MonoBehaviour
{
    [Header("─ 덱 그룹 목록 ─")]
    public DeckGroup[] deckGroups = new DeckGroup[1];

    [Header("─ 드로우 설정 ─")]
    public int drawCount = 5;
    public int maxCards = 10;

    [Header("─ 스폰 위치 ─")]
    public Transform deckSpawnPoint;

    [Header("─ 아치형 배치 설정 ─")]
    public float archRadius = 3f;

    [Range(10f, 180f)]
    public float baseAngleRange = 40f;

    public float anglePerCard = 8f;

    [Header("─ 애니메이션 설정 ─")]
    public float dealDelay = 0.1f;
    public Vector3 spawnOffset = new Vector3(0f, -3f, 0f);

    [Header("─ 출렁임 설정 ─")]
    public float waveAmount = 0.3f;

    // ─── 내부 상태 ───
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private List<GameObject> _prefabPool;
    private bool _isAnimating;
    private CardHover _currentHover;
    private CardHover _draggingCard;

    private float CurrentAngleRange =>
        baseAngleRange + Mathf.Max(0, _spawnedCards.Count - drawCount) * anglePerCard;

    private Transform Parent =>
        deckSpawnPoint != null ? deckSpawnPoint : transform;

    void Start()
    {
        _prefabPool = BuildPrefabPool();
        DrawCards();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !_isAnimating && _draggingCard == null)
            AddOneCard();

        UpdateHoverAndDrag();
    }

    // ─────────────────────────────────────────
    //  호버 + 드래그 처리
    // ─────────────────────────────────────────
    private void UpdateHoverAndDrag()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        mouseWorld.z = 0f;

        // 드래그 중
        if (_draggingCard != null)
        {
            Vector3 localPos = Parent.InverseTransformPoint(mouseWorld);
            _draggingCard.UpdateDrag(localPos);

            // 마우스 놓음
            if (Input.GetMouseButtonUp(0))
            {
                // 슬롯 위인지 확인
                Slot slot = FindSlotAtPosition(mouseWorld);

                if (slot != null && !slot.HasCard)
                {
                    // 카드를 슬롯에 올리고 덱에서 제거
                    GameObject cardObj = _draggingCard.gameObject;
                    _spawnedCards.Remove(cardObj);
                    slot.PlaceCard(cardObj);
                    UpdateAllCardBases();
                    TriggerWaveAll(null);
                }
                else
                {
                    // 슬롯 아님 → 손으로 복귀
                    _draggingCard.EndDrag();
                    TriggerWaveAll(_draggingCard);
                }

                _draggingCard = null;
                _currentHover = null;
            }
            return;
        }

        // 호버 감지
        Collider2D[] hits = Physics2D.OverlapPointAll(mouseWorld);

        CardHover topHover = null;
        int topOrder = int.MinValue;

        foreach (var hit in hits)
        {
            var hover = hit.GetComponent<CardHover>();
            if (hover != null && hover.baseSortingOrder > topOrder)
            {
                topOrder = hover.baseSortingOrder;
                topHover = hover;
            }
        }

        if (topHover != _currentHover)
        {
            if (_currentHover != null)
                _currentHover.Unhover();
            _currentHover = topHover;
            if (_currentHover != null)
                _currentHover.Hover();
        }

        // 클릭 → 드래그 시작
        if (_currentHover != null && Input.GetMouseButtonDown(0))
        {
            _draggingCard = _currentHover;
            _draggingCard.StartDrag();
            Vector3 localPos = Parent.InverseTransformPoint(mouseWorld);
            _draggingCard.UpdateDrag(localPos);
        }
    }

    // ─────────────────────────────────────────
    //  마우스 위치에서 슬롯 찾기
    // ─────────────────────────────────────────
    private Slot FindSlotAtPosition(Vector3 worldPos)
    {
        Collider2D[] hits = Physics2D.OverlapPointAll(worldPos);
        foreach (var hit in hits)
        {
            var slot = hit.GetComponent<Slot>();
            if (slot != null)
                return slot;
        }
        return null;
    }

    // ─────────────────────────────────────────
    //  출렁임
    // ─────────────────────────────────────────
    private void TriggerWaveAll(CardHover except)
    {
        foreach (var card in _spawnedCards)
        {
            if (card == null) continue;
            var hover = card.GetComponent<CardHover>();
            if (hover != null && hover != except)
                hover.TriggerWave(waveAmount);
        }
    }

    // ─────────────────────────────────────────
    //  카드 뽑기 (초기 drawCount장)
    // ─────────────────────────────────────────
    [ContextMenu("카드 뽑기")]
    public void DrawCards()
    {
        ClearCards();

        if (_prefabPool == null)
            _prefabPool = BuildPrefabPool();

        if (_prefabPool.Count == 0)
        {
            Debug.LogWarning("[Deck] 유효한 카드 프리팹이 없습니다.");
            return;
        }

        for (int i = 0; i < drawCount; i++)
        {
            GameObject prefab = _prefabPool[UnityEngine.Random.Range(0, _prefabPool.Count)];
            SpawnCard(prefab);
        }

        StartCoroutine(DealAnimation());
    }

    // ─────────────────────────────────────────
    //  스페이스바: 카드 1장 추가 (최대 maxCards장)
    // ─────────────────────────────────────────
    public void AddOneCard()
    {
        if (_prefabPool == null || _prefabPool.Count == 0) return;
        if (_spawnedCards.Count >= maxCards)
        {
            Debug.Log($"[Deck] 최대 {maxCards}장까지만 가능합니다.");
            return;
        }

        GameObject prefab = _prefabPool[UnityEngine.Random.Range(0, _prefabPool.Count)];
        SpawnCard(prefab);
        UpdateAllCardBases();
    }

    // ─────────────────────────────────────────
    //  카드 초기화
    // ─────────────────────────────────────────
    [ContextMenu("카드 초기화")]
    public void ClearCards()
    {
        StopAllCoroutines();
        _isAnimating = false;
        _draggingCard = null;
        _currentHover = null;

        foreach (GameObject card in _spawnedCards)
        {
            if (card == null) continue;
            if (Application.isPlaying)
                Destroy(card);
            else
                DestroyImmediate(card);
        }
        _spawnedCards.Clear();
    }

    // ─────────────────────────────────────────
    //  내부: 카드 생성 + CardHover 자동 부착
    // ─────────────────────────────────────────
    private GameObject SpawnCard(GameObject prefab)
    {
        GameObject card = Instantiate(prefab, Parent);
        card.transform.localPosition = spawnOffset;
        card.transform.localRotation = Quaternion.identity;

        if (card.GetComponent<Collider2D>() == null)
            card.AddComponent<BoxCollider2D>();

        if (card.GetComponent<CardHover>() == null)
            card.AddComponent<CardHover>();

        _spawnedCards.Add(card);
        return card;
    }

    // ─────────────────────────────────────────
    //  딜 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator DealAnimation()
    {
        _isAnimating = true;
        int count = _spawnedCards.Count;

        for (int i = 0; i < count; i++)
        {
            GetArchTarget(i, count, out Vector3 pos, out Quaternion rot);
            var hover = _spawnedCards[i].GetComponent<CardHover>();
            if (hover != null)
                hover.SetBase(pos, rot, i);

            yield return new WaitForSeconds(dealDelay);
        }

        _isAnimating = false;
    }

    // ─────────────────────────────────────────
    //  모든 카드 base 갱신
    // ─────────────────────────────────────────
    private void UpdateAllCardBases()
    {
        int count = _spawnedCards.Count;
        for (int i = 0; i < count; i++)
        {
            if (_spawnedCards[i] == null) continue;

            GetArchTarget(i, count, out Vector3 pos, out Quaternion rot);
            var hover = _spawnedCards[i].GetComponent<CardHover>();
            if (hover != null)
                hover.SetBase(pos, rot, i);
        }
    }

    // ─────────────────────────────────────────
    //  아치형 목표 위치 + 회전
    // ─────────────────────────────────────────
    private void GetArchTarget(int index, int total, out Vector3 pos, out Quaternion rot)
    {
        float angleRange = CurrentAngleRange;
        float t = total == 1 ? 0f : (float)index / (total - 1) - 0.5f;

        float angleDeg = t * angleRange;
        float angleRad = angleDeg * Mathf.Deg2Rad;

        pos = new Vector3(
            Mathf.Sin(angleRad),
            Mathf.Cos(angleRad) - 1f,
            0f
        ) * archRadius;

        rot = Quaternion.Euler(0f, 0f, -angleDeg);
    }

    // ─────────────────────────────────────────
    //  유효한 프리팹 풀 수집
    // ─────────────────────────────────────────
    private List<GameObject> BuildPrefabPool()
    {
        List<GameObject> pool = new List<GameObject>();

        if (deckGroups == null) return pool;

        foreach (DeckGroup group in deckGroups)
        {
            if (group?.cards == null) continue;

            foreach (CardEntry card in group.cards)
            {
                if (card != null && card.prefab != null)
                    pool.Add(card.prefab);
            }
        }

        return pool;
    }
}
