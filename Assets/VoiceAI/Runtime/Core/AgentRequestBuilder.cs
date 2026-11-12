using System.Collections.Generic;
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

            return new JObject
            {
                ["model"] = settings.Model,
                ["instructions"] = systemPrompt,
                ["input"] = input,
                ["store"] = false,
                ["max_output_tokens"] = settings.MaxOutputTokens,
                ["reasoning"] = new JObject { ["effort"] = settings.ReasoningEffort },
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
        }

        public static string BuildStateMessage(
            StageContext context, int trust, IEnumerable<string> recentEvents, TurnDirective directive)
        {
            var text = new StringBuilder();
            text.AppendLine("[현재 게임 상태]");
            text.AppendLine("상황: " + context.Brief);
            text.AppendLine("요원 위치: " + context.AgentLocation);
            text.AppendLine("신뢰도: " + trust + "/100");
            text.AppendLine("가진 물건: " + (context.Inventory.Count == 0 ? "없음" : string.Join(", ", context.Inventory)));

            text.AppendLine("보이는 오브젝트:");
            if (context.Objects.Count == 0) text.AppendLine("- 없음");
            foreach (var item in context.Objects)
            {
                var verbs = AgentVocabulary.AlwaysAllowedVerbs.Concat(item.AllowedVerbs).Distinct();
                text.Append("- ").Append(item.Id).Append(" (").Append(item.DisplayName).Append("): ")
                    .Append(item.Description).Append(" / 상태: ").Append(item.State)
                    .Append(" / 가능한 동작: ").Append(string.Join(", ", verbs));
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
            var ids = context.Objects.Select(o => o.Id).ToList();

            var action = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["verb"] = Enum(AgentVocabulary.Verbs),
                    ["target"] = NullableEnum(ids, "행동 대상 오브젝트 ID"),
                    ["item"] = NullableEnum(context.Inventory, "use에 사용할 가진 물건 ID"),
                    ["direction"] = NullableEnum(AgentVocabulary.Directions, "move_direction의 방향"),
                    ["steps"] = new JObject { ["type"] = new JArray("integer", "null"), ["description"] = "move_direction의 걸음 수" }
                },
                ["required"] = new JArray("verb", "target", "item", "direction", "steps")
            };

            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["speech"] = new JObject { ["type"] = "string", ["description"] = "요원의 대사. 자막에 표시된다." },
                    ["emotion"] = Enum(AgentVocabulary.Emotions),
                    ["comprehension"] = Enum(AgentVocabulary.Comprehension),
                    ["interpretation"] = new JObject { ["type"] = "string", ["description"] = "요원이 명령을 어떻게 이해했는지 한 문장" },
                    ["trust_delta"] = new JObject { ["type"] = "integer", ["description"] = "이번 발화로 인한 신뢰도 변화. -10~10" },
                    ["actions"] = new JObject { ["type"] = "array", ["items"] = action }
                },
                ["required"] = new JArray("speech", "emotion", "comprehension", "interpretation", "trust_delta", "actions")
            };
        }

        private static JObject Message(string role, string content)
        {
            return new JObject { ["role"] = role, ["content"] = content };
        }

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
