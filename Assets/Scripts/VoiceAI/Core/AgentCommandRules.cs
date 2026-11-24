using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentEscape.VoiceAI
{
    /// <summary>즉시 중지와 뜻이 확실한 로컬 명령을 처리한다.</summary>
    public static class AgentCommandRules
    {
        private const int MaxLocalCommandLength = 160;
        private const string Number = @"(?:[0-9]+(?:\.[0-9]+)?|[가-힣]+)";
        private const string MoveDirection = @"(?<direction>앞(?:쪽)?으로|뒤(?:쪽)?로|왼쪽으로|오른쪽으로|좌측으로|우측으로)";
        private const string TurnDirection = @"(?<direction>왼쪽(?:으로)?|오른쪽(?:으로)?|좌측(?:으로)?|우측(?:으로)?)";
        private const string MoveAmount = @"(?:(?<slight>조금)|(?<amount>" + Number + @")\s*(?<unit>칸|걸음|미터|[mM]))";
        private const string TurnAmount = @"(?<angle>" + Number + @")\s*도(?:를)?";
        private const string MoveArguments = "(?:" + MoveDirection + @"(?:\s*" + MoveAmount + ")?|" + MoveAmount + @"\s*" + MoveDirection + ")";
        private const string TurnArguments = "(?:" + TurnDirection + @"(?:\s*" + TurnAmount + ")?|" + TurnAmount + @"\s*" + TurnDirection + ")";
        private const string RequestEnding = @"(?:\s*(?:줘|주세요)|요|라)?";

        private static readonly Regex RelativeMove = new Regex(
            "^" + MoveArguments + @"\s*(?:(?:가|이동해|움직여)" + RequestEnding + "|가세요|이동하세요|움직이세요)$");
        private static readonly Regex Rotate = new Regex(
            "^" + TurnArguments + @"\s*(?:(?:돌아|회전해)" + RequestEnding + "|도세요|회전하세요)$");
        private static readonly Regex MoveThenTurn = new Regex(
            @"^(?<arguments>.+?)\s*(?:가서|가고|이동해서|이동하고)\s*,?\s*(?<turn>.+)$");
        private static readonly Regex MoveThenStop = new Regex(
            "^" + MoveArguments + @"\s*(?:가다가|이동하다가|움직이다가)\s*(?:바로\s*)?(?:멈춰|정지해)" + RequestEnding + "$");

        private static string Normalize(string text) => (text ?? "").Trim().TrimEnd('.', '!', '?', '。', '！', '？').Trim();

        public static bool IsResumeExploration(string text)
        {
            string normalized = Normalize(text);
            return normalized == "탐색 재개" || normalized == "탐색 계속";
        }

        public static bool IsStop(string text)
        {
            string normalized = Normalize(text);
            switch (normalized.ToLowerInvariant())
            {
                case "중지": case "멈춰": case "멈춰줘": case "멈춰 줘":
                case "멈춰요": case "멈춰주세요": case "멈춰 주세요":
                case "취소": case "정지": case "stop": case "cancel":
                    return true;
                default: return normalized.Length <= MaxLocalCommandLength && MoveThenStop.IsMatch(normalized);
            }
        }

        public static bool TryParseMotionCommand(string text, out AgentAction[] actions)
        {
            actions = Array.Empty<AgentAction>();
            string normalized = Normalize(text);
            if (normalized.Length > MaxLocalCommandLength) return false;

            if (TryParseMove(normalized, out var move))
            {
                actions = new[] { move };
                return true;
            }
            if (TryParseTurn(normalized, out var turn))
            {
                actions = new[] { turn };
                return true;
            }

            Match sequence = MoveThenTurn.Match(normalized);
            if (!sequence.Success || !TryParseMove(sequence.Groups["arguments"].Value + " 가", out move) ||
                !TryParseTurn(sequence.Groups["turn"].Value, out turn)) return false;
            actions = new[] { move, turn };
            return true;
        }

        private static bool TryParseMove(string text, out AgentAction action)
        {
            action = null;
            Match match = RelativeMove.Match(text);
            if (!match.Success) return false;

            string direction = match.Groups["direction"].Value[0] switch
            {
                '앞' => "forward",
                '뒤' => "back",
                '왼' => "left",
                '좌' => "left",
                _ => "right"
            };
            var move = new AgentAction { Verb = AgentVocabulary.MoveDirection, Direction = direction };
            Group amount = match.Groups["amount"];
            if (match.Groups["slight"].Success) move.Distance = 0.5f;
            else if (!amount.Success) move.Steps = 1;
            else if (TryReadNumber(amount.Value, out float value))
            {
                string unit = match.Groups["unit"].Value;
                if (unit == "칸" || unit == "걸음")
                {
                    if (value >= int.MaxValue || value != Math.Truncate(value)) return false;
                    move.Steps = (int)value;
                }
                else move.Distance = value;
            }
            else return false;
            action = move;
            return true;
        }

        private static bool TryParseTurn(string text, out AgentAction action)
        {
            action = null;
            Match match = Rotate.Match(text);
            if (!match.Success) return false;

            float angle = 90f;
            Group amount = match.Groups["angle"];
            if (amount.Success && !TryReadNumber(amount.Value, out angle)) return false;
            char direction = match.Groups["direction"].Value[0];
            action = new AgentAction { Verb = AgentVocabulary.Rotate, Angle = direction == '왼' || direction == '좌' ? -angle : angle };
            return true;
        }

        private static bool TryReadNumber(string text, out float value)
        {
            value = text switch
            {
                "한" or "하나" or "일" => 1,
                "두" or "둘" or "이" => 2,
                "세" or "셋" or "삼" => 3,
                "네" or "넷" or "사" => 4,
                "다섯" or "오" => 5,
                "여섯" or "육" => 6,
                "일곱" or "칠" => 7,
                "여덟" or "팔" => 8,
                "아홉" or "구" => 9,
                "열" or "십" => 10,
                "구십" => 90,
                _ => -1
            };
            return value >= 0 || float.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
        }

        private static readonly Regex ButtonCommand = new Regex(
            @"^(?<selector>.*?)\s*버튼(?:을|를)?\s*(?:(?<press>눌러)(?:\s*줘|주세요)?|앞(?:까지|으로)?\s*가(?:\s*줘|세요)?)$");

        public static bool TryParseButtonCommand(string text, out string verb, out string selector)
        {
            var match = ButtonCommand.Match(Normalize(text));
            verb = match.Success ? (match.Groups["press"].Success ? AgentVocabulary.Press : AgentVocabulary.MoveTo) : null;
            selector = match.Success ? match.Groups["selector"].Value.Trim() : null;
            if (selector == "저" || selector == "그" || selector == "이") selector = "";
            return match.Success;
        }

        public static AgentAction Copy(AgentAction action) => action == null ? null : new AgentAction
        {
            Verb = action.Verb, Target = action.Target, Item = action.Item,
            Direction = action.Direction, Steps = action.Steps, Distance = action.Distance,
            Angle = action.Angle, Text = action.Text
        };
    }
}
