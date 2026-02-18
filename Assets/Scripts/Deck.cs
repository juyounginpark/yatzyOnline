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
    public float animDuration = 0.3f;
    public float dealDelay = 0.1f;
    public Vector3 spawnOffset = new Vector3(0f, -3f, 0f);

    // ─── 내부 상태 ───
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private List<GameObject> _prefabPool;
    private bool _isAnimating;
    private CardHover _currentHover;
    private ContactFilter2D _contactFilter = new ContactFilter2D();
    private readonly List<RaycastHit2D> _hitResults = new List<RaycastHit2D>();

    private float CurrentAngleRange =>
        baseAngleRange + Mathf.Max(0, _spawnedCards.Count - drawCount) * anglePerCard;

    void Start()
    {
        _contactFilter.useTriggers = true;
        _contactFilter.useLayerMask = false;
        _prefabPool = BuildPrefabPool();
        DrawCards();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && !_isAnimating)
            AddOneCard();

        UpdateHover();
    }

    // ─────────────────────────────────────────
    //  마우스 호버: sortingOrder가 가장 높은 카드만
    // ─────────────────────────────────────────
    private void UpdateHover()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 worldPos = cam.ScreenToWorldPoint(Input.mousePosition);
        int hitCount = Physics2D.Raycast(worldPos, Vector2.zero, _contactFilter, _hitResults, 0f);

        CardHover topHover = null;
        int topOrder = int.MinValue;

        for (int i = 0; i < hitCount; i++)
        {
            var hover = _hitResults[i].collider.GetComponent<CardHover>();
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

        UpdateAllCardBases();
        StartCoroutine(AnimateAllCards());
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
        StartCoroutine(AnimateAddCard());
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
    }

    // ─────────────────────────────────────────
    //  내부: 카드 생성 + CardHover 자동 부착
    // ─────────────────────────────────────────
    private GameObject SpawnCard(GameObject prefab)
    {
        Transform parent = deckSpawnPoint != null ? deckSpawnPoint : transform;
        GameObject card = Instantiate(prefab, parent);
        card.transform.localPosition = spawnOffset;
        card.transform.localRotation = Quaternion.identity;

        // Collider2D 없으면 자동 추가 (마우스 감지용)
        if (card.GetComponent<Collider2D>() == null)
            card.AddComponent<BoxCollider2D>();

        // CardHover 부착
        if (card.GetComponent<CardHover>() == null)
            card.AddComponent<CardHover>();

        _spawnedCards.Add(card);
        return card;
    }

    // ─────────────────────────────────────────
    //  내부: 모든 카드의 base 위치/회전/sortingOrder 갱신
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
    //  내부: 아치형 목표 위치 + 회전
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
    //  애니메이션: 초기 카드 왼→오 촤르륵 등장
    // ─────────────────────────────────────────
    private IEnumerator AnimateAllCards()
    {
        _isAnimating = true;
        int count = _spawnedCards.Count;

        for (int i = 0; i < count; i++)
        {
            GetArchTarget(i, count, out Vector3 targetPos, out Quaternion targetRot);
            StartCoroutine(AnimateCard(_spawnedCards[i], targetPos, targetRot));
            yield return new WaitForSeconds(dealDelay);
        }

        yield return new WaitForSeconds(animDuration);
        RefreshAllHovers();
        _isAnimating = false;
    }

    // ─────────────────────────────────────────
    //  애니메이션: 카드 추가 → 전체 아치 재배치
    // ─────────────────────────────────────────
    private IEnumerator AnimateAddCard()
    {
        _isAnimating = true;
        int count = _spawnedCards.Count;

        for (int i = 0; i < count - 1; i++)
        {
            GetArchTarget(i, count, out Vector3 pos, out Quaternion rot);
            StartCoroutine(AnimateCard(_spawnedCards[i], pos, rot));
        }

        yield return new WaitForSeconds(dealDelay);
        GetArchTarget(count - 1, count, out Vector3 newPos, out Quaternion newRot);
        StartCoroutine(AnimateCard(_spawnedCards[count - 1], newPos, newRot));

        yield return new WaitForSeconds(animDuration);
        RefreshAllHovers();
        _isAnimating = false;
    }

    // ─────────────────────────────────────────
    //  애니메이션: 단일 카드 위치+회전
    // ─────────────────────────────────────────
    private IEnumerator AnimateCard(GameObject card, Vector3 targetPos, Quaternion targetRot)
    {
        if (card == null) yield break;

        Vector3 fromPos = card.transform.localPosition;
        Quaternion fromRot = card.transform.localRotation;
        float elapsed = 0f;

        while (elapsed < animDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / animDuration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);

            card.transform.localPosition = Vector3.Lerp(fromPos, targetPos, ease);
            card.transform.localRotation = Quaternion.Slerp(fromRot, targetRot, ease);
            yield return null;
        }

        card.transform.localPosition = targetPos;
        card.transform.localRotation = targetRot;
    }

    private void RefreshAllHovers()
    {
        foreach (var card in _spawnedCards)
        {
            if (card == null) continue;
            var hover = card.GetComponent<CardHover>();
            if (hover != null)
                hover.RefreshHover();
        }
    }

    // ─────────────────────────────────────────
    //  내부: 유효한 프리팹 풀 수집
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
