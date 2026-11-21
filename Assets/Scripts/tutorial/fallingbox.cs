using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class FallingBox : MonoBehaviour
{
    [Header("묵직한 낙하 설정")]
    [SerializeField] private float downwardForce = 5f; // 떨어지기 시작할 때 살짝 눌러주는 힘

    private Rigidbody rb;
    private bool hasDropped = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; // 시작 시 공중 고정
    }

    public void DropBox()
    {
        if (hasDropped) return;
        hasDropped = true;

        rb.isKinematic = false;
        rb.WakeUp();

        if (downwardForce > 0f)
        {
            rb.AddForce(Vector3.down * downwardForce, ForceMode.Impulse);
        }
    }
}