using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif

// ─────────────────────────────────────────────
//  족보(콤보) 도감
//  - 스크롤 뷰 Content 안에 콤보마다 Page 프리팹을 하나씩 생성
//  - 각 Page: Card 1~5(이미지), 조합이름, 점수
//  - 카드 숫자(1~6)에 대응하는 스프라이트는 cardSprites에 할당
// ─────────────────────────────────────────────
public class Book : MonoBehaviour
{
    [Header("─ 스크롤 뷰 ─")]
    [Tooltip("Page 프리팹 (Card 1~5 / 조합이름 / 점수 포함)")]
    public GameObject pagePrefab;

    [Tooltip("Scroll View의 Content (여기에 콤보 Page들이 생성됨)")]
    public Transform contentParent;

    [Tooltip("생성 전 Content의 기존 자식을 모두 제거")]
    public bool clearExisting = true;

    [Header("─ 배치 (Content에 Layout Group 없이 코드로 정렬) ─")]
    [Tooltip("세로로 나열 (false면 가로)")]
    public bool vertical = true;

    [Tooltip("각 Page 크기 (0이면 프리팹의 현재 크기 유지)")]
    public Vector2 pageSize = Vector2.zero;

    [Tooltip("Page 사이 간격")]
    public float spacing = 20f;

    [Tooltip("시작 여백 (x: 왼쪽/가로 시작, y: 위쪽)")]
    public Vector2 padding = Vector2.zero;

    [Tooltip("배치에 맞춰 Content 크기를 자동 조정 (스크롤 가능하도록)")]
    public bool resizeContent = true;

    [Header("─ 카드 스프라이트 (숫자 1~6, 인덱스6=조커) ─")]
    [Tooltip("인덱스 0=숫자1 ... 5=숫자6, 6=조커 (cardValues에서 7=조커)")]
    public Sprite[] cardSprites = new Sprite[7];

    [Header("─ 콤보 기여 카드 이펙트 ─")]
    [Tooltip("조합에 기여하는 카드 위에 오버레이할 이펙트 스프라이트 (GameFlow의 effectContribute에 해당)")]
    public Sprite effectContribute;

    [Header("─ Page 프리팹 자식 이름 ─")]
    public string cardChildPrefix = "Card ";   // "Card 1" ~ "Card 5"
    public int    cardSlotCount   = 5;
    public string comboNameChild  = "조합이름";
    public string scoreChild      = "점수";

    [Header("─ 점수 표시 형식 ({0} = 점수) ─")]
    public string scoreFormat = "{0}";

    private int _overlaysCreated;  // Build 시 생성된 이펙트 오버레이 수 (진단용)

    private struct DisplayCard { public int value; public bool isCombo; }

    // ── 콤보 정의 ──
    [System.Serializable]
    public class ComboEntry
    {
        public string comboName;
        [Tooltip("표시할 카드 숫자(1~6, 7=조커), 최대 5장")]
        public int[]  cardValues;
        [Tooltip("조합을 이루는(이펙트를 덮을) 카드 인덱스 0~4")]
        public int[]  comboIndices;
        public string score;
    }

    [Header("─ 콤보 목록 (낮은 점수 → 높은 점수, 오름차순) ─")]
    public List<ComboEntry> combos = new List<ComboEntry>
    {
        new ComboEntry { comboName = "하이카드",        cardValues = new[]{6,4,3,2,1}, comboIndices = new[]{0},           score = "12" },
        new ComboEntry { comboName = "원페어",          cardValues = new[]{3,3,6,2,1}, comboIndices = new[]{0,1},         score = "15" },
        new ComboEntry { comboName = "투페어",          cardValues = new[]{4,4,2,2,6}, comboIndices = new[]{0,1,2,3},     score = "28" },
        new ComboEntry { comboName = "트리플",          cardValues = new[]{5,5,5,2,1}, comboIndices = new[]{0,1,2},       score = "45" },
        new ComboEntry { comboName = "스몰스트레이트",   cardValues = new[]{1,2,3,4,6}, comboIndices = new[]{0,1,2,3},     score = "58" },
        new ComboEntry { comboName = "스트레이트(로우)", cardValues = new[]{1,2,3,4,5}, comboIndices = new[]{0,1,2,3,4},   score = "65" },
        new ComboEntry { comboName = "풀하우스",        cardValues = new[]{4,4,4,3,3}, comboIndices = new[]{0,1,2,3,4},   score = "65" },
        new ComboEntry { comboName = "스트레이트(하이)", cardValues = new[]{2,3,4,5,6}, comboIndices = new[]{0,1,2,3,4},   score = "70" },
        new ComboEntry { comboName = "포카드",          cardValues = new[]{6,6,6,6,2}, comboIndices = new[]{0,1,2,3},     score = "80" },
        new ComboEntry { comboName = "파이브카드",      cardValues = new[]{5,5,5,5,5}, comboIndices = new[]{0,1,2,3,4},   score = "95" },
        new ComboEntry { comboName = "???",            cardValues = new[]{7,7,7,7,7}, comboIndices = new[]{0,1,2,3,4},   score = "???" },
    };

    // ─────────────────────────────────────────
    //  콤보 Page들을 Content에 생성
    //  - 컴포넌트 우클릭 → "Build Pages" (편집 모드에서 실행, 씬에 저장됨)
    //  - 런타임에 자동 생성하지 않음 (게임 시작마다 다시 만들지 않음)
    // ─────────────────────────────────────────
    [ContextMenu("Build Pages")]
    public void Build()
    {
        if (pagePrefab == null || contentParent == null)
        {
            Debug.LogWarning("[Book] pagePrefab 또는 contentParent가 비어 있습니다.");
            return;
        }

        if (effectContribute == null)
            Debug.LogWarning("[Book] effectContribute가 비어 있어 콤보 이펙트가 표시되지 않습니다. (인스펙터에서 할당)");

        if (clearExisting) ClearPages();

        _overlaysCreated = 0;
        var pages = new List<RectTransform>();

        foreach (var combo in combos)
        {
            if (combo == null) continue;

            var page = InstantiatePage();
            page.SetActive(true);

            // 스케일/회전/z 정규화
            var rt = page.transform as RectTransform;
            if (rt != null)
            {
                rt.localScale    = Vector3.one;
                rt.localRotation = Quaternion.identity;
                rt.localPosition = new Vector3(rt.localPosition.x, rt.localPosition.y, 0f);
                pages.Add(rt);
            }

            FillPage(page.transform, combo);
        }

        LayoutPages(pages);
        Debug.Log($"[Book] Page {pages.Count}개 생성, 콤보 이펙트 오버레이 {_overlaysCreated}개 적용 (effectContribute={(effectContribute != null ? effectContribute.name : "NULL")})");
        MarkDirty();
    }

    // ─────────────────────────────────────────
    //  Page들을 일렬로 배치 + Content 크기 조정 (Layout Group 불필요)
    // ─────────────────────────────────────────
    private void LayoutPages(List<RectTransform> pages)
    {
        if (pages.Count == 0) return;

        // 세로: 위쪽 중앙 기준 / 가로: 왼쪽 위 기준
        Vector2 anchor = vertical ? new Vector2(0.5f, 1f) : new Vector2(0f, 1f);

        // pageSize 미지정 시 기본 크기 = 프리팹의 설계 크기 (Content에 맞춰 늘어나는 것 방지)
        Vector2 prefabSize = Vector2.zero;
        var prefabRt = pagePrefab.transform as RectTransform;
        if (prefabRt != null) prefabSize = prefabRt.rect.size;

        float used = vertical ? padding.y : padding.x;

        foreach (var rt in pages)
        {
            // 크기 고정: pageSize > 0 이면 그 값, 아니면 프리팹 설계 크기
            float w = pageSize.x > 0f ? pageSize.x : prefabSize.x;
            float h = pageSize.y > 0f ? pageSize.y : prefabSize.y;
            if (w <= 0f) w = rt.rect.width;
            if (h <= 0f) h = rt.rect.height;

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot     = anchor;
            rt.sizeDelta = new Vector2(w, h);

            if (vertical)
            {
                rt.anchoredPosition = new Vector2(padding.x, -used);
                used += h + spacing;
            }
            else
            {
                rt.anchoredPosition = new Vector2(used, -padding.y);
                used += w + spacing;
            }
        }

        used -= spacing;  // 마지막 trailing 간격 제거

        // Content 크기 조정 (스크롤 영역 확보)
        if (resizeContent && contentParent is RectTransform crt)
        {
            var sd = crt.sizeDelta;
            if (vertical) sd.y = used;
            else          sd.x = used;
            crt.sizeDelta = sd;
        }
    }

    [ContextMenu("Clear Pages")]
    public void ClearPages()
    {
        if (contentParent == null) return;
        for (int i = contentParent.childCount - 1; i >= 0; i--)
        {
            var child = contentParent.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else                       DestroyImmediate(child);
        }
        MarkDirty();
    }

    // 편집 모드에서는 프리팹 인스턴스로 생성 → 씬에 저장 + 프리팹 연결 유지
    private GameObject InstantiatePage()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (GameObject)PrefabUtility.InstantiatePrefab(pagePrefab, contentParent);
#endif
        return Instantiate(pagePrefab, contentParent, false);
    }

    // 편집 모드에서 변경 사항을 씬에 저장되도록 표시
    private void MarkDirty()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    // ─────────────────────────────────────────
    //  Page 한 장 채우기
    // ─────────────────────────────────────────
    private void FillPage(Transform page, ComboEntry combo)
    {
        // 콤보 기여 카드 인덱스 (지정값 우선, 없으면 cardValues에서 자동 계산)
        var comboSet = GetComboIndices(combo);

        // (카드값, 콤보기여) 목록을 만들고 숫자 오름차순으로 정렬
        var cards = new List<DisplayCard>();
        if (combo.cardValues != null)
            for (int i = 0; i < combo.cardValues.Length; i++)
                if (combo.cardValues[i] > 0)
                    cards.Add(new DisplayCard { value = combo.cardValues[i], isCombo = comboSet.Contains(i) });
        cards.Sort((a, b) => a.value.CompareTo(b.value));

        // 카드 이미지 채우기 (정렬된 순서대로 Card 1..N에 배치)
        for (int i = 0; i < cardSlotCount; i++)
        {
            var slot = page.Find(cardChildPrefix + (i + 1));
            if (slot == null)
            {
                Debug.LogWarning($"[Book] '{cardChildPrefix}{i + 1}' 자식을 Page 프리팹에서 찾지 못했습니다.");
                continue;
            }

            if (i >= cards.Count)
            {
                // 남는 슬롯은 숨김
                slot.gameObject.SetActive(false);
                continue;
            }

            slot.gameObject.SetActive(true);

            // 1) 카드 스프라이트 먼저 적용
            int value = cards[i].value;
            Sprite sprite = GetCardSprite(value);
            if (sprite != null)
                ApplyCardSprite(slot, sprite);
            else
                Debug.LogWarning($"[Book] 콤보 '{combo.comboName}' 카드값 {value} → cardSprites[{value - 1}]가 비어 있습니다. (인스펙터에서 할당하세요)");

            // 2) 콤보를 이루는 카드면 그 위에 이펙트 오버레이
            if (cards[i].isCombo && AddEffectOverlay(slot))
                _overlaysCreated++;
        }

        // 조합 이름
        SetText(page, comboNameChild, combo.comboName);

        // 점수
        SetText(page, scoreChild, string.Format(scoreFormat, combo.score));
    }

    // 콤보 기여 카드 인덱스 집합 — comboIndices 지정 시 그것을, 아니면 cardValues에서 자동 계산
    private HashSet<int> GetComboIndices(ComboEntry combo)
    {
        var set = new HashSet<int>();
        if (combo.comboIndices != null && combo.comboIndices.Length > 0)
        {
            foreach (int ci in combo.comboIndices) set.Add(ci);
            return set;
        }
        foreach (int i in ComputeContributingIndices(combo.cardValues)) set.Add(i);
        return set;
    }

    // cardValues로부터 조합에 기여하는 카드 인덱스 계산 (높은 콤보 우선)
    private List<int> ComputeContributingIndices(int[] values)
    {
        var result = new List<int>();
        if (values == null) return result;

        // 값 → 인덱스 목록
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < values.Length; i++)
        {
            int v = values[i];
            if (v <= 0) continue;
            if (!groups.ContainsKey(v)) groups[v] = new List<int>();
            groups[v].Add(i);
        }

        List<int> five = null, four = null, triple = null;
        var pairs = new List<List<int>>();
        foreach (var kv in groups)
        {
            int c = kv.Value.Count;
            if      (c >= 5) five   = kv.Value;
            else if (c == 4) four   = kv.Value;
            else if (c == 3) triple = kv.Value;
            else if (c == 2) pairs.Add(kv.Value);
        }

        if (five != null) return new List<int>(five);          // 파이브카드
        if (four != null) return new List<int>(four);          // 포카드

        if (triple != null && pairs.Count >= 1)                // 풀하우스
        {
            result.AddRange(triple);
            result.AddRange(pairs[0]);
            return result;
        }

        var straight = FindStraightIndices(groups);            // 스트레이트/스몰스트레이트
        if (straight != null) return straight;

        if (triple != null) return new List<int>(triple);      // 트리플

        if (pairs.Count >= 2)                                  // 투페어
        {
            result.AddRange(pairs[0]);
            result.AddRange(pairs[1]);
            return result;
        }

        if (pairs.Count == 1) return new List<int>(pairs[0]);  // 원페어

        // 하이카드: 가장 높은 값 1장
        int bestIdx = -1, bestVal = -1;
        for (int i = 0; i < values.Length; i++)
            if (values[i] > bestVal) { bestVal = values[i]; bestIdx = i; }
        if (bestIdx >= 0) result.Add(bestIdx);
        return result;
    }

    // 연속하는 distinct 값 최장 구간이 4 이상이면 그 카드 인덱스 반환
    private List<int> FindStraightIndices(Dictionary<int, List<int>> groups)
    {
        var vals = new List<int>(groups.Keys);
        vals.Sort();

        int bestStart = 0, bestLen = 1, curLen = 1;
        for (int i = 1; i < vals.Count; i++)
        {
            if (vals[i] == vals[i - 1] + 1) curLen++;
            else                            curLen = 1;
            if (curLen > bestLen) { bestLen = curLen; bestStart = i - curLen + 1; }
        }

        if (bestLen >= 4)
        {
            var res = new List<int>();
            for (int i = bestStart; i < bestStart + bestLen && i < vals.Count; i++)
                res.Add(groups[vals[i]][0]);
            return res;
        }
        return null;
    }

    // 카드 슬롯 위에 이펙트 스프라이트를 꽉 차게 오버레이 (UI Image, 카드보다 위에 렌더링)
    private bool AddEffectOverlay(Transform slot)
    {
        if (effectContribute == null) return false;

        var go = new GameObject("ComboEffect", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(slot, false);
        rt.anchorMin     = Vector2.zero;
        rt.anchorMax     = Vector2.one;
        rt.offsetMin     = Vector2.zero;
        rt.offsetMax     = Vector2.zero;
        rt.localScale    = Vector3.one;
        rt.localRotation = Quaternion.identity;

        var img = go.AddComponent<Image>();
        img.sprite        = effectContribute;
        img.raycastTarget = false;
        img.color         = Color.white;

        rt.SetAsLastSibling();  // 카드 위에 표시
        return true;
    }

    // 카드 슬롯에 스프라이트 적용 — Image면 sprite, RawImage면 texture로 (비활성 자식까지 탐색)
    private void ApplyCardSprite(Transform slot, Sprite sprite)
    {
        var img = slot.GetComponent<Image>();
        if (img == null) img = slot.GetComponentInChildren<Image>(true);
        if (img != null)
        {
            img.sprite  = sprite;
            img.enabled = true;
            return;
        }

        var raw = slot.GetComponent<RawImage>();
        if (raw == null) raw = slot.GetComponentInChildren<RawImage>(true);
        if (raw != null)
        {
            raw.texture = sprite.texture;
            raw.enabled = true;
            return;
        }

        Debug.LogWarning($"[Book] '{slot.name}'에 Image/RawImage 컴포넌트가 없어 스프라이트를 적용하지 못했습니다.");
    }

    private Sprite GetCardSprite(int value)
    {
        int idx = value - 1;
        if (cardSprites == null || idx < 0 || idx >= cardSprites.Length) return null;
        return cardSprites[idx];
    }

    private void SetText(Transform page, string childName, string text)
    {
        var t = page.Find(childName);
        if (t == null) return;

        var tmp = t.GetComponent<TMP_Text>();
        if (tmp == null) tmp = t.GetComponentInChildren<TMP_Text>();
        if (tmp != null) tmp.text = text;
    }
}
