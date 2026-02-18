using UnityEngine;

public class Slot : MonoBehaviour
{
    private GameObject _placedCard;

    public bool HasCard => _placedCard != null;

    void Awake()
    {
        if (GetComponent<Collider2D>() == null)
            gameObject.AddComponent<BoxCollider2D>();
    }

    public void PlaceCard(GameObject card)
    {
        _placedCard = card;

        // 슬롯의 자식으로 넣기
        card.transform.SetParent(transform);
        card.transform.localPosition = Vector3.zero;
        card.transform.localRotation = Quaternion.identity;

        // 슬롯 크기에 맞추기
        FitToSlot(card);

        // CardHover 비활성화
        var hover = card.GetComponent<CardHover>();
        if (hover != null)
            hover.enabled = false;
    }

    public void ClearCard()
    {
        if (_placedCard != null)
        {
            Destroy(_placedCard);
            _placedCard = null;
        }
    }

    private void FitToSlot(GameObject card)
    {
        var col = GetComponent<Collider2D>();
        Vector2 slotSize = col.bounds.size;

        // 카드의 렌더러 바운드
        var renderer = card.GetComponentInChildren<Renderer>();
        if (renderer == null) return;

        Vector3 cardSize = renderer.bounds.size;
        float scaleX = slotSize.x / cardSize.x;
        float scaleY = slotSize.y / cardSize.y;
        float scale = Mathf.Min(scaleX, scaleY);

        card.transform.localScale *= scale;
    }
}
