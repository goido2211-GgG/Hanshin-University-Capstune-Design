using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AgentEscape.VoiceAI
{
    public sealed class ChatTurn
    {
        public string Role;
        public string Content;
    }

    public sealed class LlmSettings
    {
        public string Model = "gpt-6-luna";
        public string ReasoningEffort = "low";
        public int MaxOutputTokens = 800;
    }

    /// <summary>OpenAI Responses API 요청 본문을 만든다.</summary>
    public static class AgentRequestBuilder
    {
        private const int MaxSceneObjects = 40;

        public static JObject Build(
            string systemPrompt,
            IEnumerable<ChatTurn> history,
            StageContext context,
            int trust,
            IEnumerable<string> recentEvents,
            TurnDirective directive,
            string playerText,
            LlmSettings settings)
        {
            var input = new JArray();
            foreach (var turn in history)
            {
                input.Add(Message(turn.Role, turn.Content));
            }
            // 게임 상태는 developer, 플레이어 발화는 user로 분리한다.
            // 플레이어가 말로 게임 상태나 규칙을 사칭해도 권한이 올라가지 않는다.
            input.Add(Message("developer", BuildStateMessage(context, trust, recentEvents, directive)));
            input.Add(Message("user", playerText));

            var request = new JObject
            {
                ["model"] = settings.Model,
                ["instructions"] = systemPrompt,
                ["input"] = input,
                ["store"] = false,
                ["max_output_tokens"] = settings.MaxOutputTokens,
                ["text"] = new JObject
                {
                    ["format"] = new JObject
                    {
                        ["type"] = "json_schema",
                        ["name"] = "agent_response",
                        ["strict"] = true,
                        ["schema"] = BuildSchema(context)
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(settings.ReasoningEffort))
                request["reasoning"] = new JObject { ["effort"] = settings.ReasoningEffort.Trim() };
            return request;
        }

        public static string BuildStateMessage(
            StageContext context, int trust, IEnumerable<string> recentEvents, TurnDirective directive)
        {
            var text = new StringBuilder();
            text.AppendLine("[현재 게임 상태]");
            text.AppendLine("상황: " + Limit(context.Brief, 200));
            text.AppendLine("요원 위치: " + Limit(context.AgentLocation) + Position(context.AgentPositionX, context.AgentPositionY, context.AgentPositionZ));
            if (context.AgentHeading.HasValue) text.AppendLine("요원 월드 Y축 방향(도): " + Format(context.AgentHeading.Value));
            text.AppendLine("신뢰도: " + trust + "/100");
            text.AppendLine("가진 물건: " + (context.Inventory.Count == 0 ? "없음" : string.Join(", ", context.Inventory)));
            if (!string.IsNullOrEmpty(context.SelectedTargetId))
                text.AppendLine("직전에 선택한 대상 ID: " + Limit(context.SelectedTargetId));
            if (context.LastAction != null)
                text.AppendLine("마지막 행동: " + context.LastAction +
                    (context.LastActionSucceeded.HasValue ? (context.LastActionSucceeded.Value ? " / 성공" : " / 실패") : ""));
            if (context.CurrentAction != null) text.AppendLine("진행 중인 행동: " + context.CurrentAction);

            text.AppendLine("씬에 등록된 오브젝트 (카메라 영상 정보 없음):");
            var objects = RequestObjects(context).ToArray();
            if (objects.Length == 0) text.AppendLine("- 없음");
            foreach (var item in objects)
            {
                bool canInteract = item.CanInteract ?? (item.AllowedVerbs.Contains(AgentVocabulary.Interact) ||
                    item.AllowedVerbs.Contains(AgentVocabulary.Press));
                var verbs = AgentVocabulary.AlwaysAllowedVerbs.Concat(item.AllowedVerbs)
                    .Where(AgentResponseParser.IsImplemented)
                    .Where(verb => verb != AgentVocabulary.Interact && verb != AgentVocabulary.Press);
                if (canInteract) verbs = verbs.Append(AgentVocabulary.Interact);
                text.Append("- ").Append(item.Id).Append(" (").Append(Limit(item.DisplayName)).Append(")")
                    .Append(Position(item.PositionX, item.PositionY, item.PositionZ));
                if (!string.IsNullOrWhiteSpace(item.SceneName)) text.Append(" / 오브젝트 이름: ").Append(Limit(item.SceneName));
                var aliases = item.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias))
                    .Select(alias => Limit(alias.Trim(), 40)).Distinct(System.StringComparer.OrdinalIgnoreCase).Take(6).ToArray();
                if (aliases.Length > 0) text.Append(" / 별칭: ").Append(string.Join(", ", aliases));
                if (!string.IsNullOrWhiteSpace(item.Color)) text.Append(" / 지정 색상: ").Append(Limit(item.Color.Trim(), 40));
                if (!string.IsNullOrWhiteSpace(item.Description)) text.Append(" / 설명: ").Append(Limit(item.Description, 160));
                if (!string.IsNullOrWhiteSpace(item.State)) text.Append(" / 상태: ").Append(Limit(item.State));
                text.Append(" / 가능한 동작: ").Append(string.Join(", ", verbs.Distinct()));
                if (item.MinTrust > 0)
                {
                    text.Append(" / 무섭거나 내키지 않음: 신뢰도 ").Append(item.MinTrust).Append(" 이상이어야 조작함");
                }
                text.AppendLine();
            }

            var events = recentEvents.ToList();
            if (events.Count > 0)
            {
                text.AppendLine("최근 사건:");
                foreach (var gameEvent in events) text.AppendLine("- " + gameEvent);
            }

            text.AppendLine();
            text.AppendLine("[이번 턴 지시]");
            text.Append(directive.ForceMisunderstanding
                ? "오해 유형 '" + directive.MisunderstandingType + "'로 반응한다. comprehension은 그 유형의 규칙대로 적는다."
                : "오해를 일부러 만들지 않는다. 다만 명령이 정말 모호하면 asking, 신뢰도가 부족하면 refused로 반응한다.");
            return text.ToString();
        }

        /// <summary>
        /// 구조화 출력 스키마. 대상 ID를 현재 스테이지의 오브젝트로 제한해서
        /// LLM이 없는 물건을 지어내는 일을 API 단계에서 막는다.
        /// </summary>
        public static JObject BuildSchema(StageContext context)
        {
            var ids = RequestObjects(context).Select(o => o.Id);

            var action = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["verb"] = Enum(AgentVocabulary.Verbs.Where(AgentResponseParser.IsImplemented)),
                    ["target"] = NullableEnum(ids, "행동 대상의 실제 오브젝트 ID"),
                    ["item"] = new JObject { ["type"] = "null", ["description"] = "호환용 필드. 항상 null." },
                    ["direction"] = NullableEnum(AgentVocabulary.Directions, "move_direction 방향. 행동 시작 시 로봇의 방향 기준."),
                    ["steps"] = Nullable("integer", "칸이나 걸음 수 (1~10), direction과 함께 사용. distance와 동시 사용 금지."),
                    ["distance"] = Nullable("number", "Unity 유닛으로 이동할 거리 (0.05~10), direction과 함께 사용. steps와 동시 사용 금지."),
                    ["angle"] = Nullable("number", "rotate 상대 각도 (-180~180, 0 제외). 오른쪽 양수, 왼쪽 음수."),
                    ["text"] = Nullable("string", "speak의 짧은 한국어 대사 (120자 이하).")
                },
                ["required"] = new JArray("verb", "target", "item", "direction", "steps", "distance", "angle", "text")
            };

            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["speech"] = new JObject { ["type"] = "string", ["description"] = "요원의 짧은 응답. 행동 완료를 미리 주장하지 않는다." },
                    ["emotion"] = Enum(AgentVocabulary.Emotions),
                    ["comprehension"] = Enum(AgentVocabulary.Comprehension),
                    ["interpretation"] = new JObject { ["type"] = "string", ["description"] = "요원이 명령을 어떻게 이해했는지 한 문장" },
                    ["clarify_verb"] = NullableEnum(AgentVocabulary.TargetClarificationVerbs,
                        "대상이 불분명한 명시적 행동을 다시 물을 때만 이해한 동사. 그 외에는 null."),
                    ["trust_delta"] = new JObject { ["type"] = "integer", ["description"] = "이번 발화로 인한 신뢰도 변화. -10~10" },
                    ["actions"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = action,
                        ["description"] = "순서대로 실행할 행동 목록. 최대 5개."
                    }
                },
                ["required"] = new JArray("speech", "emotion", "comprehension", "interpretation", "clarify_verb", "trust_delta", "actions")
            };
        }

        private static JObject Message(string role, string content)
        {
            return new JObject { ["role"] = role, ["content"] = content };
        }

        internal static IEnumerable<StageObject> RequestObjects(StageContext context) => context.Objects
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, System.StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            .OrderByDescending(item => item.Id == context.SelectedTargetId ||
                item.Id == context.CurrentAction?.Target || item.Id == context.LastAction?.Target)
            .ThenBy(item => item.Id, System.StringComparer.Ordinal)
            .Take(MaxSceneObjects);

        private static string Limit(string value, int maxLength = 80)
        {
            string text = (value ?? "").Replace('\r', ' ').Replace('\n', ' ');
            return text.Length <= maxLength ? text : text.Substring(0, maxLength);
        }

        private static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Position(float? x, float? y, float? z) => x.HasValue && y.HasValue && z.HasValue
            ? $" / 월드 위치 ({Format(x.Value)}, {Format(y.Value)}, {Format(z.Value)})"
            : "";

        private static JObject Nullable(string type, string description) =>
            new JObject { ["type"] = new JArray(type, "null"), ["description"] = description };

        private static JObject Enum(IEnumerable<string> values)
        {
            return new JObject { ["type"] = "string", ["enum"] = new JArray(values) };
        }

        private static JObject NullableEnum(IEnumerable<string> values, string description)
        {
            var list = values.ToList();
            if (list.Count == 0)
            {
                return new JObject { ["type"] = "null", ["description"] = description };
            }
            var enumValues = new JArray(list);
            enumValues.Add(JValue.CreateNull());
            return new JObject
            {
                ["type"] = new JArray("string", "null"),
                ["enum"] = enumValues,
                ["description"] = description
            };
        }
    }
}
