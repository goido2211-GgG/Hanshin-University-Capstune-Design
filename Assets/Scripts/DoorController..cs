using UnityEngine;

[RequireComponent(typeof(Animator))]
public class DoorController : MonoBehaviour
{
    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    // 퍼즐이 성공했을 때 외부(매니저 등)에서 이 함수를 호출해 줌
    public void OpenDoor()
    {
        if (animator != null)
        {
            // Animator에 만든 "Open" 트리거 발동!
            animator.SetTrigger("Open");
            Debug.Log("문 열림 애니메이션 재생 시작!");
        }
    }
}