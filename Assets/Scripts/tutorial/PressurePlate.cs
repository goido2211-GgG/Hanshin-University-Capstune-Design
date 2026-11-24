using UnityEngine;
using UnityEngine.Events;

public class FilteredPressurePlate : MonoBehaviour
{
    public enum AllowedTargetType
    {
        DroneOnly,
        BoxOnly
    }

    [Header("작동 허용 대상")]
    [SerializeField] private AllowedTargetType allowedTarget = AllowedTargetType.DroneOnly;

    [Header("발판 시각 연출(선택)")]
    [SerializeField] private Transform plateVisual;
    [SerializeField] private float pressedOffsetY = -0.1f;

    [Header("작동 이벤트")]
    public UnityEvent onActivated;
    public UnityEvent onDeactivated;

    private Vector3 initialLocalPos;
    private int activeOccupantCount = 0;

    private void Awake()
    {
        if (plateVisual != null)
        {
            initialLocalPos = plateVisual.localPosition;
        }
    }

    private bool IsAllowedObject(Collider other)
    {
        if (allowedTarget == AllowedTargetType.DroneOnly)
        {
            return other.CompareTag("Drone");
        }

        return other.CompareTag("Box");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsAllowedObject(other)) return;

        activeOccupantCount++;
        if (activeOccupantCount == 1)
        {
            SetPressedState(true);
            onActivated?.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsAllowedObject(other)) return;

        activeOccupantCount = Mathf.Max(0, activeOccupantCount - 1);
        if (activeOccupantCount == 0)
        {
            SetPressedState(false);
            onDeactivated?.Invoke();
        }
    }

    private void SetPressedState(bool isPressed)
    {
        if (plateVisual == null) return;

        Vector3 targetPos = initialLocalPos;
        if (isPressed)
        {
            targetPos.y += pressedOffsetY;
        }

        plateVisual.localPosition = targetPos;
    }
}