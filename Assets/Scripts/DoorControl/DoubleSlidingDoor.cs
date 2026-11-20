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

    [Header("상태 확인")]
    [SerializeField] private bool isOpen = false;

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

    // 플레이어가 문 트리거 영역을 완전히 벗어날 때 실행
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && isOpen)
        {
            CloseDoor();
        }
    }

    // 외부에서 이 함수 하나만 호출하면 양쪽 문이 동시에 작동합니다
    public void ToggleDoor() => isOpen = !isOpen;
    public void OpenDoor() => isOpen = true;
    public void CloseDoor() => isOpen = false;
}