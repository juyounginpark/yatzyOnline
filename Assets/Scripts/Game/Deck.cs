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
//  덱 그룹 (카드 8종 + 조커, 마지막 칸이 조커)
//  인덱스 0~7 = 값 1~8, 마지막 인덱스 = 조커
// ─────────────────────────────────────────────
[Serializable]
public class DeckGroup
{
    public bool isActive = true;
    public string groupName = "New Group";

    [Tooltip("그룹에 넣을 프리팹 목록 (인덱스 0~7=값 1~8, 마지막 칸=조커)")]
    public CardEntry[] cards = new CardEntry[9];
}

// ─────────────────────────────────────────────
//  덱 매니저 (드로우 + 핸드 + 슬롯 배치)
// ─────────────────────────────────────────────
public class Deck : MonoBehaviour
{
    [Header("─ 덱 그룹 목록 ─")]
    public DeckGroup[] deckGroups = new DeckGroup[1];

    [Header("─ 카드 뒷면 ─")]
    public GameObject cardBackPrefab;

    [Header("─ 상대 덱 참조 ─")]
    [Tooltip("초기 분배 시 같은 덱 풀에서 상대에게도 카드를 나눠줌 (비워두면 자동 탐색)")]
    public OppDeck oppDeck;

    [Header("─ 드로우 설정 ─")]
    [Tooltip("켜면 Start에서 자동 분배. 끄면 RoundDirector가 라운드마다 호출 (블러드 베팅 기본)")]
    public bool autoDealOnStart = false;

    [Tooltip("게임 시작 시 각 플레이어에게 나눠줄 장수")]
    public int drawCount = 2;
    public int maxCards = 8;

    [Tooltip("카드 종류별 덱 풀 보유 장수 (종류 9개 × 이 값 = 총 덱 장수)")]
    public int copiesPerCard = 5;

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
    public struct CardPool { public GameObject prefab; public int value; public bool isJoker; public int poolIndex; }
    private List<CardPool> _prefabPool;   // 카드 종류 정의 (9종, 복원용)
    private List<CardPool> _drawPile;     // 공유 유한 덱 (45장, 비복원 — 셔플 후 끝에서 pop)
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
        if (oppDeck == null) oppDeck = FindObjectOfType<OppDeck>();
        if (autoDealOnStart) DrawCards();
    }

    /// <summary>덱 풀(45장)에서 카드 1장을 뽑아 제거. 풀이 비면 false.</summary>
    public bool DrawFromPile(out CardPool card)
    {
        card = default;
        if (_drawPile == null || _drawPile.Count == 0) return false;

        int last = _drawPile.Count - 1;
        card = _drawPile[last];
        _drawPile.RemoveAt(last);
        return true;
    }

    /// <summary>덱 풀에 남은 장수.</summary>
    public int RemainingInPile => _drawPile != null ? _drawPile.Count : 0;

    /// <summary>종류별 덱 풀 잔여 수 (인덱스 0~7 = 값 1~8, 8 = 조커).</summary>
    public int[] GetRemainingCountsByType()
    {
        int[] counts = new int[9];
        if (_drawPile == null) return counts;

        foreach (var c in _drawPile)
        {
            int idx = c.isJoker ? 8 : (c.value >= 1 && c.value <= 8 ? c.value - 1 : -1);
            if (idx >= 0) counts[idx]++;
        }
        return counts;
    }

    void Update()
    {
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

                if (slot != null && !slot.HasCard && !slot.IsChainLocked)
                {
                    if (SoundManager.Instance != null)
                        SoundManager.Instance.PlaySFX(SoundManager.Instance.cardPlace);

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
    //  외부(진행 로직 등)에서 손패 카드를 슬롯에 배치
    // ─────────────────────────────────────────
    public bool PlaceCardInSlot(GameObject card, Slot slot)
    {
        if (card == null || slot == null || slot.HasCard || slot.IsChainLocked) return false;
        if (!_spawnedCards.Contains(card)) return false;

        _spawnedCards.Remove(card);
        slot.PlaceCard(card);
        UpdateAllCardBases();
        TriggerWaveAll(null);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardPlace);
        return true;
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
    //  게임 시작 분배 (덱 풀 셔플 → 플레이어 drawCount장, 상대 drawCount장)
    //  - 풀 소비 순서: 플레이어 먼저, 그다음 상대 (양 클라이언트 결정적)
    // ─────────────────────────────────────────
    [ContextMenu("카드 뽑기")]
    public void DrawCards()
    {
        PrepareDeal(null);
    }

    /// <summary>결정적 분배 (동일 seed → 양쪽 동일 덱 셔플·동일 손패)</summary>
    public void DrawCards(int seed)
    {
        PrepareDeal(new System.Random(seed));
    }

    private void PrepareDeal(System.Random rng)
    {
        ClearCards();

        if (_prefabPool == null)
            _prefabPool = BuildPrefabPool();

        if (_prefabPool.Count == 0)
        {
            Debug.LogWarning("[Deck] 유효한 카드 프리팹이 없습니다.");
            return;
        }

        BuildDrawPile(rng);
        DealBoth();
    }

    /// <summary>풀을 재구성하지 않고, 남은 풀에서 플레이어·상대 손패를 새로 뽑아 교체.</summary>
    public void RedealHands()
    {
        ClearCards();

        if (_prefabPool == null) _prefabPool = BuildPrefabPool();
        if (_drawPile == null)
        {
            Debug.LogWarning("[Deck] 덱 풀이 없어 손패를 교체할 수 없습니다.");
            return;
        }

        DealBoth();
    }

    // 남은 풀에서 플레이어 drawCount장 → 상대 drawCount장 (결정적 순서)
    private void DealBoth()
    {
        // 홀카드(손패)에는 조커가 들어가지 않음 — 풀에서 숫자 카드만 뽑는다.
        // (조커는 풀에 남아 RandomSlot 커뮤니티로만 등장)
        List<CardPool> playerCards = DrawNumbersOnly(drawCount);
        StartCoroutine(DealAnimation(playerCards));

        if (oppDeck == null) oppDeck = FindObjectOfType<OppDeck>();
        if (oppDeck != null)
        {
            List<CardPool> oppCards = DrawNumbersOnly(drawCount);
            oppDeck.DealInitialFromPile(oppCards);
        }
    }

    /// <summary>현재 플레이어 손패의 카드 스펙 목록 (앞면 프리팹 포함).</summary>
    public List<CardPool> GetHandSpecs()
    {
        var list = new List<CardPool>();
        foreach (var card in _spawnedCards)
        {
            if (card == null) continue;
            var cv = card.GetComponent<CardValue>();
            if (cv == null) continue;

            list.Add(new CardPool
            {
                value     = cv.value,
                isJoker   = cv.isJoker,
                poolIndex = cv.poolIndex,
                prefab    = GetFrontPrefab(cv.value, cv.isJoker)
            });
        }
        return list;
    }

    /// <summary>덱 풀에서 n장 pop (없으면 가능한 만큼).</summary>
    private List<CardPool> DrawN(int n)
    {
        var list = new List<CardPool>(n);
        for (int i = 0; i < n; i++)
            if (DrawFromPile(out CardPool c)) list.Add(c);
        return list;
    }

    /// <summary>덱 풀(45장)만 시드로 재구성한다 (손패 분배는 하지 않음). 홀 슬롯 모드용.</summary>
    public void BuildPool(int seed)
    {
        if (_prefabPool == null) _prefabPool = BuildPrefabPool();
        BuildDrawPile(new System.Random(seed));
    }

    /// <summary>홀카드용: 풀에서 숫자 카드만 n장 뽑아 스펙으로 반환 (조커 제외, 비복원).</summary>
    public List<CardPool> DrawHoleSpecs(int n)
    {
        if (_prefabPool == null) _prefabPool = BuildPrefabPool();
        if (_drawPile == null) return new List<CardPool>();
        return DrawNumbersOnly(n);
    }

    /// <summary>풀에서 조커를 건너뛰고 숫자 카드만 n장 뽑는다 (조커는 풀에 남김). 손패 분배용.</summary>
    private List<CardPool> DrawNumbersOnly(int n)
    {
        var list = new List<CardPool>(n);
        if (_drawPile == null) return list;

        for (int i = _drawPile.Count - 1; i >= 0 && list.Count < n; i--)
        {
            if (!_drawPile[i].isJoker)
            {
                list.Add(_drawPile[i]);
                _drawPile.RemoveAt(i);
            }
        }
        return list;
    }

    // ─────────────────────────────────────────
    //  덱 풀 구성: 종류별 copiesPerCard장 → 셔플 (Fisher-Yates)
    // ─────────────────────────────────────────
    private void BuildDrawPile(System.Random rng)
    {
        _drawPile = new List<CardPool>(_prefabPool.Count * copiesPerCard);
        foreach (var type in _prefabPool)
            for (int c = 0; c < copiesPerCard; c++)
                _drawPile.Add(type);

        for (int i = _drawPile.Count - 1; i > 0; i--)
        {
            int j = rng != null ? rng.Next(i + 1) : UnityEngine.Random.Range(0, i + 1);
            (_drawPile[i], _drawPile[j]) = (_drawPile[j], _drawPile[i]);
        }
    }

    // ─────────────────────────────────────────
    //  카드 1장 추가 (최대 maxCards장)
    // ─────────────────────────────────────────
    public void AddOneCard()
    {
        if (_spawnedCards.Count >= maxCards)
        {
            Debug.Log($"[Deck] 최대 {maxCards}장까지만 가능합니다.");
            return;
        }

        if (!DrawFromPile(out CardPool pick))
        {
            Debug.Log("[Deck] 덱 풀이 비어 더 뽑을 수 없습니다.");
            return;
        }

        SpawnCard(pick.prefab, pick.value, pick.isJoker, pick.poolIndex);
        UpdateAllCardBases();
    }

    // ─────────────────────────────────────────
    //  특정 값의 카드를 새로 생성하여 덱에 추가
    // ─────────────────────────────────────────
    public void AddCardByValue(int value, Vector3? startPos = null)
    {
        if (_prefabPool == null) return;

        foreach (var entry in _prefabPool)
        {
            if (entry.value == value && !entry.isJoker)
            {
                SpawnCard(entry.prefab, entry.value, false, entry.poolIndex, startPos);
                UpdateAllCardBases();
                TriggerWaveAll(null);
                return;
            }
        }
    }

    // ─────────────────────────────────────────
    //  조커 카드를 새로 생성하여 덱에 추가
    // ─────────────────────────────────────────
    public void AddJokerCard(Vector3? startPos = null)
    {
        if (_prefabPool == null) return;

        foreach (var entry in _prefabPool)
        {
            if (entry.isJoker)
            {
                SpawnCard(entry.prefab, 0, true, entry.poolIndex, startPos);
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
    private GameObject SpawnCard(GameObject prefab, int value, bool isJoker = false, int poolIndex = 0, Vector3? startPos = null)
    {
        GameObject card = Instantiate(prefab, Parent);

        if (startPos.HasValue)
        {
            card.transform.position = startPos.Value;
            card.transform.rotation = Quaternion.identity;
        }
        else
        {
            card.transform.localPosition = spawnOffset;
            card.transform.localRotation = Quaternion.identity;
        }

        if (card.GetComponent<Collider2D>() == null)
            card.AddComponent<BoxCollider2D>();

        if (card.GetComponent<CardHover>() == null)
            card.AddComponent<CardHover>();

        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value = value;
        cv.isJoker = isJoker;
        cv.poolIndex = poolIndex;

        _spawnedCards.Add(card);
        return card;
    }

    // ─────────────────────────────────────────
    //  딜 애니메이션: 한 장씩 생성
    // ─────────────────────────────────────────
    private IEnumerator DealAnimation(List<CardPool> cards)
    {
        _isAnimating = true;

        foreach (var pick in cards)
        {
            SpawnCard(pick.prefab, pick.value, pick.isJoker, pick.poolIndex);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

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
    //  손패 정렬: 숫자순
    // ─────────────────────────────────────────
    public void SortByNumber()
    {
        if (_isAnimating || _spawnedCards.Count <= 1) return;
        _spawnedCards.Sort((a, b) =>
        {
            var cva = a != null ? a.GetComponent<CardValue>() : null;
            var cvb = b != null ? b.GetComponent<CardValue>() : null;
            int va = cva != null ? cva.value : 0;
            int vb = cvb != null ? cvb.value : 0;
            return va.CompareTo(vb);
        });
        UpdateAllCardBases();
        TriggerWaveAll(null);
    }

    // ─────────────────────────────────────────
    //  활성 풀 전체에서 랜덤 카드 1장 (조커 포함)
    // ─────────────────────────────────────────
    public bool GetRandomPrefabFromActivePool(
        out GameObject prefab, out int value, out bool isJoker)
    {
        prefab = null; value = 0; isJoker = false;

        if (_prefabPool == null) _prefabPool = BuildPrefabPool();
        if (_prefabPool.Count == 0) return false;

        var pick = _prefabPool[UnityEngine.Random.Range(0, _prefabPool.Count)];
        prefab   = pick.prefab;
        value    = pick.value;
        isJoker  = pick.isJoker;
        return true;
    }

    // ─────────────────────────────────────────
    //  값/조커로 앞면 프리팹 조회 (상대 카드 공개 등)
    // ─────────────────────────────────────────
    public GameObject GetFrontPrefab(int value, bool isJoker)
    {
        if (_prefabPool == null) _prefabPool = BuildPrefabPool();
        foreach (var e in _prefabPool)
        {
            if (isJoker ? e.isJoker : (!e.isJoker && e.value == value))
                return e.prefab;
        }
        return null;
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
            if (group?.cards == null || !group.isActive) continue;

            for (int i = 0; i < group.cards.Length; i++)
            {
                CardEntry card = group.cards[i];
                if (card != null && card.prefab != null)
                {
                    bool joker = (i == group.cards.Length - 1); // 마지막 칸이 조커

                    int cardValue = joker ? 0 : i + 1;
                    var prefabCv = card.prefab.GetComponent<CardValue>();
                    if (!joker && prefabCv != null && prefabCv.value > 0)
                        cardValue = prefabCv.value;

                    pool.Add(new CardPool
                    {
                        prefab = card.prefab,
                        value = cardValue,
                        isJoker = joker,
                        poolIndex = pool.Count
                    });
                }
            }
        }

        return pool;
    }
}
