using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────
//  버튼으로 판넬 열고 닫기
//  - 버튼 클릭 시 지정한 판넬을 on/off 토글
//  - 시작 시 판넬은 off
// ─────────────────────────────────────────────
public class BookOpen : MonoBehaviour
{
    [Header("─ 참조 ─")]
    [Tooltip("토글을 실행할 버튼")]
    public Button button;

    [Tooltip("판넬을 닫는 X 버튼 (눌러서 북 끄기)")]
    public Button closeButton;

    [Tooltip("켜고 끌 판넬")]
    public GameObject panel;

    void Awake()
    {
        if (panel != null) panel.SetActive(false);  // 기본값 off
    }

    void Start()
    {
        if (button != null) button.onClick.AddListener(Toggle);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
    }

    public void Toggle()
    {
        if (panel == null) return;
        panel.SetActive(!panel.activeSelf);
    }

    public void Close()
    {
        if (panel == null) return;
        panel.SetActive(false);
    }
}
