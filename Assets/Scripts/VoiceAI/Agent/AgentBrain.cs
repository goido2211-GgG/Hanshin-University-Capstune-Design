using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// 신뢰도와 게임 사건을 관리하고 한 턴을 처리한다.
    /// Unity API에 의존하지 않아서 네트워크 없이 시험할 수 있다.
    /// </summary>
    public sealed class AgentBrain
    {
        public const int InitialTrust = 50;
        private const int MaxRecentEvents = 8;
        private const int MaxEventLength = 160;
        internal const int MaxPlayerTextLength = 300;

        private readonly string _systemPrompt;
        private readonly LlmSettings _settings;
        private readonly Func<JObject, CancellationToken, Task<JObject>> _send;
        private int _revision;
        private long _requestSequence;
        private readonly List<string> _recentEvents = new List<string>();

        public int Trust { get; private set; } = InitialTrust;
        public AgentApiMetrics LastApiMetrics { get; private set; }

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
            if (description.Length > MaxEventLength) description = description.Substring(0, MaxEventLength);
            _recentEvents.Add(description);
            if (_recentEvents.Count > MaxRecentEvents) _recentEvents.RemoveAt(0);
        }

        public void ResetConversation(bool keepTrust)
        {
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
            var request = AgentRequestBuilder.Build(_systemPrompt, Array.Empty<ChatTurn>(), context, Trust,
                _recentEvents, directive, playerText, _settings);

            // 실패한 턴은 상태를 바꾸지 않도록, 응답을 받은 뒤에만 기록을 갱신한다.
            long sequence = Interlocked.Increment(ref _requestSequence);
            var timer = Stopwatch.StartNew();
            JObject apiResponse = null;
            try
            {
                apiResponse = await _send(request, cancellation);
                cancellation.ThrowIfCancellationRequested();
                var response = AgentResponseParser.Parse(apiResponse);
                var metrics = RecordMetrics(sequence, timer, AgentApiOutcome.Completed, apiResponse);
                return new PreparedAgentTurn
                {
                    Owner = this, Revision = revision, PlayerText = playerText, Context = context, Response = response,
                    Cancellation = cancellation, ApiMetrics = metrics
                };
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                RecordMetrics(sequence, timer, AgentApiOutcome.Cancelled, apiResponse);
                throw;
            }
            catch
            {
                RecordMetrics(sequence, timer, AgentApiOutcome.Failed, apiResponse);
                throw;
            }
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
                Directive = new TurnDirective(),
                ApiMetrics = prepared.ApiMetrics
            };
            AgentResponseParser.Validate(response, prepared.Context, Trust, result.Actions, result.Rejected, false);
            prepared.Committed = true;
            _revision++;
            Trust = nextTrust;
            response.TrustDelta = delta;

            return result;
        }

        private AgentApiMetrics RecordMetrics(long sequence, Stopwatch timer, AgentApiOutcome outcome, JObject apiResponse)
        {
            var usage = apiResponse?["usage"] as JObject;
            var metrics = new AgentApiMetrics(outcome, timer.ElapsedMilliseconds,
                ReadTokenCount(usage?["input_tokens"]), ReadTokenCount(usage?["output_tokens"]), ReadTokenCount(usage?["total_tokens"]));
            if (sequence == Interlocked.Read(ref _requestSequence)) LastApiMetrics = metrics;
            return metrics;
        }

        private static int? ReadTokenCount(JToken value) => value?.Type == JTokenType.Integer &&
            int.TryParse(value.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : (int?)null;
    }
}
