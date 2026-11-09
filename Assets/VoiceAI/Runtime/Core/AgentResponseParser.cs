using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentEscape.VoiceAI
{
    public static class AgentResponseParser
    {
        public const int MaxActionsPerTurn = 5;
        public const int MaxTrustDelta = 10;
        public const int MaxSteps = 10;

        /// <summary>Responses API의 원본 JSON에서 AgentResponse를 꺼낸다.</summary>
        public static AgentResponse Parse(JObject apiResponse)
        {
            string status = (string)apiResponse["status"];
            if (status != "completed")
            {
                string reason = (string)apiResponse["incomplete_details"]?["reason"]
                                ?? (string)apiResponse["error"]?["message"]
                                ?? "unknown";
                throw new AgentApiException("LLM 응답이 완료되지 않았습니다. status=" + status + ", reason=" + reason);
            }

            // output_text는 SDK 편의 속성이라 원본 HTTP 응답에는 없다. output 배열을 직접 읽는다.
            var message = apiResponse["output"]?.FirstOrDefault(item => (string)item["type"] == "message");
            var content = message?["content"] as JArray;
            if (content == null) throw new AgentApiException("LLM 응답에 메시지가 없습니다.");

            var refusal = content.FirstOrDefault(part => (string)part["type"] == "refusal");
            if (refusal != null) throw new AgentApiException("LLM이 응답을 거부했습니다: " + (string)refusal["refusal"]);

            string json = (string)content.FirstOrDefault(part => (string)part["type"] == "output_text")?["text"];
            if (string.IsNullOrEmpty(json)) throw new AgentApiException("LLM 응답에 텍스트가 없습니다.");

            try
            {
                var response = JsonConvert.DeserializeObject<AgentResponse>(json);
                if (response == null) throw new AgentApiException("LLM 응답이 비어 있습니다.");
                if (response.Actions == null) response.Actions = new List<AgentAction>();
                return response;
            }
            catch (JsonException e)
            {
                throw new AgentApiException("LLM 응답 JSON을 읽지 못했습니다.", e);
            }
        }

        /// <summary>
        /// 게임 규칙의 최종 판정. 스키마를 통과했어도 현재 상태에서 불가능한 행동은 여기서 걸러낸다.
        /// 플레이어가 프롬프트를 조작해도 퍼즐을 건너뛸 수 없게 하는 마지막 관문이다.
        /// </summary>
        public static void Validate(AgentResponse response, StageContext context, int trust,
            List<AgentAction> accepted, List<RejectedAction> rejected)
        {
            var objects = context.Objects.ToDictionary(o => o.Id);

            foreach (var action in response.Actions)
            {
                if (accepted.Count >= MaxActionsPerTurn)
                {
                    rejected.Add(new RejectedAction { Action = action, Reason = "한 턴 행동 수 초과" });
                    continue;
                }

                string reason = CheckAction(action, objects, context, trust);
                if (reason != null)
                {
                    rejected.Add(new RejectedAction { Action = action, Reason = reason });
                    continue;
                }

                if (action.Verb == AgentVocabulary.MoveDirection)
                {
                    action.Steps = Math.Max(1, Math.Min(MaxSteps, action.Steps ?? 1));
                }
                accepted.Add(action);
            }
        }

        private static string CheckAction(AgentAction action, Dictionary<string, StageObject> objects,
            StageContext context, int trust)
        {
            if (!AgentVocabulary.Verbs.Contains(action.Verb)) return "알 수 없는 동사: " + action.Verb;

            if (action.Item != null && !context.Inventory.Contains(action.Item))
            {
                return "가지고 있지 않은 물건: " + action.Item;
            }

            if (action.Verb == AgentVocabulary.MoveDirection &&
                !AgentVocabulary.Directions.Contains(action.Direction))
            {
                return "방향이 없음";
            }

            if (AgentVocabulary.NoTargetVerbs.Contains(action.Verb)) return null;

            if (action.Target == null) return action.Verb + "에 대상이 없음";
            if (!objects.TryGetValue(action.Target, out var target)) return "없는 오브젝트: " + action.Target;
            if (AgentVocabulary.AlwaysAllowedVerbs.Contains(action.Verb)) return null;
            if (!target.AllowedVerbs.Contains(action.Verb)) return target.Id + "에는 " + action.Verb + "을(를) 할 수 없음";
            if (trust < target.MinTrust) return target.Id + " 조작에 신뢰도 " + target.MinTrust + " 필요 (현재 " + trust + ")";
            return null;
        }
    }
}
