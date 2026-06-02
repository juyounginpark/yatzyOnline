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
        if (slot == null || slot.HasCard || slot.IsChainLocked) return;

        var card = SpawnCard(value, type, isJoker, faceDown: true);
        if (card != null)
        {
            StartCoroutine(AnimatePlace(card, slot, null));
        }
        else
        {
            // 비주얼 프리팹이 없어도 슬롯이 비지 않게 최소 카드 생성 (DoTurnEnd와 동일 패턴)
            Debug.LogWarning($"[OppOnline] HandleCardPlaced: SpawnCard null — 최소 카드로 fallback (slot:{slotIndex})");
            var fallback = new GameObject("OppCard_" + slotIndex);
            var cv = fallback.AddComponent<CardValue>();
            cv.value    = value;
            cv.cardType = type;
            cv.isJoker  = isJoker;
            slot.PlaceCardRaw(fallback);
        }

        // 손패 개수 동기화: 상대가 카드를 냈으니 상대 손패(oppDeck)에서 1장 제거
        if (oppDeck != null) oppDeck.RemoveOneCard();
    }

    // ─────────────────────────────────────────
    //  실시간 카드 반환
    // ─────────────────────────────────────────
    public void HandleCardReturned(int slotIndex)
    {
        if (OppSlots == null || slotIndex < 0 || slotIndex >= OppSlots.Length) return;
        var slot = OppSlots[slotIndex];
        if (slot == null) return;

        bool had = slot.HasCard;
        slot.ClearCard();

        // 손패 개수 동기화: 상대가 카드를 패로 회수했으니 상대 손패(oppDeck)에 1장 복원 (뒷면)
        if (had && oppDeck != null) oppDeck.AddCardByValue(1, CardType.Attack);
    }

    // ─────────────────────────────────────────
    //  상대 턴 종료 처리
    //  MainFlow.Update()에서 직접 호출 — 이 컴포넌트가 코루틴 소유
    // ─────────────────────────────────────────
    public void HandleTurnEnd(SlotCardData[] slots, bool fieldGuard = false)
    {
        StartCoroutine(DoTurnEnd(slots, fieldGuard));
    }

    private IEnumerator DoTurnEnd(SlotCardData[] slots, bool fieldGuard)
    {
        Debug.Log($"[OppOnline] DoTurnEnd 시작 — 슬롯 수:{slots?.Length ?? 0}, Guard:{fieldGuard}");
        _animating = true;

        var toFlip     = new List<GameObject>();
        var placedSlots = new List<Slot>();

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
                // Guard 카드(잔류 또는 이번에 Guard로 갈 카드)는 뒤집지 않음
                if (!slot.IsGuard && !fieldGuard)
                    toFlip.Add(slot.GetPlacedCard());
                placedSlots.Add(slot);
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
                // 실시간 배치 패킷 없이 이번에 처음 배치된 카드 → 손패 1장 차감 (동기화)
                if (oppDeck != null) oppDeck.RemoveOneCard();
                toFlip.Add(card);
                placedSlots.Add(slot);
            }
        }

        // 2) Guard면 뒷면 유지(논리 상태만 Guard), 아니면 앞면 공개
        if (fieldGuard)
        {
            foreach (var slot in placedSlots)
                slot.MarkGuard(true);
        }
        else if (toFlip.Count > 0)
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
    //  판정 시 상대 Guard 카드 공개 (뒷면 프리팹 → 앞면 스프라이트)
    // ─────────────────────────────────────────
    public IEnumerator RevealGuardCardRoutine(Slot slot)
    {
        if (slot == null) yield break;
        var card = slot.GetPlacedCard();
        if (card == null) { slot.MarkGuard(false); yield break; }

        yield return StartCoroutine(FlipToFace(card));
        slot.MarkGuard(false);
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

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardPlace);

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

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardFlip);
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

        // SpriteRenderer가 루트가 아닌 자식에 있을 수 있으므로 GetComponentInChildren로 탐색.
        // (루트만 보면 스왑이 조용히 실패해 카드가 뒷면 그대로 남는 버그)
        var prefabSr = prefab.GetComponentInChildren<SpriteRenderer>(true);
        var sr       = card.GetComponentInChildren<SpriteRenderer>(true);
        if (prefabSr != null && sr != null)
        {
            sr.sprite  = prefabSr.sprite;
            sr.enabled = true;  // 혹시 비활성화돼 있었다면 확실히 표시
        }
        else
        {
            Debug.LogWarning($"[OppOnline] SwapSprite 실패 — prefabSr:{prefabSr!=null} sr:{sr!=null} (value:{cv.value}, type:{cv.cardType})");
        }
    }
}
