using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  슬롯 리롤 시스템
//  플레이어 턴 중 배치된 슬롯 카드를 슬롯머신 연출로 교체
// ─────────────────────────────────────────────
public class SlotRerollHandler
{
    private readonly MonoBehaviour _host;
    private readonly Slot[]    _playerSlots;
    private readonly Deck      _deck;
    private readonly GameFlow  _gameFlow;
    private readonly GameObject _rerollImagePrefab;
    private readonly int       _maxRerolls;

    private int        _remaining;
    private GameObject _overlay;
    private Slot       _hoveredSlot;
    private bool       _isRerolling;
    private readonly Dictionary<Slot, float> _slotCardPlacedTime
        = new Dictionary<Slot, float>();

    public bool IsRerolling => _isRerolling;

    public SlotRerollHandler(MonoBehaviour host, Slot[] playerSlots, Deck deck,
        GameFlow gameFlow, GameObject rerollImagePrefab, int maxRerolls)
    {
        _host              = host;
        _playerSlots       = playerSlots;
        _deck              = deck;
        _gameFlow          = gameFlow;
        _rerollImagePrefab = rerollImagePrefab;
        _maxRerolls        = maxRerolls;
        _remaining         = maxRerolls;
    }

    /// <summary>새 턴 시작 시 호출 (리롤 횟수 +1, 상태 리셋)</summary>
    public void OnNewTurn()
    {
        _remaining = Mathf.Min(_remaining + 1, _maxRerolls);
        _slotCardPlacedTime.Clear();
        ClearOverlay();
    }

    /// <summary>매 프레임 호출 — 호버 감지 + 클릭 처리</summary>
    public void Tick()
    {
        if (_isRerolling || _remaining <= 0 || _playerSlots == null)
        {
            ClearOverlay();
            return;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        // 슬롯 카드 배치 시간 추적
        foreach (var slot in _playerSlots)
        {
            if (slot == null) continue;
            if (slot.HasCard)
            {
                if (!_slotCardPlacedTime.ContainsKey(slot))
                    _slotCardPlacedTime[slot] = Time.time;
            }
            else
                _slotCardPlacedTime.Remove(slot);
        }

        Vector2 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);

        // 마우스 아래 리롤 가능한 슬롯 찾기
        Slot hoveredSlot = null;
        foreach (var slot in _playerSlots)
        {
            if (slot == null || !slot.HasCard || slot.IsChainLocked) continue;

            // 배치 직후 0.5초 쿨다운
            if (_slotCardPlacedTime.TryGetValue(slot, out float placedTime)
                && Time.time - placedTime < 0.5f)
                continue;

            var col = slot.GetComponent<Collider2D>();
            if (col != null && col.OverlapPoint(mouseWorld))
            {
                hoveredSlot = slot;
                break;
            }
        }

        // 호버 슬롯 변경 → 오버레이 갱신
        if (hoveredSlot != _hoveredSlot)
        {
            ClearOverlay();
            _hoveredSlot = hoveredSlot;

            if (_hoveredSlot != null && _rerollImagePrefab != null)
            {
                _overlay = Object.Instantiate(_rerollImagePrefab, _hoveredSlot.transform);
                _overlay.transform.localPosition = Vector3.zero;
                _overlay.transform.localScale    = Vector3.one * 1.5f;

                float ratio = (float)_remaining / _maxRerolls;
                foreach (var sr in _overlay.GetComponentsInChildren<SpriteRenderer>())
                {
                    sr.sortingOrder = 50;

                    if (sr.sprite != null)
                    {
                        Shader radialShader = Shader.Find("Custom/RadialFill");
                        if (radialShader != null)
                        {
                            Material mat = new Material(radialShader);
                            mat.SetTexture("_MainTex", sr.sprite.texture);
                            mat.SetFloat("_Fill", ratio);

                            Rect texRect = sr.sprite.textureRect;
                            float texW   = sr.sprite.texture.width;
                            float texH   = sr.sprite.texture.height;
                            mat.SetVector("_SpriteRect", new Vector4(
                                texRect.x / texW, texRect.y / texH,
                                texRect.width / texW, texRect.height / texH));

                            sr.material = mat;
                        }
                    }
                }
            }
        }

        // 클릭 → 리롤 실행
        if (_hoveredSlot != null && Input.GetMouseButtonDown(0))
        {
            Slot rerollSlot = _hoveredSlot;
            ClearOverlay();
            _host.StartCoroutine(DoSlotReroll(rerollSlot));
        }
    }

    public void ClearOverlay()
    {
        if (_overlay != null)
        {
            Object.Destroy(_overlay);
            _overlay = null;
        }
        _hoveredSlot = null;
    }

    // ─────────────────────────────────────────
    //  슬롯머신 애니메이션으로 카드 교체
    // ─────────────────────────────────────────
    private IEnumerator DoSlotReroll(Slot slot)
    {
        if (slot == null || !slot.HasCard || _deck == null) yield break;

        _isRerolling = true;

        // 리롤 전: 슬롯 이펙트 분리
        if (_gameFlow != null && _playerSlots != null)
        {
            for (int si = 0; si < _playerSlots.Length; si++)
            {
                if (_playerSlots[si] == slot)
                {
                    _gameFlow.DetachSlotEffect(si);
                    break;
                }
            }
        }

        var oldCv = slot.GetCardValue();
        if (oldCv == null) { _isRerolling = false; yield break; }

        CardType type = oldCv.cardType;

        // 최종 결과 카드 미리 결정
        GameObject finalPrefab;
        int finalValue;
        bool finalIsJoker;
        if (!_deck.GetRandomPrefabOfType(type, out finalPrefab, out finalValue, out finalIsJoker))
        {
            _isRerolling = false;
            yield break;
        }

        var slotCol = slot.GetComponent<Collider2D>();
        if (slotCol == null) { _isRerolling = false; yield break; }

        Vector2 slotWorldSize  = slotCol.bounds.size;
        Vector3 slotLossyScale = slot.transform.lossyScale;

        // ── SpriteMask 생성 (뷰포트 역할) ──
        GameObject maskObj = new GameObject("SlotRerollMask");
        maskObj.transform.SetParent(slot.transform, false);
        maskObj.transform.localPosition = Vector3.zero;

        SpriteMask mask    = maskObj.AddComponent<SpriteMask>();
        Texture2D maskTex  = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        Color[] pix        = new Color[16];
        for (int p = 0; p < 16; p++) pix[p] = Color.white;
        maskTex.SetPixels(pix);
        maskTex.Apply();
        mask.sprite = Sprite.Create(maskTex, new Rect(0, 0, 4, 4),
            new Vector2(0.5f, 0.5f), 4f);

        maskObj.transform.localScale = new Vector3(
            slotWorldSize.x / Mathf.Max(Mathf.Abs(slotLossyScale.x), 0.001f),
            slotWorldSize.y / Mathf.Max(Mathf.Abs(slotLossyScale.y), 0.001f),
            1f);

        float cardSpacing = slotWorldSize.y
            / Mathf.Max(Mathf.Abs(slotLossyScale.y), 0.001f);

        // ── 릴 컨테이너 ──
        GameObject reelContainer = new GameObject("ReelContainer");
        reelContainer.transform.SetParent(slot.transform, false);
        reelContainer.transform.localPosition = Vector3.zero;

        // ── 현재 카드를 릴에 편입 ──
        GameObject currentCard = slot.ReleaseCard();
        currentCard.transform.SetParent(reelContainer.transform);
        currentCard.transform.localPosition = Vector3.zero;

        foreach (var sr in currentCard.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
            sr.sortingOrder    = 10;
        }

        List<GameObject> reelCards = new List<GameObject> { currentCard };

        // ── 릴 카드 생성 ──
        int reelCount      = 10;
        int finalZoneStart = reelCount - 3;
        int lastPickValue  = oldCv.value;

        for (int i = 1; i < reelCount; i++)
        {
            bool isFinalZone = (i >= finalZoneStart);
            GameObject pickPrefab;
            int pickValue;
            bool pickIsJoker;

            if (isFinalZone)
            {
                pickPrefab  = finalPrefab;
                pickValue   = finalValue;
                pickIsJoker = finalIsJoker;
            }
            else
            {
                int attempts = 0;
                do
                {
                    if (!_deck.GetRandomPrefabOfType(type,
                        out pickPrefab, out pickValue, out pickIsJoker))
                    {
                        pickPrefab  = finalPrefab;
                        pickValue   = finalValue;
                        pickIsJoker = finalIsJoker;
                        break;
                    }
                    attempts++;
                } while ((pickValue == lastPickValue || pickValue == finalValue)
                         && attempts < 20);
            }
            lastPickValue = pickValue;

            GameObject reelCard = Object.Instantiate(pickPrefab, reelContainer.transform);
            var cv = reelCard.GetComponent<CardValue>();
            if (cv == null) cv = reelCard.AddComponent<CardValue>();
            cv.value    = pickValue;
            cv.isJoker  = pickIsJoker;
            cv.cardType = type;

            FitCardToSlot(reelCard, slotCol);
            reelCard.transform.localPosition = new Vector3(0f, cardSpacing * i, 0f);

            foreach (var sr in reelCard.GetComponentsInChildren<SpriteRenderer>())
            {
                sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;
                sr.sortingOrder    = 10;
            }

            foreach (var c in reelCard.GetComponentsInChildren<Collider2D>())
                c.enabled = false;

            reelCards.Add(reelCard);
        }

        // ── 릴 슬라이드 (ease-out cubic) ──
        float totalDist = cardSpacing * (reelCount - 1);
        float duration  = 1.2f;
        float elapsed   = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - (1f - t) * (1f - t) * (1f - t);
            reelContainer.transform.localPosition = new Vector3(0f, -totalDist * eased, 0f);
            yield return null;
        }
        reelContainer.transform.localPosition = new Vector3(0f, -totalDist, 0f);

        // ── 정리: 최종 카드만 남기고 나머지 제거 ──
        GameObject finalCard = reelCards[reelCards.Count - 1];

        foreach (var sr in finalCard.GetComponentsInChildren<SpriteRenderer>())
            sr.maskInteraction = SpriteMaskInteraction.None;

        finalCard.transform.SetParent(null);
        slot.PlaceCardRaw(finalCard);

        Object.Destroy(reelContainer);
        Object.Destroy(maskObj);
        if (maskTex != null) Object.Destroy(maskTex);

        _remaining--;
        _isRerolling = false;
    }

    private void FitCardToSlot(GameObject card, Collider2D slotCol)
    {
        Vector2 slotSize = slotCol.bounds.size;
        var renderer = card.GetComponentInChildren<Renderer>();
        if (renderer == null) return;
        Vector3 cardSize = renderer.bounds.size;
        if (cardSize.x < 0.001f || cardSize.y < 0.001f) return;
        float scale = Mathf.Min(slotSize.x / cardSize.x, slotSize.y / cardSize.y);
        card.transform.localScale *= scale;
    }
}
