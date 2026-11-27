using UnityEngine;

[DisallowMultipleComponent]
public sealed class FirstMapCameraFraming : MonoBehaviour
{
    [SerializeField] private drone_camera followCamera;
    [SerializeField] private Vector3 awakeOffset = new Vector3(0f, 4.5f, -6.5f);

    private Vector3 previousOffset;
    private bool framingApplied;

    // VoiceAISystem enables this component after the capsule has lifted.
    private void OnEnable()
    {
        if (followCamera == null) return;
        previousOffset = followCamera.offset;
        followCamera.offset = awakeOffset;
        framingApplied = true;
    }

    private void OnDisable()
    {
        if (framingApplied && followCamera != null) followCamera.offset = previousOffset;
        framingApplied = false;
    }
}
