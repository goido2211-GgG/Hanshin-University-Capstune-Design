using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    public enum AgentCommandRouting { PreferLocal, ApiInterpretation, LocalOnly }

    public enum VoiceAgentState { Idle, Recording, Transcribing, Thinking }

    [RequireComponent(typeof(SceneStageContext))]
    public sealed class VoiceAgent : MonoBehaviour
    {
        private const float MinRecordingSeconds = 0.3f;
        private const float SilenceRms = 0.002f;
        [Header("API 연결")]
        [Tooltip("기존 씬은 시작 시 연결합니다. 통합 시험 씬은 끄고 첫 음성/일반 채팅 요청 때 연결합니다.")]
        public bool initializeApiOnAwake = true;
        [Tooltip("Prefer Local: 확실한 명령만 로컬. Api Interpretation: 일반 명령은 API. Local Only: 키 없는 채팅 시험.")]
        public AgentCommandRouting commandRouting = AgentCommandRouting.PreferLocal;
        public bool ApiReady => _brain != null;
        public AgentApiMetrics LastApiMetrics => _brain?.LastApiMetrics;
        public long? LastTranscriptionMilliseconds { get; private set; }

        [Header("모델")]
        public string llmModel = "gpt-6-luna";
        public string reasoningEffort = "low";
        public int maxOutputTokens = 800;
        public string transcribeModel = "gpt-transcribe";
        public string language = "ko";
        [Header("녹음")]
        public int maxRecordingSeconds = 15;
        [Header("프롬프트")]
        public string systemPromptResource = "VoiceAI/Prompts/agent_system";
        [TextArea(2, 4)] public string transcribePrompt = "방탈출 게임에서 플레이어가 로봇 요원에게 내리는 짧은 한국어 음성 명령.";

        public event Action<VoiceAgentState> StateChanged;
        public event Action<string> PlayerTranscribed;
        /// <summary>해석 완료. 실제 실행 성공은 Runner.ActionFinished/CommandFinished로 확인한다.</summary>
        public event Action<AgentTurnResult> TurnCompleted;
        public event Action<string> ErrorOccurred;
        public event Action<string> LocalNotice;
        public VoiceAgentState State { get; private set; } = VoiceAgentState.Idle;
        public int Trust => _brain != null ? _brain.Trust : AgentBrain.InitialTrust;
        public string StartupError { get; private set; } = "";
        public AgentActionRunner Runner => EnsureRunner();

        private SceneStageContext _stage;
        private AgentActionRunner _runner;
        private OpenAIClient _client;
        private AgentBrain _brain;
        private MicrophoneRecorder _recorder;
        private CancellationTokenSource _lifetime;
        private CancellationTokenSource _request;
        private AgentCommandId _requestId;
        private AgentAction _previousAction;

        private AgentActionRunner EnsureRunner()
        {
            if (_runner != null) return _runner;
            _stage = GetComponent<SceneStageContext>();
            _runner = GetComponent<AgentActionRunner>();
            if (_runner == null) _runner = gameObject.AddComponent<AgentActionRunner>();
            _runner.Initialize(this, _stage);
            _runner.CommandFinished += OnCommandFinished;
            return _runner;
        }

        public void RegisterBody(AgentBodyController body) => EnsureRunner().AttachBody(body);
        public void UnregisterBody(AgentBodyController body) { if (_runner != null) _runner.DetachBody(body); }

        private void Awake()
        {
            EnsureRunner();
            _lifetime = new CancellationTokenSource();
            _recorder = new MicrophoneRecorder(maxRecordingSeconds);
            if (initializeApiOnAwake && commandRouting != AgentCommandRouting.LocalOnly) EnsureApiReady();
        }

        private bool EnsureApiReady()
        {
            if (_brain != null) return true;
            var prompt = Resources.Load<TextAsset>(systemPromptResource);
            if (prompt == null)
            {
                StartupError = "시스템 프롬프트를 찾을 수 없습니다: " + systemPromptResource;
                Fail(StartupError);
                return false;
            }
            string apiKey;
            try { apiKey = OpenAIKeyProvider.Load(); }
            catch (Exception)
            {
                StartupError = "OpenAI API 키를 불러오지 못했습니다. 키 설정을 확인해 주세요.";
                Fail(StartupError);
                return false;
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                StartupError = "OpenAI API 키가 설정되지 않았습니다.";
                Fail(StartupError);
                return false;
            }
            try
            {
                _client = new OpenAIClient(apiKey);
                _brain = new AgentBrain(prompt.text, new LlmSettings
                { Model = llmModel, ReasoningEffort = reasoningEffort, MaxOutputTokens = maxOutputTokens }, _client.CreateResponseAsync);
                StartupError = "";
                return true;
            }
            catch (Exception)
            {
                StartupError = "OpenAI API 연결을 초기화하지 못했습니다. 모델과 연결 설정을 확인해 주세요.";
                Fail(StartupError);
                return false;
            }
        }

        private void CancelRequest()
        {
            var request = _request;
            _request = null;
            if (request != null) { request.Cancel(); request.Dispose(); }
            _recorder?.Cancel();
        }

        private void OnCommandFinished(AgentExecutionResult result)
        {
            if (!result.CommandId.Equals(_requestId)) return;
            CancelRequest();
            SetState(VoiceAgentState.Idle);
        }

        private AgentCommandId BeginInput()
        {
            var previousAction = EnsureRunner().CurrentActionForContext;
            CancelRequest();
            // 먼저 이전 실행을 취소한다. 새 ID는 STT와 LLM을 거쳐 동일하게 유지된다.
            var id = _runner.BeginCommand();
            if (!_runner.IsCurrent(id)) return id;
            _previousAction = previousAction;
            _requestId = id;
            _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            return id;
        }

        private bool IsCurrent(AgentCommandId id, CancellationToken cancellation) =>
            !cancellation.IsCancellationRequested && this != null && isActiveAndEnabled &&
            _runner != null && _runner.IsCurrent(id);

        public void RequestStop()
        {
            // API 키/프롬프트 초기화가 실패한 상태에서도 실행 가능하다.
            CancelRequest();
            if (_runner != null) _runner.Stop();
            SetState(VoiceAgentState.Idle);
            AgentEventDispatch.Invoke(LocalNotice, "멈췄습니다.");
        }

        public void StartVoiceInput()
        {
            if (!isActiveAndEnabled || State == VoiceAgentState.Recording) return;
            if (commandRouting == AgentCommandRouting.LocalOnly)
            {
                Say("로컬 테스트 모드에서는 채팅으로 명령해 주세요.");
                return;
            }
            if (!EnsureApiReady()) return;
            var id = BeginInput();
            if (!_runner.IsCurrent(id)) return;
            try { _recorder.Start(); SetState(VoiceAgentState.Recording); }
            catch (Exception error) { HandleFailure(id, "", error); }
        }

        public void StopVoiceInput()
        {
            if (State != VoiceAgentState.Recording || _request == null) return;
            var id = _requestId;
            var token = _request.Token;
            try { _ = ProcessVoiceAsync(id, _recorder.Stop(), token); }
            catch (Exception error) { HandleFailure(id, "", error); }
        }

        /// <summary>새 플레이어 명령이 이전 작업을 대체한다. 로컬 중지는 모든 입력 상태에서 우선한다.</summary>
        public bool SendChat(string text)
        {
            if (AgentCommandRules.IsStop(text)) { RequestStop(); return true; }
            if (AgentCommandRules.IsResumeExploration(text)) { ResumeExploration(); return true; }
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(text)) return false;
            var id = BeginInput();
            if (_runner.IsCurrent(id)) _ = RouteTextAsync(id, text, _request.Token);
            return true;
        }

        public void ReportEvent(string description) => _brain?.ReportEvent(description);

        public void SetExplorationEnabled(bool enabled) => EnsureRunner().SetExplorationEnabled(enabled);

        internal void Say(string message) => AgentEventDispatch.Invoke(LocalNotice, "요원: " + message);

        private Task RouteTextAsync(AgentCommandId id, string text, CancellationToken token)
        {
            if (!IsCurrent(id, token)) return Task.CompletedTask;
            if (text.Trim().Length > AgentBrain.MaxPlayerTextLength)
            {
                const string message = "한 번에 300자 이내로 지시해 주세요.";
                _runner.Submit(id, Array.Empty<AgentAction>(), message);
                Say(message);
                return Task.CompletedTask;
            }
            if (SceneCommandTargeting.IsNeighborCorrection(text))
            {
                string error = _stage.ResolveNeighborButton(_runner.SelectedTarget, out var neighbor);
                if (error == null) _runner.SelectTarget(neighbor);
                _runner.Submit(id, Array.Empty<AgentAction>(), error);
                Say(error ?? $"옆의 {neighbor.displayName}을(를) 선택했습니다. 어떻게 할까요?");
                return Task.CompletedTask;
            }
            if (commandRouting != AgentCommandRouting.ApiInterpretation && TrySubmitLocal(id, text))
                return Task.CompletedTask;
            if (commandRouting == AgentCommandRouting.LocalOnly)
            {
                const string message = "로컬 테스트 모드에서 해석하지 못했습니다. 이 표현은 API 해석 모드에서 시도해 주세요.";
                _runner.Submit(id, Array.Empty<AgentAction>(), message);
                Say(message);
                return Task.CompletedTask;
            }
            if (!EnsureApiReady())
            {
                _runner.Fail(id, StartupError);
                Say(StartupError);
                return Task.CompletedTask;
            }
            return RunTurnAsync(id, text, token);
        }

        private bool TrySubmitLocal(AgentCommandId id, string text)
        {
            if (AgentCommandRules.TryParseMotionCommand(text, out var motion))
            {
                _runner.Submit(id, motion, null);
                return true;
            }
            if (!SceneCommandTargeting.TryParseButtonPlan(text, out string selector, out bool useSelected, out var verbs))
                return false;

            var target = useSelected ? _runner.SelectedTarget : null;
            string error;
            if (useSelected)
                error = target != null && target.Button != null ? null : "어느 버튼을 누를까요? 이름이나 번호를 알려 주세요.";
            else
            {
                error = _stage.ResolveButton(selector, out target, out bool noMatch);
                // Complex descriptions can resemble a local button command. Let the API interpret them.
                if (noMatch && selector.Length > 0 && commandRouting == AgentCommandRouting.PreferLocal &&
                    EnsureApiReady()) return false;
            }
            if (error != null)
            {
                _runner.Submit(id, Array.Empty<AgentAction>(), error);
                Say(error);
                return true;
            }

            var targets = new Dictionary<string, AgentInteractable>(StringComparer.Ordinal) { [target.id] = target };
            _runner.Submit(id, verbs.Select(verb => new AgentAction { Verb = verb, Target = target.id }), null, targets);
            return true;
        }

        public void ResumeExploration()
        {
            EnsureRunner().ResumeExploration();
            AgentEventDispatch.Invoke(LocalNotice, "자동 탐색 재개를 요청했습니다. 활성 탐색 컴포넌트와 대상이 필요합니다.");
        }

        public void ResetConversation(bool keepTrust = true)
        {
            RequestStop();
            _runner?.ClearCommandContext();
            _previousAction = null;
            _brain?.ResetConversation(keepTrust);
        }

        private async Task ProcessVoiceAsync(AgentCommandId id, RecordedAudio audio, CancellationToken token)
        {
            try
            {
                if (!IsCurrent(id, token)) return;
                if (audio == null || audio.Wav == null || audio.Seconds < MinRecordingSeconds)
                { _runner.Fail(id, "녹음이 너무 짧습니다."); return; }
                if (audio.Rms < SilenceRms) { HandleFailure(id, "", new AgentApiException("녹음된 소리가 거의 없습니다. 마이크를 확인해 주세요.")); return; }
                SetState(VoiceAgentState.Transcribing);
                if (!IsCurrent(id, token)) return;
                var keywords = _stage.BuildForCommand().Objects.Select(o => o.DisplayName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).Distinct().Take(24).ToArray();
                var timer = System.Diagnostics.Stopwatch.StartNew();
                string text;
                try
                {
                    text = await _client.TranscribeAsync(audio.Wav, transcribeModel, language, transcribePrompt, keywords, token);
                }
                finally
                {
                    if (IsCurrent(id, token)) LastTranscriptionMilliseconds = timer.ElapsedMilliseconds;
                }
                if (!IsCurrent(id, token)) return;
                AgentEventDispatch.Invoke(PlayerTranscribed, text);
                if (!IsCurrent(id, token)) return;
                if (AgentCommandRules.IsStop(text)) { RequestStop(); return; }
                if (AgentCommandRules.IsResumeExploration(text))
                {
                    _runner.Cancel("음성 탐색 재개 요청을 처리했습니다.");
                    ResumeExploration();
                    return;
                }
                if (string.IsNullOrWhiteSpace(text)) { _runner.Fail(id, "음성 인식 결과가 없습니다."); return; }
                await RouteTextAsync(id, text, token);
            }
            catch (OperationCanceledException) when (!IsCurrent(id, token)) { }
            catch (Exception error) { if (IsCurrent(id, token)) HandleFailure(id, "", error); }
        }

        private async Task RunTurnAsync(AgentCommandId id, string text, CancellationToken token)
        {
            try
            {
                if (!IsCurrent(id, token)) return;
                SetState(VoiceAgentState.Thinking);
                if (!IsCurrent(id, token)) return;
                var context = _stage.BuildForCommand();
                _runner.PopulateCommandContext(context, _previousAction);
                var knownTargets = new Dictionary<string, AgentInteractable>(StringComparer.Ordinal);
                foreach (var item in AgentRequestBuilder.RequestObjects(context))
                    if (_stage.TryResolve(item.Id, out var target)) knownTargets.Add(item.Id, target);
                var prepared = await _brain.PrepareTurnAsync(text, context, token);
                if (!IsCurrent(id, token)) return;
                // 최신성 검사와 상태 확정 사이에 await/외부 이벤트를 두지 않는다.
                var result = _brain.CommitTurn(prepared);
                result.CommandId = id;
                var actions = result.Actions.Select(AgentCommandRules.Copy).ToArray();
                string groundingError = result.Rejected.Count == 0
                    ? SceneCommandTargeting.CheckGrounding(text, context, actions) : null;
                string rejection = result.Rejected.Count == 0 ? groundingError : result.Rejected[0].Reason;
                if (rejection == null && actions.Any(action => action.Target != null && !knownTargets.ContainsKey(action.Target)))
                    rejection = "지정한 대상을 씬에서 찾지 못했습니다.";
                if (rejection != null)
                {
                    result.Response.Speech = rejection;
                    if (groundingError != null) result.Response.Comprehension = AgentVocabulary.Asking;
                    result.Actions.Clear();
                }
                SetState(VoiceAgentState.Idle);
                if (!IsCurrent(id, token)) return;
                AgentEventDispatch.Invoke(TurnCompleted, result);
                if (IsCurrent(id, token)) _runner.Submit(id, actions, rejection, knownTargets);
            }
            catch (OperationCanceledException) when (!IsCurrent(id, token)) { }
            catch (Exception error) { if (IsCurrent(id, token)) HandleFailure(id, text, error); }
        }

        private void HandleFailure(AgentCommandId id, string text, Exception error)
        {
            if (_runner == null || !_runner.IsCurrent(id)) return;
            string message = error is OperationCanceledException ? "OpenAI 응답 시간이 초과됐습니다. 다시 시도해 주세요." :
                error is AgentApiException ? error.Message : "요청을 처리하지 못했습니다. 연결 상태를 확인해 주세요.";
            Fail(message);
            if (!_runner.IsCurrent(id)) return;
            SetState(VoiceAgentState.Idle);
            if (!_runner.IsCurrent(id)) return;
            AgentEventDispatch.Invoke(TurnCompleted, new AgentTurnResult
            {
                CommandId = id, PlayerText = text, Trust = Trust, IsFallback = true, Directive = new TurnDirective(),
                Response = new AgentResponse { Speech = message, Emotion = "confused", Comprehension = AgentVocabulary.Asking }
            });
            _runner.Fail(id, message);
        }

        private void SetState(VoiceAgentState state)
        { State = state; AgentEventDispatch.Invoke(StateChanged, state); }
        private void Fail(string message)
        { Debug.LogError("[VoiceAI] " + message); AgentEventDispatch.Invoke(ErrorOccurred, message); }
        private void OnDisable() => RequestStop();
        private void OnDestroy()
        {
            CancelRequest();
            if (_runner != null) _runner.CommandFinished -= OnCommandFinished;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
        }
    }
}
