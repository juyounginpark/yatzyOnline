using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────
//  BettingUI — 플레이어 베팅 액션 입력
//  - 체크/벳/레이즈/콜/폴드/올인 버튼 + 베팅액 슬라이더
//  - 합법 액션만 활성화, 액션 선택 시 OnAction(action, sizeAmount) 발생
//    · Bet  : sizeAmount = 새로 거는 칩
//    · Raise: sizeAmount = 콜 위에 추가로 올리는 칩
//    · 그 외: sizeAmount 무시
// ─────────────────────────────────────────────
public class BettingUI : MonoBehaviour
{
    [Header("─ 루트 (전체 표시/숨김) ─")]
    public GameObject root;

    [Header("─ 버튼 ─")]
    public Button checkButton;
    public Button callButton;
    public Button betButton;
    public Button raiseButton;
    public Button foldButton;
    public Button allInButton;

    [Header("─ 베팅액 ─")]
    [Tooltip("벳/레이즈 크기 슬라이더")]
    public Slider sizeSlider;

    [Tooltip("현재 슬라이더 금액 표시")]
    public TMP_Text sizeText;

    [Tooltip("콜 금액/팟 정보 표시 (선택)")]
    public TMP_Text infoText;

    [Tooltip("상대 액션 메시지 표시 (예: '상대 벳 100') (선택)")]
    public TMP_Text statusText;

    [Header("─ 샷클락 표시 (선택) ─")]
    [Tooltip("남은 시간 게이지 (0~1로 채워짐)")]
    public Slider timerBar;

    [Tooltip("남은 초 텍스트")]
    public TMP_Text timerText;

    [Tooltip("슬라이더 눈금 단위 (이 배수로 스냅)")]
    public int sizeStep = 50;

    /// <summary>플레이어가 액션을 선택했을 때 발생. (action, sizeAmount)</summary>
    public event Action<BetAction, int> OnAction;

    void Awake()
    {
        Bind(checkButton,  () => Fire(BetAction.Check));
        Bind(callButton,   () => Fire(BetAction.Call));
        Bind(betButton,    () => Fire(BetAction.Bet,   SliderAmount()));
        Bind(raiseButton,  () => Fire(BetAction.Raise, SliderAmount()));
        Bind(foldButton,   () => Fire(BetAction.Fold));
        Bind(allInButton,  () => Fire(BetAction.AllIn));

        if (sizeSlider != null) sizeSlider.onValueChanged.AddListener(_ => RefreshSizeText());

        Hide();
    }

    private void Bind(Button b, Action act)
    {
        if (b != null) b.onClick.AddListener(() => act());
    }

    private void Fire(BetAction a, int amt = 0)
    {
        if (SoundManager.Instance != null) SoundManager.Instance.PlaySFX(SoundManager.Instance.uiClick);
        Hide();
        OnAction?.Invoke(a, amt);
    }

    private int SliderAmount()
    {
        if (sizeSlider == null) return sizeStep;
        int raw = Mathf.RoundToInt(sizeSlider.value);
        if (sizeStep > 1) raw = Mathf.Max(sizeStep, (raw / sizeStep) * sizeStep);
        return raw;
    }

    private void RefreshSizeText()
    {
        if (sizeText != null) sizeText.text = SliderAmount().ToString();
    }

    // ─────────────────────────────────────────
    //  합법 액션에 맞춰 버튼/슬라이더 구성 후 표시
    //  toCall : 콜에 필요한 금액 (0이면 체크/벳 가능)
    //  stack  : 플레이어 보유 LP
    //  pot    : 현재 팟
    // ─────────────────────────────────────────
    public void Show(int toCall, int stack, int pot)
    {
        if (root != null) root.SetActive(true);

        bool facingBet = toCall > 0;

        SetActive(checkButton,  !facingBet);
        SetActive(betButton,    !facingBet && stack > 0);
        SetActive(callButton,   facingBet && stack > 0);
        SetActive(raiseButton,  facingBet && stack > toCall);
        SetActive(foldButton,   facingBet);
        SetActive(allInButton,  stack > 0);

        // 슬라이더 범위: 벳/레이즈 사이즈 (최소 단위 ~ 콜 이후 남는 스택)
        if (sizeSlider != null)
        {
            int maxSize = facingBet ? Mathf.Max(0, stack - toCall) : stack;
            sizeSlider.minValue = 0;
            sizeSlider.maxValue = Mathf.Max(sizeStep, maxSize);
            sizeSlider.value    = Mathf.Min(sizeSlider.maxValue, sizeStep);
        }
        RefreshSizeText();

        if (infoText != null)
            infoText.text = facingBet ? $"콜 {toCall}  /  팟 {pot}" : $"팟 {pot}";
    }

    public void Hide()
    {
        if (root != null) root.SetActive(false);
        HideTimer();
    }

    /// <summary>샷클락 남은 시간 표시 (remaining/total). </summary>
    public void SetTimer(float remaining, float total)
    {
        float r = Mathf.Max(0f, remaining);
        if (timerBar != null)
        {
            if (!timerBar.gameObject.activeSelf) timerBar.gameObject.SetActive(true);
            timerBar.value = total > 0f ? Mathf.Clamp01(r / total) : 0f;
        }
        if (timerText != null)
        {
            if (!timerText.gameObject.activeSelf) timerText.gameObject.SetActive(true);
            timerText.text = Mathf.CeilToInt(r).ToString();
        }
    }

    public void HideTimer()
    {
        if (timerBar != null)  timerBar.gameObject.SetActive(false);
        if (timerText != null) timerText.gameObject.SetActive(false);
    }

    /// <summary>상대 액션 등 메시지 표시 (패널 표시 여부와 무관하게 갱신).</summary>
    public void ShowMessage(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    private void SetActive(Button b, bool on)
    {
        if (b != null) b.gameObject.SetActive(on);
    }
}
