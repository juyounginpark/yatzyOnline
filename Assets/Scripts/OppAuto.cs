using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  상대 AI 자동 플레이
//  - 상대 턴 감지 → 랜덤 딜레이 → 그리디 최적 배치 → 턴 종료
// ─────────────────────────────────────────────
public class OppAuto : MonoBehaviour
{
    [Header("─ 참조 ─")]
    public MainFlow mainFlow;
    public OppDeck oppDeck;
    public GameFlow gameFlow;

    [Header("─ 배치 애니메이션 ─")]
    public float placeDuration = 0.4f;

    [Header("─ 카드 공개 애니메이션 ─")]
    [Tooltip("Y축 플립 시간 (절반은 닫기, 절반은 열기)")]
    public float flipDuration = 0.5f;

    [Tooltip("카드 간 플립 딜레이")]
    public float flipStagger = 0.1f;

    // ─── 내부 상태 ───
    private bool _acting;

    // MainFlow.oppSlots를 공유 참조
    private Slot[] oppSlots => mainFlow != null ? mainFlow.oppSlots : null;

    void Update()
    {
        if (_acting) return;
        if (mainFlow == null) return;
        if (mainFlow.IsPlayerTurn || mainFlow.IsTransitioning) return;

        _acting = true;
        StartCoroutine(DoOpponentTurn());
    }

    // ─────────────────────────────────────────
    //  상대 턴 메인 코루틴
    // ─────────────────────────────────────────
    private IEnumerator DoOpponentTurn()
    {
        // 랜덤 대기 (2초 ~ turnTime)
        float delay = Random.Range(2f, mainFlow.turnTime);
        yield return new WaitForSeconds(delay);

        // 대기 중 턴이 바뀌었으면 중단
        if (mainFlow.IsPlayerTurn || mainFlow.IsTransitioning)
        {
            _acting = false;
            yield break;
        }

        // ── 1단계: 뒷면 상태로 전부 배치 ──
        List<GameObject> placedCards = new List<GameObject>();

        while (oppSlots != null)
        {
            if (mainFlow.IsPlayerTurn || mainFlow.IsTransitioning) break;

            var best = FindBestPlacement();
            if (best == null) break;

            oppDeck.RemoveCard(best.Value.card);

            // 배치 애니메이션 (뒷면 유지)
            yield return StartCoroutine(AnimatePlace(best.Value.card, best.Value.slot));

            // 슬롯에 배치 (뒷면 그대로)
            best.Value.slot.PlaceCard(best.Value.card);
            placedCards.Add(best.Value.card);

            // 연속 배치 사이 짧은 대기
            yield return new WaitForSeconds(0.3f);
        }

        // ── 2단계: Y축 플립으로 앞면 공개 ──
        if (placedCards.Count > 0)
        {
            yield return new WaitForSeconds(0.3f);
            yield return StartCoroutine(RevealAllCards(placedCards));
        }

        // 플립 완료 후 짧은 딜레이 → 턴 종료 (중앙으로 이동)
        yield return new WaitForSeconds(0.2f);

        if (!mainFlow.IsPlayerTurn && !mainFlow.IsTransitioning)
            mainFlow.EndTurn();

        _acting = false;
    }

    // ─────────────────────────────────────────
    //  그리디 최적 배치 탐색
    // ─────────────────────────────────────────
    private struct Placement
    {
        public GameObject card;
        public Slot slot;
        public float score;
    }

    private Placement? FindBestPlacement()
    {
        if (gameFlow == null || oppDeck == null) return null;

        var hand = oppDeck.SpawnedCards;
        if (hand.Count == 0) return null;

        // 현재 슬롯 점수
        int[] currentValues = GetSlotValues();
        List<int> filled = new List<int>();
        for (int i = 0; i < currentValues.Length; i++)
            if (currentValues[i] > 0) filled.Add(currentValues[i]);

        string dummyRule;
        float currentScore = filled.Count > 0
            ? gameFlow.EvaluateHand(filled.ToArray(), out dummyRule)
            : 0f;

        Placement? best = null;

        for (int c = 0; c < hand.Count; c++)
        {
            if (hand[c] == null) continue;
            var cv = hand[c].GetComponent<CardValue>();
            if (cv == null) continue;
            int cardValue = cv.value;

            for (int s = 0; s < oppSlots.Length; s++)
            {
                if (oppSlots[s] == null || oppSlots[s].HasCard) continue;

                // 시뮬레이션: 이 카드를 이 슬롯에 넣으면?
                int[] simValues = (int[])currentValues.Clone();
                simValues[s] = cardValue;

                List<int> simFilled = new List<int>();
                for (int i = 0; i < simValues.Length; i++)
                    if (simValues[i] > 0) simFilled.Add(simValues[i]);

                float simScore = gameFlow.EvaluateHand(simFilled.ToArray(), out dummyRule);

                if (simScore > currentScore && (best == null || simScore > best.Value.score))
                {
                    best = new Placement
                    {
                        card = hand[c],
                        slot = oppSlots[s],
                        score = simScore
                    };
                }
            }
        }

        return best;
    }

    // ─────────────────────────────────────────
    //  현재 상대 슬롯 값 배열
    // ─────────────────────────────────────────
    private int[] GetSlotValues()
    {
        int[] values = new int[oppSlots.Length];
        for (int i = 0; i < oppSlots.Length; i++)
        {
            if (oppSlots[i] != null && oppSlots[i].HasCard)
            {
                var cv = oppSlots[i].GetCardValue();
                values[i] = cv != null ? cv.value : 0;
            }
        }
        return values;
    }

    // ─────────────────────────────────────────
    //  배치 애니메이션: 덱 → 슬롯으로 날아감
    // ─────────────────────────────────────────
    private IEnumerator AnimatePlace(GameObject card, Slot targetSlot)
    {
        // 덱에서 분리
        card.transform.SetParent(null);

        // 최상위 표시
        var renderers = card.GetComponentsInChildren<SpriteRenderer>();
        foreach (var r in renderers)
            r.sortingOrder = 500;

        Vector3 startPos = card.transform.position;
        Quaternion startRot = card.transform.rotation;
        Vector3 endPos = targetSlot.transform.position;

        float elapsed = 0f;
        while (elapsed < placeDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / placeDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            card.transform.position = Vector3.Lerp(startPos, endPos, eased);
            card.transform.rotation = Quaternion.Slerp(startRot, Quaternion.identity, eased);

            yield return null;
        }

        card.transform.position = endPos;
        card.transform.rotation = Quaternion.identity;
    }

    // ─────────────────────────────────────────
    //  Y축 플립 공개: 전체 카드 순차 플립
    // ─────────────────────────────────────────
    private IEnumerator RevealAllCards(List<GameObject> cards)
    {
        List<Coroutine> flips = new List<Coroutine>();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            flips.Add(StartCoroutine(FlipOneCard(cards[i])));
            if (i < cards.Count - 1)
                yield return new WaitForSeconds(flipStagger);
        }

        // 마지막 플립 완료 대기
        if (flips.Count > 0)
            yield return flips[flips.Count - 1];
    }

    // ─────────────────────────────────────────
    //  Y축 플립: scale.x로 뒤집기 연출
    //  1→0 (닫기) → 스프라이트 교체 → 0→1 (열기)
    // ─────────────────────────────────────────
    private IEnumerator FlipOneCard(GameObject card)
    {
        float halfDuration = flipDuration * 0.5f;
        Vector3 originalScale = card.transform.localScale;

        // ── 닫기: scale.x → 0 ──
        float elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            float eased = t * t; // ease-in

            Vector3 s = originalScale;
            s.x = Mathf.Lerp(originalScale.x, 0f, eased);
            card.transform.localScale = s;

            yield return null;
        }

        // ── 스프라이트 교체 (카드가 옆면이라 안 보이는 순간) ──
        SwapToFaceSprite(card);

        // ── 열기: scale.x 0 → 원래 ──
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / halfDuration);
            float eased = 1f - (1f - t) * (1f - t); // ease-out

            Vector3 s = originalScale;
            s.x = Mathf.Lerp(0f, originalScale.x, eased);
            card.transform.localScale = s;

            yield return null;
        }

        // 최종 보정
        card.transform.localScale = originalScale;
    }

    // ─────────────────────────────────────────
    //  스프라이트 교체: 뒷면 → 해당 숫자 카드
    // ─────────────────────────────────────────
    private void SwapToFaceSprite(GameObject card)
    {
        var cv = card.GetComponent<CardValue>();
        if (cv == null || oppDeck == null || oppDeck.deck == null) return;

        int value = cv.value;
        if (value < 1 || value > 6) return;

        var groups = oppDeck.deck.deckGroups;
        if (groups == null || groups.Length == 0) return;

        var cards = groups[0].cards;
        if (cards == null || value - 1 >= cards.Length) return;

        var prefab = cards[value - 1].prefab;
        if (prefab == null) return;

        var prefabSr = prefab.GetComponent<SpriteRenderer>();
        if (prefabSr == null || prefabSr.sprite == null) return;

        var cardSr = card.GetComponent<SpriteRenderer>();
        if (cardSr != null)
            cardSr.sprite = prefabSr.sprite;
    }
}
