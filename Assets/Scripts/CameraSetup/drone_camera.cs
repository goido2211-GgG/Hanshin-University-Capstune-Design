using UnityEngine;

public class drone_camera : MonoBehaviour
{
    [Header("Target Settings")]
    [Tooltip("추적할 드론 오브젝트를 연결하세요.")]
    public Transform target;

    [Header("Camera Positioning")]
    [Tooltip("드론 기준 카메라의 상대적 위치 (뒤쪽, 위쪽)")]
    public Vector3 offset = new Vector3(0f, 2.5f, -5.0f);

    [Header("Smooth Speeds")]
    [Tooltip("따라잡는 속도 (위치와 회전 공용)")]
    public float smoothSpeed = 10f;

    void LateUpdate()
    {
        if (target == null) return;

        // 1. 드론의 Y축 회전(Yaw, 좌우 방향)만 추출하여 카메라 회전 기준 생성
        // 드론이 아무리 기울어져도 카메라가 바라보는 방향의 수평은 유지됩니다.
        float targetYaw = target.eulerAngles.y;
        Quaternion targetRotation = Quaternion.Euler(0f, targetYaw, 0f);

        // 2. 목표 위치 계산 (드론 위치 + 회전된 오프셋)
        Vector3 targetPosition = target.position + targetRotation * offset;

        // 부드럽게 위치 이동
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);

        // 3. 카메라 시선 처리 (기울어짐 없이 항상 드론을 바라보되, Up 벡터는 항상 월드 상단(Vector3.up) 고정)
        Quaternion lookRotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, smoothSpeed * Time.deltaTime);
    }
}