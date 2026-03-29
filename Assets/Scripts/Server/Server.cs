using UnityEngine;
using BackEnd;
using BackEnd.Tcp;

// ─────────────────────────────────────────────
//  BackendManager
//  - 앱 시작 시 뒤끝 초기화 → 게스트 로그인 → NetworkManager 알림
//  - DontDestroyOnLoad 싱글톤 (씬이 바뀌어도 유지)
// ─────────────────────────────────────────────
public class BackendManager : MonoBehaviour
{
    public static BackendManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        SendQueue.Poll();
        if (NetworkManager.Instance != null && NetworkManager.Instance.State >= NetState.LoggedIn)
            Backend.Match.Poll();
    }

    void Start()
    {
        // 1. 뒤끝 SDK 초기화
        var bro = Backend.Initialize();

        if (bro.IsSuccess())
        {
            Debug.Log("[Backend] 초기화 성공");
            Login();
        }
        else
        {
            Debug.LogError("[Backend] 초기화 실패: " + bro);
        }
    }

    // ─────────────────────────────────────────
    //  게스트 로그인 → NetworkManager에 위임
    // ─────────────────────────────────────────
    private void Login()
    {
        if (NetworkManager.Instance == null)
        {
            Debug.LogError("[Backend] NetworkManager를 찾을 수 없습니다.");
            return;
        }

        NetworkManager.Instance.GuestLogin();
    }
}
