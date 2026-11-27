using UnityEngine;

namespace AgentEscape.VoiceAI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentInteractable))]
    public sealed class AgentPlacementTarget : MonoBehaviour
    {
        public Collider support;
        public Transform approachPoint;
        public AgentInteractable box;
        public AgentButton releaseButton;

        internal AgentInteractable Interactable => GetComponent<AgentInteractable>();
        internal int RoomId => releaseButton != null ? releaseButton.roomId : 0;

        internal string ConfigurationError()
        {
            if (!isActiveAndEnabled || support == null || !support.enabled ||
                !support.gameObject.activeInHierarchy || support.isTrigger ||
                support.gameObject.scene != gameObject.scene || approachPoint == null || box == null ||
                box.gameObject.scene != gameObject.scene || RoomId <= 0)
                return "상자나 발판의 배치 지점이 설정되지 않았습니다.";
            return null;
        }

        internal bool IsAtApproach(Vector3 position)
        {
            Vector3 offset = position - approachPoint.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= 0.25f * 0.25f;
        }

        internal bool CanReachSupport(Vector3 position, Bounds bounds)
        {
            Vector3 reach = bounds.center - position;
            return System.Math.Abs(reach.y) <= 1.5f && reach.sqrMagnitude <= 3f * 3f;
        }
    }
}
