using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// ─────────────────────────────────────────────
//  MatchmakingUI
//  빈 씬에 이 스크립트 하나만 붙이면 UI가 자동 생성됩니다.
//  (Canvas, 배경, 버튼, 텍스트 모두 코드로 생성)
// ─────────────────────────────────────────────
public class MatchmakingUI : MonoBehaviour
{
    [Header("─ 씬 이름 ─")]
    public string gameSceneName = "Game";

    // ─── 런타임 생성 UI 참조 ───
    private TMP_Text _statusText;
    private Button   _findMatchButton;
    private Button   _cancelButton;
    private TMP_Text _findMatchLabel;

    // ─────────────────────────────────────────
    void Awake()
    {
        BuildUI();
    }

    void Start()
    {
        SetStatus("Logging in...");
        SetFindInteractable(false);
        _cancelButton.gameObject.SetActive(false);

        _findMatchButton.onClick.AddListener(OnFindMatch);
        _cancelButton.onClick.AddListener(OnCancel);

        if (NetworkManager.Instance == null)
        {
            SetStatus("NetworkManager not found.");
            return;
        }

        NetworkManager.Instance.OnLoginSuccess         += OnLoginSuccess;
        NetworkManager.Instance.OnLoginFailed          += OnLoginFailed;
        NetworkManager.Instance.OnMatchServerConnected += OnMatchServerConnected;
        NetworkManager.Instance.OnMatchFound           += OnMatchFound;
        NetworkManager.Instance.OnGameReady            += OnGameReady;

        if (NetworkManager.Instance.State >= NetState.LoggedIn)
            ConnectAndReady();
        // 로그인은 BackendManager가 담당 — OnLoginSuccess 이벤트로 수신
    }

    void OnDestroy()
    {
        if (NetworkManager.Instance == null) return;
        NetworkManager.Instance.OnLoginSuccess         -= OnLoginSuccess;
        NetworkManager.Instance.OnLoginFailed          -= OnLoginFailed;
        NetworkManager.Instance.OnMatchServerConnected -= OnMatchServerConnected;
        NetworkManager.Instance.OnMatchFound           -= OnMatchFound;
        NetworkManager.Instance.OnGameReady            -= OnGameReady;
    }

    // ─────────────────────────────────────────
    //  이벤트 핸들러
    // ─────────────────────────────────────────
    private void OnLoginSuccess()       => ConnectAndReady();
    private void OnLoginFailed(string m) => SetStatus("Login failed: " + m);

    private void ConnectAndReady()
    {
        SetStatus("Connecting to match server...");
        NetworkManager.Instance.ConnectMatchServer();
    }

    private void OnMatchServerConnected()
    {
        SetStatus("Ready to find match");
        SetFindInteractable(true);
    }

    private void OnFindMatch()
    {
        SetStatus("Searching for opponent...");
        SetFindInteractable(false);
        _cancelButton.gameObject.SetActive(true);
        NetworkManager.Instance.RequestMatch();
    }

    private void OnCancel()
    {
        NetworkManager.Instance.CancelMatch();
        SetStatus("Matchmaking cancelled");
        SetFindInteractable(true);
        _cancelButton.gameObject.SetActive(false);
    }

    private void OnMatchFound()
    {
        SetStatus("Match found! Preparing game...");
        _cancelButton.gameObject.SetActive(false);
    }

    private void OnGameReady()
    {
        // 이미 게임 씬에 있으면 무시 (OnSessionList 중복 발생 대비)
        if (SceneManager.GetActiveScene().name == gameSceneName) return;
        SetStatus("Game starting!");
        SceneManager.LoadScene(gameSceneName);
    }

    // ─────────────────────────────────────────
    //  헬퍼
    // ─────────────────────────────────────────
    private void SetStatus(string msg)
    {
        if (_statusText != null) _statusText.text = msg;
        Debug.Log("[Matchmaking] " + msg);
    }

    private void SetFindInteractable(bool v)
    {
        if (_findMatchButton != null) _findMatchButton.interactable = v;
    }

    // ─────────────────────────────────────────
    //  UI 자동 생성
    // ─────────────────────────────────────────
    private void BuildUI()
    {
        // ── Canvas ──
        var canvasGo = new GameObject("MatchmakingCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode =
            CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1080, 1920);
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 배경 패널 ──
        var panel = MakeImage(canvasGo, "Panel", new Color(0.08f, 0.08f, 0.12f));
        RectFill(panel);

        // ── 타이틀 ──
        var title = MakeText(panel, "Title", "YATZY ONLINE", 72, Color.white);
        Rect(title, 0f, 0.72f, 0.9f, 0.12f);

        // ── 상태 텍스트 ──
        var status = MakeText(panel, "StatusText", "Connecting...", 38, new Color(0.8f, 0.8f, 0.8f));
        Rect(status, 0f, 0.56f, 0.85f, 0.08f);
        _statusText = status.GetComponent<TMP_Text>();

        // ── 매치 찾기 버튼 ──
        _findMatchButton = MakeButton(panel, "FindMatchBtn", "Find Match",
            new Color(0.2f, 0.6f, 1f), new Color(0.15f, 0.45f, 0.8f));
        Rect(_findMatchButton.gameObject, 0f, 0.40f, 0.55f, 0.09f);
        _findMatchLabel = _findMatchButton.GetComponentInChildren<TMP_Text>();

        // ── 취소 버튼 ──
        _cancelButton = MakeButton(panel, "CancelBtn", "Cancel",
            new Color(0.7f, 0.2f, 0.2f), new Color(0.55f, 0.15f, 0.15f));
        Rect(_cancelButton.gameObject, 0f, 0.29f, 0.4f, 0.08f);

        // ── 로딩 점 (스피너 대신 간단히) ──
        var dots = MakeText(panel, "Dots", "", 50, new Color(0.5f, 0.8f, 1f));
        Rect(dots, 0f, 0.20f, 0.3f, 0.06f);
        dots.AddComponent<DotAnimator>();
    }

    // ── UI 생성 헬퍼 ──

    private GameObject MakeImage(GameObject parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        return go;
    }

    private GameObject MakeText(GameObject parent, string name, string text, int size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        return go;
    }

    private Button MakeButton(GameObject parent, string name, string label,
        Color normalColor, Color pressedColor)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var img = go.AddComponent<Image>();
        img.color = normalColor;

        var btn = go.AddComponent<Button>();
        var colors = btn.colors;
        colors.normalColor      = normalColor;
        colors.highlightedColor = Color.Lerp(normalColor, Color.white, 0.2f);
        colors.pressedColor     = pressedColor;
        colors.disabledColor    = new Color(0.4f, 0.4f, 0.4f);
        btn.colors = colors;

        var labelGo = MakeText(go, "Label", label, 44, Color.white);
        RectFill(labelGo);

        return btn;
    }

    // anchorCenter + anchoredPosition 기반 배치
    // anchorY: 0=하단, 1=상단 (normalized)
    private void Rect(GameObject go, float anchorX, float anchorY, float w, float h)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f + anchorX - w * 0.5f, anchorY);
        rt.anchorMax = new Vector2(0.5f + anchorX + w * 0.5f, anchorY + h);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void RectFill(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}

// ─────────────────────────────────────────────
//  "..." 로딩 점 애니메이션 (TMP_Text에 붙임)
// ─────────────────────────────────────────────
public class DotAnimator : MonoBehaviour
{
    private TMP_Text _text;
    private float _timer;
    private int _count;

    void Awake() => _text = GetComponent<TMP_Text>();

    void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < 0.4f) return;
        _timer = 0f;
        _count = (_count + 1) % 4;
        _text.text = new string('.', _count);
    }
}
