using UnityEngine;

public class ScreenDoorControl : MonoBehaviour
{
    [Header("이동 설정")]
    [Tooltip("닫힌 위치 기준 열릴 때 이동할 로컬 오프셋")]
    public Vector3 slideOffset = new Vector3(2f, 0f, 0f);

    [Tooltip("목표 지점까지 도달하는 데 걸리는 대략적인 시간 (작을수록 빠름)")]
    public float smoothTime = 0.25f;

    private bool isOpen = false;

    private Vector3 closedPos;
    private Vector3 openPos;
    private Vector3 currentVelocity; // SmoothDamp용 속도 참조 변수

    void Awake()
    {
        closedPos = transform.localPosition;
        openPos = closedPos + slideOffset;
    }

    void Update()
    {
        Vector3 targetPos = isOpen ? openPos : closedPos;

        // 이미 목표 위치에 거의 도달했다면 연산 중단 (최적화 및 정확한 위치 고정)
        if (Vector3.SqrMagnitude(transform.localPosition - targetPos) < 0.0001f)
        {
            transform.localPosition = targetPos;
            currentVelocity = Vector3.zero;
            return;
        }

        // 프레임에 독립적이고 부드러운 감속 이동
        transform.localPosition = Vector3.SmoothDamp(
            transform.localPosition,
            targetPos,
            ref currentVelocity,
            smoothTime
        );
    }

    public void ToggleDoor() => isOpen = !isOpen;
    public void OpenDoor() => isOpen = true;
    public void CloseDoor() => isOpen = false;

    // 씬(Scene) 뷰에서 문이 열릴 위치를 미리 시각적으로 확인
    private void OnDrawGizmosSelected()
    {
        Vector3 start = Application.isPlaying ? closedPos : transform.localPosition;
        Vector3 end = start + slideOffset;

        // 부모가 있다면 로컬 좌표를 월드 좌표로 변환하여 표시
        if (transform.parent != null)
        {
            start = transform.parent.TransformPoint(start);
            end = transform.parent.TransformPoint(end);
        }

        Gizmos.color = Color.green;
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireSphere(end, 0.1f);
    }
}