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
        private int _revision;
        private readonly List<ChatTurn> _history = new List<ChatTurn>();
        private readonly List<string> _recentEvents = new List<string>();

        public int Trust { get; private set; } = InitialTrust;

        public AgentBrain(string systemPrompt, LlmSettings settings,
            Func<JObject, CancellationToken, Task<JObject>> send, FrustrationDirector director = null)
        {
            _systemPrompt = systemPrompt;
            _settings = settings;
            _send = send;
            // director 매개변수는 기존 호출 호환용이다. 정확한 명령의 무작위 강제 오해는 사용하지 않는다.
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
            _revision++;
            if (!keepTrust) Trust = InitialTrust;
        }

        public async Task<AgentTurnResult> RunTurnAsync(string playerText, StageContext context, CancellationToken cancellation)
        {
            var prepared = await PrepareTurnAsync(playerText, context, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return CommitTurn(prepared);
        }

        public async Task<PreparedAgentTurn> PrepareTurnAsync(string playerText, StageContext context, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            playerText = (playerText ?? "").Trim();
            if (playerText.Length > MaxPlayerTextLength) playerText = playerText.Substring(0, MaxPlayerTextLength);

            int revision = _revision;
            var directive = new TurnDirective();
            var request = AgentRequestBuilder.Build(_systemPrompt, _history, context, Trust,
                _recentEvents, directive, playerText, _settings);

            // 실패한 턴은 상태를 바꾸지 않도록, 응답을 받은 뒤에만 기록을 갱신한다.
            var apiResponse = await _send(request, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var response = AgentResponseParser.Parse(apiResponse);
            return new PreparedAgentTurn
            {
                Owner = this, Revision = revision, PlayerText = playerText, Context = context, Response = response,
                Cancellation = cancellation
            };
        }

        // 호출자는 최신 작업인지 먼저 검사해야 한다. 검사와 확정 사이에 await를 두지 않는다.
        public AgentTurnResult CommitTurn(PreparedAgentTurn prepared)
        {
            if (prepared == null || prepared.Owner != this || prepared.Committed || prepared.Revision != _revision)
                throw new OperationCanceledException("이미 종료되었거나 오래된 해석 결과입니다.");
            prepared.Cancellation.ThrowIfCancellationRequested();
            var response = prepared.Response;

            int delta = Math.Max(-AgentResponseParser.MaxTrustDelta, Math.Min(AgentResponseParser.MaxTrustDelta, response.TrustDelta));
            int nextTrust = Math.Max(0, Math.Min(100, Trust + delta));

            var result = new AgentTurnResult
            {
                PlayerText = prepared.PlayerText,
                Response = response,
                Trust = nextTrust,
                Directive = new TurnDirective()
            };
            AgentResponseParser.Validate(response, prepared.Context, nextTrust, result.Actions, result.Rejected, false);
            prepared.Committed = true;
            _revision++;
            Trust = nextTrust;
            response.TrustDelta = delta;

            foreach (var rejected in result.Rejected)
            {
                ReportEvent("행동 실패: " + rejected.Action + " (" + rejected.Reason + ")");
            }

            _history.Add(new ChatTurn { Role = "user", Content = prepared.PlayerText });
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
