using System;                   // ★ 추가: Action (알림용 이벤트 자료형)
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 사람(플레이어) 상태의 이동. WASD 이동, Space 점프, 마우스 시점.
/// ControlModeSwitcher가 enabled를 켜고 끄면서 조작 여부를 정한다.
///
/// ★ 앉기
/// - SitDown(앉을 위치)을 부르면 그 위치로 이동해서 앉는다. (의자의 Seat 스크립트가 부른다)
/// - 앉으면 카메라가 앉은 눈높이(seatedEyeHeight)로 내려가고, 일어나면 원래 높이로 돌아온다.
/// - 앉아 있는 동안에는 이동·점프를 막고 시점만 돌릴 수 있다.
/// - 앉아 있을 때 Space를 누르면 일어나서, 앉기 전 위치로 돌아간다.
/// - 앉거나 일어날 때 SeatedChanged 알림을 보낸다. (의자 등이 이 알림을 듣고 반응한다)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;   // 플레이어 카메라 (Player의 자식)

    [Header("Move")]
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -20f;

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 0.1f;
    [SerializeField] private float maxPitch = 80f;

    // ★ 추가: 앉았을 때의 눈높이
    [Header("Seat")]
    [Tooltip("앉았을 때 카메라 높이 (발바닥 기준, 미터). 서 있을 때는 카메라의 원래 높이를 쓴다.")]
    [SerializeField] private float seatedEyeHeight = 1.15f;

    // ★ 추가: 지금 앉아 있는지. 다른 스크립트는 읽기만 할 수 있다. (바꾸는 건 SitDown, StandUp만)
    public bool IsSeated { get; private set; }

    // ★ 추가: 앉거나 일어날 때 보내는 알림. true = 앉음, false = 일어남.
    // event Action<bool>: "bool 값 하나를 같이 넘겨주는 알림"이라는 뜻.
    // 다른 스크립트가 player.SeatedChanged += 함수; 로 구독해 두면, 알림이 갈 때 그 함수가 불린다.
    public event Action<bool> SeatedChanged;

    private CharacterController controller;
    private float verticalVelocity;
    private float pitch;

    // ★ 추가: 앉기 직전에 서 있던 위치. 일어날 때 이 자리로 돌아간다.
    private Vector3 standPosition;

    // ★ 추가: 서 있을 때의 카메라 위치(Player 기준). 일어날 때 이 높이로 되돌린다.
    private Vector3 standingCameraLocalPosition;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        // ★ 추가: 처음 카메라 위치(서 있는 눈높이)를 기억해 둔다.
        // localPosition: 부모(Player) 기준 위치. 카메라는 Player의 자식이라 "발에서 얼마나 위에 있나"가 된다.
        if (cameraTransform != null) standingCameraLocalPosition = cameraTransform.localPosition;
    }

    private void Update()
    {
        if (PauseMenu.IsPaused) return;   // 일시정지 중에는 입력 무시

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        Look(mouse);                      // 시점은 앉아 있어도 돌릴 수 있다

        // ★ 추가: 앉아 있으면 이동하지 않고, Space로 일어나는 것만 확인한다.
        if (IsSeated)
        {
            if (keyboard.spaceKey.wasPressedThisFrame) StandUp();
            return;                       // 조기 반환: 아래 Move는 실행하지 않음 (같은 Space로 점프까지 되지 않음)
        }

        Move(keyboard);
    }

    /// <summary>
    /// ★ 추가: seatPoint 위치로 가서 앉는다. (의자의 Seat 스크립트가 부른다)
    /// </summary>
    public void SitDown(Transform seatPoint)
    {
        // 이미 앉아 있거나, 앉을 위치가 없으면 아무것도 안 한다.
        if (IsSeated || seatPoint == null) return;

        standPosition = transform.position;   // 일어날 때 돌아올 자리를 기억

        // 의자가 바라보는 방향(좌우 회전 y만)으로 몸을 돌린다.
        // Quaternion.Euler(x, y, z): 각도로 회전을 만든다. x, z를 0으로 해서 의자가 기울어져 있어도 몸은 똑바로 선다.
        Quaternion facing = Quaternion.Euler(0f, seatPoint.eulerAngles.y, 0f);
        Teleport(seatPoint.position, facing);

        // 고개(상하 시점)를 정면으로 되돌린다. Quaternion.identity = "회전 없음"
        pitch = 0f;
        if (cameraTransform != null) cameraTransform.localRotation = Quaternion.identity;

        // ★ 추가: 카메라를 앉은 눈높이로 내린다. (x, z는 그대로 두고 높이 y만 바꾼다)
        SetCameraHeight(seatedEyeHeight);

        verticalVelocity = 0f;                // 떨어지던 속도 초기화
        IsSeated = true;

        // ?. : SeatedChanged를 구독한 곳이 하나라도 있을 때만 알림을 보낸다. (없으면 null이라 건너뜀)
        SeatedChanged?.Invoke(true);
    }

    /// <summary>
    /// ★ 추가: 일어나서 앉기 전 위치로 돌아간다.
    /// </summary>
    public void StandUp()
    {
        if (!IsSeated) return;                // 앉아 있지 않으면 할 일 없음

        Teleport(standPosition, transform.rotation);   // 바라보는 방향은 지금 그대로
        SetCameraHeight(standingCameraLocalPosition.y); // ★ 추가: 카메라를 서 있는 눈높이로 되돌린다
        verticalVelocity = 0f;
        IsSeated = false;
        SeatedChanged?.Invoke(false);
    }

    /// <summary>
    /// ★ 추가: CharacterController가 붙은 오브젝트를 순간이동시킨다.
    /// CharacterController는 켜져 있으면 위치를 직접 바꿔도 자기가 알던 위치로 되돌려 버리므로,
    /// 잠깐 끄고 → 옮기고 → 다시 켠다.
    /// </summary>
    private void Teleport(Vector3 position, Quaternion rotation)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);   // 위치와 회전을 한 번에 바꾼다
        controller.enabled = true;
    }

    /// <summary>
    /// ★ 추가: 카메라 높이(Player 기준 y)만 바꾼다.
    /// </summary>
    private void SetCameraHeight(float height)
    {
        if (cameraTransform == null) return;
        Vector3 p = standingCameraLocalPosition;   // 서 있을 때 위치에서 출발해서
        p.y = height;                              // 높이만 바꾼다
        cameraTransform.localPosition = p;
    }

    private void Look(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity * GameSettings.Sensitivity;   // 설정의 감도 배율 적용

        // 몸통은 좌우, 카메라는 상하
        transform.Rotate(0f, delta.x, 0f);

        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void Move(Keyboard keyboard)
    {
        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

        // 바라보는 방향 기준, 대각선도 같은 속도
        Vector3 direction = transform.right * x + transform.forward * z;
        direction = Vector3.ClampMagnitude(direction, 1f);

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;   // 바닥에 붙어 있게 하는 약한 힘

        if (controller.isGrounded && keyboard.spaceKey.wasPressedThisFrame)
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = direction * moveSpeed + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }
}
