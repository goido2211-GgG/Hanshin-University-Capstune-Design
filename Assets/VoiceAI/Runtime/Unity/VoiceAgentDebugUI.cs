using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// 개발용 시험 화면. 빈 씬의 게임오브젝트에 이 컴포넌트만 붙이면 바로 시험할 수 있다.
    /// IMGUI라서 입력 시스템 설정과 상관없이 동작한다.
    /// </summary>
    [RequireComponent(typeof(VoiceAgent))]
    public sealed class VoiceAgentDebugUI : MonoBehaviour
    {
        private const string ChatFieldName = "VoiceAIChat";
        private static readonly string[] SampleCommands =
        {
            "빨간 상자를 집어",
            "빨간 상자를 내려놓아",
            "앞으로 다섯 걸음 이동해",
            "빨간 상자를 살펴봐"
        };

        public KeyCode pushToTalkKey = KeyCode.V;

        private VoiceAgent _agent;
        private string _chat = "";
        private string _log = "V키를 누르고 있는 동안 말하거나, 아래에 입력하세요.";
        private string _lastError = "";
        private float _requestStartedAt;
        private Vector2 _scroll;

        private void Awake()
        {
            _agent = GetComponent<VoiceAgent>();
        }

        private void OnEnable()
        {
            SetImeMode(IMECompositionMode.On);
            _agent.PlayerTranscribed += ShowTranscription;
            _agent.TurnCompleted += ShowTurn;
            _agent.ErrorOccurred += ShowError;
            _agent.StateChanged += ShowState;
            _agent.LocalNotice += Append;
            _agent.Runner.IntentAnnounced += ShowIntent;
            _agent.Runner.ActionFinished += ShowAction;
            _agent.Runner.CommandFinished += ShowCommand;
        }

        private void OnDisable()
        {
            _agent.PlayerTranscribed -= ShowTranscription;
            _agent.TurnCompleted -= ShowTurn;
            _agent.ErrorOccurred -= ShowError;
            _agent.StateChanged -= ShowState;
            _agent.LocalNotice -= Append;
            _agent.Runner.IntentAnnounced -= ShowIntent;
            _agent.Runner.ActionFinished -= ShowAction;
            _agent.Runner.CommandFinished -= ShowCommand;
            SetImeMode(IMECompositionMode.Auto);
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

        // 한글 조합 입력을 켠다. 입력 시스템 설정에 따라 지원되지 않아도 시험 화면은 계속 동작해야 한다.
        private static void SetImeMode(IMECompositionMode mode)
        {
            try
            {
                Input.imeCompositionMode = mode;
            }
            catch (InvalidOperationException e)
            {
                Debug.LogWarning("[VoiceAI] 한글 입력 모드를 켤 수 없습니다: " + e.Message);
            }
        }

        private void Start()
        {
            if (_agent.StartupError.Length > 0) _lastError = _agent.StartupError;
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
            {
                text.AppendLine("  거부됨: " + rejected.Action + " (" + rejected.Reason + ")");
            }
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
            HandlePushToTalk();

            GUILayout.BeginArea(new Rect(10, 10, Mathf.Min(720, Screen.width - 20), Screen.height - 20), GUI.skin.box);
            GUILayout.Label("상태: " + _agent.State + "   신뢰도: " + _agent.Trust);
            GUILayout.Label("실행: " + _agent.Runner.State + "   작업: " + _agent.Runner.CurrentCommand);
            GUILayout.Label("출처: " + _agent.Runner.CurrentSource + "   자동 탐색 중지: " + _agent.Runner.AutonomySuppressed);
            GUILayout.Label("예정: " + _agent.Runner.PendingIntent);
            GUILayout.Label("최근 결과: " + _agent.Runner.LastResult);
            bool stop = GUILayout.Button("즉시 중지 (Esc)");
            bool resume = GUILayout.Button("탐색 재개 (로컬)");
            if (_lastError.Length > 0) GUILayout.Label("오류: " + _lastError);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            GUILayout.Label(_log);
            GUILayout.EndScrollView();

            string sample = null;
            GUILayout.BeginHorizontal();
            foreach (string command in SampleCommands)
            {
                if (GUILayout.Button(command)) sample = command;
            }
            GUILayout.EndHorizontal();

            bool toggleRecording = GUILayout.Button(
                _agent.State == VoiceAgentState.Recording ? "녹음 끝내고 보내기" : "클릭해서 녹음 시작");

            GUILayout.BeginHorizontal();
            bool submit = Event.current.type == EventType.KeyDown &&
                          (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                          GUI.GetNameOfFocusedControl() == ChatFieldName;
            GUI.SetNextControlName(ChatFieldName);
            _chat = GUILayout.TextField(_chat, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("보내기", GUILayout.Width(80))) submit = true;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            if (stop) { _agent.RequestStop(); return; }
            if (resume) { _agent.ResumeExploration(); return; }
            if (toggleRecording) ToggleRecording();
            if (sample != null) Send(sample, false);
            else if (submit && _chat.Trim().Length > 0) Send(_chat.Trim(), true);
        }

        private void ToggleRecording()
        {
            if (_agent.State == VoiceAgentState.Recording)
            {
                _agent.StopVoiceInput();
            }
            else
            {
                _lastError = "";
                _agent.StartVoiceInput();
            }
        }

        private void Send(string text, bool clearField)
        {
            _requestStartedAt = Time.realtimeSinceStartup;
            if (_agent.SendChat(text))
            {
                Append("플레이어(채팅): " + text);
                if (clearField) _chat = "";
            }
            else
            {
                _requestStartedAt = 0f;
            }
        }

        private void HandlePushToTalk()
        {
            var current = Event.current;
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            { _agent.RequestStop(); current.Use(); return; }
            if (current.keyCode != pushToTalkKey) return;

            if (current.type == EventType.KeyDown && _agent.State != VoiceAgentState.Recording && GUI.GetNameOfFocusedControl() != ChatFieldName)
            {
                _lastError = "";
                _agent.StartVoiceInput();
                current.Use();
            }
            else if (current.type == EventType.KeyUp && _agent.State == VoiceAgentState.Recording)
            {
                _agent.StopVoiceInput();
                current.Use();
            }
        }
    }
}
