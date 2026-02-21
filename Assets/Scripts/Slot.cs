using UnityEngine;

public class Slot : MonoBehaviour
{
    private GameObject _placedCard;
    private int _placedCardValue;
    private bool _placedCardIsJoker;

    public bool allowReturn = true;

    public bool HasCard => _placedCard != null;

    void Awake()
    {
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    void Update()
    {
        if (_placedCard != null && allowReturn && Input.GetMouseButtonDown(1))
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector2 mouseWorld = cam.ScreenToWorldPoint(Input.mousePosition);
            var col = GetComponent<Collider2D>();
            if (col != null && col.OverlapPoint(mouseWorld))
            {
                ReturnCardToDeck();
            }
        }
    }

    public void PlaceCard(GameObject card)
    {
        _placedCard = card;

        var cv = card.GetComponent<CardValue>();
        _placedCardValue = cv != null ? cv.value : 0;
        _placedCardIsJoker = cv != null && cv.isJoker;

        card.transform.SetParent(transform);
        card.transform.localPosition = Vector3.zero;
        card.transform.localRotation = Quaternion.identity;

        FitToSlot(card);

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
    public GameObject ReleaseCard()
    {
        if (_placedCard == null) return null;

        GameObject card = _placedCard;
        _placedCard = null;
        card.transform.SetParent(null);
        return card;
    }

    private void ReturnCardToDeck()
    {
        var deck = FindObjectOfType<Deck>();
        if (deck == null || deck.IsHandFull) return;

        bool isJoker = _placedCardIsJoker;
        int value = _placedCardValue;

        Destroy(_placedCard);
        _placedCard = null;

        if (isJoker)
            deck.AddJokerCard();
        else
            deck.AddCardByValue(value);
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
