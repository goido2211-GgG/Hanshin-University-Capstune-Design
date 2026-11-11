using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>기존 시험 UI와 통합 씬의 명시적 채팅 입력을 함께 지원한다.</summary>
    [RequireComponent(typeof(VoiceAgent))]
    public sealed class VoiceAgentDebugUI : MonoBehaviour
    {
        private const string ChatFieldName = "VoiceAIChat";
        private static readonly string[] SampleCommands =
        {
            "빨간 상자를 집어", "빨간 상자를 내려놓아",
            "앞으로 다섯 걸음 이동해", "빨간 상자를 살펴봐"
        };

        public KeyCode pushToTalkKey = KeyCode.V;
        [Tooltip("통합 입력 조정자가 V/Enter/Esc를 처리합니다. 기존 씬은 false를 유지합니다.")]
        public bool externalInput;
        public bool compactWhenChatClosed;
        public bool IsChatOpen { get; private set; }
        public event Action<bool> ChatOpenChanged;

        private VoiceAgent _agent;
        private string _chat = "";
        private string _log = "V키를 누르고 있는 동안 말하거나, 아래에 입력하세요.";
        private string _lastError = "";
        private string _lastLocalNotice = "";
        private float _requestStartedAt;
        private Vector2 _scroll;
        private bool _focusChatPending;
        private bool _clearChatFocusPending;

        private void Awake()
        {
            _agent = GetComponent<VoiceAgent>();
            SetChatOpen(!externalInput);
        }

        private void OnEnable()
        {
            if (_agent == null) _agent = GetComponent<VoiceAgent>();
            if (_agent == null) return;
            SetImeMode(IMECompositionMode.On);
            _agent.PlayerTranscribed += ShowTranscription;
            _agent.TurnCompleted += ShowTurn;
            _agent.ErrorOccurred += ShowError;
            _agent.StateChanged += ShowState;
            _agent.LocalNotice += ShowLocalNotice;
            _agent.Runner.IntentAnnounced += ShowIntent;
            _agent.Runner.ActionFinished += ShowAction;
            _agent.Runner.CommandFinished += ShowCommand;
        }

        private void OnDisable()
        {
            CloseChat();
            if (_agent != null)
            {
                _agent.PlayerTranscribed -= ShowTranscription;
                _agent.TurnCompleted -= ShowTurn;
                _agent.ErrorOccurred -= ShowError;
                _agent.StateChanged -= ShowState;
                _agent.LocalNotice -= ShowLocalNotice;
                _agent.Runner.IntentAnnounced -= ShowIntent;
                _agent.Runner.ActionFinished -= ShowAction;
                _agent.Runner.CommandFinished -= ShowCommand;
            }
            SetImeMode(IMECompositionMode.Auto);
        }

        public void BeginChat()
        {
            SetChatOpen(true);
            _focusChatPending = true;
            _clearChatFocusPending = false;
        }

        public void CloseChat()
        {
            SetChatOpen(false);
            _focusChatPending = false;
            _clearChatFocusPending = true;
        }

        public bool SubmitChatDraft()
        {
            string text = (_chat ?? "").Trim();
            if (text.Length == 0) { CloseChat(); return true; }
            if (!Send(text, true)) return false;
            CloseChat();
            return true;
        }

        private void SetChatOpen(bool open)
        {
            if (IsChatOpen == open) return;
            IsChatOpen = open;
            AgentEventDispatch.Invoke(ChatOpenChanged, open);
        }

        public void ShowLocalNotice(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            _lastLocalNotice = message;
            Append(message);
        }

        private void ShowTranscription(string text) => Append("플레이어(음성): " + text);
        private void ShowError(string message) => _lastError = message;
        private void ShowState(VoiceAgentState state)
        {
            if (state == VoiceAgentState.Recording || state == VoiceAgentState.Thinking)
                _requestStartedAt = Time.realtimeSinceStartup;
        }
        private void ShowIntent(string intent) => Append("의도: " + intent);
        private void ShowAction(AgentExecutionResult result) => Append("행동 #" + result.CommandId + "/" + result.ActionIndex + " " + result.Outcome + ": " + result.Message);
        private void ShowCommand(AgentExecutionResult result) => Append("작업 #" + result.CommandId + " [" + result.Source + "] " + result.Outcome + ": " + result.Message);

        private static void SetImeMode(IMECompositionMode mode)
        {
            try { Input.imeCompositionMode = mode; }
            catch (InvalidOperationException error)
            { Debug.LogWarning("[VoiceAI] 한글 입력 모드를 켤 수 없습니다: " + error.Message); }
        }

        private void Start()
        {
            if (_agent != null && _agent.StartupError.Length > 0) _lastError = _agent.StartupError;
        }

        private void ShowTurn(AgentTurnResult turn)
        {
            float seconds = _requestStartedAt > 0f ? Time.realtimeSinceStartup - _requestStartedAt : 0f;
            _requestStartedAt = 0f;
            var text = new StringBuilder();
            text.AppendLine("요원: " + turn.Response.Speech);
            text.AppendLine("  이해: " + turn.Response.Comprehension + " / 감정: " + turn.Response.Emotion
                + " / 신뢰도: " + turn.Trust + " (" + turn.Response.TrustDelta.ToString("+0;-0;0") + ")"
                + " / " + seconds.ToString("0.0") + "초");
            text.AppendLine("  해석: " + turn.Response.Interpretation);
            text.AppendLine("  지시: " + (turn.Directive.ForceMisunderstanding ? "오해-" + turn.Directive.MisunderstandingType : "정상"));
            text.AppendLine("  행동: " + (turn.Actions.Count == 0 ? "없음" : string.Join(", ", turn.Actions.Select(a => a.ToString()))));
            foreach (var rejected in turn.Rejected)
                text.AppendLine("  거부됨: " + rejected.Action + " (" + rejected.Reason + ")");
            Append(text.ToString());
        }

        private void Append(string line)
        {
            _log += "\n" + line;
            if (_log.Length > 16000) _log = _log.Substring(_log.Length - 16000);
            _scroll.y = float.MaxValue;
        }

        private void OnGUI()
        {
            if (_agent == null) return;
            if (_clearChatFocusPending)
            {
                GUI.FocusControl(null);
                _clearChatFocusPending = false;
            }
            if (!externalInput) HandlePushToTalk();
            if (externalInput && compactWhenChatClosed && !IsChatOpen)
            {
                DrawCompactHUD();
                return;
            }

            GUILayout.BeginArea(new Rect(10, 10, Mathf.Min(720, Screen.width - 20), Screen.height - 20), GUI.skin.box);
            GUILayout.Label("상태: " + _agent.State + "   신뢰도: " + _agent.Trust);
            GUILayout.Label("실행: " + _agent.Runner.State + "   작업: " + _agent.Runner.CurrentCommand);
            GUILayout.Label("출처: " + _agent.Runner.CurrentSource + "   자동 탐색 중지: " + _agent.Runner.AutonomySuppressed);
            GUILayout.Label("예정: " + _agent.Runner.PendingIntent);
            GUILayout.Label("최근 결과: " + _agent.Runner.LastResult);
            bool stop = GUILayout.Button("즉시 중지 (Esc)");
            bool resume = GUILayout.Button("탐색 재개 (로컬)");
            bool close = externalInput && IsChatOpen && GUILayout.Button("채팅 닫기");
            if (_lastError.Length > 0) GUILayout.Label("오류: " + _lastError);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUILayout.Label(_log);
            GUILayout.EndScrollView();

            string sample = null;
            GUILayout.BeginHorizontal();
            foreach (string command in SampleCommands)
                if (GUILayout.Button(command)) sample = command;
            GUILayout.EndHorizontal();
            bool toggleRecording = GUILayout.Button(
                _agent.State == VoiceAgentState.Recording ? "녹음 끝내고 보내기" : "클릭해서 녹음 시작");

            bool submit = false;
            bool open = false;
            if (!externalInput || IsChatOpen)
            {
                GUILayout.BeginHorizontal();
                submit = !externalInput && Event.current.type == EventType.KeyDown &&
                    (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                    GUI.GetNameOfFocusedControl() == ChatFieldName;
                GUI.SetNextControlName(ChatFieldName);
                _chat = GUILayout.TextField(_chat, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("보내기", GUILayout.Width(80))) submit = true;
                GUILayout.EndHorizontal();
                if (_focusChatPending && Event.current.type == EventType.Repaint)
                {
                    GUI.FocusControl(ChatFieldName);
                    _focusChatPending = false;
                }
            }
            else open = GUILayout.Button("채팅 입력 열기 (Enter)");
            GUILayout.EndArea();

            if (stop) { _agent.RequestStop(); return; }
            if (resume) { _agent.ResumeExploration(); return; }
            if (close) { CloseChat(); return; }
            if (open) { BeginChat(); return; }
            if (toggleRecording) ToggleRecording();
            if (sample != null)
            {
                if (Send(sample, false) && externalInput) CloseChat();
            }
            else if (submit)
            {
                if (externalInput) SubmitChatDraft();
                else if (!string.IsNullOrWhiteSpace(_chat)) Send(_chat.Trim(), true);
            }
        }

        private void DrawCompactHUD()
        {
            GUILayout.BeginArea(new Rect(10, 10, Mathf.Min(620, Screen.width - 20), 150), GUI.skin.box);
            GUILayout.Label("Kyle: " + _agent.State + " / " + _agent.Runner.State);
            GUILayout.Label("Enter 채팅 | V 음성 | Tab 사람/드론 | F 조사 | Esc AI 중지");
            if (_agent.Runner.PendingIntent.Length > 0) GUILayout.Label("예정: " + _agent.Runner.PendingIntent);
            if (_lastLocalNotice.Length > 0) GUILayout.Label(_lastLocalNotice);
            if (_lastError.Length > 0) GUILayout.Label("오류: " + _lastError);
            GUILayout.EndArea();
        }

        private void ToggleRecording()
        {
            if (_agent.State == VoiceAgentState.Recording) _agent.StopVoiceInput();
            else { _lastError = ""; _agent.StartVoiceInput(); }
        }

        private bool Send(string text, bool clearField)
        {
            if (_agent == null) return false;
            _requestStartedAt = Time.realtimeSinceStartup;
            if (_agent.SendChat(text))
            {
                Append("플레이어(채팅): " + text);
                if (clearField) _chat = "";
                return true;
            }
            _requestStartedAt = 0f;
            return false;
        }

        private void HandlePushToTalk()
        {
            var current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            { _agent.RequestStop(); current.Use(); return; }
            if (current.keyCode != pushToTalkKey) return;
            if (current.type == EventType.KeyDown && _agent.State != VoiceAgentState.Recording &&
                GUI.GetNameOfFocusedControl() != ChatFieldName)
            {
                _lastError = "";
                _agent.StartVoiceInput();
                current.Use();
            }
            else if (current.type == EventType.KeyUp && _agent.State == VoiceAgentState.Recording)
            { _agent.StopVoiceInput(); current.Use(); }
        }
    }
}
