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
        private AgentBodyController _carryBody;
        public AgentInteractable CurrentHeldObject => _carryBody == null ? null : _carryBody.CurrentHeldObject;

        public IReadOnlyList<string> inventory
        {
            get
            {
                var held = CurrentHeldObject;
                return held == null || string.IsNullOrWhiteSpace(held.id) ? System.Array.Empty<string>() : new[] { held.id };
            }
        }

        internal void BindCarryBody(AgentBodyController body) => _carryBody = body;

        internal void CopyInventoryTo(List<string> destination)
        {
            destination.Clear();
            var held = CurrentHeldObject;
            if (held != null && !string.IsNullOrWhiteSpace(held.id)) destination.Add(held.id);
        }
        [Tooltip("씬에 AgentInteractable이 없으면 시험용 오브젝트를 사용한다.")]
        public bool useDemoObjectsWhenEmpty = true;

        public bool TryResolve(string id, out AgentInteractable target)
        {
            target = null;
            if (string.IsNullOrWhiteSpace(id)) return false;
            foreach (var item in AgentInteractable.Active)
            {
                if (item == null || !item.isActiveAndEnabled || item.gameObject.scene != gameObject.scene || item.id != id) continue;
                if (target != null) { target = null; return false; }
                target = item;
            }
            return target != null;
        }

        internal string ResolveButton(string selector, out AgentInteractable target, out bool noMatch)
        {
            target = null;
            noMatch = false;
            GameObject match = null;
            foreach (var candidate in GameObject.FindGameObjectsWithTag(AgentButton.Tag))
            {
                if (candidate.scene != gameObject.scene) continue;
                candidate.TryGetComponent<AgentInteractable>(out var item);
                candidate.TryGetComponent<AgentButton>(out var button);
                if (!string.IsNullOrEmpty(selector) &&
                    !AgentButton.Matches(candidate.name, selector) &&
                    (item == null || (!AgentButton.Matches(item.id, selector) && !AgentButton.Matches(item.displayName, selector))) &&
                    (item?.aliases == null || !item.aliases.Exists(alias => AgentButton.Matches(alias, selector))) &&
                    (button == null || !button.MatchesAlias(selector))) continue;

                if (match != null) return "버튼이 여러 개입니다. 고유한 이름이나 번호를 입력해 주세요.";
                match = candidate;
            }
            if (match == null) { noMatch = true; return "지정한 버튼을 찾지 못했습니다."; }
            if (!match.TryGetComponent<AgentInteractable>(out var selected) ||
                !selected.isActiveAndEnabled || selected.Button == null || string.IsNullOrWhiteSpace(selected.id))
                return "버튼 어댑터 또는 고유 ID가 설정되지 않았습니다: " + match.name;
            if (!TryResolve(selected.id, out target) || target != selected)
                return "버튼 ID가 중복되었습니다: " + selected.id;
            return null;
        }

        internal string ResolveNeighborButton(AgentInteractable selected, out AgentInteractable target)
        {
            const float neighborRadius = 3f;
            target = null;
            if (selected == null || selected.Button == null)
                return "어느 버튼의 옆인지 먼저 이름이나 번호를 알려 주세요.";
            foreach (var candidate in AgentInteractable.Active)
            {
                if (candidate == null || candidate == selected || candidate.Button == null ||
                    Vector3.Distance(selected.transform.position, candidate.transform.position) > neighborRadius ||
                    string.IsNullOrWhiteSpace(candidate.id) || !TryResolve(candidate.id, out var unique) || unique != candidate)
                    continue;
                if (target != null)
                {
                    target = null;
                    return "옆에 버튼이 여러 개 있습니다. 이름이나 번호를 알려 주세요.";
                }
                target = candidate;
            }
            return target != null ? null : "선택한 버튼 옆에 다른 버튼을 찾지 못했습니다.";
        }

        public StageContext Build() => Build(includeDemo: true, includeSceneDetails: false);

        internal StageContext BuildForCommand() => Build(includeDemo: false, includeSceneDetails: true);

        private StageContext Build(bool includeDemo, bool includeSceneDetails)
        {
            var context = new StageContext
            {
                StageId = stageId,
                Brief = brief,
                ConfusionLevel = confusionLevel,
                AgentLocation = agentLocation
            };
            CopyInventoryTo(context.Inventory);

            var items = new Dictionary<string, AgentInteractable>(System.StringComparer.Ordinal);
            foreach (var item in AgentInteractable.Active)
            {
                if (item == null || !item.isActiveAndEnabled || item.gameObject.scene != gameObject.scene) continue;
                if (string.IsNullOrWhiteSpace(item.id))
                {
                    Debug.LogWarning("[VoiceAI] ID가 비어 있는 AgentInteractable: " + item.name, item);
                }
                else if (items.ContainsKey(item.id))
                {
                    Debug.LogWarning("[VoiceAI] 중복된 오브젝트 ID: " + item.id, item);
                    items[item.id] = null;
                }
                else
                {
                    items.Add(item.id, item);
                }
            }

            foreach (var item in items.Values)
            {
                if (item == null) continue;
                var description = item.ToStageObject();
                if (includeSceneDetails)
                {
                    var position = item.transform.position;
                    description.SceneName = item.gameObject.name;
                    description.PositionX = position.x;
                    description.PositionY = position.y;
                    description.PositionZ = position.z;
                    var button = item.Button;
                    if (button != null)
                    {
                        if (button.aliases != null) description.Aliases.AddRange(button.aliases);
                        description.CanInteract = button.ConfigurationError() == null;
                    }
                    else
                    {
                        description.AllowedVerbs.Remove(AgentVocabulary.Press);
                        description.AllowedVerbs.Remove(AgentVocabulary.Interact);
                    }
                }
                context.Objects.Add(description);
            }
            if (includeDemo && items.Count == 0 && useDemoObjectsWhenEmpty) AddDemoObjects(context);
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
