using UnityEngine;
using Unity.Cinemachine; // Cinemachine 3.x 네임스페이스

public class CameraSetup : MonoBehaviour
{
    [Header("Cinemachine Reference")]
    [SerializeField] private CinemachineCamera cmCamera;

    [Header("Static Camera Position (No Character)")]
    // 정해두신 고정 카메라 포지션
    [SerializeField] private Vector3 roomCameraPosition = new Vector3(-28.5f, 21f, -32.5f);

    // 탑뷰 추천 기본 회전각 (필요에 따라 인스펙터에서 수정 가능)
    [SerializeField] private Vector3 roomCameraRotation = new Vector3(50f, 55f, 0f);

    private void Start()
    {
        SetupStaticRoomCamera();
    }

    /// <summary>
    /// 캐릭터가 없는 상태: 정해진 위치에 카메라를 고정시킵니다.
    /// </summary>
    public void SetupStaticRoomCamera()
    {
        if (cmCamera == null)
        {
            cmCamera = GetComponent<CinemachineCamera>();
        }

        if (cmCamera == null) return;

        // 1. 추적 대상 제거 (None 상태)
        cmCamera.Target.TrackingTarget = null;

        // 2. Cinemachine Camera 오브젝트 자체의 Transform을 원하는 좌표로 설정
        cmCamera.transform.position = roomCameraPosition;
        cmCamera.transform.rotation = Quaternion.Euler(roomCameraRotation);
    }

    /// <summary>
    /// 추후 캐릭터가 스폰되었을 때 호출하는 함수
    /// </summary>
    public void SetPlayerTarget(Transform playerTransform)
    {
        if (cmCamera == null || playerTransform == null) return;

        // 1. 추적 대상 연결
        cmCamera.Target.TrackingTarget = playerTransform;

        // 2. 위치 추적 컴포넌트(CinemachineFollow) 연결 및 오프셋 적용
        var followComponent = cmCamera.GetComponent<CinemachineFollow>();
        if (followComponent == null)
        {
            followComponent = cmCamera.gameObject.AddComponent<CinemachineFollow>();
        }

        // 정해두신 좌표를 플레이어 기준 오프셋으로 지정
        followComponent.FollowOffset = roomCameraPosition;
    }
}