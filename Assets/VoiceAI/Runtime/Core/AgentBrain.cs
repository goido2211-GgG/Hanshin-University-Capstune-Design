using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// 대화 기록, 신뢰도, 오해 판정을 관리하고 한 턴을 처리한다.
    /// Unity API에 의존하지 않아서 네트워크 없이 시험할 수 있다.
    /// </summary>
    public sealed class AgentBrain
    {
        public const int InitialTrust = 50;
        private const int MaxHistoryMessages = 12;
        private const int MaxRecentEvents = 8;
        private const int MaxPlayerTextLength = 300;

        private readonly string _systemPrompt;
        private readonly LlmSettings _settings;
        private readonly Func<JObject, CancellationToken, Task<JObject>> _send;
        private readonly FrustrationDirector _director;
        private readonly List<ChatTurn> _history = new List<ChatTurn>();
        private readonly List<string> _recentEvents = new List<string>();

        public int Trust { get; private set; } = InitialTrust;

        public AgentBrain(string systemPrompt, LlmSettings settings,
            Func<JObject, CancellationToken, Task<JObject>> send, FrustrationDirector director = null)
        {
            _systemPrompt = systemPrompt;
            _settings = settings;
            _send = send;
            _director = director ?? new FrustrationDirector();
        }

        /// <summary>실행 결과처럼 요원이 다음 턴에 알아야 할 일을 기록한다.</summary>
        public void ReportEvent(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return;
            _recentEvents.Add(description);
            if (_recentEvents.Count > MaxRecentEvents) _recentEvents.RemoveAt(0);
        }

        public void ResetConversation(bool keepTrust)
        {
            _history.Clear();
            _recentEvents.Clear();
            _director.Reset();
            if (!keepTrust) Trust = InitialTrust;
        }

        public async Task<AgentTurnResult> RunTurnAsync(string playerText, StageContext context, CancellationToken cancellation)
        {
            playerText = (playerText ?? "").Trim();
            if (playerText.Length > MaxPlayerTextLength) playerText = playerText.Substring(0, MaxPlayerTextLength);

            var directive = _director.Decide(context.ConfusionLevel);
            var request = AgentRequestBuilder.Build(_systemPrompt, _history, context, Trust,
                _recentEvents, directive, playerText, _settings);

            // 실패한 턴은 상태를 바꾸지 않도록, 응답을 받은 뒤에만 기록을 갱신한다.
            var apiResponse = await _send(request, cancellation);
            var response = AgentResponseParser.Parse(apiResponse);

            int delta = Math.Max(-AgentResponseParser.MaxTrustDelta, Math.Min(AgentResponseParser.MaxTrustDelta, response.TrustDelta));
            Trust = Math.Max(0, Math.Min(100, Trust + delta));
            _director.Report(response.Comprehension);

            var result = new AgentTurnResult
            {
                PlayerText = playerText,
                Response = response,
                Trust = Trust,
                Directive = directive
            };
            AgentResponseParser.Validate(response, context, Trust, result.Actions, result.Rejected);

            foreach (var rejected in result.Rejected)
            {
                ReportEvent("행동 실패: " + rejected.Action + " (" + rejected.Reason + ")");
            }

            _history.Add(new ChatTurn { Role = "user", Content = playerText });
            _history.Add(new ChatTurn { Role = "assistant", Content = SummarizeForHistory(result) });
            if (_history.Count > MaxHistoryMessages) _history.RemoveRange(0, _history.Count - MaxHistoryMessages);

            return result;
        }

        private static string SummarizeForHistory(AgentTurnResult result)
        {
            string actions = string.Join("; ", result.Actions.Select(a => a.ToString()));
            return result.Response.Speech + (actions.Length > 0 ? " [행동: " + actions + "]" : "");
        }
    }
}
