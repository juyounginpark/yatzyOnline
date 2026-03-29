using System.Collections;
using UnityEngine;

public class Slot : MonoBehaviour
{
    private GameObject _placedCard;
    private int _placedCardValue;
    private bool _placedCardIsJoker;
    private CardType _placedCardType;
    private bool _isFlipping;
    private Vector3 _cardFittedScale; // FitToSlot 후 카드 스케일 저장

    // 같은 프레임에 여러 슬롯이 동시에 우클릭을 처리하는 것 방지
    private static int _lastReturnFrame = -1;

    public bool allowReturn = true;

    public bool HasCard => _placedCard != null;
    public bool HasVisibleCard => _placedCard != null;

    void Awake()
    {
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    void Update()
    {
        if (_placedCard == null) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector2 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
        var col = GetComponent<Collider2D>();
        if (col == null || !col.OverlapPoint(mouseWorld)) return;

        // 우클릭: 덱으로 반환 (애니메이션)
        // _lastReturnFrame 체크로 같은 프레임에 여러 슬롯이 동시에 처리되는 것 방지
        if (allowReturn && Input.GetMouseButtonDown(1) && !_isFlipping
            && _lastReturnFrame != Time.frameCount)
        {
            _lastReturnFrame = Time.frameCount;
            StartCoroutine(ReturnAnimation());
        }
    }

    public void PlaceCard(GameObject card)
    {
        _placedCard = card;

        var cv = card.GetComponent<CardValue>();
        _placedCardValue = cv != null ? cv.value : 0;
        _placedCardIsJoker = cv != null && cv.isJoker;
        _placedCardType = cv != null ? cv.cardType : CardType.Attack;

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

    /// <summary>
    /// 카드를 파괴하지 않고 슬롯에서 분리하여 반환
    /// </summary>
    /// <summary>
    /// FitToSlot 없이 카드를 배치 (이미 스케일이 맞춰진 경우 사용)
    /// </summary>
    public void PlaceCardRaw(GameObject card)
    {
        _placedCard = card;

        var cv = card.GetComponent<CardValue>();
        _placedCardValue = cv != null ? cv.value : 0;
        _placedCardIsJoker = cv != null && cv.isJoker;
        _placedCardType = cv != null ? cv.cardType : CardType.Attack;

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

    private IEnumerator ReturnAnimation()
    {
        _isFlipping = true;

        if (_placedCard != null)
        {
            float elapsed = 0f;
            float duration = 0.2f;
            Vector3 startScale = _placedCard.transform.localScale;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                _placedCard.transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                yield return null;
            }
        }

        ReturnCardToDeck();
        _isFlipping = false;
    }
    private void ReturnCardToDeck()
    {
        var deck = FindObjectOfType<Deck>();
        if (deck == null || deck.IsHandFull) return;

        bool isJoker = _placedCardIsJoker;
        int value = _placedCardValue;
        CardType type = _placedCardType;

        Destroy(_placedCard);
        _placedCard = null;

        if (isJoker)
            deck.AddJokerCard(type);
        else
            deck.AddCardByValue(value, type);

        // 온라인: 상대에게 카드 반환 알림
        if (NetworkManager.Instance != null && NetworkManager.Instance.State == NetState.InGame)
        {
            var mf = FindObjectOfType<MainFlow>();
            if (mf != null && mf.isOnlineMode && mf.playerSlots != null)
            {
                int idx = System.Array.IndexOf(mf.playerSlots, this);
                if (idx >= 0)
                    NetworkManager.Instance.SendCardReturn(idx);
            }
        }
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
}
