using UnityEngine;

[DisallowMultipleComponent]
public sealed class FirstMapWakeButtonBridge : MonoBehaviour
{
    [SerializeField] private DroneController drone;
    [SerializeField] private FirstMapContainmentController containment;
    [SerializeField] private Transform plateVisual;
    [SerializeField, Min(0f)] private float pressDepth = 0.1f;

    private void OnTriggerEnter(Collider other) => TryPress(other);

    private void OnTriggerStay(Collider other) => TryPress(other);

    private void TryPress(Collider other)
    {
        if (drone == null || containment == null || other.isTrigger ||
            other.GetComponentInParent<DroneController>() != drone ||
            other.attachedRigidbody == null || other.attachedRigidbody.linearVelocity.y >= -0.01f ||
            other.bounds.center.y <= transform.position.y || !containment.TryBeginRelease()) return;

        if (plateVisual != null)
            plateVisual.position += Vector3.down * pressDepth;
    }
}
