using UnityEngine;

public class DoubleSlidingDoor : MonoBehaviour
{
    [Header("문 오브젝트 연결")]
    public Transform leftDoor;
    public Transform rightDoor;

    [Header("이동 설정")]
    [Tooltip("각 문이 바깥쪽으로 열릴 거리 (보통 X축 기준)")]
    public Vector3 slideDirection = new Vector3(1.5f, 0f, 0f);
    public float smoothTime = 0.25f;

    [Header("통과 태그 설정")]
    public string playerTag = "Player";
    public string droneTag = "Drone";

    private bool isOpen = false;
    private bool playerPassed = false; // 플레이어 통과 여부
    private bool dronePassed = false;  // 드론 통과 여부

    private Vector3 leftClosedPos, leftOpenPos;
    private Vector3 rightClosedPos, rightOpenPos;
    private Vector3 leftVelocity, rightVelocity;

    void Awake()
    {
        if (leftDoor != null)
        {
            leftClosedPos = leftDoor.localPosition;
            leftOpenPos = leftClosedPos - slideDirection; // 왼쪽은 반대(-) 방향
        }

        if (rightDoor != null)
        {
            rightClosedPos = rightDoor.localPosition;
            rightOpenPos = rightClosedPos + slideDirection; // 오른쪽은 정(+) 방향
        }
    }

    void Update()
    {
        if (leftDoor != null)
        {
            Vector3 leftTarget = isOpen ? leftOpenPos : leftClosedPos;
            leftDoor.localPosition = Vector3.SmoothDamp(leftDoor.localPosition, leftTarget, ref leftVelocity, smoothTime);
        }

        if (rightDoor != null)
        {
            Vector3 rightTarget = isOpen ? rightOpenPos : rightClosedPos;
            rightDoor.localPosition = Vector3.SmoothDamp(rightDoor.localPosition, rightTarget, ref rightVelocity, smoothTime);
        }
    }

    // 플레이어와 드론이 각각 트리거를 벗어날 때 체크
    private void OnTriggerExit(Collider other)
    {
        if (!isOpen) return;

        if (other.CompareTag(playerTag))
        {
            playerPassed = true;
        }
        else if (other.CompareTag(droneTag))
        {
            dronePassed = true;
        }

        // 플레이어와 드론 둘 다 통과했다면 문 닫기
        if (playerPassed && dronePassed)
        {
            CloseDoor();
        }
    }

    public void ToggleDoor()
    {
        if (isOpen) CloseDoor();
        else OpenDoor();
    }

    public void OpenDoor()
    {
        isOpen = true;
        playerPassed = false;
        dronePassed = false;
    }

    public void CloseDoor()
    {
        isOpen = false;
        playerPassed = false;
        dronePassed = false;
    }
}