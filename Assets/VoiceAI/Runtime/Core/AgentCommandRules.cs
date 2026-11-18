using System;
using System.Text.RegularExpressions;

namespace AgentEscape.VoiceAI
{
    /// <summary>1차 범위는 로컬 중지에 한정한다. 부분 문자열이나 LLM 판정을 사용하지 않는다.</summary>
    public static class AgentCommandRules
    {
        public static bool IsResumeExploration(string text)
        {
            string normalized = (text ?? "").Trim().TrimEnd('.', '!', '?', '。', '！', '？').Trim();
            return normalized == "탐색 재개" || normalized == "탐색 계속";
        }

        public static bool IsStop(string text)
        {
            string normalized = (text ?? "").Trim().TrimEnd('.', '!', '?', '。', '！', '？').Trim();
            switch (normalized.ToLowerInvariant())
            {
                case "중지": case "멈춰": case "멈춰줘": case "멈춰 줘":
                case "멈춰요": case "취소": case "정지": case "stop": case "cancel":
                    return true;
                default: return false;
            }
        }

        private static readonly Regex ButtonCommand = new Regex(
            @"^(?<selector>.*?)\s*버튼(?:을|를)?\s*(?:(?<press>눌러)(?:\s*줘|주세요)?|앞(?:까지|으로)?\s*가(?:\s*줘|세요)?)$");

        public static bool TryParseButtonCommand(string text, out string verb, out string selector)
        {
            var match = ButtonCommand.Match((text ?? "").Trim().TrimEnd('.', '!', '?', '。', '！', '？').Trim());
            verb = match.Success ? (match.Groups["press"].Success ? AgentVocabulary.Press : AgentVocabulary.MoveTo) : null;
            selector = match.Success ? match.Groups["selector"].Value.Trim() : null;
            if (selector == "저" || selector == "그" || selector == "이") selector = "";
            return match.Success;
        }

        public static AgentAction Copy(AgentAction action) => action == null ? null : new AgentAction
        {
            Verb = action.Verb, Target = action.Target, Item = action.Item,
            Direction = action.Direction, Steps = action.Steps
        };
    }
}
