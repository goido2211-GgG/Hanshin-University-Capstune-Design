using UnityEngine;

public class LeverTrigger : MonoBehaviour
{
    [SerializeField] private FallingBox targetBox;
    [SerializeField] private KeyCode interactKey = KeyCode.F;
    [SerializeField] private Transform leverHandle; // 선택: 당길 때 회전시킬 레버 손잡이
    [SerializeField] private float pulledAngle = 45f;

    private bool playerInRange = false;
    private bool isPulled = false;

    private void Update()
    {
        if (isPulled || !playerInRange) return;

        if (Input.GetKeyDown(interactKey))
        {
            PullLever();
        }
    }

    public void PullLever()
    {
        if (isPulled) return;
        isPulled = true;

        if (leverHandle != null)
        {
            leverHandle.localRotation = Quaternion.Euler(pulledAngle, 0f, 0f);
        }

        if (targetBox != null)
        {
            targetBox.DropBox();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
        }
    }
}