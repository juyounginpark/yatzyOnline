using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  메인 턴 관리
//  - 플레이어 / 상대 턴 전환
//  - 제한 시간 초과 시 자동 턴넘김
//  - 턴 종료 시 슬롯 카드가 상대 스폰으로 날아가 타격
// ─────────────────────────────────────────────
public class MainFlow : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public Deck deck;
    public OppDeck oppDeck;
    public Slot[] playerSlots;

    [Header("─ UI ─")]
    public Button endTurnButton;

    [Header("─ 턴 설정 ─")]
    public float turnTime = 30f;

    [Header("─ 공격 애니메이션 ─")]
    [Tooltip("카드가 날아가는 시간")]
    public float attackDuration = 0.4f;

    [Tooltip("카드 간 발사 딜레이")]
    public float attackStagger = 0.06f;

    // ─── 상태 ───
    private bool _isPlayerTurn = true;
    private float _timer;
    private bool _isTransitioning;

    public bool IsPlayerTurn => _isPlayerTurn;
    public float TimeRemaining => Mathf.Max(0f, _timer);
    public bool IsTransitioning => _isTransitioning;

    void Start()
    {
        _timer = turnTime;
        _isPlayerTurn = true;

        if (endTurnButton != null)
            endTurnButton.onClick.AddListener(EndTurn);

        UpdateInteraction();
    }

    void Update()
    {
        if (_isTransitioning) return;

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
            EndTurn();
    }

    // ─────────────────────────────────────────
    //  턴 넘기기 (버튼 onClick에 연결)
    // ─────────────────────────────────────────
    public void EndTurn()
    {
        if (_isTransitioning) return;
        StartCoroutine(DoEndTurn());
    }

    // ─────────────────────────────────────────
    //  턴 전환 시퀀스
    // ─────────────────────────────────────────
    private IEnumerator DoEndTurn()
    {
        _isTransitioning = true;

        // 타격 목표: 상대의 스폰 위치
        Transform target = _isPlayerTurn
            ? (oppDeck.deckSpawnPoint != null ? oppDeck.deckSpawnPoint : oppDeck.transform)
            : (deck.deckSpawnPoint != null ? deck.deckSpawnPoint : deck.transform);

        // 슬롯에서 카드 수거
        List<GameObject> flyingCards = new List<GameObject>();

        if (_isPlayerTurn)
        {
            foreach (var slot in playerSlots)
            {
                if (slot == null || !slot.HasCard) continue;
                var card = slot.ReleaseCard();
                if (card != null)
                {
                    // 날아가는 동안 최상위에 표시
                    foreach (var r in card.GetComponentsInChildren<Renderer>())
                        r.sortingOrder = 500;
                    flyingCards.Add(card);
                }
            }
        }

        // 카드 날리기
        if (flyingCards.Count > 0)
            yield return StartCoroutine(FlyAndHit(flyingCards, target.position));

        // 턴 전환
        _isPlayerTurn = !_isPlayerTurn;
        _timer = turnTime;

        UpdateInteraction();

        // 새 턴: 카드 1장 추가
        if (_isPlayerTurn)
            deck.AddOneCard();
        else
            oppDeck.AddOneCard();

        _isTransitioning = false;
    }

    // ─────────────────────────────────────────
    //  상호작용 제어
    // ─────────────────────────────────────────
    private void UpdateInteraction()
    {
        // 플레이어 턴에만 슬롯 배치 허용
        if (deck != null)
            deck.canPlaceInSlot = _isPlayerTurn;

        if (endTurnButton != null)
            endTurnButton.interactable = _isPlayerTurn;
    }

    // ─────────────────────────────────────────
    //  카드 날리기 + 타격 연출
    // ─────────────────────────────────────────
    private IEnumerator FlyAndHit(List<GameObject> cards, Vector3 targetWorld)
    {
        // 각 카드를 순차적으로 발사
        List<Coroutine> flights = new List<Coroutine>();
        for (int i = 0; i < cards.Count; i++)
        {
            flights.Add(StartCoroutine(FlyOneCard(cards[i], targetWorld)));
            if (i < cards.Count - 1)
                yield return new WaitForSeconds(attackStagger);
        }

        // 마지막 카드 도착 대기
        yield return flights[flights.Count - 1];
    }

    private IEnumerator FlyOneCard(GameObject card, Vector3 targetWorld)
    {
        Vector3 startPos = card.transform.position;
        Vector3 startScale = card.transform.localScale;

        // 모든 SpriteRenderer 수집
        var renderers = card.GetComponentsInChildren<SpriteRenderer>();
        Color[] startColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            startColors[i] = renderers[i].color;

        float elapsed = 0f;

        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / attackDuration);

            // ease-in (가속)
            float eased = t * t;

            card.transform.position = Vector3.Lerp(startPos, targetWorld, eased);
            card.transform.rotation = Quaternion.Slerp(card.transform.rotation, Quaternion.identity, t);
            card.transform.localScale = Vector3.Lerp(startScale, startScale * 0.3f, eased);

            // 페이드 아웃: 후반부(40%~100%)에서 자연스럽게
            float fadeT = Mathf.Clamp01((t - 0.4f) / 0.6f);
            float alpha = 1f - fadeT;
            for (int i = 0; i < renderers.Length; i++)
            {
                Color c = startColors[i];
                c.a = startColors[i].a * alpha;
                renderers[i].color = c;
            }

            yield return null;
        }

        Destroy(card);
    }
}
