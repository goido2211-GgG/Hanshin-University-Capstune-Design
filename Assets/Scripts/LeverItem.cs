using UnityEngine;

public class LeverItem : MonoBehaviour
{
    [Header("Lever Settings")]
    [SerializeField] private int leverID;
    [SerializeField] private PuzzleManager puzzleManager;

    private bool isPulled = false;

    public void PullLever()
    {
        if (isPulled) return;

        isPulled = true;
        transform.Rotate(30f, 0f, 0f); // 시각적 회전

        if (puzzleManager != null)
        {
            puzzleManager.SubmitLever(leverID);
        }
        Debug.Log($"레버 {leverID}번 당겨짐!");
    }

    public void ResetLever()
    {
        if (!isPulled) return;
        isPulled = false;
        transform.Rotate(-30f, 0f, 0f);
    }
}