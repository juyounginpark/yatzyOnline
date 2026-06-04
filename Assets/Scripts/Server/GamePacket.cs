using System;
using LitJson;

// ─────────────────────────────────────────────
//  패킷 타입
// ─────────────────────────────────────────────
public enum PacketType
{
    TurnEnd     = 1,
    CardPlace   = 2,
    CardReturn  = 3,
    GameOver    = 4,
    Sync        = 5,
    Draft       = 6,
    Seed        = 7,
    Hello       = 8,   // 선공 선출용 — 송신자 clientId 는 SendRaw 의 "from" 필드로 전달
}

// ─────────────────────────────────────────────
//  카드 배치 데이터
// ─────────────────────────────────────────────
[Serializable]
public struct CardPlaceData
{
    public int slotIndex, value;
    public bool isJoker;
}

// ─────────────────────────────────────────────
//  드래프트 결과 데이터 (턴 플레이어 → 상대 동기화)
//  제시된 2장(left/right) 전체 정보 + 턴 플레이어가 어느 쪽을 골랐는지
//  me  = 턴 플레이어가 자기 패로 가져간 카드
//  opp = 상대(수신자) 패로 넘어가는 카드
//  (hasMe·hasOpp 모두 false = 양쪽 손패 max로 스킵)
//  hasLeft/hasRight: 제시된 2장이 있는지 (시각 연출용)
//  choiceIsLeft: 턴 플레이어가 왼쪽을 선택했는지 (true=왼쪽, false=오른쪽)
// ─────────────────────────────────────────────
[Serializable]
public struct DraftData
{
    public bool     hasMe;
    public int      meV;
    public bool     meJoker;
    public bool     hasOpp;
    public int      oppV;
    public bool     oppJoker;

    // 제시된 2장 정보 (상대 턴 시각 연출용)
    public bool     hasLeft;
    public int      leftV;
    public bool     leftJoker;
    public bool     hasRight;
    public int      rightV;
    public bool     rightJoker;
    public bool     choiceIsLeft;  // 턴 플레이어가 왼쪽(left)을 선택했는가?
    public bool     isSingleCard;  // 단일 카드 드래프트인가? (한쪽 손패 max)
}

// ─────────────────────────────────────────────
//  슬롯 카드 데이터
// ─────────────────────────────────────────────
[Serializable]
public struct SlotCardData
{
    public int  slotIndex;
    public int  value;
    public bool isJoker;

    public static SlotCardData Empty(int idx) =>
        new SlotCardData { slotIndex = idx, value = 0 };
}

// ─────────────────────────────────────────────
//  패킷 직렬화 / 역직렬화
//  LitJson.JsonData 사용 (Backend SDK 내장 LitJSON.dll)
// ─────────────────────────────────────────────
public static class GamePacket
{
    // ── TurnEnd ──
    public static string MakeTurnEnd(SlotCardData[] slots,
        float senderHp = -1f, float receiverHp = -1f, bool fieldGuard = false)
    {
        var j = new JsonData();
        j["t"]     = (int)PacketType.TurnEnd;
        j["g"]     = fieldGuard ? 1 : 0;   // 보내는 쪽 필드가 통째로 Guard(뒷면)인지
        j["slots"] = new JsonData();
        j["slots"].SetJsonType(JsonType.Array);

        foreach (var s in slots)
        {
            if (s.value <= 0) continue;
            var entry = new JsonData();
            entry["si"] = s.slotIndex;
            entry["v"]  = s.value;
            entry["j"]  = s.isJoker ? 1 : 0;
            j["slots"].Add(entry);
        }

        // HP 동기화 (10배 스케일링으로 소수점 1자리 보존)
        if (senderHp >= 0f)   j["shp"] = (int)(senderHp * 10f);
        if (receiverHp >= 0f) j["rhp"] = (int)(receiverHp * 10f);

        return JsonMapper.ToJson(j);
    }

    /// <summary>HP 값 파싱 — senderHp, receiverHp (없으면 -1). 10배 스케일링 복원.</summary>
    public static void ParseHp(JsonData j, out float senderHp, out float receiverHp)
    {
        senderHp   = j.Keys.Contains("shp") ? (int)j["shp"] / 10f : -1f;
        receiverHp = j.Keys.Contains("rhp") ? (int)j["rhp"] / 10f : -1f;
    }

    public static SlotCardData[] ParseTurnEnd(JsonData j)
    {
        if (!j.Keys.Contains("slots")) return new SlotCardData[0];
        var arr = j["slots"];
        var result = new SlotCardData[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            result[i] = new SlotCardData
            {
                slotIndex = (int)arr[i]["si"],
                value     = (int)arr[i]["v"],
                isJoker   = (int)arr[i]["j"] == 1,
            };
        }
        return result;
    }

    /// <summary>보내는 쪽 필드 Guard 여부 (없으면 false)</summary>
    public static bool ParseFieldGuard(JsonData j)
    {
        return j.Keys.Contains("g") && (int)j["g"] == 1;
    }

    // ── CardPlace ──
    public static string MakeCardPlace(int slotIndex, int value, bool isJoker = false)
    {
        var j = new JsonData();
        j["t"]  = (int)PacketType.CardPlace;
        j["si"] = slotIndex;
        j["v"]  = value;
        j["j"]  = isJoker ? 1 : 0;
        return JsonMapper.ToJson(j);
    }

    public static bool ParseCardPlace(JsonData j,
        out int slotIndex, out int value, out bool isJoker)
    {
        slotIndex = (int)j["si"];
        value     = (int)j["v"];
        isJoker   = (int)j["j"] == 1;
        return true;
    }

    // ── CardReturn ──
    public static string MakeCardReturn(int slotIndex)
    {
        var j = new JsonData();
        j["t"]  = (int)PacketType.CardReturn;
        j["si"] = slotIndex;
        return JsonMapper.ToJson(j);
    }

    // ── GameOver ──
    public static string MakeGameOver(bool receiverWins)
    {
        var j = new JsonData();
        j["t"]   = (int)PacketType.GameOver;
        j["win"] = receiverWins ? 1 : 0;
        return JsonMapper.ToJson(j);
    }

    // ── Sync (실시간 타이머+HP 동기화) ──
    public static string MakeSync(float timer, float senderHp, float receiverHp)
    {
        var j = new JsonData();
        j["t"]   = (int)PacketType.Sync;
        j["tm"]  = (int)(timer * 100f);   // 0.01초 단위
        j["shp"] = (int)(senderHp * 10f); // 10배 스케일링 (소수점 1자리)
        j["rhp"] = (int)(receiverHp * 10f);
        return JsonMapper.ToJson(j);
    }

    public static void ParseSync(JsonData j, out float timer, out float senderHp, out float receiverHp)
    {
        timer      = (int)j["tm"] / 100f;
        senderHp   = (int)j["shp"] / 10f;
        receiverHp = (int)j["rhp"] / 10f;
    }

    // ── Draft (드래프트 결과 동기화 — 제시 2장 + 선택 방향 포함) ──
    public static string MakeDraft(bool hasMe, int meV, bool meJoker,
                                   bool hasOpp, int oppV, bool oppJoker,
                                   bool hasLeft, int leftV, bool leftJoker,
                                   bool hasRight, int rightV, bool rightJoker,
                                   bool choiceIsLeft, bool isSingleCard = false)
    {
        var j = new JsonData();
        j["t"]  = (int)PacketType.Draft;
        j["hm"] = hasMe ? 1 : 0;
        j["mv"] = meV;  j["mj"] = meJoker ? 1 : 0;
        j["ho"] = hasOpp ? 1 : 0;
        j["ov"] = oppV; j["oj"] = oppJoker ? 1 : 0;
        // 제시 2장 정보 (상대 클라이언트 시각 연출용)
        j["hl"] = hasLeft ? 1 : 0;
        j["lv"] = leftV;  j["lj"] = leftJoker ? 1 : 0;
        j["hr"] = hasRight ? 1 : 0;
        j["rv"] = rightV; j["rj"] = rightJoker ? 1 : 0;
        j["cl"] = choiceIsLeft ? 1 : 0;
        j["sc"] = isSingleCard ? 1 : 0;
        return JsonMapper.ToJson(j);
    }

    public static DraftData ParseDraft(JsonData j)
    {
        return new DraftData
        {
            hasMe    = (int)j["hm"] == 1,
            meV      = (int)j["mv"],
            meJoker  = (int)j["mj"] == 1,
            hasOpp   = (int)j["ho"] == 1,
            oppV     = (int)j["ov"],
            oppJoker = (int)j["oj"] == 1,
            // 제시 2장 정보
            hasLeft     = j.Keys.Contains("hl") && (int)j["hl"] == 1,
            leftV       = j.Keys.Contains("lv")  ? (int)j["lv"]  : 0,
            leftJoker   = j.Keys.Contains("lj")  && (int)j["lj"] == 1,
            hasRight    = j.Keys.Contains("hr") && (int)j["hr"] == 1,
            rightV      = j.Keys.Contains("rv")  ? (int)j["rv"]  : 0,
            rightJoker  = j.Keys.Contains("rj")  && (int)j["rj"] == 1,
            choiceIsLeft = j.Keys.Contains("cl") && (int)j["cl"] == 1,
            isSingleCard = j.Keys.Contains("sc") && (int)j["sc"] == 1,
        };
    }

    // ── Seed (초기 카드 뽑기 RNG 동기화) ──
    public static string MakeSeed(int seed)
    {
        var j = new JsonData();
        j["t"] = (int)PacketType.Seed;
        j["s"] = seed;
        return JsonMapper.ToJson(j);
    }

    public static int ParseSeed(JsonData j)
    {
        return (int)j["s"];
    }

    // ── Hello (선공 선출 핸드셰이크) ── 송신자 clientId 는 SendRaw 가 "from" 에 채움
    public static string MakeHello()
    {
        var j = new JsonData();
        j["t"] = (int)PacketType.Hello;
        return JsonMapper.ToJson(j);
    }

    // ── 수신 파싱 ──
    public static JsonData Parse(string raw) => JsonMapper.ToObject(raw);

    public static PacketType GetType(JsonData j) => (PacketType)(int)j["t"];
}
