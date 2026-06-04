using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  RandomSlot — 중앙 커뮤니티 카드 보드 (서비스)
//  - Deck 공유 풀(45장)에서 5장을 비복원으로 가져와 뒷면 배치
//  - 숫자 카드는 오름차순, 조커는 랜덤 위치
//  - 공개/쇼다운은 RoundDirector가 호출 (단독 버튼/클릭/자동분배 없음)
//  - LeftOver가 FaceDownCount/GetFaceDownCountsByType로 잔여 계산
// ─────────────────────────────────────────────
public class RandomSlot : MonoBehaviour
{
    [Header("─ 참조 ─")]
    [Tooltip("카드를 가져올 공유 덱 (비워두면 자동 탐색)")]
    public Deck deck;

    [Tooltip("커뮤니티 5장을 배치할 슬롯 (1번=인덱스0, 오름차순 기준)")]
    public Slot[] slots = new Slot[5];

    [Header("─ 공개 연출 ─")]
    [Tooltip("한 장 공개 시 뒤집기 시간 (클수록 천천히 — 긴장감)")]
    public float revealDuration = 0.6f;

    [Header("─ 재배치 애니메이션 ─")]
    [Tooltip("새 카드 한 장씩 배치 간격(초)")]
    public float dealStagger = 0.08f;

    [Tooltip("새 카드가 커지며 나타나는 시간(초)")]
    public float dealPopDuration = 0.15f;

    [Tooltip("덱 앵커에서 슬롯으로 날아오는 시간(초)")]
    public float dealMoveDuration = 0.25f;

    [Header("─ 덱 앵커 ─")]
    [Tooltip("카드가 출발하는 덱 앵커 (비우면 Deck.deckSpawnPoint → 그것도 없으면 자기 위치)")]
    public Transform dealAnchor;

    [Tooltip("쇼다운 시 커뮤니티 카드 소진 시간")]
    public float consumeDuration = 0.2f;

    [Header("─ 쇼다운 ─")]
    [Tooltip("상대 덱 (비우면 자동 탐색)")]
    public OppDeck oppDeck;

    [Tooltip("플레이어 손패를 공개할 쇼다운 슬롯")]
    public Slot[] playerShowdownSlots = new Slot[2];

    [Tooltip("상대 손패를 공개할 쇼다운 슬롯")]
    public Slot[] oppShowdownSlots = new Slot[2];

    [Header("─ 쇼다운 카메라 연출 ─")]
    [Tooltip("줌에 사용할 카메라 (비우면 Camera.main)")]
    public Camera showdownCamera;

    public float zoomDuration = 0.4f;
    public float zoomOrthoSize = 2f;
    public float zoomFov = 30f;
    public float holdDuration = 0.4f;

    [Header("─ 극적 공개 ─")]
    public float quickRevealDuration = 0.12f;
    public float dramaticRevealDuration = 1.2f;
    public float dramaticShakeIntensity = 0.12f;

    void Start()
    {
        if (deck == null)    deck = FindObjectOfType<Deck>();
        if (oppDeck == null) oppDeck = FindObjectOfType<OppDeck>();
    }

    // ─────────────────────────────────────────
    //  잔여 계산용 (LeftOver)
    // ─────────────────────────────────────────
    public Slot[] CommunitySlots => slots;

    /// <summary>현재 뒷면(미공개) 카드 수.</summary>
    public int FaceDownCount
    {
        get
        {
            int n = 0;
            if (slots != null)
                foreach (var s in slots)
                    if (s != null && s.HasCard && s.IsGuard) n++;
            return n;
        }
    }

    /// <summary>뒷면(미공개) 카드의 종류별 장수 (0~7=값1~8, 8=조커).</summary>
    public int[] GetFaceDownCountsByType()
    {
        int[] counts = new int[9];
        if (slots == null) return counts;
        foreach (var s in slots)
        {
            if (s == null || !s.HasCard || !s.IsGuard) continue;
            var cv = s.GetCardValue();
            if (cv == null) continue;
            int idx = cv.isJoker ? 8 : (cv.value >= 1 && cv.value <= 8 ? cv.value - 1 : -1);
            if (idx >= 0) counts[idx]++;
        }
        return counts;
    }

    // ─────────────────────────────────────────
    //  서비스 API (RoundDirector가 호출)
    // ─────────────────────────────────────────

    /// <summary>덱 풀에서 슬롯 수만큼 뽑아 뒷면 즉시 배치.</summary>
    public void DealCommunity()
    {
        if (deck == null) deck = FindObjectOfType<Deck>();
        if (deck == null || slots == null || slots.Length == 0)
        {
            Debug.LogWarning("[RandomSlot] Deck 또는 슬롯이 없습니다.");
            return;
        }

        foreach (var s in slots)
            if (s != null) s.ClearCard();

        var drawn = DrawForSlots();
        if (drawn.Count == 0)
        {
            Debug.Log("[RandomSlot] 덱 풀이 비어 커뮤니티를 배치할 수 없습니다.");
            return;
        }

        var plan = BuildPlacementPlan(drawn);
        for (int i = 0; i < plan.Length; i++)
            if (slots[i] != null) PlaceFaceDown(slots[i], plan[i]);
    }

    /// <summary>덱 풀에서 뽑아 한 장씩 커지며 배치하는 애니메이션 버전.</summary>
    public IEnumerator DealCommunityAnimated()
    {
        if (deck == null) deck = FindObjectOfType<Deck>();
        if (deck == null || slots == null) yield break;

        foreach (var s in slots)
            if (s != null) s.ClearCard();

        var drawn = DrawForSlots();
        if (drawn.Count == 0)
        {
            Debug.Log("[RandomSlot] 덱 풀이 비어 커뮤니티를 배치할 수 없습니다.");
            yield break;
        }

        var plan = BuildPlacementPlan(drawn);
        for (int i = 0; i < plan.Length; i++)
        {
            if (slots[i] == null) continue;
            yield return StartCoroutine(DealCardToSlot(slots[i], plan[i]));
            if (dealStagger > 0f) yield return new WaitForSeconds(dealStagger);
        }
    }

    [Header("─ 긴장 연출 ─")]
    [Tooltip("카드 공개 직전 멈칫하는 시간(초) — 긴장감")]
    public float anticipation = 0.25f;

    /// <summary>지정 슬롯을 천천히 공개 (초기 오픈 클릭 등). 뒷면이 아니면 무시.</summary>
    public IEnumerator RevealSlot(Slot slot)
    {
        if (slot == null || !slot.HasCard || !slot.IsGuard) yield break;
        yield return StartCoroutine(AnticipateAndFlip(slot));
        FireJokerIfAny(slot);
    }

    /// <summary>가장 앞쪽 뒷면 카드 한 장을 천천히 공개 (베팅 라운드 종료 시).</summary>
    public IEnumerator RevealNextHidden()
    {
        foreach (var s in slots)
        {
            if (s != null && s.HasCard && s.IsGuard)
            {
                yield return StartCoroutine(AnticipateAndFlip(s));
                FireJokerIfAny(s);
                yield break;
            }
        }
    }

    /// <summary>남은 뒷면 카드 전부를 동시에 공개 (올인 등).</summary>
    public IEnumerator RevealAllHidden()
    {
        var revealing = new List<Slot>();
        var running   = new List<Coroutine>();
        foreach (var s in slots)
            if (s != null && s.HasCard && s.IsGuard)
            {
                revealing.Add(s);
                running.Add(StartCoroutine(s.FlipTo(false, revealDuration)));
            }
        foreach (var c in running) yield return c;

        // 공개된 것 중 조커가 있으면 글리치 1회
        foreach (var s in revealing)
        {
            var cv = s.GetCardValue();
            if (cv != null && cv.isJoker) { Juicer.Instance?.JokerGlitch(); break; }
        }
    }

    // 공개 직전 멈칫(긴장 펀치) 후 뒤집기
    private IEnumerator AnticipateAndFlip(Slot slot)
    {
        if (anticipation > 0f)
        {
            Juicer.Instance?.RevealTick();
            yield return new WaitForSeconds(anticipation);
        }
        yield return StartCoroutine(slot.FlipTo(false, revealDuration));
    }

    // 슬롯이 조커면 시스템 에러 연출
    private void FireJokerIfAny(Slot slot)
    {
        if (slot == null) return;
        var cv = slot.GetCardValue();
        if (cv != null && cv.isJoker) Juicer.Instance?.JokerGlitch();
    }

    // ─────────────────────────────────────────
    //  쇼다운: 커뮤니티 소진 → 양쪽 손패를 쇼다운 슬롯에 뒷면 장착 →
    //          카메라 줌+슬로우+흔들림으로 한 장씩 공개 (재분배는 Director가 담당)
    // ─────────────────────────────────────────
    public IEnumerator RunShowdown(List<Deck.CardPool> playerSpecs, List<Deck.CardPool> oppSpecs,
                                   ResultUI resultUI = null,
                                   string playerRule = "", float playerScore = 0f,
                                   string oppRule = "", float oppScore = 0f,
                                   float resultHold = 1.5f,
                                   Deck playerDeck = null, OppDeck opponentDeck = null,
                                   List<Vector3> playerStarts = null, List<Vector3> oppStarts = null)
    {
        Camera cam = showdownCamera != null ? showdownCamera : Camera.main;
        Vector3 camHome  = cam != null ? cam.transform.position : Vector3.zero;
        float   camHomeS = cam != null ? (cam.orthographic ? cam.orthographicSize : cam.fieldOfView) : 0f;

        // ── 1) 손에 든 카드는 전부 제거하고, 손패를 쇼다운 슬롯에 뒷면으로 '놓는' 모션 ──
        // 손패 카드의 현재 위치(출발점)를 먼저 캡처한 뒤 손패 제거
        // (RoundDirector가 playerStarts/oppStarts로 넘겨줌 — 손패/홀 슬롯에서 자연 이동)
        if (playerDeck != null)   playerDeck.ClearCards();
        if (opponentDeck != null) opponentDeck.ClearCards();
        yield return StartCoroutine(PlaceHandAnimated(playerShowdownSlots, playerSpecs, playerStarts));
        yield return StartCoroutine(PlaceHandAnimated(oppShowdownSlots, oppSpecs, oppStarts));

        // ── 2) 커뮤니티: 한 장씩 줌, 뒷면이면 천천히 공개 (긴장 증폭) ──
        var community = CardSlots(slots);
        if (community.Count > 0)
        {
            if (cam != null && SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.cameraPan);
            yield return StartCoroutine(PanZoomTo(cam, community[0], zoomDuration));

            foreach (var s in community)
            {
                yield return StartCoroutine(PanZoomTo(cam, s, zoomDuration * 0.6f));
                if (s.IsGuard) yield return StartCoroutine(SlowFlip(s, cam, true));
                else if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);
            }
            if (cam != null) yield return StartCoroutine(MoveCamera(cam, camHome, camHomeS, zoomDuration));
        }

        // ── 3) 내 패부터 한 장씩 → 마지막 카드 뒤집으며 결과 fade-in, n초 유지 ──
        yield return StartCoroutine(RevealHandWithResult(
            playerShowdownSlots, cam, camHome, camHomeS, resultUI, playerRule, playerScore, resultHold));

        // ── 4) 상대 패 시작 시 결과 off ──
        if (resultUI != null) yield return StartCoroutine(resultUI.FadeOut());

        // ── 5) 상대 패: 동일하게 마지막 카드에서 결과 ──
        yield return StartCoroutine(RevealHandWithResult(
            oppShowdownSlots, cam, camHome, camHomeS, resultUI, oppRule, oppScore, resultHold));

        if (cam != null) yield return StartCoroutine(MoveCamera(cam, camHome, camHomeS, zoomDuration));
    }

    // 손패를 쇼다운 슬롯에 뒷면으로 한 장씩 '놓는' 모션
    //  starts 지정 시 그 위치(손패/홀 슬롯)에서 자연스럽게 이동, 없으면 덱 앵커에서
    private IEnumerator PlaceHandAnimated(Slot[] handSlots, List<Deck.CardPool> specs, List<Vector3> starts = null)
    {
        if (handSlots == null || specs == null) yield break;
        int n = Mathf.Min(handSlots.Length, specs.Count);
        for (int i = 0; i < n; i++)
        {
            if (handSlots[i] == null) continue;
            Vector3? from = (starts != null && i < starts.Count) ? starts[i] : (Vector3?)null;
            yield return StartCoroutine(DealCardToSlot(handSlots[i], specs[i], from));
            if (dealStagger > 0f) yield return new WaitForSeconds(dealStagger);
        }
    }

    // 한 패를 한 장씩 공개. 마지막 카드 뒤집으며 결과 표시 + 유지.
    private IEnumerator RevealHandWithResult(Slot[] handSlots, Camera cam, Vector3 camHome, float camHomeS,
                                             ResultUI resultUI, string rule, float score, float hold)
    {
        var cards = CardSlots(handSlots);
        if (cards.Count == 0) yield break;
        Slot last = cards[cards.Count - 1];

        if (cam != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cameraPan);
        yield return StartCoroutine(PanZoomTo(cam, cards[0], zoomDuration));

        foreach (var s in cards)
        {
            yield return StartCoroutine(PanZoomTo(cam, s, zoomDuration * 0.6f));

            if (s == last)
            {
                // 마지막 카드: 뒤집으며 결과 fade-in (동시), 흔들림
                if (SoundManager.Instance != null)
                    SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionShake, 0.7f);

                Coroutine flip  = StartCoroutine(s.FlipTo(false, dramaticRevealDuration));
                Coroutine shake = cam != null
                    ? StartCoroutine(RampShake(cam, dramaticRevealDuration, dramaticShakeIntensity)) : null;
                if (resultUI != null)
                    StartCoroutine(resultUI.ShowResult(rule, score, dramaticRevealDuration));

                yield return flip;
                if (shake != null) yield return shake;
                FireJokerIfAny(s);

                if (hold > 0f) yield return new WaitForSeconds(hold);
            }
            else
            {
                // 나머지: 빠르게 한 장씩
                if (s.IsGuard) yield return StartCoroutine(s.FlipTo(false, quickRevealDuration));
                FireJokerIfAny(s);
            }
        }
    }

    // 카메라를 슬롯으로 줌/팬
    private IEnumerator PanZoomTo(Camera cam, Slot s, float dur)
    {
        if (cam == null || s == null) yield break;
        Vector3 p = s.transform.position;
        Vector3 to = new Vector3(p.x, p.y, cam.transform.position.z);
        float toSize = cam.orthographic ? zoomOrthoSize : zoomFov;
        yield return StartCoroutine(MoveCamera(cam, to, toSize, dur));
    }

    // 뒷면 슬롯을 천천히 공개 (+흔들림, 조커면 글리치)
    private IEnumerator SlowFlip(Slot s, Camera cam, bool withShake)
    {
        if (s == null || !s.IsGuard) yield break;
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionShake, 0.6f);

        Coroutine flip = StartCoroutine(s.FlipTo(false, dramaticRevealDuration));
        if (withShake && cam != null)
            yield return StartCoroutine(RampShake(cam, dramaticRevealDuration, dramaticShakeIntensity));
        yield return flip;
        FireJokerIfAny(s);
    }

    // 카드가 놓인 슬롯만 추려 반환
    private List<Slot> CardSlots(Slot[] arr)
    {
        var list = new List<Slot>();
        if (arr != null)
            foreach (var s in arr)
                if (s != null && s.HasCard) list.Add(s);
        return list;
    }

    /// <summary>쇼다운 슬롯 비우기 (라운드 정리 시 Director가 호출).</summary>
    public void ClearShowdownSlots()
    {
        if (playerShowdownSlots != null)
            foreach (var s in playerShowdownSlots) if (s != null) s.ClearCard();
        if (oppShowdownSlots != null)
            foreach (var s in oppShowdownSlots) if (s != null) s.ClearCard();
    }

    // ─────────────────────────────────────────
    //  내부
    // ─────────────────────────────────────────
    private List<Deck.CardPool> DrawForSlots()
    {
        var drawn = new List<Deck.CardPool>();
        foreach (var s in slots)
        {
            if (s == null) continue;
            if (!deck.DrawFromPile(out Deck.CardPool c)) break; // 풀 소진
            drawn.Add(c);
        }
        return drawn;
    }

    // 슬롯별 배치 계획: 숫자 오름차순, 조커 랜덤 위치
    private Deck.CardPool[] BuildPlacementPlan(List<Deck.CardPool> drawn)
    {
        int count = drawn.Count;

        var numbered = new List<Deck.CardPool>();
        var jokers   = new List<Deck.CardPool>();
        foreach (var c in drawn)
        {
            if (c.isJoker) jokers.Add(c);
            else           numbered.Add(c);
        }
        numbered.Sort((a, b) => a.value.CompareTo(b.value));

        bool[] jokerPos = new bool[count];
        var indices = new List<int>(count);
        for (int i = 0; i < count; i++) indices.Add(i);
        for (int i = indices.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (indices[i], indices[j]) = (indices[j], indices[i]);
        }
        for (int k = 0; k < jokers.Count; k++) jokerPos[indices[k]] = true;

        var plan = new Deck.CardPool[count];
        int ni = 0, ji = 0;
        for (int i = 0; i < count; i++)
            plan[i] = jokerPos[i] ? jokers[ji++] : numbered[ni++];
        return plan;
    }

    private void PlaceFaceDown(Slot slot, Deck.CardPool spec)
    {
        if (spec.prefab == null) return;

        GameObject card = Instantiate(spec.prefab);
        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value     = spec.value;
        cv.isJoker   = spec.isJoker;
        cv.poolIndex = spec.poolIndex;

        slot.PlaceCardFaceDown(card);
    }

    private void PlaceIntoSlots(Slot[] showdownSlots, List<Deck.CardPool> specs)
    {
        if (showdownSlots == null || specs == null) return;
        int n = Mathf.Min(showdownSlots.Length, specs.Count);
        for (int i = 0; i < n; i++)
            if (showdownSlots[i] != null) PlaceFaceDown(showdownSlots[i], specs[i]);
    }

    private Slot LastCardSlot(Slot[] showdownSlots)
    {
        Slot last = null;
        if (showdownSlots != null)
            foreach (var s in showdownSlots)
                if (s != null && s.HasCard && s.IsGuard) last = s;
        return last;
    }

    private IEnumerator RevealShowdownCard(Slot slot, Camera cam, bool dramatic, Vector3 camHomePos, float camHomeSize)
    {
        if (!dramatic)
        {
            yield return StartCoroutine(slot.FlipTo(false, quickRevealDuration));
            yield break;
        }

        if (cam != null)
        {
            if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SoundManager.Instance.cameraPan);
            Vector3 p = slot.transform.position;
            Vector3 to = new Vector3(p.x, p.y, cam.transform.position.z);
            float toSize = cam.orthographic ? zoomOrthoSize : zoomFov;
            yield return StartCoroutine(MoveCamera(cam, to, toSize, zoomDuration));
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.tensionShake, 0.7f);

        Coroutine flip = StartCoroutine(slot.FlipTo(false, dramaticRevealDuration));
        if (cam != null)
            yield return StartCoroutine(RampShake(cam, dramaticRevealDuration, dramaticShakeIntensity));
        yield return flip;

        if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

        if (cam != null)
            yield return StartCoroutine(MoveCamera(cam, camHomePos, camHomeSize, zoomDuration));
    }

    private IEnumerator RampShake(Camera cam, float dur, float maxIntensity)
    {
        Vector3 basePos = cam.transform.position;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / dur);
            Vector2 off = Random.insideUnitCircle * (maxIntensity * k);
            cam.transform.position = basePos + new Vector3(off.x, off.y, 0f);
            yield return null;
        }
        cam.transform.position = basePos;
    }

    private IEnumerator MoveCamera(Camera cam, Vector3 toPos, float toSize, float dur)
    {
        if (cam == null) yield break;
        if (dur <= 0f)
        {
            cam.transform.position = toPos;
            if (cam.orthographic) cam.orthographicSize = toSize; else cam.fieldOfView = toSize;
            yield break;
        }

        Vector3 fromPos = cam.transform.position;
        float fromSize = cam.orthographic ? cam.orthographicSize : cam.fieldOfView;

        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            cam.transform.position = Vector3.Lerp(fromPos, toPos, k);
            float s = Mathf.Lerp(fromSize, toSize, k);
            if (cam.orthographic) cam.orthographicSize = s; else cam.fieldOfView = s;
            yield return null;
        }
        cam.transform.position = toPos;
        if (cam.orthographic) cam.orthographicSize = toSize; else cam.fieldOfView = toSize;
    }

    private IEnumerator ConsumeCards(List<GameObject> cards)
    {
        if (cards.Count == 0) yield break;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardPlace);

        var from = new List<Vector3>(cards.Count);
        foreach (var c in cards)
            from.Add(c != null ? c.transform.localScale : Vector3.one);

        float t = 0f;
        while (t < consumeDuration)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / consumeDuration);
            for (int i = 0; i < cards.Count; i++)
                if (cards[i] != null) cards[i].transform.localScale = from[i] * k;
            yield return null;
        }

        foreach (var c in cards)
            if (c != null) Destroy(c);
    }

    // 출발 위치에서 슬롯으로 날아와 뒷면으로 놓이는 딜 모션 (홀/쇼다운 슬롯 재사용)
    //  fromWorld 지정 시 그 월드 좌표에서 출발(손패→슬롯 등), 없으면 덱 앵커에서.
    public IEnumerator DealCardToSlot(Slot slot, Deck.CardPool spec, Vector3? fromWorld = null)
    {
        if (slot == null || spec.prefab == null) yield break;

        // 슬롯에 먼저 뒷면 배치(스케일/핏 확정) 후, 시작점으로 옮겨 lerp
        PlaceFaceDown(slot, spec);
        GameObject card = slot.GetPlacedCard();
        if (card == null) yield break;

        Vector3 targetLocal  = card.transform.localPosition;   // 슬롯 중심
        Vector3 targetScale  = card.transform.localScale;
        Vector3 startLocal   = slot.transform.InverseTransformPoint(fromWorld ?? AnchorWorld());

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

        if (dealMoveDuration <= 0f)
        {
            card.transform.localPosition = targetLocal;
            card.transform.localScale    = targetScale;
            yield break;
        }

        float t = 0f;
        while (t < dealMoveDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dealMoveDuration));
            card.transform.localPosition = Vector3.Lerp(startLocal, targetLocal, k);
            card.transform.localScale    = Vector3.Lerp(targetScale * 0.7f, targetScale, k);
            yield return null;
        }
        card.transform.localPosition = targetLocal;
        card.transform.localScale    = targetScale;
    }

    // 카드가 출발하는 덱 앵커의 월드 좌표
    private Vector3 AnchorWorld()
    {
        if (dealAnchor != null) return dealAnchor.position;
        if (deck != null && deck.deckSpawnPoint != null) return deck.deckSpawnPoint.position;
        return transform.position;
    }

    private IEnumerator PlaceWithPop(Slot slot, Deck.CardPool spec)
    {
        PlaceFaceDown(slot, spec);

        GameObject card = slot.GetPlacedCard();
        if (card == null) yield break;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

        Vector3 target = card.transform.localScale;
        if (dealPopDuration <= 0f) { card.transform.localScale = target; yield break; }

        float t = 0f;
        while (t < dealPopDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dealPopDuration);
            card.transform.localScale = target * k;
            yield return null;
        }
        card.transform.localScale = target;
    }
}
