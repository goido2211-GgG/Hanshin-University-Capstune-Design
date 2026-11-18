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
        public const float MinDistance = 0.05f;
        public const float MaxDistance = 10f;
        public const float MaxRotationDegrees = 180f;
        public const int MaxSpeechLength = 120;

        /// <summary>Responses API의 원본 JSON에서 AgentResponse를 꺼낸다.</summary>
        public static AgentResponse Parse(JObject apiResponse)
        {
            string status = (string)apiResponse?["status"];
            if (status != "completed")
            {
                string reason = (string)(apiResponse?["incomplete_details"] as JObject)?["reason"];
                throw new AgentApiException(reason == "max_output_tokens"
                    ? "명령 해석이 모델의 출력 한도를 넘었습니다. 명령을 짧게 나눠 주세요."
                    : "OpenAI가 명령 해석을 끝내지 못했습니다.");
            }

            // Responses HTTP의 메시지와 content에서 구조화된 텍스트를 가져온다.
            var message = (apiResponse["output"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(item => (string)item["type"] == "message");
            var content = (message?["content"] as JArray)?.OfType<JObject>().ToArray();
            if (content == null) throw new AgentApiException("OpenAI 응답에 메시지가 없습니다.");
            if (content.Any(part => (string)part["type"] == "refusal"))
                throw new AgentApiException("OpenAI가 이 명령의 해석을 거부했습니다.");

            string json = (string)content.FirstOrDefault(part => (string)part["type"] == "output_text")?["text"];
            if (string.IsNullOrEmpty(json)) throw new AgentApiException("OpenAI 응답에 텍스트가 없습니다.");

            try
            {
                var plan = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (!(plan["actions"] is JArray actions) || actions.Any(action => !HasValidArgumentTypes(action)))
                    throw new AgentApiException("OpenAI 응답의 행동 인자 형식이 올바르지 않습니다.");
                var response = plan.ToObject<AgentResponse>();
                if (response.Speech != null && response.Speech.Length > MaxSpeechLength)
                    response.Speech = response.Speech.Substring(0, MaxSpeechLength);
                return response;
            }
            catch (JsonException e)
            {
                throw new AgentApiException("OpenAI 응답의 명령 형식을 읽지 못했습니다.", e);
            }
        }

        // Json.NET은 문자열 숫자도 수치 필드로 변환하므로 변환 전에 JSON 타입을 확인한다.
        private static bool HasValidArgumentTypes(JToken token)
        {
            if (!(token is JObject action) || action["verb"]?.Type != JTokenType.String) return false;
            foreach (var field in action.Properties())
            {
                var type = field.Value.Type;
                switch (field.Name)
                {
                    case "verb":
                    case "target":
                    case "item":
                    case "direction":
                    case "text":
                        if (type != JTokenType.Null && type != JTokenType.String) return false;
                        break;
                    case "steps":
                        if (type != JTokenType.Null && type != JTokenType.Integer) return false;
                        break;
                    case "distance":
                    case "angle":
                        if (type != JTokenType.Null && type != JTokenType.Integer && type != JTokenType.Float) return false;
                        break;
                    default: return false;
                }
            }
            return true;
        }

        /// <summary>게임 상태와 권한을 기준으로 실행 계획을 확인한다.</summary>
        public static void Validate(AgentResponse response, StageContext context, int trust,
            List<AgentAction> accepted, List<RejectedAction> rejected, bool checkInventory = true)
        {
            var actions = response.Actions;
            if (actions == null) return;
            if (actions.Count > MaxActionsPerTurn)
            {
                rejected.Add(new RejectedAction { Reason = "한 번에 최대 5개 행동을 지시할 수 있습니다." });
                return;
            }

            foreach (var action in actions)
            {
                string reason = response.Comprehension != AgentVocabulary.Understood
                    ? "확실하게 이해한 명령만 실행합니다."
                    : CheckAction(action, context, trust, checkInventory);
                if (reason == null && IsButtonAction(action.Verb) &&
                    context.Objects.Any(target => target.Id == action.Target && target.CanInteract == false))
                    reason = "지정한 버튼이 현재 연결되어 있지 않습니다.";
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
            if (action == null) return "행동이 비어 있습니다.";
            if (!IsImplemented(action.Verb)) return "아직 사용할 수 없는 행동입니다.";

            bool isMovement = action.Verb == AgentVocabulary.MoveDirection;
            bool noTarget = AgentVocabulary.NoTargetVerbs.Contains(action.Verb);
            if (action.Item != null || (noTarget && action.Target != null) ||
                (!isMovement && (action.Direction != null || action.Steps.HasValue || action.Distance.HasValue)) ||
                (action.Verb != AgentVocabulary.Rotate && action.Angle.HasValue) ||
                (action.Verb != AgentVocabulary.Speak && action.Text != null))
                return "행동에 사용할 수 없는 인자가 있습니다.";

            if (isMovement)
            {
                if (!AgentVocabulary.Directions.Contains(action.Direction)) return "어느 방향으로 갈까요?";
                if (action.Distance.HasValue && action.Steps.HasValue) return "걸음 수와 거리는 하나만 지정해 주세요.";
                if (action.Steps.HasValue && (action.Steps.Value < 1 || action.Steps.Value > MaxSteps))
                    return "한 번에 1~10칸까지 이동할 수 있습니다.";
                if (action.Distance.HasValue && !InRange(action.Distance.Value, MinDistance, MaxDistance))
                    return "한 번에 0.05~10 유닛까지 이동할 수 있습니다.";
            }
            if (action.Verb == AgentVocabulary.Rotate &&
                (!action.Angle.HasValue || action.Angle.Value == 0 || !InRange(action.Angle.Value, -MaxRotationDegrees, MaxRotationDegrees)))
                return "0도를 제외한 180도 이내의 회전 각도를 지정해 주세요.";
            if (action.Verb == AgentVocabulary.Speak && (string.IsNullOrWhiteSpace(action.Text) || action.Text.Length > MaxSpeechLength))
                return "말할 문장을 120자 이내로 지정해 주세요.";
            if (noTarget) return null;

            if (string.IsNullOrWhiteSpace(action.Target)) return "어느 대상을 말씀하시는 건가요?";
            var targets = context.Objects.Where(o => o.Id == action.Target).ToArray();
            if (targets.Length == 0) return "지정한 대상을 씬에서 찾지 못했습니다.";
            if (targets.Length != 1) return "대상 ID가 중복되었습니다.";
            var target = targets[0];
            if (AgentVocabulary.AlwaysAllowedVerbs.Contains(action.Verb)) return null;
            if (checkInventory && action.Verb == AgentVocabulary.Drop && !context.Inventory.Contains(action.Target))
                return "가지고 있지 않은 대상: " + target.DisplayName;
            if (checkInventory && action.Verb == AgentVocabulary.PickUp && context.Inventory.Contains(action.Target))
                return "이미 들고 있는 대상: " + target.DisplayName;
            bool isButtonAction = IsButtonAction(action.Verb);
            bool allowed = isButtonAction
                ? target.AllowedVerbs.Contains(AgentVocabulary.Press) || target.AllowedVerbs.Contains(AgentVocabulary.Interact)
                : target.AllowedVerbs.Contains(action.Verb);
            if (!allowed) return isButtonAction
                ? "지정한 대상에 사용할 수 있는 버튼 동작이 없습니다."
                : "지정한 대상에 이 행동을 실행할 수 없습니다.";
            if (trust < target.MinTrust) return target.DisplayName + " 조작에 신뢰도 " + target.MinTrust + " 필요 (현재 " + trust + ")";
            return null;
        }

        private static bool InRange(float value, float minimum, float maximum) => value >= minimum && value <= maximum;

        private static bool IsButtonAction(string verb) => verb == AgentVocabulary.Interact || verb == AgentVocabulary.Press;

        public static bool IsImplemented(string verb) => verb == AgentVocabulary.MoveTo ||
            verb == AgentVocabulary.MoveDirection || verb == AgentVocabulary.Inspect ||
            verb == AgentVocabulary.PickUp || verb == AgentVocabulary.Drop ||
            verb == AgentVocabulary.Wait || verb == AgentVocabulary.Stop || verb == AgentVocabulary.Press ||
            verb == AgentVocabulary.Interact || verb == AgentVocabulary.Rotate || verb == AgentVocabulary.Speak;
    }
}
