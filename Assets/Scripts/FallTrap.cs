using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FallTrap : MonoBehaviour
{
    [Header("Trap Settings")]
    [Tooltip("플레이어가 발판 위에 서 있어야 하는 시간 (초)")]
    [SerializeField] private float fallDelay = 5f;

    private Rigidbody rb;
    private float stayTimer = 0f;
    private bool isTriggered = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // 처음에는 공중에 떠 있거나 멈춰 있도록 Kinematic 활성화
        rb.isKinematic = true;
    }

    // 플레이어가 발판 위에 머무는 동안 계속 호출됨
    private void OnCollisionStay(Collision collision)
    {
        if (isTriggered) return;

        // 닿은 오브젝트가 "Player" 태그를 가지고 있는지 확인
        if (collision.gameObject.CompareTag("Player"))
        {
            stayTimer += Time.deltaTime;

            // 디버그용 로그 (콘솔창에서 시간이 차오르는 것을 확인 가능)
            Debug.Log($"함정 가동 중... 체류 시간: {stayTimer:F1}초");

            if (stayTimer >= fallDelay)
            {
                ActivateTrap();
            }
        }
    }

    // 플레이어가 발판에서 벗어났을 때 타이머 초기화 (선택 사항: 5초를 누적시키고 싶다면 이 함수를 지우세요)
    private void OnCollisionExit(Collision collision)
    {
        if (isTriggered) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            Debug.Log("플레이어가 발판을 벗어남. 타이머 초기화.");
            stayTimer = 0f;
        }
    }

    private void ActivateTrap()
    {
        isTriggered = true;
        Debug.Log("함정 발동! 바닥 낙하 시작!");

        // 물리 작용을 활성화하여 아래로 뚝 떨어지게 만듦
        rb.isKinematic = false;

        // (선택 사항) 몇 초 뒤에 함정 오브젝트를 완전히 없애고 싶다면:
        // Destroy(gameObject, 3f);
    }
}