using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  턴 전환 애니메이션 유틸리티
//  MainFlow에서 분리된 모든 시각 연출 코루틴
//
//  MonoBehaviour가 아닌 일반 클래스 —
//  생성 시 host를 받아 StartCoroutine 위임
// ─────────────────────────────────────────────
public class TurnAnimator
{
    private readonly MonoBehaviour _host;

    // ── 설정값 (MainFlow SerializedField에서 복사) ──
    public float attackDuration       = 0.4f;
    public float attackStagger        = 0.06f;
    public float hitShakeDuration     = 0.35f;
    public float hitShakeIntensity    = 0.15f;
    public float cameraShakeIntensity = 0.08f;
    public float showcaseMoveDuration = 0.5f;

    public TurnAnimator(MonoBehaviour host)
    {
        _host = host;
    }

    // ─────────────────────────────────────────
    //  카드를 한 점으로 모으기
    // ─────────────────────────────────────────
    public IEnumerator GatherToPoint(List<GameObject> cards, Vector3 point)
    {
        int count = cards.Count;
        Vector3[] starts = new Vector3[count];
        for (int i = 0; i < count; i++)
            starts[i] = cards[i] != null ? cards[i].transform.position : point;

        float elapsed = 0f;
        while (elapsed < showcaseMoveDuration)
        {
            elapsed += Time.deltaTime;
            float eased = Smoothstep(elapsed / showcaseMoveDuration);

            for (int i = 0; i < count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.position = Vector3.Lerp(starts[i], point, eased);
            }
            yield return null;
        }

        for (int i = 0; i < count; i++)
            if (cards[i] != null)
                cards[i].transform.position = point;
    }

    // ─────────────────────────────────────────
    //  카드 날리기: 회전 → 순차 발사 + 타격
    // ─────────────────────────────────────────
    public IEnumerator FlyAndHit(List<GameObject> cards, Vector3 targetWorld)
    {
        // 1) 45도 회전
        float rotateDuration = 0.3f;
        Quaternion[] startRots  = new Quaternion[cards.Count];
        Quaternion[] targetRots = new Quaternion[cards.Count];

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            startRots[i]  = cards[i].transform.rotation;
            targetRots[i] = startRots[i] * Quaternion.Euler(0f, 0f, 45f);
        }

        float elapsed = 0f;
        while (elapsed < rotateDuration)
        {
            elapsed += Time.deltaTime;
            float eased = Smoothstep(elapsed / rotateDuration);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.rotation =
                    Quaternion.Slerp(startRots[i], targetRots[i], eased);
            }
            yield return null;
        }

        // 2) 순차 발사
        List<Coroutine> flights = new List<Coroutine>();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            flights.Add(_host.StartCoroutine(FlyOneCard(cards[i], targetWorld)));
            if (i < cards.Count - 1)
                yield return new WaitForSeconds(attackStagger);
        }

        // 마지막 카드 도착 대기
        if (flights.Count > 0)
            yield return flights[flights.Count - 1];
    }

    private IEnumerator FlyOneCard(GameObject card, Vector3 targetWorld)
    {
        Vector3 startPos   = card.transform.position;
        Vector3 startScale = card.transform.localScale;
        Quaternion startRot = card.transform.rotation;

        var renderers = card.GetComponentsInChildren<SpriteRenderer>();
        Color[] startColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            startColors[i] = renderers[i].color;

        float elapsed = 0f;
        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / attackDuration);
            float eased = t * t; // ease-in

            card.transform.position   = Vector3.Lerp(startPos, targetWorld, eased);
            card.transform.rotation   = startRot;
            card.transform.localScale = Vector3.Lerp(startScale, startScale * 0.3f, eased);

            // 후반 40%~100% 페이드 아웃
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

        Object.Destroy(card);
    }

    // ─────────────────────────────────────────
    //  피격 연출: 대상 흔들림 + 빨간 번쩍임
    // ─────────────────────────────────────────
    public IEnumerator ShakeTransform(Transform target, float duration, float intensity)
    {
        Vector3 originalPos = target.localPosition;
        SpriteRenderer sr = target.GetComponentInChildren<SpriteRenderer>();
        Color originalColor = sr != null ? sr.color : Color.white;
        Color redTint = new Color(1f, 0.3f, 0.3f, 1f);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration); // 감쇠
            float offsetX = Random.Range(-1f, 1f) * intensity * t;
            float offsetY = Random.Range(-1f, 1f) * intensity * t;
            target.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);

            if (sr != null)
            {
                float flash = Mathf.PingPong(elapsed * 15f, 1f) * t;
                sr.color = Color.Lerp(originalColor, redTint, flash);
            }

            yield return null;
        }

        target.localPosition = originalPos;
        if (sr != null) sr.color = originalColor;
    }

    // ─────────────────────────────────────────
    //  피격 연출: 카메라 흔들림
    // ─────────────────────────────────────────
    public IEnumerator ShakeCamera(float duration, float intensity)
    {
        Camera cam = Camera.main;
        if (cam == null) yield break;

        Vector3 originalPos = cam.transform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = 1f - (elapsed / duration);
            float offsetX = Random.Range(-1f, 1f) * intensity * t;
            float offsetY = Random.Range(-1f, 1f) * intensity * t;
            cam.transform.localPosition = originalPos + new Vector3(offsetX, offsetY, 0f);
            yield return null;
        }

        cam.transform.localPosition = originalPos;
    }

    // ─────────────────────────────────────────
    //  힐 연출: 위→아래 초록빛 웨이브
    // ─────────────────────────────────────────
    public IEnumerator HealGreenWave(IReadOnlyList<GameObject> cards, float healAmount)
    {
        if (cards == null || cards.Count == 0) yield break;

        float intensity = Mathf.Clamp01(healAmount / 30f);
        float duration  = 0.4f + intensity * 0.3f;

        var renderers     = new List<List<SpriteRenderer>>();
        var originalColors = new List<List<Color>>();
        var cardBounds    = new List<float>();

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null)
            {
                renderers.Add(null); originalColors.Add(null); cardBounds.Add(0f);
                continue;
            }
            var srs  = new List<SpriteRenderer>(cards[i].GetComponentsInChildren<SpriteRenderer>());
            var cols = new List<Color>();
            foreach (var s in srs) cols.Add(s.color);
            renderers.Add(srs);
            originalColors.Add(cols);

            var mainSr = cards[i].GetComponentInChildren<SpriteRenderer>();
            cardBounds.Add(mainSr != null && mainSr.sprite != null
                ? mainSr.sprite.bounds.extents.y * mainSr.transform.lossyScale.y
                : 0.5f);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);

            for (int i = 0; i < cards.Count; i++)
            {
                if (renderers[i] == null) continue;
                for (int j = 0; j < renderers[i].Count; j++)
                {
                    if (renderers[i][j] == null) continue;
                    float spriteY    = renderers[i][j].transform.localPosition.y;
                    float normalizedY = Mathf.Clamp01((cardBounds[i] - spriteY) / (cardBounds[i] * 2f));
                    float wave       = Mathf.Clamp01(1f - Mathf.Abs(progress - normalizedY) * 4f);
                    Color c = originalColors[i][j];
                    renderers[i][j].color = Color.Lerp(c,
                        new Color(c.r * 0.5f, 1f, c.g * 0.5f + 0.3f, c.a),
                        wave * intensity);
                }
            }
            yield return null;
        }

        // 원래 색상 복원
        for (int i = 0; i < cards.Count; i++)
        {
            if (renderers[i] == null) continue;
            for (int j = 0; j < renderers[i].Count; j++)
                if (renderers[i][j] != null)
                    renderers[i][j].color = originalColors[i][j];
        }
    }

    // ─────────────────────────────────────────
    //  체인 카드 → 상대 슬롯 날리기 + 잠금
    // ─────────────────────────────────────────
    public IEnumerator FlyChainCardsAndLock(
        List<GameObject> chainCards, List<Slot> slotsToLock, GameObject chainLockPrefab)
    {
        if (chainCards.Count == 0 || slotsToLock.Count == 0) yield break;

        // 모든 체인 카드 동시에 각 슬롯으로 날리기
        for (int i = 0; i < chainCards.Count; i++)
        {
            Slot targetSlot = slotsToLock[i % slotsToLock.Count];
            _host.StartCoroutine(FlyOneChainCard(chainCards[i], targetSlot));
        }

        // 도착 + 0.5초 보여주기
        yield return new WaitForSeconds(attackDuration + 0.5f);

        // 체인 카드 페이드아웃 + 오버레이 페이드인 동시
        float fadeDuration = 0.4f;

        foreach (var card in chainCards)
            if (card != null)
                _host.StartCoroutine(FadeOutAndDestroy(card, fadeDuration));

        foreach (var slot in slotsToLock)
        {
            slot.ChainLock(chainLockPrefab);
            _host.StartCoroutine(slot.FadeInChain(fadeDuration));
        }

        yield return new WaitForSeconds(fadeDuration);
    }

    private IEnumerator FlyOneChainCard(GameObject card, Slot targetSlot)
    {
        if (card == null || targetSlot == null) yield break;

        card.transform.SetParent(null);
        Vector3 start = card.transform.position;
        Vector3 end   = targetSlot.transform.position;
        float elapsed = 0f;

        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / attackDuration);
            card.transform.position = Vector3.Lerp(start, end, t * t);
            yield return null;
        }

        card.transform.position = end;
    }

    private IEnumerator FadeOutAndDestroy(GameObject obj, float duration)
    {
        if (obj == null) yield break;
        var renderers = obj.GetComponentsInChildren<SpriteRenderer>();
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
        Object.Destroy(obj);
    }

    // ─── 공용 이징 ───
    private static float Smoothstep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
