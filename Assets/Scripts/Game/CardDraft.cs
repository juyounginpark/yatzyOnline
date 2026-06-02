using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  카드 드래프트
//  - 매 턴 시작 시 draftAnchor 기준 좌/우에 카드 2장 스폰 (OPP 턴엔 oppDraftAnchor)
//  - 턴 플레이어가 1장 선택 → 자기 패, 나머지 1장 → 상대 패
//  - OPP 턴: 1~3초 랜덤 대기 후 AI 휴리스틱으로 자동 선택, 직전 시각 피드백
//  - 호버 시 시각 피드백 (커지기/작아지기)
//  - 선택 후: 목적지 카드(투명)를 미리 스폰 → 그 위치로 비행 + 크로스페이드 swap
//  - OPP로 가는 카드는 OppDeck가 카드 뒷면을 자동 스폰하므로 뒷면 상태로 도착
// ─────────────────────────────────────────────
public class CardDraft : MonoBehaviour
{
    [Header("─ 참조 ─")]
    [Tooltip("카드가 출발하는 위치 (덱). 미설정 시 각 카드가 자기 앵커에서 그대로 출발")]
    public Transform deckAnchor;

    [Tooltip("상대 덱 앵커 (상대 파괴 카드가 날아갈 위치). 미설정 시 OppDeck 스폰 지점 사용")]
    public Transform oppDeckAnchor;

    [Tooltip("1번 카드 도착 위치/크기/회전 앵커")]
    public Transform card1Anchor;

    [Tooltip("2번 카드 도착 위치/크기/회전 앵커")]
    public Transform card2Anchor;

    public Deck      deck;
    public OppDeck   oppDeck;
    public MainFlow  mainFlow;
    public GameFlow  gameFlow;

    [Header("─ 배치 ─")]
    [Tooltip("패 카드 크기 대비 드래프트 카드 추가 배율 (1 = 패 카드와 동일)")]
    public float cardScale = 1f;

    [Tooltip("덱에서 나올 때 초기 크기 (도착 크기 대비 비율). 0.4 = 도착 크기의 40%로 시작해서 점점 커짐")]
    [Range(0.01f, 2f)]
    public float deckInitialScaleMul = 0.4f;

    [Tooltip("드래프트 카드 SpriteRenderer 정렬 순서 (sortingOrder)")]
    public int cardSortingOrder = 100;

    [Header("─ OPP 동작 ─")]
    [Tooltip("OPP가 선택을 결정하기까지 최소 대기 시간(초)")]
    public float oppPickDelayMin = 1f;

    [Tooltip("OPP가 선택을 결정하기까지 최대 대기 시간(초)")]
    public float oppPickDelayMax = 3f;

    [Tooltip("OPP 클릭 직전, 어떤 카드를 고를지 호버로 미리 보여주는 시간(초)")]
    public float oppDecisionRevealTime = 0.4f;

    [Header("─ 단일 카드(한쪽 손패 max) ─")]
    [Tooltip("한쪽만 max일 때 카드 한 장이 자동으로 비행하기까지 대기 시간(초)")]
    public float singleCardAutoDelay = 2f;

    [Header("─ 호버 피드백 ─")]
    [Tooltip("호버된 카드의 추가 배율")]
    public float hoverScaleMul   = 1.15f;

    [Tooltip("호버되지 않은 카드의 배율")]
    public float unhoverScaleMul = 0.9f;

    [Tooltip("호버/언호버 스케일 보간 속도")]
    public float hoverLerpSpeed  = 12f;

    [Header("─ 드로우(딜) 애니메이션 ─")]
    [Tooltip("카드 한 장이 anchor → 최종 자리로 펴지는 시간(초). 두 장 순차로 진행")]
    public float dealDuration = 0.35f;

    [Header("─ 비행 애니메이션 ─")]
    [Tooltip("선택된 카드가 목적지 카드까지 가는 시간")]
    public float flyDuration = 0.55f;

    [Tooltip("비행 후반부에 드래프트↔목적지 카드 크로스페이드가 진행되는 구간 비율 (0~1)")]
    [Range(0.05f, 1f)]
    public float crossfadeTail = 0.35f;

    [Tooltip("비행 중 아치 높이(월드 단위)")]
    public float arcHeight = 0.35f;

    // ─── 상태 ───
    private GameObject _leftCard;
    private GameObject _rightCard;
    private Vector3    _leftBaseScale;
    private Vector3    _rightBaseScale;
    private bool _isDrafting;
    private bool _waitingForPlayerClick;
    private bool _isResolving;
    private float _playerPickTimer;

    // 호버 테두리 (GameFlow.effectHover 참조)
    private GameObject _hoverBorderFx;
    private GameObject _hoverBorderHost;

    public bool IsDrafting => _isDrafting;

    // 온라인: 상대(턴 플레이어)의 드래프트 패킷을 기다리는 중
    private bool _awaitingRemoteDraft;
    public bool IsAwaitingRemoteDraft => _awaitingRemoteDraft;

    [Header("─ 온라인 ─")]
    [Tooltip("원격 드래프트 패킷 대기 최대 시간(초). 초과 시 스킵하여 데드락 방지")]
    public float remoteDraftTimeout = 8f;
    private float _remoteDraftWait;

    [Header("─ 뒷면 드래프트 ─")]
    [Tooltip("각 드래프트 카드가 뒷면으로 제시될 확률(0~1). 뽑아서 패에 넣으면 앞면으로 공개")]
    [Range(0f, 1f)] public float faceDownChance = 0.1f;

    void Update()
    {
        if (!_isDrafting || _isResolving) return;

        // 온라인: 원격 드래프트 패킷 대기 중 — 너무 오래 안 오면 스킵 (데드락 방지)
        if (_awaitingRemoteDraft)
        {
            _remoteDraftWait += Time.deltaTime;
            if (_remoteDraftWait >= remoteDraftTimeout)
            {
                Debug.LogWarning("[CardDraft] 원격 드래프트 패킷 타임아웃 — 드래프트 스킵");
                _awaitingRemoteDraft = false;
                _isDrafting = false;
            }
            return;
        }

        if (_waitingForPlayerClick)
        {
            _playerPickTimer -= Time.deltaTime;
            if (_playerPickTimer <= 0f)
            {
                // 시간 초과 → 랜덤 선택
                GameObject pick = Random.value < 0.8f ? _leftCard : _rightCard;
                GameObject other = (pick == _leftCard) ? _rightCard : _leftCard;
                StartCoroutine(ResolvePick(chosen: pick, leftover: other, playerChose: true));
                return;
            }

            UpdateHoverFeedback();
            HandleClick();
        }
    }

    // ─────────────────────────────────────────
    //  호버 피드백 (스케일 + GameFlow.effectHover 테두리)
    // ─────────────────────────────────────────
    private void UpdateHoverFeedback()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        GameObject hovered = null;

        if (IsOverCard(mouseWorld, _leftCard))       hovered = _leftCard;
        else if (IsOverCard(mouseWorld, _rightCard)) hovered = _rightCard;

        ApplyHoverScale(_leftCard,  _leftBaseScale,  isHovered: hovered == _leftCard,  otherIsHovered: hovered == _rightCard);
        ApplyHoverScale(_rightCard, _rightBaseScale, isHovered: hovered == _rightCard, otherIsHovered: hovered == _leftCard);

        UpdateHoverBorder(hovered);
    }

    // ─────────────────────────────────────────
    //  GameFlow.effectHover 스프라이트를 가져와 호버 카드 위에 테두리로 부착
    // ─────────────────────────────────────────
    private void UpdateHoverBorder(GameObject hovered)
    {
        if (gameFlow == null || gameFlow.effectHover == null) return;

        if (hovered == _hoverBorderHost) return;  // 변동 없음

        if (hovered != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardHover);

        // 기존 부착 해제
        if (_hoverBorderFx != null)
        {
            _hoverBorderFx.transform.SetParent(null);
            _hoverBorderFx.SetActive(false);
        }

        _hoverBorderHost = hovered;
        if (hovered == null) return;

        if (_hoverBorderFx == null)
        {
            _hoverBorderFx = new GameObject("DraftHoverBorder");
            var sr = _hoverBorderFx.AddComponent<SpriteRenderer>();
            sr.sprite = gameFlow.effectHover;
            sr.color  = Color.white;
        }

        AttachBorderToCard(_hoverBorderFx, hovered, gameFlow.effectHover, sortOrder: cardSortingOrder + 1);
    }

    private void AttachBorderToCard(GameObject fx, GameObject card, Sprite sprite, int sortOrder)
    {
        fx.SetActive(true);
        fx.transform.SetParent(card.transform);
        fx.transform.localPosition = Vector3.zero;
        fx.transform.localRotation = Quaternion.identity;

        var cardSr = card.GetComponentInChildren<SpriteRenderer>();
        if (cardSr == null || cardSr.sprite == null) return;

        var fxSr = fx.GetComponent<SpriteRenderer>();
        fxSr.sortingOrder = sortOrder;

        Vector2 cardSpriteSize = cardSr.sprite.bounds.size;
        Vector3 cardLossy      = cardSr.transform.lossyScale;
        float cardW = cardSpriteSize.x * Mathf.Abs(cardLossy.x);
        float cardH = cardSpriteSize.y * Mathf.Abs(cardLossy.y);

        Vector2 effectSize = sprite.bounds.size;
        Vector3 parentLossy = card.transform.lossyScale;
        float lsX = parentLossy.x != 0f ? (cardW / effectSize.x) / Mathf.Abs(parentLossy.x) : 1f;
        float lsY = parentLossy.y != 0f ? (cardH / effectSize.y) / Mathf.Abs(parentLossy.y) : 1f;
        fx.transform.localScale = new Vector3(lsX, lsY, 1f);
    }

    private void DetachHoverBorder()
    {
        if (_hoverBorderFx != null)
        {
            _hoverBorderFx.transform.SetParent(null);
            _hoverBorderFx.SetActive(false);
        }
        _hoverBorderHost = null;
    }

    private void ApplyHoverScale(GameObject card, Vector3 baseScale, bool isHovered, bool otherIsHovered)
    {
        if (card == null) return;

        Vector3 target;
        if (isHovered)           target = baseScale * hoverScaleMul;
        else if (otherIsHovered) target = baseScale * unhoverScaleMul;
        else                     target = baseScale;

        float t = 1f - Mathf.Exp(-hoverLerpSpeed * Time.deltaTime);
        card.transform.localScale = Vector3.Lerp(card.transform.localScale, target, t);
    }

    private bool IsOverCard(Vector2 worldPos, GameObject card)
    {
        if (card == null) return false;
        var cols = card.GetComponentsInChildren<Collider2D>();
        foreach (var c in cols)
            if (c.OverlapPoint(worldPos)) return true;
        return false;
    }

    private void HandleClick()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);

        if (IsOverCard(mouseWorld, _leftCard))
        {
            if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SoundManager.Instance.uiClick);
            StartCoroutine(ResolvePick(chosen: _leftCard,  leftover: _rightCard, playerChose: true));
        }
        else if (IsOverCard(mouseWorld, _rightCard))
        {
            if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SoundManager.Instance.uiClick);
            StartCoroutine(ResolvePick(chosen: _rightCard, leftover: _leftCard,  playerChose: true));
        }
    }

    // ─────────────────────────────────────────
    //  드래프트 시작 (MainFlow에서 턴 시작 시 호출)
    //  - 양쪽 손패 max 체크:
    //    • 둘 다 max → 드래프트 스킵
    //    • 한 쪽 max → 카드 1장만 띄우고 singleCardAutoDelay 후 max가 아닌 쪽으로
    //    • 둘 다 여유 → 평소대로 2장 드래프트
    // ─────────────────────────────────────────
    public void StartDraft()
    {
        if (_isDrafting) return;
        if (deck == null)
        {
            Debug.LogWarning("[CardDraft] deck 미설정");
            return;
        }
        if (card1Anchor == null || card2Anchor == null)
        {
            Debug.LogWarning("[CardDraft] card1Anchor / card2Anchor 미설정");
            return;
        }

        bool playerTurn = mainFlow == null || mainFlow.IsPlayerTurn;

        // 온라인 + 상대 턴: 로컬에서 카드를 생성/선택하지 않고 상대의 드래프트 패킷을 기다린다.
        // (로컬 RNG/AI로 드래프트하면 양 클라이언트의 패가 어긋나므로 반드시 차단)
        if (mainFlow != null && mainFlow.isOnlineMode && !playerTurn)
        {
            _isDrafting = true;
            _isResolving = false;
            _waitingForPlayerClick = false;
            _awaitingRemoteDraft = true;
            _remoteDraftWait = 0f;
            return;
        }

        Transform deckRefForScale;
        if (playerTurn)
            deckRefForScale = (deck.deckSpawnPoint != null) ? deck.deckSpawnPoint : deck.transform;
        else
            deckRefForScale = (oppDeck != null && oppDeck.deckSpawnPoint != null)
                ? oppDeck.deckSpawnPoint
                : (oppDeck != null ? oppDeck.transform : deck.transform);

        // ─── 손패 한도 체크 ───
        int maxCards   = deck.maxCards;
        int playerCnt  = deck.SpawnedCards.Count;
        int oppCnt     = oppDeck != null ? oppDeck.SpawnedCards.Count : 0;
        bool playerFull = playerCnt >= maxCards;
        bool oppFull    = oppDeck != null && oppCnt >= maxCards;

        if (playerFull && oppFull)
        {
            // 둘 다 max → 드래프트 스킵
            Debug.Log("[CardDraft] 양쪽 손패 max — 드래프트 스킵");
            // 온라인: 내 턴이면 상대에게 빈 드래프트를 보내 대기를 풀어준다 (데드락 방지)
            if (mainFlow != null && mainFlow.isOnlineMode && playerTurn)
                SendDraftResult(null, null, null, null, false, false);
            return;
        }

        _isDrafting = true;
        _isResolving = false;
        _waitingForPlayerClick = false;

        if (!playerFull && !oppFull)
            StartCoroutine(DealDraftSequence(deckRefForScale, playerTurn));
        else
            StartCoroutine(DealSingleCardSequence(deckRefForScale, playerFull));
    }

    // ─────────────────────────────────────────
    //  단일 카드 시퀀스: 한 쪽이 max일 때 카드 1장만 띄우고 자동 전달
    //  playerFull=true → 카드는 OPP 패로, false → 카드는 플레이어 패로
    // ─────────────────────────────────────────
    private IEnumerator DealSingleCardSequence(Transform deckRefForScale, bool playerFull)
    {
        _leftCard = SpawnDraftCardAtAnchor(card1Anchor, deckRefForScale, out Vector3 finalScale);
        if (_leftCard == null)
        {
            _isDrafting = false;
            yield break;
        }

        // 도착 X 좌표를 card1 ↔ card2 중점으로
        Vector3 midpointWorld = (card1Anchor.position + card2Anchor.position) * 0.5f;
        Vector3 midpointLocal = card1Anchor.InverseTransformPoint(midpointWorld);
        Vector3 finalLocalPos = new Vector3(midpointLocal.x, 0f, 0f);

        MaybeFaceDownDraftCard(_leftCard);  // 확률적으로 뒷면 제시
        yield return StartCoroutine(AnimateCardDeal(_leftCard, card1Anchor, finalScale, finalLocalPos));

        _leftBaseScale = _leftCard.transform.localScale;

        // 카드 표시 대기
        yield return new WaitForSeconds(singleCardAutoDelay);

        // max가 아닌 쪽으로 전달
        bool toPlayer = !playerFull;
        yield return StartCoroutine(ResolveSingleCard(_leftCard, toPlayer));
    }

    // ─────────────────────────────────────────
    //  단일 카드 비행/해제 (선택 없음, 무조건 toPlayer로)
    // ─────────────────────────────────────────
    private IEnumerator ResolveSingleCard(GameObject card, bool toPlayer)
    {
        _isResolving = true;
        DetachHoverBorder();

        if (card != null) card.transform.localScale = _leftBaseScale;

        var cv = card != null ? card.GetComponent<CardValue>() : null;

        // 온라인: 단일 카드 드래프트 결과 전송 (toPlayer면 내 패, 아니면 상대 패)
        SendDraftResult(toPlayer ? cv : null, toPlayer ? null : cv, cv, null, false, true);

        // OPP로 가는 경우 카드 뒷면으로 비주얼 교체
        if (!toPlayer) OverlayBackOnCard(card);

        GameObject dest = SpawnDestinationCard(cv, toPlayer);

        yield return StartCoroutine(FlyToTarget(card, dest));

        if (card != null) Destroy(card);
        if (dest != null) SetCardAlpha(dest, 1f);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

        _leftCard  = null;
        _rightCard = null;
        _isResolving = false;
        _isDrafting  = false;
    }

    // ─────────────────────────────────────────
    //  딜 시퀀스: 카드1 → 카드2 순차로 자기 앵커에서 펴짐
    // ─────────────────────────────────────────
    private IEnumerator DealDraftSequence(Transform deckRefForScale, bool playerTurn)
    {
        _leftCard = SpawnDraftCardAtAnchor(card1Anchor, deckRefForScale, out Vector3 c1FinalScale);
        if (_leftCard != null)
        {
            MaybeFaceDownDraftCard(_leftCard);  // 확률적으로 뒷면 제시
            yield return StartCoroutine(AnimateCardDeal(_leftCard, card1Anchor, c1FinalScale));
        }

        _rightCard = SpawnDraftCardAtAnchor(card2Anchor, deckRefForScale, out Vector3 c2FinalScale);
        if (_rightCard != null)
        {
            MaybeFaceDownDraftCard(_rightCard);  // 확률적으로 뒷면 제시
            yield return StartCoroutine(AnimateCardDeal(_rightCard, card2Anchor, c2FinalScale));
        }

        if (_leftCard == null || _rightCard == null)
        {
            CleanupDraftCards();
            _isDrafting = false;
            yield break;
        }

        _leftBaseScale  = _leftCard.transform.localScale;
        _rightBaseScale = _rightCard.transform.localScale;

        if (playerTurn)
        {
            _waitingForPlayerClick = true;
            _playerPickTimer = 5f;
        }
        else
            StartCoroutine(OppPickRoutine());
    }

    // ─────────────────────────────────────────
    //  카드 1장을 deckAnchor 위치에서 스폰 (deckAnchor의 크기·회전·위치 inherit)
    //  최종 위치는 targetAnchor. 애니메이션이 둘을 연결.
    //  애니메이션 종료 시 적용할 최종 localScale은 out으로 반환.
    // ─────────────────────────────────────────
    private GameObject SpawnDraftCardAtAnchor(Transform targetAnchor, Transform deckRefForScale,
        out Vector3 finalLocalScale)
    {
        finalLocalScale = Vector3.one;

        if (!deck.GetRandomPrefabFromActivePool(
            out GameObject prefab, out int value, out bool isJoker, out CardType cardType))
            return null;

        // 카드를 targetAnchor의 child로 instantiate (parent 고정 → 애니메이션은 local로)
        GameObject card = Instantiate(prefab, targetAnchor);

        // 최종 스케일 (도착 시점): 패 카드와 동일 월드 스케일이 되도록 targetAnchor.lossyScale로 보정
        Vector3 deckLossy   = deckRefForScale != null ? deckRefForScale.lossyScale : Vector3.one;
        Vector3 anchorLossy = targetAnchor.lossyScale;
        Vector3 prefabScale = prefab.transform.localScale;
        finalLocalScale = new Vector3(
            prefabScale.x * Mathf.Abs(deckLossy.x) / Mathf.Max(Mathf.Abs(anchorLossy.x), 0.0001f) * cardScale,
            prefabScale.y * Mathf.Abs(deckLossy.y) / Mathf.Max(Mathf.Abs(anchorLossy.y), 0.0001f) * cardScale,
            prefabScale.z);

        // 초기 상태: deckAnchor가 있으면 그 위치/회전, 없으면 targetAnchor에서 그대로 출발.
        // 초기 크기는 finalLocalScale × deckInitialScaleMul (도착 크기 대비 비율로 명시 제어)
        if (deckAnchor != null)
        {
            card.transform.position = deckAnchor.position;
            card.transform.rotation = deckAnchor.rotation;
        }
        else
        {
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
        }
        card.transform.localScale = finalLocalScale * Mathf.Max(0.01f, deckInitialScaleMul);

        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value    = value;
        cv.isJoker  = isJoker;
        cv.cardType = cardType;

        var hover = card.GetComponent<CardHover>();
        if (hover != null) hover.enabled = false;

        if (card.GetComponent<Collider2D>() == null)
            card.AddComponent<BoxCollider2D>();

        // 정렬 순서 일괄 설정 (다른 UI/카드 위에 표시되도록)
        foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
            sr.sortingOrder = cardSortingOrder;

        return card;
    }

    // ─────────────────────────────────────────
    //  딜 애니메이션: deckAnchor 위치/축소 크기/회전 → 도착 위치/패 크기/월드 회전 0°
    //  finalLocalPos 미지정 시 targetAnchor 원점(0,0,0)으로 도착
    // ─────────────────────────────────────────
    private IEnumerator AnimateCardDeal(GameObject card, Transform targetAnchor,
        Vector3 finalLocalScale, Vector3 finalLocalPos = default)
    {
        if (card == null) yield break;

        Vector3    startPos   = card.transform.localPosition;
        Quaternion startRot   = card.transform.localRotation;
        Vector3    startScale = card.transform.localScale;

        Quaternion finalLocalRot = targetAnchor != null
            ? Quaternion.Inverse(targetAnchor.rotation)
            : Quaternion.identity;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

        float duration = Mathf.Max(0.01f, dealDuration);
        float elapsed  = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);  // ease-out quad

            card.transform.localPosition = Vector3.Lerp   (startPos,   finalLocalPos,   eased);
            card.transform.localRotation = Quaternion.Slerp(startRot,  finalLocalRot,  eased);
            card.transform.localScale    = Vector3.Lerp   (startScale, finalLocalScale, eased);

            yield return null;
        }

        card.transform.localPosition = finalLocalPos;
        card.transform.localRotation = finalLocalRot;
        card.transform.localScale    = finalLocalScale;
    }

    // ─────────────────────────────────────────
    //  OPP 선택 코루틴: 1~3초 랜덤 대기 + 직전 시각 피드백
    // ─────────────────────────────────────────
    private IEnumerator OppPickRoutine()
    {
        GameObject pick  = ChooseBestForOpp(_leftCard, _rightCard);
        GameObject other = (pick == _leftCard) ? _rightCard : _leftCard;

        float totalDelay = Random.Range(oppPickDelayMin, oppPickDelayMax);
        float reveal     = Mathf.Clamp(oppDecisionRevealTime, 0f, totalDelay);
        float waitBefore = Mathf.Max(0f, totalDelay - reveal);

        if (waitBefore > 0f) yield return new WaitForSeconds(waitBefore);
        
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardHover);
            
        yield return StartCoroutine(OppRevealPick(pick, other, reveal));

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.uiClick);

        yield return StartCoroutine(
            ResolvePick(chosen: pick, leftover: other, playerChose: false));
    }

    private IEnumerator OppRevealPick(GameObject pick, GameObject other, float duration)
    {
        if (duration <= 0f) yield break;

        Vector3 pickBase  = (pick  == _leftCard) ? _leftBaseScale : _rightBaseScale;
        Vector3 otherBase = (other == _leftCard) ? _leftBaseScale : _rightBaseScale;

        Vector3 pickTarget  = pickBase  * hoverScaleMul;
        Vector3 otherTarget = otherBase * unhoverScaleMul;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Exp(-hoverLerpSpeed * Time.deltaTime);
            if (pick  != null) pick.transform.localScale  = Vector3.Lerp(pick.transform.localScale,  pickTarget,  t);
            if (other != null) other.transform.localScale = Vector3.Lerp(other.transform.localScale, otherTarget, t);
            yield return null;
        }
    }

    // ─────────────────────────────────────────
    //  AI 휴리스틱
    // ─────────────────────────────────────────
    private GameObject ChooseBestForOpp(GameObject a, GameObject b)
    {
        float scoreA = ScoreOppHandWith(a);
        float scoreB = ScoreOppHandWith(b);
        if (Mathf.Approximately(scoreA, scoreB))
            return Random.value < 0.5f ? a : b;
        return scoreB > scoreA ? b : a;
    }

    private float ScoreOppHandWith(GameObject candidate)
    {
        if (gameFlow == null || oppDeck == null || candidate == null) return 0f;
        var cv = candidate.GetComponent<CardValue>();
        if (cv == null) return 0f;

        var hand = oppDeck.SpawnedCards;
        int count = hand.Count + 1;
        int[]  values = new int[count];
        bool[] jokers = new bool[count];

        for (int i = 0; i < hand.Count; i++)
        {
            var hcv = hand[i] != null ? hand[i].GetComponent<CardValue>() : null;
            values[i] = hcv != null ? (hcv.isJoker ? 0 : hcv.value) : 0;
            jokers[i] = hcv != null && hcv.isJoker;
        }
        values[count - 1] = cv.isJoker ? 0 : cv.value;
        jokers[count - 1] = cv.isJoker;

        return gameFlow.EvaluateValues(values, jokers, out _);
    }

    // ─────────────────────────────────────────
    //  선택 처리: 투명 목적지 카드 미리 스폰 → 비행 → 크로스페이드 swap
    //  OPP로 가는 카드는 비행 전에 deck.cardBackPrefab으로 비주얼 교체
    // ─────────────────────────────────────────
    private IEnumerator ResolvePick(GameObject chosen, GameObject leftover, bool playerChose)
    {
        _waitingForPlayerClick = false;
        _isResolving = true;

        // 호버 테두리 분리 (비행 중 카드에 붙어서 같이 날아가지 않게)
        DetachHoverBorder();

        bool chosenToPlayer   =  playerChose;
        bool leftoverToPlayer = !playerChose;

        // 호버 잔재 스케일 정리
        if (chosen   != null) chosen.transform.localScale   = (chosen   == _leftCard) ? _leftBaseScale  : _rightBaseScale;
        if (leftover != null) leftover.transform.localScale = (leftover == _leftCard) ? _leftBaseScale  : _rightBaseScale;

        // 1) 데이터 캐시 (오브젝트 교체 전에)
        var chosenCv   = chosen   != null ? chosen.GetComponent<CardValue>()   : null;
        var leftoverCv = leftover != null ? leftover.GetComponent<CardValue>() : null;

        // 제시된 2장 정보 캐시 (left = _leftCard, right = _rightCard)
        var leftCv  = _leftCard  != null ? _leftCard.GetComponent<CardValue>()  : null;
        var rightCv = _rightCard != null ? _rightCard.GetComponent<CardValue>() : null;
        bool choiceIsLeft = (chosen == _leftCard);

        // 온라인: 내 턴 드래프트 결과 전송 (chosen=내 패, leftover=상대 패, 2장 정보 포함)
        if (playerChose) SendDraftResult(chosenCv, leftoverCv, leftCv, rightCv, choiceIsLeft, false);

        // 2) OPP로 가는 카드는 카드 뒷면 비주얼로 교체 (Deck.cardBackPrefab 참조)
        if (!chosenToPlayer)   OverlayBackOnCard(chosen);
        if (!leftoverToPlayer) OverlayBackOnCard(leftover);

        // 3) 목적지 카드 미리 스폰 (투명 상태)
        GameObject chosenDest   = SpawnDestinationCard(chosenCv,   chosenToPlayer);
        GameObject leftoverDest = SpawnDestinationCard(leftoverCv, leftoverToPlayer);

        // 4) 두 카드 동시 비행 (위치/스케일/회전 매 프레임 추적, 마지막 구간 크로스페이드)
        Coroutine cA = StartCoroutine(FlyToTarget(chosen,   chosenDest));
        Coroutine cB = StartCoroutine(FlyToTarget(leftover, leftoverDest));
        yield return cA;
        yield return cB;

        // 3) 도착: 드래프트 카드 파괴, 목적지 카드 완전 가시화
        if (chosen   != null) Destroy(chosen);
        if (leftover != null) Destroy(leftover);
        if (chosenDest   != null) SetCardAlpha(chosenDest,   1f);
        if (leftoverDest != null) SetCardAlpha(leftoverDest, 1f);

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);

        _leftCard  = null;
        _rightCard = null;
        _isResolving = false;
        _isDrafting  = false;
    }

    // ─────────────────────────────────────────
    //  목적지 카드 스폰: 패에 실제 카드 추가 후 투명화. 마지막에 추가된 카드를 반환.
    // ─────────────────────────────────────────
    private GameObject SpawnDestinationCard(CardValue cv, bool toPlayer)
    {
        if (cv == null) return null;

        IReadOnlyList<GameObject> hand = null;
        if (toPlayer)
        {
            if (deck == null) return null;
            if (cv.isJoker) deck.AddJokerCard(cv.cardType);
            else            deck.AddCardByValue(cv.value, cv.cardType);
            hand = deck.SpawnedCards;
        }
        else
        {
            if (oppDeck == null) return null;
            oppDeck.AddCardByValue(cv.value, cv.cardType);
            hand = oppDeck.SpawnedCards;
        }

        if (hand == null || hand.Count == 0) return null;

        GameObject dest = hand[hand.Count - 1];
        SetCardAlpha(dest, 0f);
        return dest;
    }

    // ─────────────────────────────────────────
    //  비행: 위치 + 월드 스케일 + 월드 회전을 매 프레임 목적지에 맞춰 보간,
    //        후반부 크로스페이드로 드래프트↔목적지 자연 swap
    // ─────────────────────────────────────────
    private IEnumerator FlyToTarget(GameObject card, GameObject target)
    {
        if (card == null) yield break;
        if (target == null) { Destroy(card); yield break; }

        Vector3    startPos   = card.transform.position;
        Quaternion startRot   = card.transform.rotation;
        Vector3    startScale = card.transform.localScale;

        Transform cardParent = card.transform.parent;

        float duration  = Mathf.Max(0.05f, flyDuration);
        float fadeStart = 1f - Mathf.Clamp(crossfadeTail, 0.05f, 1f);
        float elapsed   = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t);  // ease-out quad

            // 위치 (가벼운 아치)
            Vector3 targetPos = target.transform.position;
            Vector3 pos = Vector3.Lerp(startPos, targetPos, eased);
            pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;
            card.transform.position = pos;

            // 월드 회전 보간 (목적지 카드의 회전에 맞춰 부드럽게 기울어짐)
            Quaternion targetRot = target.transform.rotation;
            card.transform.rotation = Quaternion.Slerp(startRot, targetRot, eased);

            // 월드 스케일 보간 (서로 다른 부모 lossyScale 보정)
            Vector3 targetWorld   = target.transform.lossyScale;
            Vector3 parentLossy   = cardParent != null ? cardParent.lossyScale : Vector3.one;
            Vector3 desiredLocal  = new Vector3(
                Mathf.Abs(targetWorld.x) / Mathf.Max(Mathf.Abs(parentLossy.x), 0.0001f),
                Mathf.Abs(targetWorld.y) / Mathf.Max(Mathf.Abs(parentLossy.y), 0.0001f),
                1f);
            card.transform.localScale = Vector3.Lerp(startScale, desiredLocal, eased);



            yield return null;
        }
    }

    // ─────────────────────────────────────────
    //  카드 비주얼을 카드 뒷면으로 교체 (자식 모든 SpriteRenderer 비활성 + back 자식 부착)
    //  deck.cardBackPrefab을 참조. 위치/스케일/회전은 부모(드래프트 카드)가 그대로 유지.
    // ─────────────────────────────────────────
    // 확률(faceDownChance)에 따라 드래프트 카드를 뒷면으로 제시.
    // 패에 들어갈 땐 목적지 카드(새 앞면)로 자연스럽게 공개된다.
    private void MaybeFaceDownDraftCard(GameObject card)
    {
        if (card == null) return;
        if (Random.value < faceDownChance)
            OverlayBackOnCard(card);
    }

    private void OverlayBackOnCard(GameObject card)
    {
        if (card == null) return;
        // 이미 뒷면이면 중복 적용 방지 (제시 단계 뒷면 + 상대 전달 뒷면 충돌 차단)
        if (card.transform.Find("DraftBackOverlay") != null) return;

        GameObject backPrefab = deck != null ? deck.cardBackPrefab : null;
        if (backPrefab == null) return;

        // 1) 기존 앞면 SpriteRenderer 모두 비활성
        foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
            sr.enabled = false;

        // 2) 카드 뒷면 자식으로 부착
        GameObject back = Instantiate(backPrefab, card.transform);
        back.name = "DraftBackOverlay";
        back.transform.localPosition = Vector3.zero;
        back.transform.localRotation = Quaternion.identity;
        back.transform.localScale    = Vector3.one;

        // 3) 뒷면 콜라이더는 비활성 (호버/클릭 영향 차단)
        foreach (var c in back.GetComponentsInChildren<Collider2D>())
            c.enabled = false;

        // 4) 뒷면 정렬 순서 일치
        foreach (var sr in back.GetComponentsInChildren<SpriteRenderer>())
            sr.sortingOrder = cardSortingOrder;
    }

    // ─────────────────────────────────────────
    //  알파 헬퍼
    // ─────────────────────────────────────────
    private static void SetCardAlpha(GameObject card, float alpha)
    {
        if (card == null) return;
        foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
        {
            Color c = sr.color;
            c.a = alpha;
            sr.color = c;
        }
    }

    // ─────────────────────────────────────────
    //  온라인 드래프트 동기화
    // ─────────────────────────────────────────
    // 내 턴 드래프트 결과를 상대에게 전송.
    // me=내 패로 간 카드, opp=상대 패로 간 카드 (null=없음)
    // leftCv/rightCv=제시된 2장, choiceIsLeft=왼쪽 선택 여부
    private void SendDraftResult(CardValue meCard, CardValue oppCard,
        CardValue leftCv, CardValue rightCv, bool choiceIsLeft, bool isSingleCard)
    {
        if (mainFlow == null || !mainFlow.isOnlineMode || !mainFlow.IsPlayerTurn) return;
        if (NetworkManager.Instance == null) return;

        NetworkManager.Instance.SendDraft(
            meCard  != null, meCard  != null ? meCard.value  : 0,
            meCard  != null ? meCard.cardType  : CardType.Attack, meCard  != null && meCard.isJoker,
            oppCard != null, oppCard != null ? oppCard.value : 0,
            oppCard != null ? oppCard.cardType : CardType.Attack, oppCard != null && oppCard.isJoker,
            // 제시 2장 정보
            leftCv  != null, leftCv  != null ? leftCv.value  : 0,
            leftCv  != null ? leftCv.cardType  : CardType.Attack, leftCv  != null && leftCv.isJoker,
            rightCv != null, rightCv != null ? rightCv.value : 0,
            rightCv != null ? rightCv.cardType : CardType.Attack, rightCv != null && rightCv.isJoker,
            choiceIsLeft, isSingleCard);
    }

    // ─────────────────────────────────────────
    //  상대(턴 플레이어)의 드래프트 결과를 받아 시각 연출과 함께 반영
    //  MainFlow 폴링에서 호출
    // ─────────────────────────────────────────
    public void ApplyRemoteDraft(DraftData d)
    {
        _awaitingRemoteDraft = false;
        StartCoroutine(RemoteDraftRoutine(d));
    }

    private IEnumerator RemoteDraftRoutine(DraftData d)
    {
        _isResolving = true;

        // 양쪽 손패 max로 스킵된 경우 → 데이터만 적용
        if (!d.hasMe && !d.hasOpp && !d.hasLeft && !d.hasRight)
        {
            _isResolving = false;
            _isDrafting = false;
            yield break;
        }

        // 시각 연출이 가능한 경우 (제시 2장 정보가 있을 때)
        if (d.hasLeft && d.hasRight && !d.isSingleCard)
        {
            // ── 상대 턴 2장 드래프트 시각 연출 ──
            Transform deckRefForScale = (oppDeck != null && oppDeck.deckSpawnPoint != null)
                ? oppDeck.deckSpawnPoint
                : (oppDeck != null ? oppDeck.transform : deck.transform);

            // 1) 카드 2장 스폰 + 딜 애니메이션
            _leftCard = SpawnRemoteDraftCard(card1Anchor, deckRefForScale,
                d.leftV, d.leftType, d.leftJoker, out Vector3 c1FinalScale);
            if (_leftCard != null)
            {
                MaybeFaceDownDraftCard(_leftCard);  // 확률적으로 뒷면 제시 (연출용)
                yield return StartCoroutine(AnimateCardDeal(_leftCard, card1Anchor, c1FinalScale));
            }

            _rightCard = SpawnRemoteDraftCard(card2Anchor, deckRefForScale,
                d.rightV, d.rightType, d.rightJoker, out Vector3 c2FinalScale);
            if (_rightCard != null)
            {
                MaybeFaceDownDraftCard(_rightCard);  // 확률적으로 뒷면 제시 (연출용)
                yield return StartCoroutine(AnimateCardDeal(_rightCard, card2Anchor, c2FinalScale));
            }

            if (_leftCard == null || _rightCard == null)
            {
                // 스폰 실패 시 데이터만 적용하고 종료
                CleanupDraftCards();
                ApplyRemoteDraftData(d);
                _isResolving = false;
                _isDrafting = false;
                yield break;
            }

            _leftBaseScale  = _leftCard.transform.localScale;
            _rightBaseScale = _rightCard.transform.localScale;

            // 2) 상대가 선택한 카드를 호버 연출로 보여주기
            GameObject pick  = d.choiceIsLeft ? _leftCard : _rightCard;
            GameObject other = d.choiceIsLeft ? _rightCard : _leftCard;

            float totalDelay = Random.Range(oppPickDelayMin, oppPickDelayMax);
            float reveal     = Mathf.Clamp(oppDecisionRevealTime, 0f, totalDelay);
            float waitBefore = Mathf.Max(0f, totalDelay - reveal);

            if (waitBefore > 0f) yield return new WaitForSeconds(waitBefore);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.cardHover);

            yield return StartCoroutine(OppRevealPick(pick, other, reveal));

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.uiClick);

            // 3) 카드 비행: 상대가 고른 카드 → 상대 패(oppDeck), 남은 카드 → 내 패(deck)
            // chosen(상대가 선택) → oppDeck (뒷면으로), leftover → deck (앞면으로)
            DetachHoverBorder();
            if (pick  != null) pick.transform.localScale  = (pick  == _leftCard) ? _leftBaseScale : _rightBaseScale;
            if (other != null) other.transform.localScale = (other == _leftCard) ? _leftBaseScale : _rightBaseScale;

            // 상대 패로 가는 카드(pick)는 뒷면 교체
            OverlayBackOnCard(pick);

            // 데이터 적용 + 투명 목적지 카드 생성
            var pickCv  = pick  != null ? pick.GetComponent<CardValue>()  : null;
            var otherCv = other != null ? other.GetComponent<CardValue>() : null;

            // pick → 상대 패 (oppDeck)
            GameObject pickDest = null;
            if (d.hasMe && oppDeck != null)
            {
                oppDeck.AddCardByValue(d.meV, d.meType);
                var hand = oppDeck.SpawnedCards;
                if (hand != null && hand.Count > 0)
                {
                    pickDest = hand[hand.Count - 1];
                    SetCardAlpha(pickDest, 0f);
                }
            }

            // other → 내 패 (deck)
            GameObject otherDest = null;
            if (d.hasOpp && deck != null)
            {
                if (d.oppJoker) deck.AddJokerCard(d.oppType);
                else            deck.AddCardByValue(d.oppV, d.oppType);
                var hand = deck.SpawnedCards;
                if (hand != null && hand.Count > 0)
                {
                    otherDest = hand[hand.Count - 1];
                    SetCardAlpha(otherDest, 0f);
                }
            }

            // 비행 애니메이션
            Coroutine cA = StartCoroutine(FlyToTarget(pick,  pickDest));
            Coroutine cB = StartCoroutine(FlyToTarget(other, otherDest));
            yield return cA;
            yield return cB;

            // 정리
            if (pick  != null) Destroy(pick);
            if (other != null) Destroy(other);
            if (pickDest  != null) SetCardAlpha(pickDest,  1f);
            if (otherDest != null) SetCardAlpha(otherDest, 1f);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);
        }
        else
        {
            // 제시 2장 정보가 없거나 단일 카드 → 기존 방식 (데이터만 적용 + 사운드)
            ApplyRemoteDraftData(d);

            if (SoundManager.Instance != null)
                SoundManager.Instance.PlaySFX(SoundManager.Instance.cardDraw);
        }

        _leftCard  = null;
        _rightCard = null;
        _isResolving = false;
        _isDrafting  = false;
    }

    // 제시 2장 정보 없이 데이터만 적용하는 fallback
    private void ApplyRemoteDraftData(DraftData d)
    {
        if (d.hasMe && oppDeck != null)
            oppDeck.AddCardByValue(d.meV, d.meType);

        if (d.hasOpp && deck != null)
        {
            if (d.oppJoker) deck.AddJokerCard(d.oppType);
            else            deck.AddCardByValue(d.oppV, d.oppType);
        }
    }

    // ─────────────────────────────────────────
    //  상대 턴 드래프트 시각 연출용 카드 스폰
    //  (로컬 RNG를 쓰지 않고, 패킷에서 받은 값/타입으로 프리팹을 찾아 생성)
    // ─────────────────────────────────────────
    private GameObject SpawnRemoteDraftCard(Transform targetAnchor, Transform deckRefForScale,
        int value, CardType cardType, bool isJoker, out Vector3 finalLocalScale)
    {
        finalLocalScale = Vector3.one;

        // 프리팹 찾기: 해당 value/type에 맞는 프리팹을 덱 풀에서 검색
        GameObject prefab = null;
        if (deck != null)
        {
            if (isJoker)
            {
                deck.GetRandomPrefabFromActivePool(out prefab, out _, out _, out _);
                // 조커는 풀에서 아무거나 가져와도 CardValue로 덮어씌우므로 OK
                // 하지만 정확한 조커 프리팹을 찾아보자
                deck.GetRandomPrefabFromActivePool(out GameObject jpf, out int jv, out bool jk, out CardType jct);
                // 조커 전용 프리팹이 있으면 사용
                foreach (var g in deck.deckGroups)
                {
                    if (g == null || !g.isActive || g.cards == null) continue;
                    if (g.cards.Length >= 7 && g.cards[6] != null && g.cards[6].prefab != null)
                    { prefab = g.cards[6].prefab; break; }
                }
            }
            else
            {
                // value + cardType 매칭
                foreach (var g in deck.deckGroups)
                {
                    if (g == null || !g.isActive || g.cards == null) continue;
                    if (g.groupType != cardType) continue;
                    int idx = value - 1;
                    if (idx >= 0 && idx < g.cards.Length && g.cards[idx] != null && g.cards[idx].prefab != null)
                    { prefab = g.cards[idx].prefab; break; }
                }
                // type 불일치 fallback: value만 매칭
                if (prefab == null)
                {
                    foreach (var g in deck.deckGroups)
                    {
                        if (g == null || !g.isActive || g.cards == null) continue;
                        int idx = value - 1;
                        if (idx >= 0 && idx < g.cards.Length && g.cards[idx] != null && g.cards[idx].prefab != null)
                        { prefab = g.cards[idx].prefab; break; }
                    }
                }
            }
        }

        if (prefab == null) return null;

        // 카드 instantiate (SpawnDraftCardAtAnchor와 동일 패턴)
        GameObject card = Instantiate(prefab, targetAnchor);

        Vector3 deckLossy   = deckRefForScale != null ? deckRefForScale.lossyScale : Vector3.one;
        Vector3 anchorLossy = targetAnchor.lossyScale;
        Vector3 prefabScale = prefab.transform.localScale;
        finalLocalScale = new Vector3(
            prefabScale.x * Mathf.Abs(deckLossy.x) / Mathf.Max(Mathf.Abs(anchorLossy.x), 0.0001f) * cardScale,
            prefabScale.y * Mathf.Abs(deckLossy.y) / Mathf.Max(Mathf.Abs(anchorLossy.y), 0.0001f) * cardScale,
            prefabScale.z);

        if (deckAnchor != null)
        {
            card.transform.position = deckAnchor.position;
            card.transform.rotation = deckAnchor.rotation;
        }
        else
        {
            card.transform.localPosition = Vector3.zero;
            card.transform.localRotation = Quaternion.identity;
        }
        card.transform.localScale = finalLocalScale * Mathf.Max(0.01f, deckInitialScaleMul);

        var cv = card.GetComponent<CardValue>();
        if (cv == null) cv = card.AddComponent<CardValue>();
        cv.value    = value;
        cv.isJoker  = isJoker;
        cv.cardType = cardType;

        var hover = card.GetComponent<CardHover>();
        if (hover != null) hover.enabled = false;

        if (card.GetComponent<Collider2D>() == null)
            card.AddComponent<BoxCollider2D>();

        foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
            sr.sortingOrder = cardSortingOrder;

        return card;
    }

    private void CleanupDraftCards()
    {
        if (_leftCard  != null) Destroy(_leftCard);
        if (_rightCard != null) Destroy(_rightCard);
        _leftCard = null;
        _rightCard = null;
    }
}
