using UnityEngine;
using UnityEngine.Events;

public class PressurePlate : MonoBehaviour
{
    [Header("움직임 설정")]
    [Tooltip("실제로 아래로 내려앉을 발판 상판 오브젝트")]
    public Transform plateTop;

    [Tooltip("밟혔을 때 내려갈 깊이 (예: 0.1 = 10cm 아래로)")]
    public float pressDepth = 0.1f;

    [Tooltip("내려앉고 올라오는 속도 (작을수록 빠름)")]
    public float smoothTime = 0.15f;

    [Header("이벤트 연결 (문 열기 등)")]
    public UnityEvent onPressed;
    public UnityEvent onReleased;

    [SerializeField] private bool useExternalPressure;

    private Vector3 initialPos;
    private Vector3 pressedPos;
    private Vector3 currentVelocity;
    private int objectsOnPlate = 0; // 위에 올라간 물체/플레이어 수

    void Awake()
    {
        if (plateTop != null)
        {
            initialPos = plateTop.localPosition;
            Vector3 displacement = Vector3.down * pressDepth;
            if (useExternalPressure && plateTop.parent != null)
                displacement = plateTop.parent.InverseTransformVector(displacement);
            pressedPos = initialPos + displacement;
        }
    }

    void Update()
    {
        if (plateTop == null) return;

        // 올라간 물체가 1개 이상이면 눌린 위치, 0개면 원래 위치로 부드럽게 이동
        Vector3 targetPos = (objectsOnPlate > 0) ? pressedPos : initialPos;
        plateTop.localPosition = Vector3.SmoothDamp(
            plateTop.localPosition,
            targetPos,
            ref currentVelocity,
            smoothTime
        );
    }

    public void SetExternalPressure(bool pressed)
    {
        if (!useExternalPressure || pressed == (objectsOnPlate > 0)) return;
        objectsOnPlate = pressed ? 1 : 0;
        (pressed ? onPressed : onReleased)?.Invoke();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (useExternalPressure) return;
        // 플레이어나 상자 등 상호작용할 태그 확인 (필요시 조건 수정)
        if (other.CompareTag("Player") || other.CompareTag("Pushable"))
        {
            objectsOnPlate++;

            // 처음 밟힌 순간에만 이벤트 실행
            if (objectsOnPlate == 1)
            {
                onPressed?.Invoke();
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (useExternalPressure) return;
        if (other.CompareTag("Player") || other.CompareTag("Pushable"))
        {
            objectsOnPlate = Mathf.Max(0, objectsOnPlate - 1);

            // 모든 물체가 발판에서 내려왔을 때만 이벤트 실행
            if (objectsOnPlate == 0)
            {
                onReleased?.Invoke();
            }
        }
    }
}
