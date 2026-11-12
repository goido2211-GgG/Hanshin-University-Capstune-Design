using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>향후 좌석/단말 조건을 연결하는 선택적 드론 진입 규칙.</summary>
public interface IDroneAccessPolicy
{
    bool CanEnterDrone(PlayerController player, out string reason);
}

/// <summary>사람/드론 조작과 카메라, AudioListener, 커서 상태를 관리한다.</summary>
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

    [Header("통합 입력 (기존 씬은 기본값 유지)")]
    public bool externalInput;
    [Tooltip("비워 두면 기존 노트북 거리 조건만 사용합니다.")]
    public MonoBehaviour droneAccessPolicy;

    public bool InputSuppressed { get; private set; }
    public bool IsDroneMode => mode == Mode.Drone;
    public Camera ActiveCamera => IsDroneMode ? droneCamera : playerCamera;
    public string LastDeniedReason { get; private set; } = "";

    private Mode mode = Mode.Player;

    private void Start() => Apply(Mode.Player);

    private void Update()
    {
        if (PauseMenu.IsPaused) return;               // 일시정지 중에는 Tab 전환 막기 (내 작업)
        if (externalInput || InputSuppressed) return; // 외부 입력 사용 중이거나 채팅 입력 중이면 막기 (팀원 작업)
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame) TryToggleMode();
    }

    public void SetInputSuppressed(bool suppressed)
    {
        InputSuppressed = suppressed;
        RefreshMode();
    }

    public bool TryToggleMode()
    {
        LastDeniedReason = "";
        if (InputSuppressed) { LastDeniedReason = "채팅 입력 중에는 조작 모드를 바꿀 수 없습니다."; return false; }
        if (player == null || drone == null) { LastDeniedReason = "Player/Drone 컨트롤러 연결이 필요합니다."; return false; }
        if (mode == Mode.Player && !CanEnterDrone()) return false;
        Apply(mode == Mode.Player ? Mode.Drone : Mode.Player);
        return true;
    }

    private bool CanEnterDrone()
    {
        if (laptop != null && Vector3.Distance(player.transform.position, laptop.position) > laptopUseDistance)
        { LastDeniedReason = "드론 조작 단말 가까이에서 전환해 주세요."; return false; }
        if (droneAccessPolicy == null) return true;
        if (!(droneAccessPolicy is IDroneAccessPolicy policy))
        { LastDeniedReason = "드론 진입 규칙이 IDroneAccessPolicy를 구현해야 합니다."; return false; }
        try
        {
            if (policy.CanEnterDrone(player, out string reason)) return true;
            LastDeniedReason = string.IsNullOrWhiteSpace(reason) ? "드론 진입 조건을 충족하지 않았습니다." : reason;
        }
        catch (Exception error)
        {
            LastDeniedReason = "드론 진입 조건을 확인하지 못했습니다.";
            Debug.LogException(error, this);
        }
        return false;
    }

    private void Apply(Mode newMode)
    {
        mode = newMode;
        RefreshMode();
    }

    private void RefreshMode()
    {
        bool isPlayer = mode == Mode.Player;
        // 채팅 동안 두 컨트롤러를 멈추되 현재 시점은 유지한다.
        // 이 시험 단계에서는 사람의 중력 갱신도 컨트롤러와 함께 잠시 멈춘다.
        if (player != null) player.enabled = isPlayer && !InputSuppressed;
        if (drone != null) drone.enabled = !isPlayer && !InputSuppressed;
        SetCamera(playerCamera, isPlayer);
        SetCamera(droneCamera, !isPlayer);
        Cursor.lockState = InputSuppressed ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = InputSuppressed;
    }

    private static void SetCamera(Camera cam, bool active)
    {
        if (cam == null) return;
        cam.enabled = active;
        var listener = cam.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = active;
    }
}
