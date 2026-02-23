using System.Collections;
using UnityEngine;

public class Slot : MonoBehaviour
{
    private GameObject _placedCard;
    private GameObject _cardBackInstance;
    private int _placedCardValue;
    private bool _placedCardIsJoker;
    private CardType _placedCardType;
    private bool _isFaceDown;
    private bool _revealedOnly; // 앞면 공개만, 점수 계산 제외
    private bool _isFlipping;

    public bool allowReturn = true;
    public bool IsFaceDown => _isFaceDown;

    public bool HasCard => _placedCard != null;
    public bool HasVisibleCard => _placedCard != null && !_isFaceDown && !_revealedOnly;

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

        // 좌클릭: 덱으로 반환 (애니메이션)
        if (allowReturn && Input.GetMouseButtonDown(0) && !_isFlipping)
        {
            StartCoroutine(ReturnAnimation());
        }

        // 우클릭: 앞면/뒷면 토글 (애니메이션)
        if (Input.GetMouseButtonDown(1) && !_isFlipping)
        {
            StartCoroutine(FlipAnimation());
        }
    }

    public void PlaceCard(GameObject card)
    {
        _placedCard = card;
        _isFaceDown = false;
        _revealedOnly = false;

        var cv = card.GetComponent<CardValue>();
        _placedCardValue = cv != null ? cv.value : 0;
        _placedCardIsJoker = cv != null && cv.isJoker;
        _placedCardType = cv != null ? cv.cardType : CardType.Attack;

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

    /// <summary>
    /// 뒷면 → 앞면 전환 애니메이션 (점수 계산 제외 상태)
    /// </summary>
    private IEnumerator FlipAnimation()
    {
        _isFlipping = true;
        bool toFaceDown = !_isFaceDown;

        float duration = 0.3f;
        float half = duration * 0.5f;

        // 뒷면 인스턴스의 Transform 또는 카드 Transform
        Transform flipTarget = _isFaceDown && _cardBackInstance != null
            ? _cardBackInstance.transform
            : _placedCard.transform;
        Vector3 origScale = flipTarget.localScale;

        // 전반: X 스케일 축소
        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            flipTarget.localScale = new Vector3(origScale.x * (1f - t), origScale.y, origScale.z);
            yield return null;
        }

        // 상태 전환
        _isFaceDown = toFaceDown;
        SetFaceDown(_isFaceDown);

        // 새 대상의 Transform
        flipTarget = _isFaceDown && _cardBackInstance != null
            ? _cardBackInstance.transform
            : _placedCard.transform;
        origScale = flipTarget.localScale;
        flipTarget.localScale = new Vector3(0f, origScale.y, origScale.z);

        // 후반: X 스케일 복원
        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            flipTarget.localScale = new Vector3(origScale.x * t, origScale.y, origScale.z);
            yield return null;
        }

        flipTarget.localScale = origScale;
        _isFlipping = false;
    }

    public Coroutine RevealCard()
    {
        if (!_isFaceDown || _placedCard == null) return null;
        return StartCoroutine(RevealAnimation());
    }

    private IEnumerator RevealAnimation()
    {
        float duration = 0.3f;
        float half = duration * 0.5f;
        Transform cardT = _placedCard.transform;
        Vector3 origScale = cardT.localScale;

        // 전반: X 스케일 0으로 줄이기 (뒷면 → 납작)
        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            cardT.localScale = new Vector3(origScale.x * (1f - t), origScale.y, origScale.z);
            yield return null;
        }

        // 뒷면 제거, 앞면 표시
        _isFaceDown = false;
        _revealedOnly = true;
        SetFaceDown(false);

        // 후반: X 스케일 복원 (납작 → 앞면)
        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            cardT.localScale = new Vector3(origScale.x * t, origScale.y, origScale.z);
            yield return null;
        }

        cardT.localScale = origScale;
    }

    public void ClearCard()
    {
        ClearCardBack();
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

        // 뒷면이면 앞면으로 복원 후 반환
        if (_isFaceDown)
        {
            foreach (var r in _placedCard.GetComponentsInChildren<Renderer>())
                r.enabled = true;
        }
        ClearCardBack();

        GameObject card = _placedCard;
        _placedCard = null;
        card.transform.SetParent(null);
        return card;
    }

    private IEnumerator ReturnAnimation()
    {
        _isFlipping = true;

        // 표시 중인 대상 (뒷면이면 뒷면 인스턴스, 아니면 카드 본체)
        Transform target = (_isFaceDown && _cardBackInstance != null)
            ? _cardBackInstance.transform
            : _placedCard.transform;
        Vector3 origScale = target.localScale;

        // 축소 애니메이션 (0.2초)
        float duration = 0.2f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float s = 1f - t;
            target.localScale = new Vector3(origScale.x * s, origScale.y * s, origScale.z);
            yield return null;
        }

        _isFlipping = false;
        ReturnCardToDeck();
    }

    private void ReturnCardToDeck()
    {
        var deck = FindObjectOfType<Deck>();
        if (deck == null || deck.IsHandFull) return;

        ClearCardBack();

        bool isJoker = _placedCardIsJoker;
        int value = _placedCardValue;
        CardType type = _placedCardType;

        Destroy(_placedCard);
        _placedCard = null;

        if (isJoker)
            deck.AddJokerCard(type);
        else
            deck.AddCardByValue(value, type);
    }

    private void SetFaceDown(bool faceDown)
    {
        if (faceDown)
        {
            // 앞면 숨기기
            foreach (var r in _placedCard.GetComponentsInChildren<Renderer>())
                r.enabled = false;

            // 뒷면 생성 (Deck에서 프리팹 참조)
            var deckRef = FindObjectOfType<Deck>();
            GameObject backPrefab = deckRef != null ? deckRef.cardBackPrefab : null;
            if (_cardBackInstance == null && backPrefab != null)
            {
                _cardBackInstance = Instantiate(backPrefab, transform);
                _cardBackInstance.transform.localPosition = Vector3.zero;
                _cardBackInstance.transform.localRotation = Quaternion.identity;
                FitToSlot(_cardBackInstance);

                foreach (var r in _cardBackInstance.GetComponentsInChildren<Renderer>())
                    r.sortingOrder = 1;
            }

            if (_cardBackInstance != null)
                _cardBackInstance.SetActive(true);
        }
        else
        {
            // 앞면 보이기
            foreach (var r in _placedCard.GetComponentsInChildren<Renderer>())
            {
                r.enabled = true;
                r.sortingOrder = 1;
            }

            // 뒷면 숨기기
            if (_cardBackInstance != null)
                _cardBackInstance.SetActive(false);
        }
    }

    private void ClearCardBack()
    {
        if (_cardBackInstance != null)
        {
            Destroy(_cardBackInstance);
            _cardBackInstance = null;
        }
        _isFaceDown = false;
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
