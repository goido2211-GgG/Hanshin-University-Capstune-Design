using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    public enum VoiceAgentState { Idle, Recording, Transcribing, Thinking }

    /// <summary>
    /// 음성/채팅 입력부터 요원 응답까지 담당하는 진입점.
    /// 게임 코드는 StartVoiceInput, StopVoiceInput, SendChat을 호출하고 TurnCompleted를 구독한다.
    /// </summary>
    [RequireComponent(typeof(SceneStageContext))]
    public sealed class VoiceAgent : MonoBehaviour
    {
        private const float MinRecordingSeconds = 0.3f;
        private const float SilenceRms = 0.002f;

        [Header("모델")]
        public string llmModel = "gpt-6-luna";
        [Tooltip("none, low, medium, high. 높을수록 느리다.")]
        public string reasoningEffort = "low";
        public int maxOutputTokens = 800;
        public string transcribeModel = "gpt-transcribe";
        public string language = "ko";

        [Header("녹음")]
        public int maxRecordingSeconds = 15;

        [Header("프롬프트")]
        public string systemPromptResource = "VoiceAI/Prompts/agent_system";
        [TextArea(2, 4)]
        public string transcribePrompt = "방탈출 게임에서 플레이어가 로봇 요원에게 내리는 짧은 한국어 음성 명령.";

        public event Action<VoiceAgentState> StateChanged;
        /// <summary>음성 인식이 끝났을 때. 자막 표시용.</summary>
        public event Action<string> PlayerTranscribed;
        /// <summary>요원의 응답과 실행할 행동이 나왔을 때.</summary>
        public event Action<AgentTurnResult> TurnCompleted;
        /// <summary>플레이어에게 보여주지 않을 개발자용 오류 메시지.</summary>
        public event Action<string> ErrorOccurred;

        public VoiceAgentState State { get; private set; } = VoiceAgentState.Idle;
        public int Trust => _brain != null ? _brain.Trust : AgentBrain.InitialTrust;
        /// <summary>초기화 실패 이유. 다른 컴포넌트의 Awake보다 먼저 발생할 수 있어서 따로 보관한다.</summary>
        public string StartupError { get; private set; } = "";

        private SceneStageContext _stage;
        private OpenAIClient _client;
        private AgentBrain _brain;
        private MicrophoneRecorder _recorder;
        private CancellationTokenSource _lifetime;

        private void Awake()
        {
            _stage = GetComponent<SceneStageContext>();
            _lifetime = new CancellationTokenSource();
            _recorder = new MicrophoneRecorder(maxRecordingSeconds);

            var prompt = Resources.Load<TextAsset>(systemPromptResource);
            if (prompt == null)
            {
                StartupError = "시스템 프롬프트를 찾을 수 없습니다: Resources/" + systemPromptResource;
                Fail(StartupError);
                return;
            }

            try
            {
                _client = new OpenAIClient(OpenAIKeyProvider.Load());
            }
            catch (AgentApiException e)
            {
                StartupError = e.Message;
                Fail(StartupError);
                return;
            }

            var settings = new LlmSettings
            {
                Model = llmModel,
                ReasoningEffort = reasoningEffort,
                MaxOutputTokens = maxOutputTokens
            };
            _brain = new AgentBrain(prompt.text, settings, _client.CreateResponseAsync);
        }

        private void OnDestroy()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            _recorder?.Cancel();
        }

        public void StartVoiceInput()
        {
            if (_brain == null || State != VoiceAgentState.Idle) return;
            try
            {
                _recorder.Start();
                SetState(VoiceAgentState.Recording);
            }
            catch (AgentApiException e)
            {
                Fail(e.Message);
            }
        }

        public void StopVoiceInput()
        {
            if (State != VoiceAgentState.Recording) return;
            _ = ProcessVoiceAsync(_recorder.Stop());
        }

        /// <summary>채팅 입력. 처리 중이면 false를 돌려준다.</summary>
        public bool SendChat(string text)
        {
            if (_brain == null || State != VoiceAgentState.Idle || string.IsNullOrWhiteSpace(text)) return false;
            _ = RunTurnAsync(text);
            return true;
        }

        /// <summary>행동 결과나 기믹 변화처럼 요원이 다음 턴에 알아야 할 일을 알린다.</summary>
        public void ReportEvent(string description)
        {
            _brain?.ReportEvent(description);
        }

        /// <summary>스테이지가 바뀔 때 호출한다.</summary>
        public void ResetConversation(bool keepTrust = true)
        {
            _brain?.ResetConversation(keepTrust);
        }

        private async Task ProcessVoiceAsync(RecordedAudio audio)
        {
            try
            {
                if (audio == null || audio.Wav == null || audio.Seconds < MinRecordingSeconds)
                {
                    SetState(VoiceAgentState.Idle);
                    return;
                }
                if (audio.Rms < SilenceRms)
                {
                    SetState(VoiceAgentState.Idle);
                    Fail("녹음된 소리가 거의 없습니다. 마이크 권한과 입력 장치를 확인해 주세요. RMS=" + audio.Rms);
                    return;
                }

                SetState(VoiceAgentState.Transcribing);
                var keywords = _stage.Build().Objects.Select(o => o.DisplayName).ToArray();
                string text = await _client.TranscribeAsync(audio.Wav, transcribeModel, language,
                    transcribePrompt, keywords, _lifetime.Token);

                if (string.IsNullOrWhiteSpace(text))
                {
                    SetState(VoiceAgentState.Idle);
                    return;
                }

                PlayerTranscribed?.Invoke(text);
                await RunTurnAsync(text);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                HandleTurnFailure("", e);
            }
        }

        private async Task RunTurnAsync(string text)
        {
            SetState(VoiceAgentState.Thinking);
            try
            {
                var result = await _brain.RunTurnAsync(text, _stage.Build(), _lifetime.Token);
                SetState(VoiceAgentState.Idle);
                TurnCompleted?.Invoke(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                HandleTurnFailure(text, e);
            }
        }

        // 시연 중 API 오류가 나도 게임이 멈춘 것처럼 보이지 않게 캐릭터 대사로 대체한다.
        private void HandleTurnFailure(string playerText, Exception error)
        {
            Fail(error.Message);
            SetState(VoiceAgentState.Idle);
            TurnCompleted?.Invoke(new AgentTurnResult
            {
                PlayerText = playerText,
                Trust = Trust,
                IsFallback = true,
                Directive = new TurnDirective(),
                Response = new AgentResponse
                {
                    Speech = "치직... 목소리가 끊겼어요. 다시 말해 주실래요?",
                    Emotion = "confused",
                    Comprehension = AgentVocabulary.Asking
                }
            });
        }

        private void SetState(VoiceAgentState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }

        private void Fail(string message)
        {
            Debug.LogError("[VoiceAI] " + message);
            ErrorOccurred?.Invoke(message);
        }
    }
}
