using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    public enum VoiceAgentState { Idle, Recording, Transcribing, Thinking }

    [RequireComponent(typeof(SceneStageContext))]
    public sealed class VoiceAgent : MonoBehaviour
    {
        private const float MinRecordingSeconds = 0.3f;
        private const float SilenceRms = 0.002f;
        [Header("API 연결")]
        [Tooltip("기존 씬은 시작 시 연결합니다. 통합 시험 씬은 끄고 첫 음성/일반 채팅 요청 때 연결합니다.")]
        public bool initializeApiOnAwake = true;
        public bool ApiReady => _brain != null;

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
            if (initializeApiOnAwake) EnsureApiReady();
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
            try
            {
                _client = new OpenAIClient(OpenAIKeyProvider.Load());
                _brain = new AgentBrain(prompt.text, new LlmSettings
                { Model = llmModel, ReasoningEffort = reasoningEffort, MaxOutputTokens = maxOutputTokens }, _client.CreateResponseAsync);
                StartupError = "";
                return true;
            }
            catch (Exception error)
            {
                StartupError = "API 연결 초기화 실패: " + error.Message;
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
            if (result.Outcome != AgentExecutionOutcome.Completed) ReportEvent(result.Message);
        }

        private AgentCommandId BeginInput()
        {
            CancelRequest();
            // 먼저 이전 실행을 취소한다. 새 ID는 STT와 LLM을 거쳐 동일하게 유지된다.
            var id = EnsureRunner().BeginCommand();
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
            if (!isActiveAndEnabled || State == VoiceAgentState.Recording || !EnsureApiReady()) return;
            var id = BeginInput();
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
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(text) || !EnsureApiReady()) return false;
            var id = BeginInput();
            _ = RunTurnAsync(id, text, _request.Token);
            return true;
        }

        public void ReportEvent(string description) => _brain?.ReportEvent(description);

        public void ResumeExploration()
        {
            EnsureRunner().ResumeExploration();
            AgentEventDispatch.Invoke(LocalNotice, "자동 탐색 재개를 요청했습니다. 활성 탐색 컴포넌트와 대상이 필요합니다.");
        }

        public void ResetConversation(bool keepTrust = true)
        {
            RequestStop();
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
                var keywords = _stage.Build().Objects.Select(o => o.DisplayName).ToArray();
                string text = await _client.TranscribeAsync(audio.Wav, transcribeModel, language, transcribePrompt, keywords, token);
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
                await RunTurnAsync(id, text, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (IsCurrent(id, token)) HandleFailure(id, "", error); }
        }

        private async Task RunTurnAsync(AgentCommandId id, string text, CancellationToken token)
        {
            try
            {
                if (!IsCurrent(id, token)) return;
                SetState(VoiceAgentState.Thinking);
                if (!IsCurrent(id, token)) return;
                var prepared = await _brain.PrepareTurnAsync(text, _stage.Build(), token);
                if (!IsCurrent(id, token)) return;
                // 최신성 검사와 상태 확정 사이에 await/외부 이벤트를 두지 않는다.
                var result = _brain.CommitTurn(prepared);
                result.CommandId = id;
                var actions = result.Actions.Select(AgentCommandRules.Copy).ToArray();
                string rejection = result.Rejected.Count == 0 ? null : string.Join("; ", result.Rejected.Select(r => r.Reason));
                SetState(VoiceAgentState.Idle);
                if (!IsCurrent(id, token)) return;
                AgentEventDispatch.Invoke(TurnCompleted, result);
                if (IsCurrent(id, token)) _runner.Submit(id, actions, rejection);
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (IsCurrent(id, token)) HandleFailure(id, text, error); }
        }

        private void HandleFailure(AgentCommandId id, string text, Exception error)
        {
            if (_runner == null || !_runner.IsCurrent(id)) return;
            Fail(error.Message);
            if (!_runner.IsCurrent(id)) return;
            SetState(VoiceAgentState.Idle);
            if (!_runner.IsCurrent(id)) return;
            AgentEventDispatch.Invoke(TurnCompleted, new AgentTurnResult
            {
                CommandId = id, PlayerText = text, Trust = Trust, IsFallback = true, Directive = new TurnDirective(),
                Response = new AgentResponse { Speech = "치직... 목소리가 끊겼어요. 다시 말해 주실래요?", Emotion = "confused", Comprehension = AgentVocabulary.Asking }
            });
            _runner.Fail(id, error.Message);
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
