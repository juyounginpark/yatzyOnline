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

    // ─── 인게임 릴레이 큐 (이벤트 대신 폴링 방식) ───
    public readonly System.Collections.Generic.Queue<CardPlaceData> IncomingCardPlaces
        = new System.Collections.Generic.Queue<CardPlaceData>();
    public readonly System.Collections.Generic.Queue<int> IncomingCardReturns
        = new System.Collections.Generic.Queue<int>();
    public SlotCardData[] IncomingTurnEnd { get; private set; }
    public void ConsumeIncomingTurnEnd() => IncomingTurnEnd = null;

    // 하위 호환 — 이전 코드에서 참조하는 경우 대비
    public SlotCardData[] PendingTurnEnd => IncomingTurnEnd;
    public void ConsumePendingTurnEnd() => ConsumeIncomingTurnEnd();

    private string _pendingGameHost;
    private ushort _pendingGamePort;
    private string _pendingGameToken;
    private string _gameRoomToken;
    private bool   _pendingJoinRoom;
    private bool   _pendingGameStart;

    // ─────────────────────────────────────────
    //  이벤트
    // ─────────────────────────────────────────
    public event Action               OnLoginSuccess;
    public event Action<string>       OnLoginFailed;
    public event Action               OnMatchServerConnected;
    public event Action               OnMatchFound;
    public event Action               OnGameReady;
    public event Action<bool>         OnGoFirstDecided;
    public event Action<SlotCardData[]> OnOpponentTurnEnd;
    public event Action<int, int, CardType, bool> OnOpponentCardPlaced;
    public event Action<int>          OnOpponentCardReturned;
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
            OnGoFirstDecided?.Invoke(IGoFirst);
            OnGameReady?.Invoke();
        }

        if (_pendingGameToken == null) return;

        string host  = _pendingGameHost;
        ushort port  = _pendingGamePort;
        _gameRoomToken    = _pendingGameToken;
        _pendingGameToken = null;

        Backend.Match.OnSessionJoinInServer += OnGameServerJoinedHandler;

        ErrorInfo errorInfo;
        Backend.Match.JoinGameServer(host, port, false, out errorInfo);

        if (errorInfo.Category != ErrorCode.Success)
        {
            Debug.LogError("[Net] 게임 서버 연결 실패: " + errorInfo);
            Backend.Match.OnSessionJoinInServer -= OnGameServerJoinedHandler;
            State = NetState.Disconnected;
        }
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

        Backend.Match.OnJoinMatchMakingServer += OnJoinMatchServerHandler;
        Backend.Match.OnMatchMakingRoomCreate += OnMatchMakingRoomCreateHandler;
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

        // 인게임 이벤트 등록
        Backend.Match.OnSessionListInServer += OnSessionListHandler;
        Backend.Match.OnMatchRelay          += OnRelayReceivedHandler;
        Backend.Match.OnSessionOffline      += OnSessionOfflineHandler;

        // Poll 콜백 내부에서 직접 JoinGameServer 호출 시 SDK 충돌 가능
        // → 다음 Update 프레임에서 실행
        _pendingGameHost  = host;
        _pendingGamePort  = port;
        _pendingGameToken = token;
    }

    // ─────────────────────────────────────────
    //  내부: 세션 목록 수신 (양쪽 입장 완료 = 게임 시작)
    // ─────────────────────────────────────────
    private void OnGameServerJoinedHandler(JoinChannelEventArgs args)
    {
        Backend.Match.OnSessionJoinInServer -= OnGameServerJoinedHandler;
        Debug.Log("[Net] 게임 서버 접속 완료 — ErrInfo: " + args.ErrInfo.Category);
        if (args.ErrInfo.Category != ErrorCode.Success)
        {
            Debug.LogError("[Net] 게임 서버 접속 실패: " + args.ErrInfo);
            State = NetState.Disconnected;
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
        IsHost  = Backend.Match.IsSuperGamer();
        IGoFirst = IsHost; // 호스트 = 선공, 게스트 = 후공
        Debug.Log($"[Net] 게임 시작! IsHost: {IsHost}, IGoFirst: {IGoFirst}");
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
            case PacketType.GameReady:
                break; // 씬 이동은 OnSessionListInServer에서 처리 — 릴레이 불필요

            case PacketType.TurnEnd:
                Debug.Log("[Net] TurnEnd 큐에 저장");
                IncomingTurnEnd = GamePacket.ParseTurnEnd(json);
                break;

            case PacketType.CardPlace:
            {
                int si, v; CardType ct; bool joker;
                GamePacket.ParseCardPlace(json, out si, out v, out ct, out joker);
                Debug.Log($"[Net] CardPlace 큐에 저장 — slot:{si}");
                IncomingCardPlaces.Enqueue(new CardPlaceData
                    { slotIndex = si, value = v, cardType = ct, isJoker = joker });
                break;
            }
            case PacketType.CardReturn:
                Debug.Log($"[Net] CardReturn 큐에 저장 — slot:{(int)json["si"]}");
                IncomingCardReturns.Enqueue((int)json["si"]);
                break;

            case PacketType.GameOver:
                OnGameOver?.Invoke((int)json["win"] == 1);
                break;
        }
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
    public void SendTurnEnd(SlotCardData[] slots)
    {
        if (State != NetState.InGame) { Debug.LogWarning("[Net] SendTurnEnd 무시 — State:" + State); return; }
        Debug.Log($"[Net] TurnEnd 전송 — 슬롯 수: {slots?.Length ?? 0}");
        SendRaw(GamePacket.MakeTurnEnd(slots));
    }

    public void SendCardPlace(int slotIndex, int value, CardType type, bool isJoker = false)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeCardPlace(slotIndex, value, type, isJoker));
    }

    public void SendCardReturn(int slotIndex)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeCardReturn(slotIndex));
    }

    public void SendGameOver(bool opponentWins)
    {
        if (State != NetState.InGame) return;
        SendRaw(GamePacket.MakeGameOver(receiverWins: opponentWins));
    }

    private void SendRaw(string json)
    {
        // from 필드 삽입 — 수신 측에서 자신이 보낸 패킷인지 구분
        try
        {
            var j = GamePacket.Parse(json);
            j["from"] = _myClientId;
            json = JsonMapper.ToJson(j);
        }
        catch { /* 파싱 실패 시 원본 그대로 전송 */ }

        byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
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
    }
}
