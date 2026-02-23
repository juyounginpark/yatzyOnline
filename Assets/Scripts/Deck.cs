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
//  이름을 붙일 수 있는 덱 그룹 (최대 7개 카드, 7번째는 조커)
// ─────────────────────────────────────────────
[Serializable]
public class DeckGroup
{
    public string groupName = "New Group";
    public CardType groupType = CardType.Attack;

    [Tooltip("그룹에 넣을 프리팹 목록 (최대 7개, 7번째는 조커)")]
    public CardEntry[] cards = new CardEntry[7];
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
    public int maxCards = 8;

    [Header("─ 스폰 위치 ─")]
    public Transform deckSpawnPoint;

    [Header("─ 아치형 배치 설정 ─")]
    public float archRadius = 3f;

    [Range(10f, 180f)]
    public float baseAngleRange = 40f;
    public float anglePerCard = 8f;

    [Header("─ 소팅 설정 ─")]
    [Tooltip("덱 카드 소팅 오더 시작값 (슬롯보다 높게)")]
    public int baseSortingOrder = 100;

    [Header("─ 애니메이션 설정 ─")]
    public float dealDelay = 0.08f;
    public Vector3 spawnOffset = new Vector3(0f, -3f, 0f);

    [Header("─ 출렁임 설정 ─")]
    public float waveAmount = 0.3f;

    // ─── 내부 상태 ───
    private readonly List<GameObject> _spawnedCards = new List<GameObject>();
    private struct CardPool { public GameObject prefab; public int value; public bool isJoker; public CardType cardType; public int poolIndex; }
    private List<CardPool> _prefabPool;
    private bool _isAnimating;
    private CardHover _currentHover;
    private CardHover _draggingCard;

    [HideInInspector] public bool canPlaceInSlot = true;

    public bool IsAnimating => _isAnimating;
    public bool IsHandFull => _spawnedCards.Count >= maxCards;
    public GameObject HoveredCard => _currentHover != null ? _currentHover.gameObject : null;
    public GameObject DraggedCard => _draggingCard != null ? _draggingCard.gameObject : null;
    public IReadOnlyList<GameObject> SpawnedCards => _spawnedCards;

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

            if (Input.GetMouseButtonUp(0))
            {
                Slot slot = canPlaceInSlot ? FindSlotAtPosition(mouseWorld) : null;

                if (slot != null && !slot.HasCard)
                {
                    GameObject cardObj = _draggingCard.gameObject;
                    _spawnedCards.Remove(cardObj);
                    slot.PlaceCard(cardObj);
                    UpdateAllCardBases();
                    TriggerWaveAll(null);
                }
                else
                {
                    _draggingCard.EndDrag();
                    TriggerWaveAll(_draggingCard);
                }

                SetOtherCardsAlpha(null, 1f);
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
            // 떨림 방지: 현재 호버 카드의 base 영역 안이면 호버 유지
            // (호버 애니메이션으로 콜라이더가 올라가면서 아래 카드가 감지되는 것 방지)
            // 단, 더 높은 우선순위 카드로의 전환은 허용
            if (_currentHover != null
                && (topHover == null || topHover.baseSortingOrder < _currentHover.baseSortingOrder))
            {
                Vector3 baseWorld = Parent.TransformPoint(_currentHover.baseLocalPos);
                var sr = _currentHover.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                {
                    Vector2 spriteSize = sr.sprite.bounds.size;
                    Vector3 scale = sr.transform.lossyScale;
                    float halfW = spriteSize.x * Mathf.Abs(scale.x) * 0.5f;
                    float halfH = spriteSize.y * Mathf.Abs(scale.y) * 0.5f;

                    // 카드 회전 고려: 마우스를 카드 로컬 좌표로 변환
                    float angle = -_currentHover.baseLocalRot.eulerAngles.z * Mathf.Deg2Rad;
                    Vector2 diff = (Vector2)(mouseWorld - baseWorld);
                    Vector2 local = new Vector2(
                        diff.x * Mathf.Cos(angle) - diff.y * Mathf.Sin(angle),
                        diff.x * Mathf.Sin(angle) + diff.y * Mathf.Cos(angle)
                    );

                    if (Mathf.Abs(local.x) < halfW && Mathf.Abs(local.y) < halfH)
                        topHover = _currentHover;
                }
            }
        }

        if (topHover != _currentHover)
        {
            if (_currentHover != null)
                _currentHover.Unhover();
            _currentHover = topHover;
            if (_currentHover != null)
            {
                _currentHover.Hover();
                SetOtherCardsAlpha(_currentHover.gameObject, 0.5f);
            }
            else
            {
                SetOtherCardsAlpha(null, 1f);
            }
        }

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
    //  나머지 카드 투명도 설정
    // ─────────────────────────────────────────
    private void SetOtherCardsAlpha(GameObject except, float alpha)
    {
        foreach (var card in _spawnedCards)
        {
            if (card == null) continue;
            float a = (card == except) ? 1f : alpha;
            foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
            {
                Color c = sr.color;
                c.a = a;
                sr.color = c;
            }
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

        var pick = _prefabPool[UnityEngine.Random.Range(0, _prefabPool.Count)];
        SpawnCard(pick.prefab, pick.value, pick.isJoker, pick.cardType, pick.poolIndex);
        UpdateAllCardBases();
    }

    // ─────────────────────────────────────────
    //  특정 값의 카드를 새로 생성하여 덱에 추가
    // ─────────────────────────────────────────
    public void AddCardByValue(int value, CardType cardType = CardType.Attack)
    {
        if (_prefabPool == null) return;

        // 해당 value + type의 프리팹 찾기
        foreach (var entry in _prefabPool)
        {
            if (entry.value == value && !entry.isJoker && entry.cardType == cardType)
            {
                SpawnCard(entry.prefab, entry.value, false, entry.cardType, entry.poolIndex);
                UpdateAllCardBases();
                TriggerWaveAll(null);
                return;
            }
        }

        // 타입 일치 없으면 value만 매칭
        foreach (var entry in _prefabPool)
        {
            if (entry.value == value && !entry.isJoker)
            {
                SpawnCard(entry.prefab, entry.value, false, cardType, entry.poolIndex);
                UpdateAllCardBases();
                TriggerWaveAll(null);
                return;
            }
        }
    }

    // ─────────────────────────────────────────
    //  조커 카드를 새로 생성하여 덱에 추가
    // ─────────────────────────────────────────
    public void AddJokerCard(CardType cardType = CardType.Attack)
    {
        if (_prefabPool == null) return;

        foreach (var entry in _prefabPool)
        {
            if (entry.isJoker)
            {
                SpawnCard(entry.prefab, 0, true, cardType, entry.poolIndex);
                UpdateAllCardBases();
                TriggerWaveAll(null);
                return;
            }
        }
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
    private GameObject SpawnCard(GameObject prefab, int value, bool isJoker = false, CardType cardType = CardType.Attack, int poolIndex = 0)
    {
        GameObject card = Instantiate(prefab, Parent);
        card.transform.localPosition = spawnOffset;
        card.transform.localRotation = Quaternion.identity;

        if (card.GetComponent<Collider2D>() == null)
            card.AddComponent<BoxCollider2D>();

        if (card.GetComponent<CardHover>() == null)
            card.AddComponent<CardHover>();

        // 카드 값 부여
        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value = value;
        cv.isJoker = isJoker;
        cv.cardType = cardType;
        cv.poolIndex = poolIndex;

        _spawnedCards.Add(card);
        return card;
    }

    // ─────────────────────────────────────────
    //  딜 애니메이션: 한 장씩 생성하면서 쫘라락
    // ─────────────────────────────────────────
    private IEnumerator DealAnimation()
    {
        _isAnimating = true;

        for (int i = 0; i < drawCount; i++)
        {
            // 한 장씩 생성
            var pick = _prefabPool[UnityEngine.Random.Range(0, _prefabPool.Count)];
            SpawnCard(pick.prefab, pick.value, pick.isJoker, pick.cardType, pick.poolIndex);

            // 현재까지 생성된 전체 카드 재배치
            int count = _spawnedCards.Count;
            for (int j = 0; j < count; j++)
            {
                GetArchTarget(j, count, out Vector3 pos, out Quaternion rot);
                var hover = _spawnedCards[j].GetComponent<CardHover>();
                if (hover != null)
                    hover.SetBase(pos, rot, baseSortingOrder + j * 2);
            }

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
                hover.SetBase(pos, rot, baseSortingOrder + i * 2);
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
    private List<CardPool> BuildPrefabPool()
    {
        List<CardPool> pool = new List<CardPool>();

        if (deckGroups == null) return pool;

        foreach (DeckGroup group in deckGroups)
        {
            if (group?.cards == null) continue;

            for (int i = 0; i < group.cards.Length; i++)
            {
                CardEntry card = group.cards[i];
                if (card != null && card.prefab != null)
                {
                    bool joker = (i == 6); // 7번째 카드는 조커
                    pool.Add(new CardPool
                    {
                        prefab = card.prefab,
                        value = joker ? 0 : i + 1,  // 원소 순서대로 1~6, 조커는 0
                        isJoker = joker,
                        cardType = group.groupType,
                        poolIndex = pool.Count
                    });
                }
            }
        }

        return pool;
    }
}
