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
    private bool _isPeeked; // 방어 카드 앞면 엿보기 (IsFaceDown 유지)
    private Vector3 _cardFittedScale; // FitToSlot 후 카드 스케일 저장
    private Vector3 _cardBackFittedScale; // FitToSlot 후 뒷면 스케일 저장

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

        // 우클릭: 전체 슬롯 카드 뒤집기 (애니메이션)
        if (Input.GetMouseButtonDown(1) && !_isFlipping)
        {
            bool targetFaceDown = !_isFaceDown;
            foreach (var s in FindObjectsOfType<Slot>())
            {
                if (s.HasCard && s.allowReturn && s.IsFaceDown != targetFaceDown)
                    s.FlipPublic();
            }
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

        // 새 카드 배치 시 같은 그룹의 뒷면 카드를 전부 앞면으로 되돌림
        if (allowReturn)
        {
            foreach (var s in FindObjectsOfType<Slot>())
            {
                if (s != this && s.allowReturn && s.HasCard && s.IsFaceDown)
                    s.FlipPublic();
            }
        }
    }

    /// <summary>
    /// 카드를 뒷면(방어) 상태로 슬롯에 배치
    /// PlaceCard 후 즉시 뒷면 전환 (같은 프레임, 앞면 노출 없음)
    /// </summary>
    public void PlaceCardFaceDown(GameObject card)
    {
        PlaceCard(card);
        _isFaceDown = true;
        SetFaceDown(true);
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

        // 전반 대상: 현재 보이는 것
        Transform fromTarget = (_isFaceDown && _cardBackInstance != null)
            ? _cardBackInstance.transform
            : _placedCard.transform;
        Vector3 fromScale = fromTarget.localScale;

        // 전반: 현재 보이는 면 X 축소
        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            fromTarget.localScale = new Vector3(fromScale.x * (1f - t), fromScale.y, fromScale.z);
            yield return null;
        }
        fromTarget.localScale = new Vector3(0f, fromScale.y, fromScale.z);

        // 상태 전환
        _isFaceDown = toFaceDown;
        SetFaceDown(_isFaceDown);

        // 후반 대상: 새로 보이는 것
        Transform toTarget = (_isFaceDown && _cardBackInstance != null)
            ? _cardBackInstance.transform
            : _placedCard.transform;

        // 후반 목표 스케일: 뒷면이면 저장된 뒷면 스케일, 앞면이면 저장된 앞면 스케일
        Vector3 toScale = _isFaceDown
            ? _cardBackFittedScale
            : _cardFittedScale;
        toTarget.localScale = new Vector3(0f, toScale.y, toScale.z);

        // 후반: 새로 보이는 면 X 복원
        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            toTarget.localScale = new Vector3(toScale.x * t, toScale.y, toScale.z);
            yield return null;
        }

        toTarget.localScale = toScale;
        _isFlipping = false;
    }

    public void FlipPublic()
    {
        if (_placedCard != null && !_isFlipping)
            StartCoroutine(FlipAnimation());
    }

    public Coroutine RevealCard()
    {
        if (!_isFaceDown || _placedCard == null) return null;

        // 이미 Peek 중이면 애니메이션 없이 상태만 전환
        if (_isPeeked)
        {
            _isPeeked = false;
            _isFaceDown = false;
            _revealedOnly = true;
            if (_cardBackInstance != null)
            {
                Destroy(_cardBackInstance);
                _cardBackInstance = null;
            }
            return null;
        }

        return StartCoroutine(RevealAnimation());
    }

    private IEnumerator RevealAnimation()
    {
        float duration = 0.3f;
        float half = duration * 0.5f;

        // 전반: 뒷면 X 스케일 축소
        Transform backT = _cardBackInstance != null ? _cardBackInstance.transform : null;
        Vector3 backOrigScale = backT != null ? backT.localScale : Vector3.one;

        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            if (backT != null)
                backT.localScale = new Vector3(backOrigScale.x * (1f - t), backOrigScale.y, backOrigScale.z);
            yield return null;
        }

        // 뒷면 제거, 앞면 표시
        _isFaceDown = false;
        _revealedOnly = true;
        SetFaceDown(false);

        // 후반: 앞면 X 스케일 복원 (저장된 스케일 사용)
        Transform cardT = _placedCard.transform;
        Vector3 targetScale = _cardFittedScale;
        cardT.localScale = new Vector3(0f, targetScale.y, targetScale.z);

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            cardT.localScale = new Vector3(targetScale.x * t, targetScale.y, targetScale.z);
            yield return null;
        }

        cardT.localScale = targetScale;
    }

    /// <summary>
    /// 방어 카드를 시각적으로 앞면으로 보여줌 (IsFaceDown 유지, 방어 기능 유지)
    /// 뒷면 X 축소 → 앞면 X 확대 애니메이션
    /// </summary>
    public Coroutine PeekDefenseCard()
    {
        if (!_isFaceDown || _placedCard == null) return null;
        return StartCoroutine(PeekAnimation());
    }

    private IEnumerator PeekAnimation()
    {
        _isPeeked = true;

        float duration = 0.3f;
        float half = duration * 0.5f;

        // 전반: 뒷면 X 스케일 축소
        Transform backT = _cardBackInstance != null ? _cardBackInstance.transform : null;
        Vector3 backOrigScale = backT != null ? backT.localScale : Vector3.one;

        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            if (backT != null)
                backT.localScale = new Vector3(backOrigScale.x * (1f - t), backOrigScale.y, backOrigScale.z);
            yield return null;
        }

        // 뒷면 숨기고 앞면 표시 (_isFaceDown은 유지)
        if (_cardBackInstance != null)
            _cardBackInstance.SetActive(false);

        foreach (var r in _placedCard.GetComponentsInChildren<Renderer>())
        {
            r.enabled = true;
            r.sortingOrder = 1;
        }

        // 후반: 앞면 X 스케일 복원
        Transform cardT = _placedCard.transform;
        Vector3 targetScale = _cardFittedScale;
        cardT.localScale = new Vector3(0f, targetScale.y, targetScale.z);

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            cardT.localScale = new Vector3(targetScale.x * t, targetScale.y, targetScale.z);
            yield return null;
        }

        cardT.localScale = targetScale;
    }

    /// <summary>
    /// 방어 카드 앞면 보기 해제 (다시 뒷면 표시)
    /// </summary>
    public void UnpeekDefenseCard()
    {
        if (!_isPeeked || _placedCard == null) return;
        _isPeeked = false;

        // 앞면 X 스케일을 다시 0으로 (뒷면 상태 복원)
        Vector3 s = _cardFittedScale;
        _placedCard.transform.localScale = new Vector3(0f, s.y, s.z);

        foreach (var r in _placedCard.GetComponentsInChildren<Renderer>())
            r.enabled = false;
        if (_cardBackInstance != null)
            _cardBackInstance.SetActive(true);
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
        _isPeeked = false;

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
                _cardBackFittedScale = _cardBackInstance.transform.localScale;

                foreach (var r in _cardBackInstance.GetComponentsInChildren<Renderer>())
                    r.sortingOrder = 1;
            }

            if (_cardBackInstance != null)
            {
                _cardBackInstance.transform.localScale = _cardBackFittedScale;
                _cardBackInstance.SetActive(true);
            }
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
        _isPeeked = false;
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
