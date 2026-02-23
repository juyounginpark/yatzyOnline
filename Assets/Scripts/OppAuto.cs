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
    private bool _animating;   // 카드 배치/플립 애니메이션 중

    // MainFlow에서 AI 활동 상태 확인용
    public bool IsActing => _acting;
    public bool IsAnimating => _animating;

    // 현재 AnimatePlace 중인 카드 (LateUpdate 회전 차단용)
    private readonly HashSet<GameObject> _placingCards = new HashSet<GameObject>();

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

        // 대기 중 이미 플레이어 턴이면 중단
        if (mainFlow.IsPlayerTurn)
        {
            _acting = false;
            yield break;
        }

        // ── 배치할 카드 결정 ──
        List<Placement> placements = FindComboPlacement();

        // ── 1단계: 뒷면 상태로 배치 ──
        List<GameObject> placedCards = new List<GameObject>();

        _animating = true;  // 카드 애니메이션 시작 → 타이머 일시정지

        for (int i = 0; i < placements.Count; i++)
        {
            if (mainFlow.IsPlayerTurn) break;

            var p = placements[i];
            oppDeck.RemoveCard(p.card);

            // 배치 애니메이션 (뒷면 유지, 끝까지 실행)
            yield return StartCoroutine(AnimatePlace(p.card, p.slot));
            placedCards.Add(p.card);

            // 연속 배치 사이 짧은 대기
            if (i < placements.Count - 1)
                yield return new WaitForSeconds(0.3f);
        }

        // ── 2단계: Y축 플립으로 앞면 공개 (카드가 다 뒤집어지기 전까지 턴 안 넘김) ──
        if (placedCards.Count > 0)
        {
            yield return new WaitForSeconds(0.3f);
            yield return StartCoroutine(RevealAllCards(placedCards));
        }

        _animating = false;  // 카드 애니메이션 종료 → 타이머 재개

        // 플립 완료 후 짧은 딜레이 → 턴 종료
        yield return new WaitForSeconds(0.2f);

        if (!mainFlow.IsPlayerTurn && !mainFlow.IsTransitioning)
            mainFlow.EndTurn();

        _acting = false;
    }

    // ─────────────────────────────────────────
    //  콤보 배치 탐색
    //  콤보 배치 탐색 (전수 탐색)
    //  1) 핸드 카드의 모든 부분집합 × 빈 슬롯 순열을 시뮬레이션
    //  2) 콤보(원페어 이상)를 이루는 최고 점수 배치를 채택
    //  3) 콤보가 없으면 가장 높은 숫자 카드 1장만 배치
    // ─────────────────────────────────────────
    private struct Placement
    {
        public GameObject card;
        public Slot slot;
        public float score;
    }

    private List<Placement> FindComboPlacement()
    {
        List<Placement> result = new List<Placement>();
        if (gameFlow == null || oppDeck == null) return result;

        var hand = oppDeck.SpawnedCards;
        if (hand.Count == 0) return result;

        // 현재 슬롯 상태
        int[] currentValues = GetSlotValues();

        // 빈 슬롯 인덱스
        List<int> emptySlotIndices = new List<int>();
        for (int s = 0; s < oppSlots.Length; s++)
            if (oppSlots[s] != null && !oppSlots[s].HasCard)
                emptySlotIndices.Add(s);

        if (emptySlotIndices.Count == 0) return result;

        // 핸드 카드 인덱스 & 값 수집
        List<int> handIndices = new List<int>();
        List<int> handValues = new List<int>();
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i] == null) continue;
            var cv = hand[i].GetComponent<CardValue>();
            if (cv == null) continue;
            handIndices.Add(i);
            handValues.Add(cv.value);
        }

        if (handIndices.Count == 0) return result;

        int maxPlace = Mathf.Min(handIndices.Count, emptySlotIndices.Count);

        // ── 전수 탐색: 콤보 등급 최고 → 카드 수 최소 → 점수 최고 ──
        int bestRank = -1;
        int bestCardCount = int.MaxValue;
        float bestScore = -1f;
        string bestRule = "";
        int[] bestCardSel = null;   // 선택된 핸드 인덱스 (handIndices 내 인덱스)
        int[] bestSlotSel = null;   // 선택된 빈 슬롯 인덱스 (emptySlotIndices 내 인덱스)

        // k장 배치 시도 (1장~maxPlace장)
        for (int k = 1; k <= maxPlace; k++)
        {
            // 핸드에서 k장 선택하는 모든 조합
            foreach (var cardCombo in Combinations(handIndices.Count, k))
            {
                // 빈 슬롯에서 k개 선택하는 순열
                foreach (var slotPerm in Permutations(emptySlotIndices.Count, k))
                {
                    // 시뮬레이션
                    int[] sim = (int[])currentValues.Clone();
                    for (int i = 0; i < k; i++)
                    {
                        int slotIdx = emptySlotIndices[slotPerm[i]];
                        int cardVal = handValues[cardCombo[i]];
                        sim[slotIdx] = cardVal;
                    }

                    List<int> filled = new List<int>();
                    for (int i = 0; i < sim.Length; i++)
                        if (sim[i] > 0) filled.Add(sim[i]);

                    string rule;
                    float score = gameFlow.EvaluateHand(filled.ToArray(), out rule);

                    // 콤보(원페어 이상)만 채택
                    if (rule == "하이카드" || rule == "") continue;

                    int rank = ComboRank(rule);

                    // 비교: 등급 높을수록 → 카드 적을수록 → 점수 높을수록
                    bool isBetter = false;
                    if (rank > bestRank)
                        isBetter = true;
                    else if (rank == bestRank && k < bestCardCount)
                        isBetter = true;
                    else if (rank == bestRank && k == bestCardCount && score > bestScore)
                        isBetter = true;

                    if (isBetter)
                    {
                        bestRank = rank;
                        bestCardCount = k;
                        bestScore = score;
                        bestRule = rule;
                        bestCardSel = new int[k];
                        bestSlotSel = new int[k];
                        System.Array.Copy(cardCombo, bestCardSel, k);
                        System.Array.Copy(slotPerm, bestSlotSel, k);
                    }
                }
            }
        }

        // ── 콤보 찾았으면 해당 카드만 배치 ──
        if (bestCardSel != null)
        {
            for (int i = 0; i < bestCardSel.Length; i++)
            {
                int hi = handIndices[bestCardSel[i]];
                int si = emptySlotIndices[bestSlotSel[i]];
                result.Add(new Placement
                {
                    card = hand[hi],
                    slot = oppSlots[si],
                    score = bestScore
                });
            }
            return result;
        }

        // ── 콤보 없으면: 가장 높은 숫자 카드 1장만 ──
        int highestIdx = 0;
        for (int i = 1; i < handValues.Count; i++)
            if (handValues[i] > handValues[highestIdx]) highestIdx = i;

        result.Add(new Placement
        {
            card = hand[handIndices[highestIdx]],
            slot = oppSlots[emptySlotIndices[0]],
            score = handValues[highestIdx]
        });

        return result;
    }

    // ── 콤보 등급 (높을수록 강함) ──
    private static int ComboRank(string rule)
    {
        switch (rule)
        {
            case "파이브카드":       return 9;
            case "포카드":          return 8;
            case "풀하우스":        return 7;
            case "스트레이트(하이)": return 6;
            case "스트레이트(로우)": return 5;
            case "스몰스트레이트":   return 4;
            case "트리플":          return 3;
            case "투페어":          return 2;
            case "원페어":          return 1;
            default:                return 0;
        }
    }

    // ── 조합(Combination) 생성: n개 중 k개 선택 ──
    private static List<int[]> Combinations(int n, int k)
    {
        var results = new List<int[]>();
        int[] combo = new int[k];
        GenerateCombinations(results, combo, 0, 0, n, k);
        return results;
    }

    private static void GenerateCombinations(List<int[]> results, int[] combo, int start, int idx, int n, int k)
    {
        if (idx == k)
        {
            results.Add((int[])combo.Clone());
            return;
        }
        for (int i = start; i < n; i++)
        {
            combo[idx] = i;
            GenerateCombinations(results, combo, i + 1, idx + 1, n, k);
        }
    }

    // ── 순열(Permutation) 생성: n개 중 k개 순서 있게 선택 ──
    private static List<int[]> Permutations(int n, int k)
    {
        var results = new List<int[]>();
        int[] perm = new int[k];
        bool[] used = new bool[n];
        GeneratePermutations(results, perm, used, 0, n, k);
        return results;
    }

    private static void GeneratePermutations(List<int[]> results, int[] perm, bool[] used, int idx, int n, int k)
    {
        if (idx == k)
        {
            results.Add((int[])perm.Clone());
            return;
        }
        for (int i = 0; i < n; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            perm[idx] = i;
            GeneratePermutations(results, perm, used, idx + 1, n, k);
            used[i] = false;
        }
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
    //  [버그 수정]
    //  1) IsTransitioning 감지 시 즉시 중단, 슬롯에 배치하지 않음
    //  2) _placingCards에 등록하여 OppDeck.LateUpdate 회전 차단
    //  3) 콜백으로 성공 여부 반환
    // ─────────────────────────────────────────
    private IEnumerator AnimatePlace(GameObject card, Slot targetSlot)
    {
        // 덱에서 분리
        card.transform.SetParent(null);

        // 배치 중임을 표시 (LateUpdate 회전 차단)
        _placingCards.Add(card);

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

        _placingCards.Remove(card);

        // 정상 완료: 슬롯에 배치
        card.transform.position = endPos;
        card.transform.rotation = Quaternion.identity;

        targetSlot.PlaceCard(card);
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

        // 카드 타입에 맞는 그룹 찾기
        DeckGroup matchedGroup = null;
        foreach (var g in groups)
        {
            if (g != null && g.groupType == cv.cardType)
            {
                matchedGroup = g;
                break;
            }
        }
        if (matchedGroup == null) matchedGroup = groups[0];

        var cards = matchedGroup.cards;
        if (cards == null || value - 1 >= cards.Length) return;

        var prefab = cards[value - 1].prefab;
        if (prefab == null) return;

        var prefabSr = prefab.GetComponent<SpriteRenderer>();
        if (prefabSr == null || prefabSr.sprite == null) return;

        var cardSr = card.GetComponent<SpriteRenderer>();
        if (cardSr != null)
            cardSr.sprite = prefabSr.sprite;
    }

    // ─────────────────────────────────────────
    //  배치 중인 카드 집합을 외부(OppDeck)에 노출
    // ─────────────────────────────────────────
    public bool IsCardPlacing(GameObject card) => _placingCards.Contains(card);
}
