using System;
using LitJson;

// ─────────────────────────────────────────────
//  패킷 타입
// ─────────────────────────────────────────────
public enum PacketType
{
    GameReady   = 0,
    TurnEnd     = 1,
    CardPlace   = 2,
    CardReturn  = 3,
    GameOver    = 4,
    Sync        = 5,
}

// ─────────────────────────────────────────────
//  카드 배치 데이터
// ─────────────────────────────────────────────
[Serializable]
public struct CardPlaceData
{
    public int slotIndex, value;
    public CardType cardType;
    public bool isJoker;
}

// ─────────────────────────────────────────────
//  슬롯 카드 데이터
// ─────────────────────────────────────────────
[Serializable]
public struct SlotCardData
{
    public int  slotIndex;
    public int  value;
    public int  cardType;   // (int)CardType
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
    // ── GameReady ──
    public static string MakeGameReady(bool receiverGoesFirst)
    {
        var j = new JsonData();
        j["t"]     = (int)PacketType.GameReady;
        j["first"] = receiverGoesFirst ? 1 : 0;
        return JsonMapper.ToJson(j);
    }

    public static bool ParseGameReady(JsonData j, out bool receiverGoesFirst)
    {
        receiverGoesFirst = (int)j["first"] == 1;
        return true;
    }

    // ── TurnEnd ──
    public static string MakeTurnEnd(SlotCardData[] slots, int[] chainLockIndices = null,
        float senderHp = -1f, float receiverHp = -1f)
    {
        var j = new JsonData();
        j["t"]     = (int)PacketType.TurnEnd;
        j["slots"] = new JsonData();
        j["slots"].SetJsonType(JsonType.Array);

        foreach (var s in slots)
        {
            if (s.value <= 0) continue;
            var entry = new JsonData();
            entry["si"] = s.slotIndex;
            entry["v"]  = s.value;
            entry["ct"] = s.cardType;
            entry["j"]  = s.isJoker ? 1 : 0;
            j["slots"].Add(entry);
        }

        // 체인 잠금 대상 슬롯 인덱스
        if (chainLockIndices != null && chainLockIndices.Length > 0)
        {
            j["cl"] = new JsonData();
            j["cl"].SetJsonType(JsonType.Array);
            foreach (int idx in chainLockIndices)
                j["cl"].Add(idx);
        }

        // HP 동기화
        if (senderHp >= 0f)   j["shp"] = (int)senderHp;
        if (receiverHp >= 0f) j["rhp"] = (int)receiverHp;

        return JsonMapper.ToJson(j);
    }

    /// <summary>HP 값 파싱 — senderHp, receiverHp (없으면 -1)</summary>
    public static void ParseHp(JsonData j, out float senderHp, out float receiverHp)
    {
        senderHp   = j.Keys.Contains("shp") ? (float)(int)j["shp"] : -1f;
        receiverHp = j.Keys.Contains("rhp") ? (float)(int)j["rhp"] : -1f;
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
                cardType  = (int)arr[i]["ct"],
                isJoker   = (int)arr[i]["j"] == 1,
            };
        }
        return result;
    }

    public static int[] ParseChainLockIndices(JsonData j)
    {
        if (!j.Keys.Contains("cl")) return null;
        var arr = j["cl"];
        var result = new int[arr.Count];
        for (int i = 0; i < arr.Count; i++)
            result[i] = (int)arr[i];
        return result;
    }

    // ── CardPlace ──
    public static string MakeCardPlace(int slotIndex, int value, CardType type, bool isJoker = false)
    {
        var j = new JsonData();
        j["t"]  = (int)PacketType.CardPlace;
        j["si"] = slotIndex;
        j["v"]  = value;
        j["ct"] = (int)type;
        j["j"]  = isJoker ? 1 : 0;
        return JsonMapper.ToJson(j);
    }

    public static bool ParseCardPlace(JsonData j,
        out int slotIndex, out int value, out CardType type, out bool isJoker)
    {
        slotIndex = (int)j["si"];
        value     = (int)j["v"];
        type      = (CardType)(int)j["ct"];
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
        j["shp"] = (int)senderHp;
        j["rhp"] = (int)receiverHp;
        return JsonMapper.ToJson(j);
    }

    public static void ParseSync(JsonData j, out float timer, out float senderHp, out float receiverHp)
    {
        timer      = (int)j["tm"] / 100f;
        senderHp   = (float)(int)j["shp"];
        receiverHp = (float)(int)j["rhp"];
    }

    // ── 수신 파싱 ──
    public static JsonData Parse(string raw) => JsonMapper.ToObject(raw);

    public static PacketType GetType(JsonData j) => (PacketType)(int)j["t"];
}
