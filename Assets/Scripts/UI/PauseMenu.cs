using UnityEngine;
using UnityEngine.EventSystems;   // EventSystem (UI 선택 상태 관리)
using UnityEngine.InputSystem;    // 새 Input System (Keyboard.current)

/// <summary>
/// Esc로 일시정지 메뉴를 열고 닫는다.
/// 열려 있는 동안 게임 시간을 멈추고(Time.timeScale = 0) 커서를 풀어 준다.
/// 다른 스크립트는 PauseMenu.IsPaused를 보고 입력을 무시한다.
/// 이 스크립트는 항상 켜져 있는 오브젝트(UI Canvas)에 붙인다.
///
/// ★ 팝업(설정, 조작법)
/// - 메뉴 버튼으로 팝업을 연다. 한 번에 하나만 열린다.
/// - 팝업이 열려 있을 때 Esc를 누르면 팝업만 닫히고 메뉴로 돌아간다. (한 단계 뒤로)
/// - 메뉴만 열려 있을 때 Esc를 누르면 게임으로 돌아간다.
///
/// ★ 채팅 입력 중(ControlModeSwitcher.InputSuppressed)에는 Esc로 메뉴를 열지 않는다.
///   (채팅창이 Esc를 따로 쓸 수 있게 양보)
/// </summary>
public class PauseMenu : MonoBehaviour
{
    /// <summary>지금 일시정지 중인지. 다른 스크립트에서 읽기만 한다.</summary>
    public static bool IsPaused { get; private set; }

    [Header("메뉴")]
    [Tooltip("Hierarchy의 Pause_Menu 오브젝트 (켜고 끌 대상)")]
    [SerializeField] private GameObject menuRoot;

    // ★ 추가: 팝업 두 개
    [Header("팝업")]
    [Tooltip("설정 팝업 (Setting_Popup)")]
    [SerializeField] private GameObject settingsPopup;

    [Tooltip("조작법 팝업 (Controls_Popup)")]
    [SerializeField] private GameObject controlsPopup;

    // ★ 추가: 채팅 입력 중인지 알기 위해 팀원이 만든 ControlModeSwitcher를 연결
    [Header("다른 시스템 (선택)")]
    [Tooltip("Hierarchy의 ModeSwitcher 오브젝트. 비워 두면 채팅 확인을 건너뛴다.")]
    [SerializeField] private ControlModeSwitcher modeSwitcher;

    // ★ 추가: 지금 열려 있는 팝업. 열린 팝업이 없으면 null.
    private GameObject openPopup;

    private void Awake()
    {
        // 플레이를 시작할 때 항상 "일시정지 아님" 상태로 맞춘다.
        IsPaused = false;
        Time.timeScale = 1f;
        HidePopups();                                   // ★ 추가: 팝업도 모두 숨김
        if (menuRoot != null) menuRoot.SetActive(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

        // ★ 추가: 게임 중이면서 채팅 입력 중이면, 메뉴를 열지 않는다.
        if (!IsPaused && IsChatting()) return;

        // ★ 추가: 팝업이 열려 있으면 팝업만 닫고 끝낸다. (메뉴는 그대로)
        if (openPopup != null)
        {
            ClosePopup();
            return;
        }

        if (IsPaused) Resume();
        else Pause();
    }

    /// <summary>메뉴 열기: 시간 정지, 커서 표시</summary>
    public void Pause()
    {
        IsPaused = true;
        Time.timeScale = 0f;
        menuRoot.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>메뉴 닫기: 시간 재개, 커서 다시 잠금 ("이어서 하기" 버튼에도 연결)</summary>
    public void Resume()
    {
        HidePopups();                                   // ★ 추가: 열려 있던 팝업도 닫음
        IsPaused = false;
        Time.timeScale = 1f;
        menuRoot.SetActive(false);

        ClearSelection();                               // ★ 변경: 아래 함수로 옮김

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ★ 추가: "설정" 버튼에 연결
    public void OpenSettings()
    {
        OpenPopup(settingsPopup);
    }

    // ★ 추가: "조작방법" 버튼에 연결
    public void OpenControls()
    {
        OpenPopup(controlsPopup);
    }

    // ★ 추가: 팝업의 X 버튼에 연결. 열려 있는 팝업을 닫고 메뉴로 돌아간다.
    public void ClosePopup()
    {
        if (openPopup == null) return;   // 열린 팝업이 없으면 할 일 없음

        openPopup.SetActive(false);
        openPopup = null;
        ClearSelection();
    }

    /// <summary>"게임 종료" 버튼에 연결</summary>
    public void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;   // 에디터에서는 플레이만 멈춤
#else
        Application.Quit();                                // 빌드된 게임은 종료
#endif
    }

    // ★ 추가: 팝업 하나를 연다. 다른 팝업이 열려 있었으면 먼저 닫는다.
    private void OpenPopup(GameObject popup)
    {
        if (popup == null)
        {
            // Inspector 칸을 비워 둔 경우. 오류 대신 경고만 남기고 넘어간다.
            Debug.LogWarning("[PauseMenu] 팝업 칸이 비어 있습니다. Inspector에서 연결해 주세요.", this);
            return;
        }

        ClosePopup();            // 열려 있던 팝업 닫기 (한 번에 하나만)
        popup.SetActive(true);
        openPopup = popup;
        ClearSelection();
    }

    // ★ 추가: 팝업을 모두 숨긴다.
    private void HidePopups()
    {
        if (settingsPopup != null) settingsPopup.SetActive(false);
        if (controlsPopup != null) controlsPopup.SetActive(false);
        openPopup = null;
    }

    // ★ 추가: 채팅 입력 중이면 true
    private bool IsChatting()
    {
        return modeSwitcher != null && modeSwitcher.InputSuppressed;
    }

    // ★ 추가(옮김): 버튼이 파란 "선택됨" 상태로 남지 않게 선택 해제
    private void ClearSelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void OnDestroy()
    {
        // 씬이 바뀌거나 끝날 때 멈춘 상태가 남지 않게
        IsPaused = false;
        Time.timeScale = 1f;
    }
}