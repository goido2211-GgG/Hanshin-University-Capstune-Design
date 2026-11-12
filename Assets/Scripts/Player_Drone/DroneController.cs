using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 드론 상태의 이동. WASD 수평 이동, Space 상승, Ctrl 하강, 마우스 시점.
/// 중력 없이 떠 있고, 콜라이더로 벽과 물건에 부딪힌다.
/// 이동 범위 제한은 아직 없음.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;   // 드론 카메라 (Drone의 자식)

    [Header("Move")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float verticalSpeed = 3f;
    [SerializeField] private float acceleration = 20f;    // 값이 클수록 반응이 빠름

    [Header("Look")]
    [SerializeField] private float lookSensitivity = 0.1f;
    [SerializeField] private float maxPitch = 80f;

    private Rigidbody body;
    private Vector3 targetVelocity;
    private float pitch;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;
    }

    private void OnDisable()
    {
        targetVelocity = Vector3.zero;
        if (body != null) body.linearVelocity = Vector3.zero;   // 조작이 끝나면 그 자리에 멈춤
    }

    private void Update()
    {
        if (PauseMenu.IsPaused) return;   // 일시정지 중에는 입력 무시
        
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;

        Look(mouse);
        ReadMoveInput(keyboard);
    }

    private void FixedUpdate()
    {
        body.linearVelocity = Vector3.MoveTowards(
            body.linearVelocity, targetVelocity, acceleration * Time.fixedDeltaTime);
    }

    private void Look(Mouse mouse)
    {
        Vector2 delta = mouse.delta.ReadValue() * lookSensitivity;

        transform.Rotate(0f, delta.x, 0f);

        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        if (cameraTransform != null)
            cameraTransform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    private void ReadMoveInput(Keyboard keyboard)
    {
        float x = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float z = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

        float up = 0f;
        if (keyboard.spaceKey.isPressed) up += 1f;
        if (keyboard.leftCtrlKey.isPressed) up -= 1f;

        // 수평은 바라보는 방향(좌우 회전만 반영) 기준
        Vector3 horizontal = Vector3.ClampMagnitude(transform.right * x + transform.forward * z, 1f);
        targetVelocity = horizontal * moveSpeed + Vector3.up * (up * verticalSpeed);
    }
}
