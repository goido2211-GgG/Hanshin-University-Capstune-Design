using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// 클립을 재생하는 Animator와 같은 GameObject에 붙이는 Animation Event 수신기.
    /// 현재는 의도적으로 무음이다. 이동·조사·Animator 파라미터·퍼즐 상태를 변경하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotAnimationEventReceiver : MonoBehaviour
    {
        public void OnFootstep(AnimationEvent animationEvent)
        {
            // Walk_N/Run 클립 이벤트를 수신만 한다. 인자나 외부 컴포넌트에 접근하지 않는다.
        }

        public void OnLand(AnimationEvent animationEvent)
        {
            // 착지 클립의 기존 이벤트도 안전하게 수신한다. 착지 행동을 새로 구현하는 것은 아니다.
        }
    }
}
