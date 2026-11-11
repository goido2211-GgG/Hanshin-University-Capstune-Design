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
            List<AgentAction> accepted, List<RejectedAction> rejected, bool checkInventory = true)
        {
            foreach (var action in response.Actions ?? new List<AgentAction>())
            {
                if (action != null && action.Verb == AgentVocabulary.MoveDirection)
                    action.Steps = Math.Max(1, Math.Min(MaxSteps, action.Steps ?? 1));
                if (accepted.Count >= MaxActionsPerTurn)
                {
                    rejected.Add(new RejectedAction { Action = action, Reason = "한 턴 행동 수 초과" });
                    continue;
                }

                string reason = response.Comprehension == AgentVocabulary.Asking || response.Comprehension == AgentVocabulary.Refused
                    ? "질문 또는 거부 응답의 행동은 실행하지 않습니다."
                    : CheckAction(action, context, trust, checkInventory);
                if (reason != null)
                {
                    rejected.Add(new RejectedAction { Action = action, Reason = reason });
                    continue;
                }

                accepted.Add(action);
            }
        }

        public static string CheckAction(AgentAction action, StageContext context, int trust, bool checkInventory = true)
        {
            if (action == null) return "행동이 비어 있음";
            if (!AgentVocabulary.Verbs.Contains(action.Verb)) return "알 수 없는 동사: " + action.Verb;
            if (!IsImplemented(action.Verb)) return "아직 구현되지 않은 행동: " + action.Verb;

            if (checkInventory && action.Item != null && !context.Inventory.Contains(action.Item))
            {
                return "가지고 있지 않은 물건: " + action.Item;
            }

            if (action.Verb == AgentVocabulary.MoveDirection &&
                !AgentVocabulary.Directions.Contains(action.Direction))
            {
                return "방향이 없음";
            }
            if (action.Verb == AgentVocabulary.MoveDirection && action.Steps.HasValue &&
                (action.Steps.Value < 1 || action.Steps.Value > MaxSteps)) return "걸음 수 범위를 벗어났습니다.";

            if (AgentVocabulary.NoTargetVerbs.Contains(action.Verb)) return null;

            if (action.Target == null) return action.Verb + "에 대상이 없음";
            var targets = context.Objects.Where(o => o.Id == action.Target).ToArray();
            if (targets.Length != 1) return "없거나 중복된 오브젝트: " + action.Target;
            var target = targets[0];
            if (AgentVocabulary.AlwaysAllowedVerbs.Contains(action.Verb)) return null;
            if (checkInventory && action.Verb == AgentVocabulary.Drop && !context.Inventory.Contains(action.Target))
                return "가지고 있지 않은 대상: " + action.Target;
            if (checkInventory && action.Verb == AgentVocabulary.PickUp && context.Inventory.Contains(action.Target))
                return "이미 들고 있는 대상: " + action.Target;
            if (!target.AllowedVerbs.Contains(action.Verb)) return target.Id + "에는 " + action.Verb + "을(를) 할 수 없음";
            if (trust < target.MinTrust) return target.Id + " 조작에 신뢰도 " + target.MinTrust + " 필요 (현재 " + trust + ")";
            return null;
        }

        public static bool IsImplemented(string verb) => verb == AgentVocabulary.MoveTo ||
            verb == AgentVocabulary.MoveDirection || verb == AgentVocabulary.Inspect ||
            verb == AgentVocabulary.PickUp || verb == AgentVocabulary.Drop ||
            verb == AgentVocabulary.Wait || verb == AgentVocabulary.Stop;
    }
}
