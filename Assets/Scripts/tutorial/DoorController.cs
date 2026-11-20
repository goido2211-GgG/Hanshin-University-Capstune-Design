using UnityEngine;

[RequireComponent(typeof(Animator))]
public class DoorController1 : MonoBehaviour
{
    [Header("Animator 파라미터 이름")]
    [SerializeField] private string isOpenBoolName = "IsOpen";
    [SerializeField] private string openTriggerName = "Open";
    [SerializeField] private string closeTriggerName = "Close";

    [Header("동작 방식")]
    [SerializeField] private bool useBoolParameter = true;
    [SerializeField] private bool openOnce = false;

    private Animator animator;
    private bool isOpen = false;
    private bool hasOpenedOnce = false;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    public void OpenDoor()
    {
        if (openOnce && hasOpenedOnce) return;
        if (useBoolParameter && isOpen) return;

        isOpen = true;
        hasOpenedOnce = true;

        if (useBoolParameter)
        {
            animator.SetBool(isOpenBoolName, true);
        }
        else if (!string.IsNullOrEmpty(openTriggerName))
        {
            animator.SetTrigger(openTriggerName);
        }
    }

    public void CloseDoor()
    {
        if (openOnce) return;
        if (useBoolParameter && !isOpen) return;

        isOpen = false;

        if (useBoolParameter)
        {
            animator.SetBool(isOpenBoolName, false);
        }
        else if (!string.IsNullOrEmpty(closeTriggerName))
        {
            animator.SetTrigger(closeTriggerName);
        }
    }
}