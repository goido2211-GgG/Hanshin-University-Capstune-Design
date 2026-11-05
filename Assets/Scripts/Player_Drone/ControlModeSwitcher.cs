using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tab 키로 사람 ↔ 드론 조작을 전환한다.
/// 한쪽을 조작하는 동안 다른 쪽은 멈춘다.
/// laptop을 지정하면 노트북 가까이에서만 드론으로 전환할 수 있다. (비워 두면 어디서든 가능)
/// </summary>
public class ControlModeSwitcher : MonoBehaviour
{
    private enum Mode { Player, Drone }

    [Header("Player")]
    [SerializeField] private PlayerController player;
    [SerializeField] private Camera playerCamera;

    [Header("Drone")]
    [SerializeField] private DroneController drone;
    [SerializeField] private Camera droneCamera;

    [Header("Laptop (선택)")]
    [SerializeField] private Transform laptop;
    [SerializeField] private float laptopUseDistance = 2f;

    private Mode mode = Mode.Player;

    private void Start()
    {
        Apply(Mode.Player);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.tabKey.wasPressedThisFrame) return;

        if (mode == Mode.Player)
        {
            if (CanUseLaptop()) Apply(Mode.Drone);
        }
        else
        {
            Apply(Mode.Player);   // 드론 상태에서는 어디서든 사람으로 복귀
        }
    }

    private bool CanUseLaptop()
    {
        if (laptop == null) return true;
        return Vector3.Distance(player.transform.position, laptop.position) <= laptopUseDistance;
    }

    private void Apply(Mode newMode)
    {
        mode = newMode;
        bool isPlayer = newMode == Mode.Player;

        player.enabled = isPlayer;
        drone.enabled = !isPlayer;

        SetCamera(playerCamera, isPlayer);
        SetCamera(droneCamera, !isPlayer);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private static void SetCamera(Camera cam, bool active)
    {
        if (cam == null) return;
        cam.enabled = active;

        AudioListener listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = active;
    }
}
