using System;

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

        public static AgentAction Copy(AgentAction action) => action == null ? null : new AgentAction
        {
            Verb = action.Verb, Target = action.Target, Item = action.Item,
            Direction = action.Direction, Steps = action.Steps
        };
    }
}
