using UnityEngine;
using UnityEngine.Events;

public class ProximityDoorTrigger : MonoBehaviour
{
    [Header("감지 대상 태그")]
    [SerializeField] private string playerTag = "Player";

    [Header("감지 이벤트")]
    public UnityEvent onPlayerEnter;
    public UnityEvent onPlayerExit;

    private int insideCount = 0;

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        insideCount++;
        if (insideCount == 1)
        {
            onPlayerEnter?.Invoke();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        insideCount = Mathf.Max(0, insideCount - 1);
        if (insideCount == 0)
        {
            onPlayerExit?.Invoke();
        }
    }
}