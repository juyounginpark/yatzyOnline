using System;
using UnityEngine;
using BackEnd;
using BackEnd.Tcp;
using LitJson;

// ─────────────────────────────────────────────
//  네트워크 상태
// ─────────────────────────────────────────────
public enum NetState
{
    Idle,
    LoggingIn,
    LoggedIn,
    ConnectingMatchServer,
    Matching,
    Matched,
    InGame,
    Disconnected,
}

// ─────────────────────────────────────────────
//  NetworkManager  (뒤끝 v5.18 인게임 멀티플레이)
//
//  API 정리 (DLL 분석 기준):
//    JoinMatchMakingServer(out ErrorInfo)         – 동기 요청, 완료는 OnJoinMatchMakingServer
//    RequestMatchMaking(MatchType, MatchModeType, matchCardIndate)
//    CancelMatchMaking()
//    JoinGameServer(addr, port, isReconnect, out ErrorInfo)
//    JoinGameRoom(roomToken)
//    SendDataToInGameRoom(byte[])
//    LeaveGameServer()  / LeaveMatchMakingServer()
// ─────────────────────────────────────────────
public class NetworkManager : MonoBehaviour
{
    public static NetworkManager Instance { get; private set; }

    [Header("─ 매칭 설정 (뒤끝 콘솔 확인) ─")]
    [Tooltip("매치 타입 (콘솔 > 인게임 멀티플레이 설정 참고)")]
    public MatchType     matchType     = MatchType.Random;
    public MatchModeType matchModeType = MatchModeType.OneOnOne;

    [Tooltip("뒤끝 콘솔에서 생성한 매치 카드 inDate 값")]
    public string matchCardIndate = "";

    // ─────────────────────────────────────────
    public NetState State   { get; private set; } = NetState.Idle;
    public bool     IsHost  { get; private set; } = false;
    // 게임 씬 로드 후 MainFlow에서 직접 읽을 수 있도록 저장
    public bool     IGoFirst { get; private set; } = false;

    // 클라이언트 고유 ID — IsRemote 대신 패킷 from 필드로 자신 패킷 구분
    private string _myClientId;

    // ── 결정적 선공 선출 (IsSuperGamer 가 양쪽 true 를 주는 경우 대비) ──
    // 서로 Hello 를 교환해 상대 clientId 를 알아낸 뒤, 더 작은 clientId 가 선공이 된다(양쪽 동일 계산).
    private string _oppClientId;
    private bool   _helloReplied;
    public bool    FirstResolved { get; private set; }

    // ─── 인게임 릴레이 큐 (이벤트 대신 폴링 방식) ───
    public readonly System.Collections.Generic.Queue<CardPlaceData> IncomingCardPlaces
        = new System.Collections.Generic.Queue<CardPlaceData>();
    public readonly System.Collections.Generic.Queue<int> IncomingCardReturns
        = new System.Collections.Generic.Queue<int>();
    public SlotCardData[] IncomingTurnEnd { get; private set; }
    public float          IncomingSenderHp   { get; private set; } = -1f;
    public float          IncomingReceiverHp { get; private set; } = -1f;
    public bool           IncomingFieldGuard { get; private set; } = false;
    public void ConsumeIncomingTurnEnd()
    {
        IncomingTurnEnd = null;
        IncomingSenderHp   = -1f;
        IncomingReceiverHp = -1f;
        IncomingFieldGuard = false;
    }

    // ─── Sync 패킷 (실시간 타이머+HP) ───
    public float IncomingSyncTimer      { get; private set; } = -1f;
    public float IncomingSyncSenderHp   { get; private set; } = -1f;
    public float IncomingSyncReceiverHp { get; private set; } = -1f;
    public bool  HasIncomingSync        { get; private set; }
    public void ConsumeIncomingSync() { HasIncomingSync = false; }

    // ─── 드래프트 동기화 (턴 플레이어 → 상대) ───
    public DraftData IncomingDraft     { get; private set; }
    public bool      HasIncomingDraft  { get; private set; }
    public void ConsumeIncomingDraft() { HasIncomingDraft = false; }

    // ─── Seed 동기화 (초기 카드 뽑기 RNG) ───
    public int  IncomingSeed     { get; private set; }
    public bool HasIncomingSeed  { get; private set; }
    public void ConsumeIncomingSeed() { HasIncomingSeed = false; }

    private string _pendingGameHost;
    private ushort _pendingGamePort;
    private string _pendingGameToken;
    private string _gameRoomToken;
    private bool   _pendingJoinRoom;
    private bool   _pendingGameStart;

    // ─── 게임 서버 재접속 ───
    private int   _joinRetryCount;
    private float _joinRetryTimer;
    private const int   MaxJoinRetries   = 3;
    private const float JoinRetryDelay   = 2f;

    // ─────────────────────────────────────────
    //  이벤트
    // ─────────────────────────────────────────
    public event Action               OnLoginSuccess;
    public event Action<string>       OnLoginFailed;
    public event Action               OnMatchServerConnected;
    public event Action               OnMatchFound;
    public event Action               OnGameReady;
    public event Action<bool>         OnGameOver;
    public event Action               OnOpponentDisconnected;

    // ─────────────────────────────────────────
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _myClientId = Guid.NewGuid().ToString("N").Substring(0, 8);
        Debug.Log($"[Net] 클라이언트 ID: {_myClientId}");
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ─────────────────────────────────────────
    void Update()
    {
        if (_pendingJoinRoom)
        {
            _pendingJoinRoom = false;
            Backend.Match.JoinGameRoom(_gameRoomToken);
            OnMatchFound?.Invoke();
        }

        if (_pendingGameStart)
        {
            _pendingGameStart = false;
            // 호스트/게스트 모두 여기서 씬 이동 — 릴레이 패킷에 의존하지 않음
            OnGameReady?.Invoke();
        }

        // 재접속 타이머
        if (_joinRetryTimer > 0f)
        {
            _joinRetryTimer -= Time.deltaTime;
            if (_joinRetryTimer <= 0f)
                TryJoinGameServer();
            return;
        }

        if (_pendingGameToken == null) return;

        _gameRoomToken    = _pendingGameToken;
        _pendingGameToken = null;
        _joinRetryCount   = 0;

        TryJoinGameServer();
    }

    // ─────────────────────────────────────────
    //  1. 게스트 로그인
    // ─────────────────────────────────────────
    public void GuestLogin()
    {
        if (State != NetState.Idle && State != NetState.Disconnected) return;

        State = NetState.LoggingIn;
        Debug.Log("[Net] 게스트 로그인 시도...");

        var bro = LoginAndEnsureNickname();
        if (!bro)
        {
            State = NetState.Disconnected;
            OnLoginFailed?.Invoke("Login failed");
            return;
        }

        State = NetState.LoggedIn;
        Debug.Log("[Net] 로그인 성공");
        OnLoginSuccess?.Invoke();
    }

    private bool LoginAndEnsureNickname()
    {
#if UNITY_EDITOR
        // 에디터에서는 dataPath 해시로 인스턴스별 고유 계정 사용 (ParrelSync 대응)
        string instanceId = "dev_" + System.Math.Abs(UnityEngine.Application.dataPath.GetHashCode()).ToString();
        string pw = "yatzy_dev_pw";
        var loginBro = Backend.BMember.CustomLogin(instanceId, pw);
        if (!loginBro.IsSuccess())
        {
            // 계정 없으면 회원가입 후 로그인
            var signupBro = Backend.BMember.CustomSignUp(instanceId, pw);
            if (!signupBro.IsSuccess())
            {
                Debug.LogError("[Net] 개발 계정 생성 실패: " + signupBro);
                return false;
            }
            loginBro = Backend.BMember.CustomLogin(instanceId, pw);
        }
        if (!loginBro.IsSuccess())
        {
            Debug.LogError("[Net] 개발 계정 로그인 실패: " + loginBro);
            return false;
        }
        Debug.Log("[Net] 개발 계정 로그인: " + instanceId);
#else
        var loginBro = Backend.BMember.GuestLogin();
        if (!loginBro.IsSuccess())
        {
            Debug.LogError("[Net] 로그인 실패: " + loginBro);
            return false;
        }
#endif
        // 닉네임 없으면 자동 설정 (매칭 서버 필수)
        var info = Backend.BMember.GetUserInfo();
        bool hasNickname = info.IsSuccess() &&
                           info.GetReturnValuetoJSON()?["row"]?["nickname"] != null &&
                           info.GetReturnValuetoJSON()["row"]["nickname"].ToString().Length > 0;

        if (!hasNickname)
        {
            string nick = "Player_" + UnityEngine.Random.Range(10000, 99999);
            Backend.BMember.UpdateNickname(nick);
            Debug.Log("[Net] 닉네임 설정: " + nick);
        }

        return true;
    }

    // ─────────────────────────────────────────
    //  2. 매칭 서버 연결
    //  JoinMatchMakingServer(out ErrorInfo) — 동기 호출
    //  연결 완료는 OnJoinMatchMakingServer 이벤트로 수신
    // ─────────────────────────────────────────
    public void ConnectMatchServer()
    {
        if (State != NetState.LoggedIn) return;

        State = NetState.ConnectingMatchServer;
        Debug.Log("[Net] 매칭 서버 연결 중...");

        // 중복 구독 방지 — 항상 -= 후 += 패턴
        Backend.Match.OnJoinMatchMakingServer -= OnJoinMatchServerHandler;
        Backend.Match.OnJoinMatchMakingServer += OnJoinMatchServerHandler;
        Backend.Match.OnMatchMakingRoomCreate -= OnMatchMakingRoomCreateHandler;
        Backend.Match.OnMatchMakingRoomCreate += OnMatchMakingRoomCreateHandler;
        Backend.Match.OnMatchMakingResponse   -= OnMatchMakingResponseHandler;
        Backend.Match.OnMatchMakingResponse   += OnMatchMakingResponseHandler;

        ErrorInfo errorInfo;
        Backend.Match.JoinMatchMakingServer(out errorInfo);

        if (errorInfo.Category != ErrorCode.Success)
        {
            Debug.LogError("[Net] 매칭 서버 연결 요청 실패: " + errorInfo);
            State = NetState.LoggedIn;
        }
    }

    private void OnJoinMatchServerHandler(JoinChannelEventArgs args)
    {
        if (args.ErrInfo.Category != ErrorCode.Success)
        {
            Debug.LogError("[Net] 매칭 서버 연결 실패: " + args.ErrInfo);
            State = NetState.LoggedIn;
            OnLoginFailed?.Invoke("Match server connect failed: " + args.ErrInfo);
            return;
        }
        Backend.Match.CreateMatchRoom();
    }

    private void OnMatchMakingRoomCreateHandler(MatchMakingInteractionEventArgs args)
    {
        if (args.ErrInfo != ErrorCode.Success)
        {
            Debug.LogError("[Net] 룸 생성 실패: " + args.Reason);
            State = NetState.LoggedIn;
            OnLoginFailed?.Invoke("Room create failed: " + args.Reason);
            return;
        }
        State = NetState.LoggedIn;
        Debug.Log("[Net] 룸 생성 완료");
        OnMatchServerConnected?.Invoke();
    }

    // ─────────────────────────────────────────
    //  3. 매칭 요청
    //  RequestMatchMaking(MatchType, MatchModeType, matchCardIndate)
    // ─────────────────────────────────────────
    public void RequestMatch()
    {
        if (State >= NetState.Matching) return;
        State = NetState.Matching;
        Debug.Log("[Net] 매칭 요청...");
        Backend.Match.RequestMatchMaking(matchType, matchModeType, matchCardIndate);
    }

    // ─────────────────────────────────────────
    //  4. 매칭 취소
    // ─────────────────────────────────────────
    public void CancelMatch()
    {
        if (State != NetState.Matching) return;
        Backend.Match.CancelMatchMaking();
        State = NetState.LoggedIn;
        Debug.Log("[Net] 매칭 취소");
    }

    // ─────────────────────────────────────────
    //  내부: 매칭 완료 콜백
    //  RoomInfo.m_inGameServerEndPoint → address/port
    //  RoomInfo.m_inGameRoomToken       → room token
    // ─────────────────────────────────────────
    private void OnMatchMakingResponseHandler(MatchMakingResponseEventArgs args)
    {
        if (args == null)
        {
            Debug.LogError("[Net] 매칭 응답 오류: args null");
            State = NetState.LoggedIn;
            return;
        }
        Debug.Log($"[Net] 매칭 응답 — ErrInfo: {args.ErrInfo} / Reason: {args.Reason} / RoomInfo: {(args.RoomInfo != null ? "OK" : "null")}");

        if (args.RoomInfo == null)
        {
            if (args.ErrInfo == ErrorCode.Match_InProgress)
            {
                Debug.Log("[Net] 매칭 진행 중...");
                return;
            }
            if (args.ErrInfo != ErrorCode.Success)
            {
                Debug.LogError($"[Net] 매칭 실패: {args.ErrInfo} / {args.Reason}");
                State = NetState.LoggedIn;
            }
            return;
        }

        Debug.Log("[Net] 매칭 완료! 게임 서버 연결 중...");
        State = NetState.Matched;

        string host  = args.RoomInfo.m_inGameServerEndPoint.m_address;
        ushort port  = args.RoomInfo.m_inGameServerEndPoint.m_port;
        string token = args.RoomInfo.m_inGameRoomToken;

        // 인게임 이벤트 등록 (중복 구독 방지)
        Backend.Match.OnSessionListInServer -= OnSessionListHandler;
        Backend.Match.OnSessionListInServer += OnSessionListHandler;
        Backend.Match.OnMatchRelay          -= OnRelayReceivedHandler;
        Backend.Match.OnMatchRelay          += OnRelayReceivedHandler;
        Backend.Match.OnSessionOffline      -= OnSessionOfflineHandler;
        Backend.Match.OnSessionOffline      += OnSessionOfflineHandler;

        // Poll 콜백 내부에서 직접 JoinGameServer 호출 시 SDK 충돌 가능
        // → 다음 Update 프레임에서 실행
        _pendingGameHost  = host;
        _pendingGamePort  = port;
        _pendingGameToken = token;
    }

    // ─────────────────────────────────────────
    //  내부: 게임 서버 접속 시도 (재시도 지원)
    // ─────────────────────────────────────────
    private void TryJoinGameServer()
    {
        _joinRetryCount++;
        Debug.Log($"[Net] 게임 서버 접속 시도 ({_joinRetryCount}/{MaxJoinRetries}) — {_pendingGameHost}:{_pendingGamePort}");

        Backend.Match.OnSessionJoinInServer -= OnGameServerJoinedHandler;
        Backend.Match.OnSessionJoinInServer += OnGameServerJoinedHandler;

        ErrorInfo errorInfo;
        Backend.Match.JoinGameServer(_pendingGameHost, _pendingGamePort, false, out errorInfo);

        if (errorInfo.Category != ErrorCode.Success)
        {
            Debug.LogError("[Net] 게임 서버 연결 실패: " + errorInfo);
            Backend.Match.OnSessionJoinInServer -= OnGameServerJoinedHandler;
            ScheduleRetryOrFail();
        }
    }

    private void ScheduleRetryOrFail()
    {
        if (_joinRetryCount < MaxJoinRetries)
        {
            Debug.LogWarning($"[Net] {JoinRetryDelay}초 후 재시도... ({_joinRetryCount}/{MaxJoinRetries})");
            _joinRetryTimer = JoinRetryDelay;
        }
        else
        {
            Debug.LogError("[Net] 게임 서버 접속 최종 실패 — 재시도 횟수 초과");
            State = NetState.Disconnected;
        }
    }

    // ─────────────────────────────────────────
    //  내부: 세션 목록 수신 (양쪽 입장 완료 = 게임 시작)
    // ─────────────────────────────────────────
    private void OnGameServerJoinedHandler(JoinChannelEventArgs args)
    {
        Backend.Match.OnSessionJoinInServer -= OnGameServerJoinedHandler;
        Debug.Log($"[Net] 게임 서버 접속 응답 — Category: {args.ErrInfo.Category}, Detail: {args.ErrInfo.Detail}");

        if (args.ErrInfo.Category != ErrorCode.Success)
        {
            Debug.LogError($"[Net] 게임 서버 접속 실패: {args.ErrInfo} (시도 {_joinRetryCount}/{MaxJoinRetries})");
            ScheduleRetryOrFail();
            return;
        }
        _pendingJoinRoom = true;
    }

    private void OnSessionListHandler(MatchInGameSessionListEventArgs args)
    {
        if (State == NetState.InGame)
        {
            Debug.Log("[Net] OnSessionList 중복 수신 무시 (이미 InGame)");
            return;
        }
        State   = NetState.InGame;
        // 잠정값(폴백): 선출 핸드셰이크가 끝나기 전/실패 시 사용. ResolveFirstPlayer 가 확정 덮어씀.
        IsHost  = Backend.Match.IsSuperGamer();
        IGoFirst = IsHost; // 호스트 = 선공, 게스트 = 후공 (잠정)
        Debug.Log($"[Net] 게임 시작! IsSuperGamer(잠정): IsHost:{IsHost}, IGoFirst:{IGoFirst}");
        _pendingGameStart = true; // 호스트/게스트 모두 씬 이동
    }

    // ─────────────────────────────────────────
    //  내부: 릴레이 데이터 수신
    //  MatchRelayEventArgs.BinaryUserData (byte[])
    // ─────────────────────────────────────────
    private void OnRelayReceivedHandler(MatchRelayEventArgs args)
    {
        if (args.BinaryUserData == null || args.BinaryUserData.Length == 0) return;

        string rawJson = System.Text.Encoding.UTF8.GetString(args.BinaryUserData);

        JsonData json;
        try { json = GamePacket.Parse(rawJson); }
        catch (Exception e) { Debug.LogError("[Net] JSON 파싱 실패: " + e.Message); return; }

        // from 필드로 자신이 보낸 패킷 무시
        if (json.Keys.Contains("from") && (string)json["from"] == _myClientId) return;

        Debug.Log($"[Net] 릴레이 수신 — type:{GamePacket.GetType(json)} from:{(json.Keys.Contains("from") ? (string)json["from"] : "?")}");

        switch (GamePacket.GetType(json))
        {
            case PacketType.TurnEnd:
                Debug.Log("[Net] TurnEnd 큐에 저장");
                IncomingTurnEnd = GamePacket.ParseTurnEnd(json);
                IncomingFieldGuard = GamePacket.ParseFieldGuard(json);
                float shp, rhp;
                GamePacket.ParseHp(json, out shp, out rhp);
                IncomingSenderHp   = shp;
                IncomingReceiverHp = rhp;
                break;

            case PacketType.CardPlace:
            {
                int si, v; bool joker;
                GamePacket.ParseCardPlace(json, out si, out v, out joker);
                Debug.Log($"[Net] CardPlace 큐에 저장 — slot:{si}");
                IncomingCardPlaces.Enqueue(new CardPlaceData
                    { slotIndex = si, value = v, isJoker = joker });
                break;
            }
            case PacketType.CardReturn:
                Debug.Log($"[Net] CardReturn 큐에 저장 — slot:{(int)json["si"]}");
                IncomingCardReturns.Enqueue((int)json["si"]);
                break;

            case PacketType.Sync:
            {
                float syncTm, syncShp, syncRhp;
                GamePacket.ParseSync(json, out syncTm, out syncShp, out syncRhp);
                IncomingSyncTimer      = syncTm;
                IncomingSyncSenderHp   = syncShp;
                IncomingSyncReceiverHp = syncRhp;
                HasIncomingSync        = true;
                break;
            }
            case PacketType.Draft:
                IncomingDraft    = GamePacket.ParseDraft(json);
                HasIncomingDraft = true;
                Debug.Log("[Net] Draft 수신");
                break;

            case PacketType.Seed:
                IncomingSeed    = GamePacket.ParseSeed(json);
                HasIncomingSeed = true;
                Debug.Log($"[Net] Seed 수신: {IncomingSeed}");
                break;

            case PacketType.GameOver:
                if (_gameOverHandled) break;  // 중복 발화 방지 (양측이 동시에 보낸 경우)
                _gameOverHandled = true;
                OnGameOver?.Invoke((int)json["win"] == 1);
                break;

            case PacketType.Hello:
            {
                // 상대 clientId = 패킷의 "from". 이걸 알면 선공을 결정적으로 정한다.
                string oppId = json.Keys.Contains("from") ? (string)json["from"] : null;
                if (!string.IsNullOrEmpty(oppId))
                {
                    _oppClientId = oppId;
                    ResolveFirstPlayer();
                    // 내 Hello 가 상대에게 안 닿았을 수 있으니 1회 답신 (핑퐁 방지: 1회만)
                    if (!_helloReplied) { _helloReplied = true; SendHello(); }
                }
                break;
            }
        }
    }

    // ── 선공 선출: 더 작은 clientId 가 선공(=호스트=시드 생성). 양쪽이 동일하게 계산. ──
    public void SendHello()
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeHello());
    }

    private void ResolveFirstPlayer()
    {
        if (string.IsNullOrEmpty(_myClientId) || string.IsNullOrEmpty(_oppClientId)) return;
        bool iAmFirst = string.CompareOrdinal(_myClientId, _oppClientId) < 0;
        IGoFirst = iAmFirst;
        IsHost   = iAmFirst;   // 시드 생성도 선공자가 담당 (양쪽 정확히 1명)
        FirstResolved = true;
        Debug.Log($"[Net] 선공 확정 — my:{_myClientId} opp:{_oppClientId} → IGoFirst:{IGoFirst}");
    }

    // ─────────────────────────────────────────
    //  내부: 상대 오프라인
    // ─────────────────────────────────────────
    private void OnSessionOfflineHandler(MatchInGameSessionEventArgs args)
    {
        Debug.LogWarning("[Net] 상대방 연결 끊김");
        OnOpponentDisconnected?.Invoke();
    }

    // ─────────────────────────────────────────
    //  5. 데이터 송신
    // ─────────────────────────────────────────
    public void SendTurnEnd(SlotCardData[] slots,
        float senderHp = -1f, float receiverHp = -1f, bool fieldGuard = false)
    {
        if (State != NetState.InGame) { Debug.LogWarning("[Net] SendTurnEnd 무시 — State:" + State); return; }
        Debug.Log($"[Net] TurnEnd 전송 — 슬롯 수: {slots?.Length ?? 0}, HP:{senderHp}/{receiverHp}, Guard:{fieldGuard}");
        SendRaw(GamePacket.MakeTurnEnd(slots, senderHp, receiverHp, fieldGuard));
    }

    public void SendSync(float timer, float senderHp, float receiverHp)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeSync(timer, senderHp, receiverHp));
    }

    public void SendCardPlace(int slotIndex, int value, bool isJoker = false)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeCardPlace(slotIndex, value, isJoker));
    }

    public void SendCardReturn(int slotIndex)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeCardReturn(slotIndex));
    }

    // 드래프트 결과 송신 (내 턴에 카드를 고른 뒤 호출)
    // me  = 내 패로 가져간 카드, opp = 상대 패로 넘긴 카드
    // left/right = 제시된 2장 전체 정보, choiceIsLeft = 왼쪽 선택 여부
    public void SendDraft(bool hasMe, int meV, bool meJoker,
                          bool hasOpp, int oppV, bool oppJoker,
                          bool hasLeft, int leftV, bool leftJoker,
                          bool hasRight, int rightV, bool rightJoker,
                          bool choiceIsLeft, bool isSingleCard = false)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeDraft(hasMe, meV, meJoker,
                                     hasOpp, oppV, oppJoker,
                                     hasLeft, leftV, leftJoker,
                                     hasRight, rightV, rightJoker,
                                     choiceIsLeft, isSingleCard));
    }

    // RNG Seed 송신 (호스트 → 게스트, 초기 카드 뽑기 동기화)
    public void SendSeed(int seed)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeSeed(seed));
    }

    // 정책: 패배측(HP 0 도달측)에서만 호출 — 양측이 동시 호출해도 _gameOverHandled로 중복 방지
    public void SendGameOver(bool opponentWins)
    {
        if (State != NetState.InGame || _gameOverHandled) return;
        _gameOverHandled = true;
        SendRaw(GamePacket.MakeGameOver(receiverWins: opponentWins));
    }

    private bool _gameOverHandled;

    private void SendRaw(string json)
    {
        // from 필드 삽입 — 수신 측에서 자신이 보낸 패킷인지 구분
        // 파싱 실패 시 from 누락 → 자기 에코 위험 → 송신 차단
        JsonData j;
        try { j = GamePacket.Parse(json); }
        catch (Exception e)
        {
            Debug.LogError($"[Net] SendRaw 패킷 파싱 실패 — 전송 취소 (자기 에코 방지): {e.Message}");
            return;
        }
        j["from"] = _myClientId;

        byte[] data = System.Text.Encoding.UTF8.GetBytes(JsonMapper.ToJson(j));
        Backend.Match.SendDataToInGameRoom(data);
    }

    // ─────────────────────────────────────────
    //  6. 연결 해제
    // ─────────────────────────────────────────
    public void Disconnect()
    {
        Backend.Match.OnJoinMatchMakingServer -= OnJoinMatchServerHandler;
        Backend.Match.OnMatchMakingRoomCreate -= OnMatchMakingRoomCreateHandler;
        Backend.Match.OnMatchMakingResponse   -= OnMatchMakingResponseHandler;
        Backend.Match.OnSessionListInServer   -= OnSessionListHandler;
        Backend.Match.OnMatchRelay            -= OnRelayReceivedHandler;
        Backend.Match.OnSessionOffline        -= OnSessionOfflineHandler;

        Backend.Match.LeaveGameServer();
        Backend.Match.LeaveMatchMakingServer();
        State = NetState.Disconnected;

        // 다음 매치로 이전 패킷이 새지 않도록 큐/상태 초기화
        ClearIncomingQueues();
    }

    private void ClearIncomingQueues()
    {
        IncomingCardPlaces.Clear();
        IncomingCardReturns.Clear();
        ConsumeIncomingTurnEnd();
        ConsumeIncomingSync();
        ConsumeIncomingDraft();
        ConsumeIncomingSeed();
        IncomingSyncTimer      = -1f;
        IncomingSyncSenderHp   = -1f;
        IncomingSyncReceiverHp = -1f;
        _gameOverHandled       = false;
    }
}
