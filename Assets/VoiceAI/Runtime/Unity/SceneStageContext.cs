using System.Collections.Generic;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>씬의 AgentInteractable을 모아서 현재 상황을 만든다. 스테이지마다 하나씩 둔다.</summary>
    public sealed class SceneStageContext : MonoBehaviour
    {
        public string stageId = "stage_01";
        [TextArea(2, 5)] public string brief = "요원은 처음 보는 방에서 막 전원이 켜졌다. 드론에서 나오는 목소리만이 유일한 말 상대다.";
        [Range(0, 3), Tooltip("0: 잘 알아들음, 1: 가끔 오해, 2: 자주 오해, 3: 대부분 오해")]
        public int confusionLevel = 1;
        public string agentLocation = "방 중앙";
        [Tooltip("요원이 들고 있는 오브젝트 ID")]
        public List<string> inventory = new List<string>();
        [Tooltip("씬에 AgentInteractable이 없으면 시험용 오브젝트를 사용한다.")]
        public bool useDemoObjectsWhenEmpty = true;

        public StageContext Build()
        {
            var context = new StageContext
            {
                StageId = stageId,
                Brief = brief,
                ConfusionLevel = confusionLevel,
                AgentLocation = agentLocation,
                Inventory = new List<string>(inventory)
            };

            var seen = new HashSet<string>();
            foreach (var item in AgentInteractable.Active)
            {
                if (string.IsNullOrEmpty(item.id))
                {
                    Debug.LogWarning("[VoiceAI] ID가 비어 있는 AgentInteractable: " + item.name, item);
                }
                else if (!seen.Add(item.id))
                {
                    Debug.LogWarning("[VoiceAI] 중복된 오브젝트 ID: " + item.id, item);
                }
                else
                {
                    context.Objects.Add(item.ToStageObject());
                }
            }

            if (context.Objects.Count == 0 && useDemoObjectsWhenEmpty) AddDemoObjects(context);
            return context;
        }

        private static void AddDemoObjects(StageContext context)
        {
            context.Objects.Add(new StageObject
            {
                Id = "door_exit", DisplayName = "출구 문", Description = "철문. 옆에 카드 인식기가 있다.",
                State = "잠김", AllowedVerbs = new List<string> { "open", "push", "use" }
            });
            context.Objects.Add(new StageObject
            {
                Id = "desk", DisplayName = "책상", Description = "서랍이 달린 낡은 책상.",
                State = "서랍 닫힘", AllowedVerbs = new List<string> { "open", "close", "push" }
            });
            context.Objects.Add(new StageObject
            {
                Id = "keycard", DisplayName = "카드키", Description = "책상 위의 하얀 카드.",
                State = "책상 위", AllowedVerbs = new List<string> { "pick_up", "drop" }
            });
            context.Objects.Add(new StageObject
            {
                Id = "red_button", DisplayName = "빨간 버튼", Description = "벽에 달린 크고 수상한 버튼.",
                State = "눌리지 않음", AllowedVerbs = new List<string> { "press" }, MinTrust = 70
            });
        }
    }
}
