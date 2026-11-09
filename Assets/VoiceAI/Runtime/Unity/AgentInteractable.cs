using System.Collections.Generic;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// 요원이 인식하고 조작할 수 있는 맵 오브젝트에 붙인다.
    /// 설명에 퍼즐 정답을 쓰지 않는다. 여기 적힌 내용은 AI가 그대로 말할 수 있다.
    /// </summary>
    public sealed class AgentInteractable : MonoBehaviour
    {
        private static readonly List<AgentInteractable> ActiveItems = new List<AgentInteractable>();
        public static IReadOnlyList<AgentInteractable> Active => ActiveItems;

        [Tooltip("영문 고유 ID. 예: door_exit")]
        public string id;
        [Tooltip("플레이어가 부를 이름. 음성 인식 힌트로도 쓰인다. 예: 출구 문")]
        public string displayName;
        [TextArea] public string description;
        [Tooltip("현재 상태. 기믹 코드에서 바꾼다. 예: 잠김")]
        public string state;
        [Tooltip("move_to, inspect 외에 허용할 동사. 예: open, press, pick_up")]
        public List<string> allowedVerbs = new List<string>();
        [Range(0, 100), Tooltip("이 신뢰도 미만이면 요원이 조작을 거부한다.")]
        public int minTrust;

        private void OnEnable() { ActiveItems.Add(this); }
        private void OnDisable() { ActiveItems.Remove(this); }

        public StageObject ToStageObject()
        {
            return new StageObject
            {
                Id = id,
                DisplayName = displayName,
                Description = description,
                State = state,
                AllowedVerbs = new List<string>(allowedVerbs),
                MinTrust = minTrust
            };
        }
    }
}
