using System.Collections.Generic;
using Newtonsoft.Json;

namespace AgentEscape.VoiceAI
{
    /// <summary>LLM이 고를 수 있는 값 목록. 여기 없는 값은 API 단계에서 막힌다.</summary>
    public static class AgentVocabulary
    {
        public const string MoveTo = "move_to";
        public const string MoveDirection = "move_direction";
        public const string FollowDrone = "follow_drone";
        public const string Stop = "stop";
        public const string Wait = "wait";
        public const string Inspect = "inspect";
        public const string PickUp = "pick_up";
        public const string Drop = "drop";
        public const string Use = "use";
        public const string Open = "open";
        public const string Close = "close";
        public const string Push = "push";
        public const string Pull = "pull";
        public const string Press = "press";

        public static readonly string[] Verbs =
        {
            MoveTo, MoveDirection, FollowDrone, Stop, Wait, Inspect,
            PickUp, Drop, Use, Open, Close, Push, Pull, Press
        };

        /// <summary>대상 없이 실행할 수 있는 동사.</summary>
        public static readonly HashSet<string> NoTargetVerbs = new HashSet<string>
        {
            MoveDirection, FollowDrone, Stop, Wait
        };

        /// <summary>어떤 오브젝트에든 항상 허용되는 동사. 신뢰도 검사도 받지 않는다.</summary>
        public static readonly HashSet<string> AlwaysAllowedVerbs = new HashSet<string>
        {
            MoveTo, Inspect
        };

        public static readonly string[] Directions = { "forward", "back", "left", "right" };

        public static readonly string[] Emotions =
        {
            "neutral", "curious", "confused", "proud", "happy", "scared", "hurt", "annoyed"
        };

        public const string Understood = "understood";
        public const string Misunderstood = "misunderstood";
        public const string Asking = "asking";
        public const string Refused = "refused";

        public static readonly string[] Comprehension = { Understood, Misunderstood, Asking, Refused };
    }

    /// <summary>요원이 실행할 행동 하나. 요원 구현 담당자가 받아서 실행한다.</summary>
    public sealed class AgentAction
    {
        [JsonProperty("verb")] public string Verb;
        [JsonProperty("target")] public string Target;
        [JsonProperty("item")] public string Item;
        [JsonProperty("direction")] public string Direction;
        [JsonProperty("steps")] public int? Steps;

        public override string ToString()
        {
            var text = Verb;
            if (Target != null) text += " target=" + Target;
            if (Item != null) text += " item=" + Item;
            if (Direction != null) text += " direction=" + Direction;
            if (Steps != null) text += " steps=" + Steps;
            return text;
        }
    }

    /// <summary>LLM 응답 원본.</summary>
    public sealed class AgentResponse
    {
        [JsonProperty("speech")] public string Speech = "";
        [JsonProperty("emotion")] public string Emotion = "neutral";
        [JsonProperty("comprehension")] public string Comprehension = AgentVocabulary.Understood;
        [JsonProperty("interpretation")] public string Interpretation = "";
        [JsonProperty("trust_delta")] public int TrustDelta;
        [JsonProperty("actions")] public List<AgentAction> Actions = new List<AgentAction>();
    }

    /// <summary>검증에서 탈락한 행동과 그 이유.</summary>
    public sealed class RejectedAction
    {
        public AgentAction Action;
        public string Reason;
    }

    /// <summary>한 턴의 최종 결과. 게임 코드는 이것만 보면 된다.</summary>
    public sealed class AgentTurnResult
    {
        public string PlayerText;
        public AgentResponse Response;
        public List<AgentAction> Actions = new List<AgentAction>();
        public List<RejectedAction> Rejected = new List<RejectedAction>();
        public int Trust;
        public TurnDirective Directive;
        /// <summary>API 오류로 대체 대사를 사용한 턴이면 true.</summary>
        public bool IsFallback;
    }

    /// <summary>요원에게 현재 보이는 오브젝트. 퍼즐 정답은 넣지 않는다.</summary>
    public sealed class StageObject
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string State;
        /// <summary>move_to, inspect 외에 허용할 동사. 비어 있으면 그 둘만 가능하다.</summary>
        public List<string> AllowedVerbs = new List<string>();
        /// <summary>이 값보다 신뢰도가 낮으면 조작을 거부한다. 0이면 조건 없음.</summary>
        public int MinTrust;
    }

    /// <summary>턴마다 게임이 LLM에게 알려주는 현재 상황.</summary>
    public sealed class StageContext
    {
        public string StageId = "";
        /// <summary>요원이 아는 범위의 상황 설명.</summary>
        public string Brief = "";
        /// <summary>0: 잘 알아들음 ~ 3: 자주 오해함.</summary>
        public int ConfusionLevel;
        public string AgentLocation = "";
        public List<StageObject> Objects = new List<StageObject>();
        /// <summary>요원이 들고 있는 오브젝트 ID. Objects에도 들어 있어야 한다.</summary>
        public List<string> Inventory = new List<string>();
    }
}
