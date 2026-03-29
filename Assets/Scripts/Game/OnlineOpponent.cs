using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  OnlineOpponent
//  NetworkManager 큐에서 데이터를 받아 상대 턴을 처리
//  - MainFlow.Update()에서 HandleCardPlaced / HandleCardReturned / HandleTurnEnd 직접 호출
//  - 모든 코루틴은 이 컴포넌트에서만 실행
// ─────────────────────────────────────────────
public class OnlineOpponent : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public MainFlow mainFlow;
    public OppDeck  oppDeck;

    [Header("─ 배치 애니메이션 ─")]
    public float placeDuration = 0.3f;

    [Header("─ 카드 공개 애니메이션 ─")]
    public float flipDuration = 0.4f;
    public float flipStagger  = 0.08f;

    private bool _animating;
    public bool IsAnimating => _animating;

    private Slot[] OppSlots => mainFlow != null ? mainFlow.oppSlots : null;

    // ─────────────────────────────────────────
    void Start()
    {
        if (mainFlow == null) mainFlow = FindObjectOfType<MainFlow>();
        if (oppDeck  == null) oppDeck  = FindObjectOfType<OppDeck>();
        Debug.Log($"[OppOnline] 준비완료 — mainFlow:{mainFlow != null} oppDeck:{oppDeck != null}");
    }

    public void Init(MainFlow mf, OppDeck od)
    {
        mainFlow = mf;
        oppDeck  = od;
        Debug.Log($"[OppOnline] Init — mainFlow:{mf != null} oppDeck:{od != null}");
    }

    // ─────────────────────────────────────────
    //  실시간 카드 배치
    // ─────────────────────────────────────────
    public void HandleCardPlaced(int slotIndex, int value, CardType type, bool isJoker)
    {
        if (OppSlots == null || slotIndex < 0 || slotIndex >= OppSlots.Length) return;
        var slot = OppSlots[slotIndex];
        if (slot == null || slot.HasCard) return;

        var card = SpawnCard(value, type, isJoker, faceDown: true);
        if (card != null)
            StartCoroutine(AnimatePlace(card, slot, null));
    }

    // ─────────────────────────────────────────
    //  실시간 카드 반환
    // ─────────────────────────────────────────
    public void HandleCardReturned(int slotIndex)
    {
        if (OppSlots == null || slotIndex < 0 || slotIndex >= OppSlots.Length) return;
        OppSlots[slotIndex]?.ClearCard();
    }

    // ─────────────────────────────────────────
    //  상대 턴 종료 처리
    //  MainFlow.Update()에서 직접 호출 — 이 컴포넌트가 코루틴 소유
    // ─────────────────────────────────────────
    public void HandleTurnEnd(SlotCardData[] slots)
    {
        StartCoroutine(DoTurnEnd(slots));
    }

    private IEnumerator DoTurnEnd(SlotCardData[] slots)
    {
        Debug.Log($"[OppOnline] DoTurnEnd 시작 — 슬롯 수:{slots?.Length ?? 0}");
        _animating = true;

        var toFlip = new List<GameObject>();

        // 1) 슬롯 채우기
        foreach (var data in slots)
        {
            if (data.value <= 0) continue;
            if (OppSlots == null || data.slotIndex >= OppSlots.Length) continue;
            var slot = OppSlots[data.slotIndex];
            if (slot == null) continue;

            if (slot.HasCard)
            {
                // 이미 실시간으로 배치됨 — CardValue만 갱신
                var cv = slot.GetCardValue();
                if (cv != null)
                {
                    cv.value    = data.value;
                    cv.cardType = (CardType)data.cardType;
                    cv.isJoker  = data.isJoker;
                }
                toFlip.Add(slot.GetPlacedCard());
            }
            else
            {
                // 실시간 패킷 없이 처음 배치
                var card = SpawnCard(data.value, (CardType)data.cardType, data.isJoker, faceDown: true);
                if (card == null)
                {
                    // 비주얼 없어도 데미지 계산을 위해 최소 카드 생성
                    card = new GameObject("OppCard_" + data.slotIndex);
                    var cv2 = card.AddComponent<CardValue>();
                    cv2.value    = data.value;
                    cv2.cardType = (CardType)data.cardType;
                    cv2.isJoker  = data.isJoker;
                    slot.PlaceCardRaw(card);
                }
                else
                {
                    yield return StartCoroutine(AnimatePlace(card, slot, null));
                    yield return new WaitForSeconds(0.1f);
                }
                toFlip.Add(card);
            }
        }

        // 2) 앞면 공개
        if (toFlip.Count > 0)
        {
            yield return new WaitForSeconds(0.2f);
            for (int i = 0; i < toFlip.Count; i++)
            {
                if (toFlip[i] != null)
                    StartCoroutine(FlipToFace(toFlip[i]));
                if (i < toFlip.Count - 1)
                    yield return new WaitForSeconds(flipStagger);
            }
            yield return new WaitForSeconds(flipDuration + flipStagger * toFlip.Count);
        }

        _animating = false;
        yield return new WaitForSeconds(0.15f);

        // 3) MainFlow EndTurn 호출
        if (mainFlow != null && !mainFlow.IsPlayerTurn && !mainFlow.IsTransitioning)
        {
            Debug.Log("[OppOnline] → mainFlow.EndTurn()");
            mainFlow.EndTurn();
        }
        else
        {
            Debug.LogWarning($"[OppOnline] EndTurn 조건 불만족 IsPlayerTurn:{mainFlow?.IsPlayerTurn} IsTransitioning:{mainFlow?.IsTransitioning}");
        }
    }

    // ─────────────────────────────────────────
    //  카드 생성
    // ─────────────────────────────────────────
    private GameObject SpawnCard(int value, CardType type, bool isJoker, bool faceDown)
    {
        GameObject prefab = null;
        if (faceDown && oppDeck != null)
        {
            prefab = oppDeck.cardBackPrefab;
            if (prefab == null && oppDeck.deck != null)
                prefab = oppDeck.deck.cardBackPrefab; // fallback: 플레이어 덱의 뒷면 프리팹
        }

        if (prefab == null)
        {
            Debug.LogWarning($"[OppOnline] SpawnCard: prefab null — oppDeck:{oppDeck != null}, oppDeck.cardBackPrefab:{oppDeck?.cardBackPrefab != null}, oppDeck.deck:{oppDeck?.deck != null}");
        }

        GameObject card = prefab != null
            ? Instantiate(prefab)
            : null;

        if (card == null) return null;

        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value    = value;
        cv.cardType = type;
        cv.isJoker  = isJoker;

        return card;
    }

    // ─────────────────────────────────────────
    //  배치 애니메이션
    // ─────────────────────────────────────────
    private IEnumerator AnimatePlace(GameObject card, Slot targetSlot, System.Action onDone)
    {
        Vector3 startPos = oppDeck != null
            ? (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint.position : oppDeck.transform.position)
            : targetSlot.transform.position;

        card.transform.position = startPos;
        card.transform.SetParent(null);

        foreach (var r in card.GetComponentsInChildren<SpriteRenderer>())
            r.sortingOrder = 500;

        float elapsed = 0f;
        while (elapsed < placeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / placeDuration);
            float eased = t * t * (3f - 2f * t);
            card.transform.position = Vector3.Lerp(startPos, targetSlot.transform.position, eased);
            yield return null;
        }

        card.transform.position = targetSlot.transform.position;
        card.transform.rotation = Quaternion.identity;
        targetSlot.PlaceCard(card);
        onDone?.Invoke();
    }

    // ─────────────────────────────────────────
    //  뒷면 → 앞면 플립
    // ─────────────────────────────────────────
    private IEnumerator FlipToFace(GameObject card)
    {
        if (card == null) yield break;
        float half = flipDuration * 0.5f;
        Vector3 orig = card.transform.localScale;

        // 닫기
        float e = 0f;
        while (e < half)
        {
            e += Time.deltaTime;
            Vector3 s = orig;
            s.x = Mathf.Lerp(orig.x, 0f, Mathf.Clamp01(e / half));
            card.transform.localScale = s;
            yield return null;
        }

        SwapSprite(card);

        // 열기
        e = 0f;
        while (e < half)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / half);
            Vector3 s = orig;
            s.x = Mathf.Lerp(0f, orig.x, 1f - (1f - t) * (1f - t));
            card.transform.localScale = s;
            yield return null;
        }
        card.transform.localScale = orig;
    }

    private void SwapSprite(GameObject card)
    {
        var cv = card.GetComponent<CardValue>();
        var deck = oppDeck?.deck;
        if (cv == null || deck == null) return;
        if (cv.value < 1 || cv.value > 6) return;

        DeckGroup group = null;
        foreach (var g in deck.deckGroups)
            if (g != null && g.groupType == cv.cardType) { group = g; break; }
        if (group == null && deck.deckGroups.Length > 0) group = deck.deckGroups[0];
        if (group == null) return;

        if (cv.value - 1 >= group.cards.Length) return;
        var prefab = group.cards[cv.value - 1].prefab;
        if (prefab == null) return;

        var prefabSr = prefab.GetComponent<SpriteRenderer>();
        var sr = card.GetComponent<SpriteRenderer>();
        if (prefabSr != null && sr != null)
            sr.sprite = prefabSr.sprite;
    }
}
