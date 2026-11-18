using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;

namespace AgentEscape.VoiceAI
{
    public interface IAgentButtonReceiver
    {
        bool TryPress();
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentInteractable))]
    public sealed class AgentButton : MonoBehaviour
    {
        public const string Tag = "AIButton";

        public Transform approachPoint;
        [Min(0.05f)] public float interactionDistance = 0.25f;
        public List<string> aliases = new List<string>();
        public MonoBehaviour receiver;
        public UnityEvent onPress = new UnityEvent();

        internal bool MatchesAlias(string selector)
        {
            foreach (string alias in aliases)
                if (Matches(alias, selector)) return true;
            return false;
        }

        internal static bool Matches(string value, string selector)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = Regex.Replace(value.Trim(), @"\s+", " ");
            selector = Regex.Replace(selector.Trim(), @"\s+", " ");
            return string.Equals(value, selector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, selector + " 버튼", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, selector + "버튼", StringComparison.OrdinalIgnoreCase);
        }

        internal string ConfigurationError()
        {
            if (approachPoint == null || interactionDistance <= 0f ||
                float.IsNaN(interactionDistance) || float.IsInfinity(interactionDistance))
                return "버튼 접근 지점 또는 조작 거리가 설정되지 않았습니다: " + name;
            if (receiver != null)
                return receiver is IAgentButtonReceiver && receiver.isActiveAndEnabled ? null :
                    "활성 IAgentButtonReceiver가 필요합니다: " + name;
            if (onPress != null)
            {
                for (int index = 0; index < onPress.GetPersistentEventCount(); index++)
                    if (onPress.GetPersistentTarget(index) != null &&
                        onPress.GetPersistentListenerState(index) != UnityEventCallState.Off &&
                        UnityEventBase.GetValidMethodInfo(onPress.GetPersistentTarget(index), onPress.GetPersistentMethodName(index), Type.EmptyTypes) != null)
                        return null;
            }
            return "버튼 On Press에 인자 없는 이벤트가 연결되지 않았습니다: " + name;
        }

        internal string TryPress()
        {
            string error = ConfigurationError();
            if (error != null) return error;
            if (receiver != null)
                return ((IAgentButtonReceiver)receiver).TryPress() ? null : "버튼이 작동을 거부했습니다: " + name;
            onPress.Invoke();
            return null;
        }
    }
}
