using System.Collections;
using UnityEngine;

public class CapsuleLift : MonoBehaviour
{
    [SerializeField] private float liftHeight = 4f;
    [SerializeField] private float liftSpeed = 3f;

    private bool isLifted = false;
    public bool HasFinished { get; private set; }

    public void LiftCapsule()
    {
        if (isLifted) return;
        isLifted = true;
        StartCoroutine(LiftRoutine());
    }

    private IEnumerator LiftRoutine()
    {
        Vector3 targetPos = transform.position + Vector3.up * liftHeight;

        while (Vector3.Distance(transform.position, targetPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPos,
                liftSpeed * Time.deltaTime
            );
            yield return null;
        }

        transform.position = targetPos;
        HasFinished = true;
    }
}
