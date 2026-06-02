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
    //  카드를 한 행으로 나열 (center 기준 가로 정렬)
    // ─────────────────────────────────────────
    public IEnumerator LineUpAt(List<GameObject> cards, Vector3 center, float spacing)
    {
        int n = cards.Count;
        if (n == 0) yield break;

        Vector3[] starts  = new Vector3[n];
        Vector3[] targets = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            starts[i] = cards[i] != null ? cards[i].transform.position : center;
            float x = center.x + (i - (n - 1) * 0.5f) * spacing;
            targets[i] = new Vector3(x, center.y, center.z);
        }

        float elapsed = 0f;
        while (elapsed < showcaseMoveDuration)
        {
            elapsed += Time.deltaTime;
            float eased = Smoothstep(elapsed / showcaseMoveDuration);
            for (int i = 0; i < n; i++)
                if (cards[i] != null)
                    cards[i].transform.position = Vector3.Lerp(starts[i], targets[i], eased);
            yield return null;
        }

        for (int i = 0; i < n; i++)
            if (cards[i] != null) cards[i].transform.position = targets[i];
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
    //  카드 날리기: 중앙으로 뭉치면서 회전 → 순차 발사 + 타격
    // ─────────────────────────────────────────
    public IEnumerator FlyAndHit(List<GameObject> cards, Vector3 targetWorld)
    {
        if (cards == null || cards.Count == 0) yield break;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 1) 가운데로 뭉치면서 45도 회전
        float gatherDur = 0.25f;
        Quaternion[] startRots  = new Quaternion[cards.Count];
        Quaternion[] targetRots = new Quaternion[cards.Count];
        Vector3[] startPos      = new Vector3[cards.Count];
        
        Vector3 center = Vector3.zero;
        int validCount = 0;

        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i] == null) continue;
            startPos[i]   = cards[i].transform.position;
            startRots[i]  = cards[i].transform.rotation;
            targetRots[i] = startRots[i] * Quaternion.Euler(0f, 0f, 45f);
            
            center += startPos[i];
            validCount++;
        }
        if (validCount > 0) center /= validCount;

        float elapsed = 0f;
        while (elapsed < gatherDur)
        {
            elapsed += Time.deltaTime;
            float eased = Smoothstep(elapsed / gatherDur);
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == null) continue;
                cards[i].transform.position = Vector3.Lerp(startPos[i], center, eased);
                cards[i].transform.rotation = Quaternion.Slerp(startRots[i], targetRots[i], eased);
            }
            yield return null;
        }

        yield return new WaitForSeconds(0.05f); // 뭉치고 잠깐 대기

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

    // ─────────────────────────────────────────
    //  단일 카드를 한 지점으로 날린 뒤 소멸 (덱 앵커 복귀/파괴 연출)
    // ─────────────────────────────────────────
    public IEnumerator FlyCardTo(GameObject card, Vector3 targetWorld)
    {
        if (card == null) yield break;
        yield return _host.StartCoroutine(FlyOneCard(card, targetWorld));
    }

    // ─────────────────────────────────────────
    //  카드 뭉치를 한 지점으로 강타한 뒤 원위치 복귀 (생존 카드의 공격 연출)
    // ─────────────────────────────────────────
    public IEnumerator SlamAndReturn(List<GameObject> cards, Vector3 target)
    {
        if (cards == null || cards.Count == 0) yield break;

        int n = cards.Count;
        Vector3[] starts = new Vector3[n];
        Vector3 center = Vector3.zero;
        int validCount = 0;
        for (int i = 0; i < n; i++)
        {
            if (cards[i] != null) {
                starts[i] = cards[i].transform.position;
                center += starts[i];
                validCount++;
            }
            else starts[i] = target;
        }
        if (validCount > 0) center /= validCount;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 0) 먼저 가운데로 뭉치기
        float gatherDur = 0.15f;
        float e = 0f;
        while (e < gatherDur)
        {
            e += Time.deltaTime;
            float eased = Smoothstep(e / gatherDur);
            for (int i = 0; i < n; i++)
                if (cards[i] != null) cards[i].transform.position = Vector3.Lerp(starts[i], center, eased);
            yield return null;
        }
        yield return new WaitForSeconds(0.05f);

        // 1) 전진 (가속)
        float outDur = 0.22f;
        e = 0f;
        while (e < outDur)
        {
            e += Time.deltaTime;
            float eased = (e / outDur) * (e / outDur);
            for (int i = 0; i < n; i++)
                if (cards[i] != null) cards[i].transform.position = Vector3.Lerp(center, target, Mathf.Clamp01(eased));
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

        yield return new WaitForSeconds(0.05f); // 임팩트 정지

        // 복귀 (감속)
        float backDur = 0.2f;
        e = 0f;
        while (e < backDur)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / backDur);
            float eased = 1f - (1f - t) * (1f - t);
            for (int i = 0; i < n; i++)
                if (cards[i] != null) cards[i].transform.position = Vector3.Lerp(target, starts[i], eased);
            yield return null;
        }

        for (int i = 0; i < n; i++)
            if (cards[i] != null) cards[i].transform.position = starts[i];
    }

    // ─────────────────────────────────────────
    //  Guard vs Guard: 대치된 양측 카드 모두가 패자의 본체로 날아가 강타 (올인 연출)
    //  강타 직후 패자의 카드는 파괴되고, 승자의 카드는 원위치로 복귀
    // ─────────────────────────────────────────
    public IEnumerator AllInSlamAndReturn(List<GameObject> winnerCards, List<GameObject> loserCards, Vector3 target)
    {
        int wLen = winnerCards.Count;
        int lLen = loserCards.Count;
        
        Vector3[] wStarts = new Vector3[wLen];
        Vector3 wCenter = Vector3.zero;
        int wCount = 0;
        for (int i = 0; i < wLen; i++) {
            wStarts[i] = winnerCards[i] != null ? winnerCards[i].transform.position : target;
            if(winnerCards[i] != null) { wCenter += wStarts[i]; wCount++; }
        }
        if(wCount > 0) wCenter /= wCount;

        Vector3[] lStarts = new Vector3[lLen];
        Vector3 lCenter = Vector3.zero;
        int lCount = 0;
        for (int i = 0; i < lLen; i++) {
            lStarts[i] = loserCards[i] != null ? loserCards[i].transform.position : target;
            if(loserCards[i] != null) { lCenter += lStarts[i]; lCount++; }
        }
        if(lCount > 0) lCenter /= lCount;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 0) 양 진영 모두 각자의 중심으로 뭉치기
        float gatherDur = 0.15f;
        float e = 0f;
        while (e < gatherDur)
        {
            e += Time.deltaTime;
            float eased = Smoothstep(e / gatherDur);
            for (int i = 0; i < wLen; i++)
                if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(wStarts[i], wCenter, eased);
            for (int i = 0; i < lLen; i++)
                if (loserCards[i] != null) loserCards[i].transform.position = Vector3.Lerp(lStarts[i], lCenter, eased);
            yield return null;
        }
        yield return new WaitForSeconds(0.05f);

        // 1) 전진 (가속) - 뭉친 상태로 모두 함께 타겟으로
        float outDur = 0.25f;
        e = 0f;
        while (e < outDur)
        {
            e += Time.deltaTime;
            float eased = (e / outDur) * (e / outDur); // 가속
            for (int i = 0; i < wLen; i++)
                if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(wCenter, target, Mathf.Clamp01(eased));
            for (int i = 0; i < lLen; i++)
                if (loserCards[i] != null) loserCards[i].transform.position = Vector3.Lerp(lCenter, target, Mathf.Clamp01(eased));
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

        // 임팩트 정지
        yield return new WaitForSeconds(0.05f);

        // 패자 카드는 그 자리에서 파괴
        foreach (var c in loserCards)
        {
            if (c != null)
                Object.Destroy(c);
        }

        // 승자 카드 복귀 (감속)
        float backDur = 0.25f;
        e = 0f;
        while (e < backDur)
        {
            e += Time.deltaTime;
            float t = Mathf.Clamp01(e / backDur);
            float eased = 1f - (1f - t) * (1f - t);
            for (int i = 0; i < wLen; i++)
                if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(target, wStarts[i], eased);
            yield return null;
        }

        for (int i = 0; i < wLen; i++)
            if (winnerCards[i] != null) winnerCards[i].transform.position = wStarts[i];
    }

    // ─────────────────────────────────────────
    //  Attack vs Guard (Attack 승리): 
    //  공격 카드가 방어 카드를 먼저 때림 -> 방어 카드 진동+페이드아웃 -> 공격 카드가 패자 본체로 날아감
    // ─────────────────────────────────────────
    public IEnumerator StrikeGuardAndFly(List<GameObject> atkCards, List<GameObject> defCards, Vector3 targetBody)
    {
        // 1) 공격 카드가 방어 카드 쪽(중앙)으로 날아감
        Vector3 defCenter = Vector3.zero;
        int defCount = 0;
        foreach(var c in defCards) {
            if(c != null) { defCenter += c.transform.position; defCount++; }
        }
        if(defCount > 0) defCenter /= defCount;
        else defCenter = targetBody; // 방어 카드가 없으면 바로 바디로

        int aLen = atkCards.Count;
        Vector3[] aStarts = new Vector3[aLen];
        Vector3 aCenter = Vector3.zero;
        int aCount = 0;
        for (int i = 0; i < aLen; i++) {
            aStarts[i] = atkCards[i] != null ? atkCards[i].transform.position : defCenter;
            if (atkCards[i] != null) { aCenter += aStarts[i]; aCount++; }
        }
        if (aCount > 0) aCenter /= aCount;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 0) 공격 카드 뭉치기
        float gatherDur = 0.15f;
        float e = 0f;
        while (e < gatherDur)
        {
            e += Time.deltaTime;
            float eased = Smoothstep(e / gatherDur);
            for (int i = 0; i < aLen; i++)
                if (atkCards[i] != null) atkCards[i].transform.position = Vector3.Lerp(aStarts[i], aCenter, eased);
            yield return null;
        }
        yield return new WaitForSeconds(0.05f);

        // 1) 공격 카드가 방어 카드 중심부로 날아감 (가속)
        float flyDur = 0.15f;
        e = 0f;
        while(e < flyDur)
        {
            e += Time.deltaTime;
            float eased = (e / flyDur) * (e / flyDur); // 가속
            for(int i = 0; i < aLen; i++)
                if(atkCards[i] != null) atkCards[i].transform.position = Vector3.Lerp(aCenter, defCenter, eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

        // 2) 임팩트: 방어 카드 진동 + 페이드아웃 (0.35초)
        float shakeDur = 0.35f;
        e = 0f;
        Vector3[] dStarts = new Vector3[defCards.Count];
        SpriteRenderer[][] dRenders = new SpriteRenderer[defCards.Count][];
        for(int i = 0; i < defCards.Count; i++) {
            if(defCards[i] != null) {
                dStarts[i] = defCards[i].transform.position;
                dRenders[i] = defCards[i].GetComponentsInChildren<SpriteRenderer>();
            }
        }

        while(e < shakeDur)
        {
            e += Time.deltaTime;
            float p = e / shakeDur;
            float alpha = 1f - p;
            for(int i = 0; i < defCards.Count; i++)
            {
                if(defCards[i] != null)
                {
                    // 진동
                    defCards[i].transform.position = dStarts[i] + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0f);
                    // 페이드 아웃
                    if(dRenders[i] != null) {
                        foreach(var sr in dRenders[i]) {
                            Color c = sr.color;
                            c.a = alpha;
                            sr.color = c;
                        }
                    }
                }
            }
            yield return null;
        }

        // 방어 카드 파괴
        foreach(var c in defCards)
            if(c != null) Object.Destroy(c);

        // 3) 남은 공격 카드가 패자 본체(targetBody)로 날아가며 소멸
        e = 0f;
        Vector3[] aMids = new Vector3[aLen];
        for(int i = 0; i < aLen; i++)
            aMids[i] = atkCards[i] != null ? atkCards[i].transform.position : targetBody;

        while(e < flyDur)
        {
            e += Time.deltaTime;
            float eased = (e / flyDur) * (e / flyDur);
            for(int i = 0; i < aLen; i++)
                if(atkCards[i] != null) atkCards[i].transform.position = Vector3.Lerp(aMids[i], targetBody, eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

        // 공격 카드도 도착 후 파괴 (소모됨)
        foreach(var c in atkCards)
            if(c != null) Object.Destroy(c);
    }

    // ─────────────────────────────────────────
    //  Guard 승리 시 (Attack 차단): 공격 카드가 방어 카드에 부딪히고 산산조각(파괴) 나는 연출
    // ─────────────────────────────────────────
    public IEnumerator BlockAndShatter(List<GameObject> atkCards, List<GameObject> defCards)
    {
        // 1) 공격 카드가 방어 카드 쪽(중앙)으로 날아감
        Vector3 defCenter = Vector3.zero;
        int defCount = 0;
        foreach(var c in defCards) {
            if(c != null) { defCenter += c.transform.position; defCount++; }
        }
        if(defCount > 0) defCenter /= defCount;
        else defCenter = Vector3.zero;

        int aLen = atkCards.Count;
        Vector3[] aStarts = new Vector3[aLen];
        Vector3 aCenter = Vector3.zero;
        int aCount = 0;
        for (int i = 0; i < aLen; i++) {
            aStarts[i] = atkCards[i] != null ? atkCards[i].transform.position : defCenter;
            if (atkCards[i] != null) { aCenter += aStarts[i]; aCount++; }
        }
        if (aCount > 0) aCenter /= aCount;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 0) 공격 카드 뭉치기
        float gatherDur = 0.15f;
        float e = 0f;
        while (e < gatherDur)
        {
            e += Time.deltaTime;
            float eased = Smoothstep(e / gatherDur);
            for (int i = 0; i < aLen; i++)
                if (atkCards[i] != null) atkCards[i].transform.position = Vector3.Lerp(aStarts[i], aCenter, eased);
            yield return null;
        }
        yield return new WaitForSeconds(0.05f);

        // 1) 공격 카드가 방어 카드 중심부로 날아감 (돌진)
        float flyDur = 0.15f;
        e = 0f;
        while(e < flyDur)
        {
            e += Time.deltaTime;
            float eased = (e / flyDur) * (e / flyDur); // 가속
            for(int i = 0; i < aLen; i++)
                if(atkCards[i] != null) atkCards[i].transform.position = Vector3.Lerp(aCenter, defCenter, eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatBlock);

        // 2) 임팩트: 방어 카드 실드(하얗게 번쩍) + 카메라 셰이크
        _host.StartCoroutine(ShakeCamera(0.2f, 0.1f));
        foreach (var dc in defCards)
        {
            if (dc != null) _host.StartCoroutine(FlashWhite(dc, 0.2f));
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.shatter);

        // 3) 공격 카드 산산조각 (사방으로 튕겨나가며 흩어짐 + 페이드아웃)
        float shatterDur = 0.35f;
        e = 0f;
        
        Vector3[] scatterDirs = new Vector3[aLen];
        SpriteRenderer[][] aRenders = new SpriteRenderer[aLen][];
        Vector3[] impactPos = new Vector3[aLen];

        for(int i = 0; i < aLen; i++) {
            if(atkCards[i] != null) {
                impactPos[i] = atkCards[i].transform.position;
                scatterDirs[i] = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f).normalized * Random.Range(2f, 4f);
                aRenders[i] = atkCards[i].GetComponentsInChildren<SpriteRenderer>();
            }
        }

        while(e < shatterDur)
        {
            e += Time.deltaTime;
            float p = e / shatterDur;
            float alpha = 1f - p;
            float easeOut = 1f - (1f - p) * (1f - p); // 감속하며 날아감

            for(int i = 0; i < aLen; i++)
            {
                if(atkCards[i] != null)
                {
                    // 튕겨나감 + 회전
                    atkCards[i].transform.position = impactPos[i] + scatterDirs[i] * easeOut;
                    atkCards[i].transform.Rotate(0f, 0f, Random.Range(10f, 30f)); // 빙글빙글

                    // 페이드 아웃
                    if(aRenders[i] != null) {
                        foreach(var sr in aRenders[i]) {
                            Color c = sr.color;
                            c.a = alpha;
                            sr.color = c;
                        }
                    }
                }
            }
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.shatter);

        // 공격 카드 파괴
        foreach(var c in atkCards)
            if(c != null) Object.Destroy(c);
    }

    private IEnumerator FlashWhite(GameObject obj, float duration)
    {
        var renderers = obj.GetComponentsInChildren<SpriteRenderer>();
        if (renderers.Length == 0) yield break;

        Color[] originals = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) originals[i] = renderers[i].color;

        float e = 0f;
        while (e < duration)
        {
            e += Time.deltaTime;
            float t = e / duration;
            // 0 -> 1 -> 0 (PingPong)
            float intensity = Mathf.Sin(t * Mathf.PI);
            
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].color = Color.Lerp(originals[i], Color.white, intensity * 0.8f);
            }
            yield return null;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null) renderers[i].color = originals[i];
        }
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

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

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

    public IEnumerator FadeOutAndDestroy(GameObject obj, float duration)
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
    // ─────────────────────────────────────────
    //  통일된 데미지 연출: 패배 카드 타격 -> 본체 타격 -> 소멸/복귀
    // ─────────────────────────────────────────
    public IEnumerator UnifiedCombatStrike(List<GameObject> winnerCards, List<GameObject> loserCards, Vector3 targetBody, bool winnerIsGuard, bool loserIsGuard, System.Action onBodyHit)
    {
        int wLen = winnerCards.Count;
        int lLen = loserCards.Count;

        Vector3[] wStarts = new Vector3[wLen];
        Vector3 wCenter = Vector3.zero;
        int wCount = 0;
        for (int i = 0; i < wLen; i++)
        {
            wStarts[i] = winnerCards[i] != null ? winnerCards[i].transform.position : targetBody;
            if (winnerCards[i] != null) { wCenter += wStarts[i]; wCount++; }
        }
        if (wCount > 0) wCenter /= wCount;

        Vector3[] lStartsOriginal = new Vector3[lLen];
        Vector3 lCenter = Vector3.zero;
        int lCount = 0;
        for (int i = 0; i < lLen; i++)
        {
            if (loserCards[i] != null) { 
                lStartsOriginal[i] = loserCards[i].transform.position;
                lCenter += lStartsOriginal[i]; 
                lCount++; 
            }
        }
        if (lCount > 0) lCenter /= lCount;
        else lCenter = targetBody; // 패자 카드가 없으면 바로 본체로

        bool bothAttack = !winnerIsGuard && !loserIsGuard;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 0) 승자 카드 뭉치기 (공격 vs 공격이 아닐 때만 승자가 뭉침)
        float gatherDur = 0.15f;
        float e = 0f;
        
        if (!bothAttack)
        {
            while (e < gatherDur)
            {
                e += Time.deltaTime;
                float eased = Smoothstep(e / gatherDur);
                for (int i = 0; i < wLen; i++)
                    if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(wStarts[i], wCenter, eased);
                yield return null;
            }
            yield return new WaitForSeconds(0.05f);
        }

        // 1) 카드 간 충돌 (패자 카드가 있는 경우)
        Vector3[] wClashPos = new Vector3[wLen];
        Vector3[] lClashPos = new Vector3[lLen];

        if (lCount > 0)
        {
            Vector3 dir = (lCenter - wCenter).normalized;
            float dashDist = Vector3.Distance(wCenter, lCenter) * 0.5f - 0.4f;

            for (int i = 0; i < wLen; i++)
            {
                if (bothAttack) wClashPos[i] = wStarts[i] + dir * dashDist;
                else wClashPos[i] = lCenter; // 공격vs수비는 승자가 뭉쳐서 수비 중심을 때림
            }

            for (int i = 0; i < lLen; i++)
            {
                if (bothAttack) lClashPos[i] = lStartsOriginal[i] - dir * dashDist;
                else lClashPos[i] = lStartsOriginal[i]; // 수비는 움직이지 않음
            }

            float flyDur = bothAttack ? 0.2f : 0.15f;
            
            e = 0f;
            while (e < flyDur)
            {
                e += Time.deltaTime;
                float eased = (e / flyDur) * (e / flyDur); // 가속
                
                for (int i = 0; i < wLen; i++)
                    if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(bothAttack ? wStarts[i] : wCenter, wClashPos[i], eased);
                
                if (bothAttack)
                {
                    for (int i = 0; i < lLen; i++)
                        if (loserCards[i] != null) loserCards[i].transform.position = Vector3.Lerp(lStartsOriginal[i], lClashPos[i], eased);
                }
                yield return null;
            }

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);
                if (bothAttack) SoundManager.Instance.PlaySFX(SoundManager.Instance.shatter); // 격돌 효과음 강화
            }

            if (bothAttack)
            {
                // 격돌 시 카메라 살짝 흔들림 추가
                _host.StartCoroutine(ShakeCamera(0.2f, 0.15f));
            }

            // 2) 임팩트: 패자 카드 진동 + 페이드아웃 (0.35초)
            float shakeDur = 0.35f;
            e = 0f;
            Vector3[] lStarts = new Vector3[lLen];
            SpriteRenderer[][] lRenders = new SpriteRenderer[lLen][];
            for (int i = 0; i < lLen; i++)
            {
                if (loserCards[i] != null)
                {
                    lStarts[i] = loserCards[i].transform.position; // 충돌 위치에서 시작
                    lRenders[i] = loserCards[i].GetComponentsInChildren<SpriteRenderer>();
                }
            }

            while (e < shakeDur)
            {
                e += Time.deltaTime;
                float p = e / shakeDur;
                float alpha = 1f - p;
                for (int i = 0; i < lLen; i++)
                {
                    if (loserCards[i] != null)
                    {
                        loserCards[i].transform.position = lStarts[i] + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0f);
                        if (lRenders[i] != null)
                        {
                            foreach (var sr in lRenders[i])
                            {
                                Color c = sr.color;
                                c.a = alpha;
                                sr.color = c;
                            }
                        }
                    }
                }

                // 승자 카드도 같이 진동하며 밀고 있는 연출
                if (bothAttack)
                {
                    for (int i = 0; i < wLen; i++)
                    {
                        if (winnerCards[i] != null)
                            winnerCards[i].transform.position = wClashPos[i] + new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(-0.05f, 0.05f), 0f);
                    }
                }
                yield return null;
            }

            // 방어/패자 카드 파괴
            foreach (var c in loserCards)
                if (c != null) Object.Destroy(c);
        }

        // 3) 승리 카드가 패자 본체(targetBody)로 날아감
        e = 0f;
        float bodyFlyDur = 0.2f;
        Vector3[] wMids = new Vector3[wLen];
        for (int i = 0; i < wLen; i++)
            wMids[i] = winnerCards[i] != null ? winnerCards[i].transform.position : targetBody;

        while (e < bodyFlyDur)
        {
            e += Time.deltaTime;
            float eased = (e / bodyFlyDur) * (e / bodyFlyDur);
            for (int i = 0; i < wLen; i++)
                if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(wMids[i], targetBody, eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

        // 본체 타격 
        onBodyHit?.Invoke();

        if (winnerIsGuard)
        {
            // 수비 카드면 본체 타격 후 원위치(자신의 슬롯)로 복귀
            yield return new WaitForSeconds(0.05f); // 타격감 위해 살짝 정지
            float backDur = 0.25f;
            e = 0f;
            while (e < backDur)
            {
                e += Time.deltaTime;
                float t = Mathf.Clamp01(e / backDur);
                float eased = 1f - (1f - t) * (1f - t);
                for (int i = 0; i < wLen; i++)
                    if (winnerCards[i] != null) winnerCards[i].transform.position = Vector3.Lerp(targetBody, wStarts[i], eased);
                yield return null;
            }
            for (int i = 0; i < wLen; i++)
                if (winnerCards[i] != null) winnerCards[i].transform.position = wStarts[i];
        }
        else
        {
            // 공격 카드면 소멸
            foreach (var c in winnerCards)
                if (c != null) Object.Destroy(c);
        }
    }

    // ─────────────────────────────────────────
    //  공격 vs 공격 무승부 (양쪽 동시 파괴 연출)
    // ─────────────────────────────────────────
    public IEnumerator MutualDestructionClash(List<GameObject> pCards, List<GameObject> oCards)
    {
        int pLen = pCards.Count;
        int oLen = oCards.Count;
        if (pLen == 0 || oLen == 0) yield break;

        Vector3 pCenter = Vector3.zero;
        Vector3[] pStarts = new Vector3[pLen];
        for (int i = 0; i < pLen; i++) {
            if (pCards[i] != null) {
                pStarts[i] = pCards[i].transform.position;
                pCenter += pStarts[i];
            }
        }
        pCenter /= pLen;

        Vector3 oCenter = Vector3.zero;
        Vector3[] oStarts = new Vector3[oLen];
        for (int i = 0; i < oLen; i++) {
            if (oCards[i] != null) {
                oStarts[i] = oCards[i].transform.position;
                oCenter += oStarts[i];
            }
        }
        oCenter /= oLen;

        Vector3 dir = (oCenter - pCenter).normalized;
        float dashDist = Vector3.Distance(pCenter, oCenter) * 0.5f - 0.4f;

        Vector3[] pClash = new Vector3[pLen];
        for (int i = 0; i < pLen; i++) pClash[i] = pStarts[i] + dir * dashDist;

        Vector3[] oClash = new Vector3[oLen];
        for (int i = 0; i < oLen; i++) oClash[i] = oStarts[i] - dir * dashDist;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 중앙 격돌 (가로 형태 유지)
        float flyDur = 0.2f;
        float e = 0f;
        while (e < flyDur)
        {
            e += Time.deltaTime;
            float eased = (e / flyDur) * (e / flyDur);
            for (int i = 0; i < pLen; i++) if (pCards[i] != null) pCards[i].transform.position = Vector3.Lerp(pStarts[i], pClash[i], eased);
            for (int i = 0; i < oLen; i++) if (oCards[i] != null) oCards[i].transform.position = Vector3.Lerp(oStarts[i], oClash[i], eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);
            SoundManager.Instance.PlaySFX(SoundManager.Instance.shatter);
        }

        _host.StartCoroutine(ShakeCamera(0.25f, 0.2f));

        // 양쪽 파괴 페이드아웃
        float shakeDur = 0.35f;
        e = 0f;
        while (e < shakeDur)
        {
            e += Time.deltaTime;
            float alpha = 1f - (e / shakeDur);
            
            System.Action<List<GameObject>, Vector3[]> shakeAndFade = (cards, clashPosArray) => {
                for (int i = 0; i < cards.Count; i++) {
                    var c = cards[i];
                    if (c != null) {
                        c.transform.position = clashPosArray[i] + new Vector3(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f), 0f);
                        foreach (var sr in c.GetComponentsInChildren<SpriteRenderer>()) {
                            Color color = sr.color;
                            color.a = alpha;
                            sr.color = color;
                        }
                    }
                }
            };

            shakeAndFade(pCards, pClash);
            shakeAndFade(oCards, oClash);
            yield return null;
        }

        foreach (var c in pCards) if (c != null) Object.Destroy(c);
        foreach (var c in oCards) if (c != null) Object.Destroy(c);
    }

    // ─────────────────────────────────────────
    //  공격 vs 공격 동시 타격 (양쪽 본체 데미지)
    // ─────────────────────────────────────────
    public IEnumerator MutualAttackClash(List<GameObject> pCards, List<GameObject> oCards, Vector3 pTargetBody, Vector3 oTargetBody, System.Action onPHit, System.Action onOHit)
    {
        int pLen = pCards.Count;
        int oLen = oCards.Count;
        if (pLen == 0 || oLen == 0) yield break;

        Vector3 pCenter = Vector3.zero;
        Vector3[] pStarts = new Vector3[pLen];
        for (int i = 0; i < pLen; i++) {
            if (pCards[i] != null) {
                pStarts[i] = pCards[i].transform.position;
                pCenter += pStarts[i];
            }
        }
        pCenter /= pLen;

        Vector3 oCenter = Vector3.zero;
        Vector3[] oStarts = new Vector3[oLen];
        for (int i = 0; i < oLen; i++) {
            if (oCards[i] != null) {
                oStarts[i] = oCards[i].transform.position;
                oCenter += oStarts[i];
            }
        }
        oCenter /= oLen;

        Vector3 dir = (oCenter - pCenter).normalized;
        float dashDist = Vector3.Distance(pCenter, oCenter) * 0.5f - 0.4f;

        Vector3[] pClash = new Vector3[pLen];
        for (int i = 0; i < pLen; i++) pClash[i] = pStarts[i] + dir * dashDist;

        Vector3[] oClash = new Vector3[oLen];
        for (int i = 0; i < oLen; i++) oClash[i] = oStarts[i] - dir * dashDist;

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.gatherPower);

        // 1) 중앙으로 격돌 (가로 형태 유지)
        float flyDur = 0.2f;
        float e = 0f;
        while (e < flyDur)
        {
            e += Time.deltaTime;
            float eased = (e / flyDur) * (e / flyDur);
            for (int i = 0; i < pLen; i++) if (pCards[i] != null) pCards[i].transform.position = Vector3.Lerp(pStarts[i], pClash[i], eased);
            for (int i = 0; i < oLen; i++) if (oCards[i] != null) oCards[i].transform.position = Vector3.Lerp(oStarts[i], oClash[i], eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);
            SoundManager.Instance.PlaySFX(SoundManager.Instance.shatter);
        }
        _host.StartCoroutine(ShakeCamera(0.2f, 0.15f));

        // 2) 서로 힘겨루기 진동 (페이드아웃 없음)
        float shakeDur = 0.35f;
        e = 0f;
        while (e < shakeDur)
        {
            e += Time.deltaTime;
            for (int i = 0; i < pLen; i++) if (pCards[i] != null) pCards[i].transform.position = pClash[i] + new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(-0.05f, 0.05f), 0f);
            for (int i = 0; i < oLen; i++) if (oCards[i] != null) oCards[i].transform.position = oClash[i] + new Vector3(Random.Range(-0.05f, 0.05f), Random.Range(-0.05f, 0.05f), 0f);
            yield return null;
        }

        // 3) 서로 교차해서 상대 본체로 날아감
        float bodyFlyDur = 0.2f;
        e = 0f;
        while (e < bodyFlyDur)
        {
            e += Time.deltaTime;
            float eased = (e / bodyFlyDur) * (e / bodyFlyDur);
            for (int i = 0; i < pLen; i++) if (pCards[i] != null) pCards[i].transform.position = Vector3.Lerp(pClash[i], pTargetBody, eased);
            for (int i = 0; i < oLen; i++) if (oCards[i] != null) oCards[i].transform.position = Vector3.Lerp(oClash[i], oTargetBody, eased);
            yield return null;
        }

        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(SoundManager.Instance.combatHit);

        onPHit?.Invoke();
        onOHit?.Invoke();

        foreach (var c in pCards) if (c != null) Object.Destroy(c);
        foreach (var c in oCards) if (c != null) Object.Destroy(c);
    }
}
