using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Esc로 일시정지 메뉴를 열고 닫는다.
/// 열려 있는 동안 게임 시간을 멈추고(Time.timeScale = 0) 커서를 풀어 준다.
/// 다른 스크립트는 PauseMenu.IsPaused를 보고 입력을 무시한다.
/// 이 스크립트는 항상 켜져 있는 오브젝트(UI Canvas)에 붙인다.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    /// <summary>지금 일시정지 중인지. 다른 스크립트에서 읽기만 한다.</summary>
    public static bool IsPaused { get; private set; }

    [SerializeField] private GameObject menuRoot;   // Hierarchy의 PauseMenu 오브젝트

    private void Awake()
    {
        // 플레이를 시작할 때 항상 "일시정지 아님" 상태로 맞춘다.
        IsPaused = false;
        Time.timeScale = 1f;
        if (menuRoot != null) menuRoot.SetActive(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame) return;

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
        IsPaused = false;
        Time.timeScale = 1f;
        menuRoot.SetActive(false);

        // 버튼이 파란 "선택됨" 상태로 남지 않게 선택 해제
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
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

    private void OnDestroy()
    {
        // 씬이 바뀌거나 끝날 때 멈춘 상태가 남지 않게
        IsPaused = false;
        Time.timeScale = 1f;
    }
}