using AgentEscape.VoiceAI;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(BoxCollider), typeof(AgentInteractable))]
public sealed class FirstMapCratePuzzle : MonoBehaviour
{
    [SerializeField] private BoxCollider plateSurface;
    [SerializeField] private PressurePlate pressurePlate;

    private Rigidbody crateBody;
    private BoxCollider crateCollider;
    private AgentInteractable interaction;
    private bool onPlate;

    private void Awake()
    {
        crateBody = GetComponent<Rigidbody>();
        crateCollider = GetComponent<BoxCollider>();
        interaction = GetComponent<AgentInteractable>();
        interaction.enabled = false;
    }

    private bool HasWeight => !crateBody.isKinematic && crateCollider.enabled;
    private bool IsSettled => crateBody.IsSleeping() ||
        (crateBody.linearVelocity.sqrMagnitude <= 0.04f && crateBody.angularVelocity.sqrMagnitude <= 0.25f);

    private void FixedUpdate()
    {
        if (!HasWeight) onPlate = false;

        if (pressurePlate != null)
            pressurePlate.SetExternalPressure(onPlate && IsSettled && FitsOnPlate());
    }

    private void OnCollisionEnter(Collision collision) => ReadSupport(collision);
    private void OnCollisionStay(Collision collision) => ReadSupport(collision);

    private void ReadSupport(Collision collision)
    {
        if (!HasWeight) return;
        bool supportedFromBelow = false;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint contact = collision.GetContact(i);
            if (contact.thisCollider != crateCollider || contact.normal.y < 0.65f) continue;
            supportedFromBelow = true;
            break;
        }

        if (supportedFromBelow && !interaction.enabled && IsSettled) interaction.enabled = true;
        if (collision.collider == plateSurface) onPlate = supportedFromBelow;
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider == plateSurface) onPlate = false;
    }

    private bool FitsOnPlate()
    {
        if (plateSurface == null || !plateSurface.enabled) return false;
        Bounds plate = plateSurface.bounds;
        Bounds crate = crateCollider.bounds;
        const float tolerance = 0.04f;
        return crate.min.x >= plate.min.x - tolerance && crate.max.x <= plate.max.x + tolerance &&
            crate.min.z >= plate.min.z - tolerance && crate.max.z <= plate.max.z + tolerance &&
            Mathf.Abs(crate.min.y - plate.max.y) <= tolerance;
    }

    private void OnDisable()
    {
        if (pressurePlate != null) pressurePlate.SetExternalPressure(false);
    }
}
