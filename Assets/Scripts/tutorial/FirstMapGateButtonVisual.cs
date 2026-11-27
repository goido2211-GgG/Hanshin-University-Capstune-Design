using UnityEngine;

public sealed class FirstMapGateButtonVisual : MonoBehaviour
{
    [SerializeField] private Transform buttonTop;
    [SerializeField, Min(0f)] private float pressDepth = 0.08f;

    private Vector3 restingPosition;
    private float releaseAt;

    private void Awake()
    {
        if (buttonTop != null) restingPosition = buttonTop.localPosition;
    }

    public void Press()
    {
        if (buttonTop == null) return;
        if (releaseAt <= 0f) buttonTop.position += transform.forward * pressDepth;
        releaseAt = Time.time + 0.3f;
    }

    private void Update()
    {
        if (releaseAt <= 0f || Time.time < releaseAt) return;
        Release();
    }

    private void OnDisable()
    {
        if (releaseAt > 0f && buttonTop != null) Release();
    }

    private void Release()
    {
        buttonTop.localPosition = restingPosition;
        releaseAt = 0f;
    }
}
