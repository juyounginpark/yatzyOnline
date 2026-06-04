using System.Collections;
using UnityEngine;

// 카드가 놓이는 슬롯 프리미티브.
// (턴/회수/가드토글 등 게임 진행 입력 처리는 제거됨 — 새 진행 로직에서 제어)
public class Slot : MonoBehaviour
{
    private GameObject _placedCard;
    private int _placedCardValue;
    private bool _placedCardIsJoker;
    private bool _isFlipping;
    private bool _isFlipped;
    private Vector3 _cardFittedScale; // FitToSlot 후 카드 스케일 저장

    public bool allowReturn = true;

    // ─── 체인 잠금 ───
    private bool _isChainLocked;
    private GameObject _chainOverlay;

    public bool IsChainLocked => _isChainLocked;
    public bool IsGuard => _isFlipped;

    public bool HasCard => _placedCard != null;
    public bool HasVisibleCard => _placedCard != null;

    void Awake()
    {
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    private IEnumerator FlipCardRoutine(GameObject card, GameObject backPrefab, bool toBack, float duration = 0.15f)
    {
        if (card == null) yield break;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.cardFlip);

        float elapsed = 0f;
        Vector3 origScale = card.transform.localScale;

        // 1) 절반 뒤집기 (X 스케일 0으로)
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            card.transform.localScale = new Vector3(Mathf.Lerp(origScale.x, 0f, t), origScale.y, origScale.z);
            yield return null;
        }

        // 2) 비주얼 교체
        ApplyFace(card, backPrefab, toBack);

        // 3) 나머지 절반 뒤집기 (X 스케일 복구)
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            card.transform.localScale = new Vector3(Mathf.Lerp(0f, origScale.x, t), origScale.y, origScale.z);
            yield return null;
        }

        card.transform.localScale = origScale;
    }

    // 앞면(toBack=false)/뒷면(toBack=true) 비주얼만 즉시 교체 (스케일 애니메이션 없음)
    private void ApplyFace(GameObject card, GameObject backPrefab, bool toBack)
    {
        if (toBack)
        {
            var cardSr = card.GetComponentInChildren<SpriteRenderer>();

            // 앞면 렌더러 비활성화
            foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
                sr.enabled = false;

            GameObject back = Instantiate(backPrefab, card.transform);
            back.name = "CardBack_Slot";
            back.transform.localPosition = Vector3.zero;
            back.transform.localRotation = Quaternion.identity;

            // 뒷면 크기 보정 (원본 스프라이트 크기에 맞춤)
            var backSr = back.GetComponentInChildren<SpriteRenderer>();
            if (cardSr != null && backSr != null)
            {
                Vector2 cardSize = cardSr.sprite.bounds.size;
                Vector2 backSize = backSr.sprite.bounds.size;
                float ratioX = backSize.x != 0 ? cardSize.x / backSize.x : 1f;
                float ratioY = backSize.y != 0 ? cardSize.y / backSize.y : 1f;
                back.transform.localScale = new Vector3(ratioX, ratioY, 1f);
            }

            foreach (var r in back.GetComponentsInChildren<Renderer>())
                r.sortingOrder = 2;
        }
        else
        {
            // 뒷면 오브젝트 파괴
            Transform backChild = card.transform.Find("CardBack_Slot");
            if (backChild != null)
                Destroy(backChild.gameObject);

            // 앞면 렌더러 복구
            foreach (var sr in card.GetComponentsInChildren<SpriteRenderer>())
                sr.enabled = true;
        }
    }

    /// <summary>카드를 즉시 뒷면 상태로 배치한다 (애니메이션·사운드 없음).</summary>
    public void PlaceCardFaceDown(GameObject card)
    {
        PlaceCard(card);

        _isFlipped         = true;
        _placedCardValue   = 0;
        _placedCardIsJoker = false;

        var deck = FindObjectOfType<Deck>();
        if (deck != null && deck.cardBackPrefab != null)
            ApplyFace(_placedCard, deck.cardBackPrefab, true);
    }

    public IEnumerator ForceReveal()
    {
        yield return StartCoroutine(FlipTo(false));
    }

    /// <summary>느린 속도로 카드를 앞면으로 공개 (극적인 연출용)</summary>
    public IEnumerator ForceRevealSlow(float duration = 0.5f)
    {
        yield return StartCoroutine(FlipTo(false, duration));
    }

    /// <summary>
    /// 카드를 앞면(toBack=false)/뒷면(toBack=true)으로 뒤집는다.
    /// 이미 해당 상태면 아무 것도 하지 않는다.
    /// </summary>
    public IEnumerator FlipTo(bool toBack, float duration = 0.15f)
    {
        if (_placedCard == null || _isFlipped == toBack) yield break;

        var deck = FindObjectOfType<Deck>();
        if (deck == null || deck.cardBackPrefab == null) yield break;

        _isFlipped = toBack;
        var cv = _placedCard.GetComponent<CardValue>();
        if (toBack)
        {
            _placedCardValue   = 0;
            _placedCardIsJoker = false;
        }
        else if (cv != null)
        {
            _placedCardValue   = cv.value;
            _placedCardIsJoker = cv.isJoker;
        }

        yield return StartCoroutine(FlipCardRoutine(_placedCard, deck.cardBackPrefab, toBack, duration));
    }

    /// <summary>비주얼 전환 없이 Guard(뒷면) 논리 상태만 설정한다.</summary>
    public void MarkGuard(bool guard)
    {
        _isFlipped = guard;
        var cv = _placedCard != null ? _placedCard.GetComponent<CardValue>() : null;
        if (guard)
        {
            _placedCardValue   = 0;
            _placedCardIsJoker = false;
        }
        else if (cv != null)
        {
            _placedCardValue   = cv.value;
            _placedCardIsJoker = cv.isJoker;
        }
    }

    /// <summary>카드를 파괴하지 않고 슬롯 참조만 비운다.</summary>
    public void ForgetCard()
    {
        _placedCard  = null;
        _isFlipped   = false;
    }

    public void PlaceCard(GameObject card)
    {
        if (_placedCard != null && _placedCard != card)
            Destroy(_placedCard);

        _placedCard = card;
        _isFlipped = false;

        var cv = card.GetComponent<CardValue>();
        _placedCardValue = cv != null ? cv.value : 0;
        _placedCardIsJoker = cv != null && cv.isJoker;

        card.transform.SetParent(transform);
        card.transform.localPosition = Vector3.zero;
        card.transform.localRotation = Quaternion.identity;

        FitToSlot(card);
        _cardFittedScale = card.transform.localScale;

        var hover = card.GetComponent<CardHover>();
        if (hover != null)
            hover.enabled = false;

        var colliders = card.GetComponentsInChildren<Collider2D>();
        foreach (var c in colliders)
            c.enabled = false;

        var renderers = card.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
            r.sortingOrder = 1;
    }

    public GameObject GetPlacedCard()
    {
        return _placedCard;
    }

    public CardValue GetCardValue()
    {
        if (_placedCard == null) return null;
        return _placedCard.GetComponent<CardValue>();
    }

    public void ClearCard()
    {
        if (_placedCard != null)
        {
            Destroy(_placedCard);
            _placedCard = null;
        }
    }

    /// <summary>FitToSlot 없이 카드를 배치 (이미 스케일이 맞춰진 경우 사용)</summary>
    public void PlaceCardRaw(GameObject card)
    {
        if (_placedCard != null && _placedCard != card)
            Destroy(_placedCard);

        _placedCard = card;

        var cv = card.GetComponent<CardValue>();
        _placedCardValue = cv != null ? cv.value : 0;
        _placedCardIsJoker = cv != null && cv.isJoker;

        card.transform.SetParent(transform);
        card.transform.localPosition = Vector3.zero;
        card.transform.localRotation = Quaternion.identity;

        _cardFittedScale = card.transform.localScale;

        var hover = card.GetComponent<CardHover>();
        if (hover != null)
            hover.enabled = false;

        var colliders = card.GetComponentsInChildren<Collider2D>();
        foreach (var c in colliders)
            c.enabled = false;

        var renderers = card.GetComponentsInChildren<Renderer>();
        foreach (var r in renderers)
            r.sortingOrder = 1;
    }

    public GameObject ReleaseCard()
    {
        if (_placedCard == null) return null;

        GameObject card = _placedCard;
        _placedCard = null;
        card.transform.SetParent(null);
        return card;
    }

    private void FitToSlot(GameObject card)
    {
        var col = GetComponent<Collider2D>();
        Vector2 slotSize = col.bounds.size;

        var renderer = card.GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        Vector3 cardSize = renderer.bounds.size;
        float scaleX = slotSize.x / cardSize.x;
        float scaleY = slotSize.y / cardSize.y;
        float scale = Mathf.Min(scaleX, scaleY);

        card.transform.localScale *= scale;
    }

    // ────────────────────────────────────────
    //  체인 잠금: 슬롯 위에 체인 오버레이 생성
    // ────────────────────────────────────────
    public void ChainLock(GameObject chainPrefab)
    {
        _isChainLocked = true;

        _chainOverlay = Instantiate(chainPrefab, transform);
        _chainOverlay.transform.localPosition = Vector3.zero;
        _chainOverlay.transform.localRotation = Quaternion.identity;
        _chainOverlay.transform.localScale = Vector3.one;

        FitToSlot(_chainOverlay);

        foreach (var sr in _chainOverlay.GetComponentsInChildren<SpriteRenderer>())
        {
            sr.sortingOrder = 10;
            Color c = sr.color;
            c.a = 0f;
            sr.color = c;
        }
    }

    public IEnumerator FadeInChain(float duration = 0.5f)
    {
        if (_chainOverlay == null) yield break;
        var renderers = _chainOverlay.GetComponentsInChildren<SpriteRenderer>();

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float a = Mathf.Clamp01(elapsed / duration);
            foreach (var sr in renderers)
            {
                Color c = sr.color;
                c.a = a;
                sr.color = c;
            }
            yield return null;
        }

        StartCoroutine(ChainShakeLoop());
    }

    private IEnumerator ChainShakeLoop()
    {
        while (_isChainLocked && _chainOverlay != null)
        {
            yield return new WaitForSeconds(Random.Range(0.8f, 2f));
            if (!_isChainLocked || _chainOverlay == null) break;

            int shakes = Random.Range(2, 4);
            float intensity = 0.03f;
            for (int i = 0; i < shakes; i++)
            {
                Vector3 offset = new Vector3(
                    Random.Range(-intensity, intensity),
                    Random.Range(-intensity, intensity),
                    0f);
                _chainOverlay.transform.localPosition = offset;
                yield return new WaitForSeconds(0.04f);
            }
            _chainOverlay.transform.localPosition = Vector3.zero;
        }
    }

    public IEnumerator UnlockChain(float duration = 0.5f)
    {
        _isChainLocked = false;

        if (_chainOverlay != null)
        {
            var renderers = _chainOverlay.GetComponentsInChildren<SpriteRenderer>();
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float a = 1f - Mathf.Clamp01(elapsed / duration);
                foreach (var sr in renderers)
                {
                    Color c = sr.color;
                    c.a = a;
                    sr.color = c;
                }
                yield return null;
            }
            Destroy(_chainOverlay);
            _chainOverlay = null;
        }
    }
}
