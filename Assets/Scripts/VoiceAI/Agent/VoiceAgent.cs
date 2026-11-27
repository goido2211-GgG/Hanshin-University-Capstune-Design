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
        public bool IsAwaitingTarget => _pendingCommand != null;
        public AgentInteractable CurrentHeldObject => _runner == null ? null : _runner.CurrentHeldObject;
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
        private PendingCommand _pendingCommand;

        private sealed class PendingCommand
        {
            internal string[] Verbs;
            internal bool ButtonsOnly;
            internal bool DoorsOnly;
            internal bool BoxesOnly;
            internal string[] CandidateIds;
        }

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
            try
            {
                if (!OpenAIKeyProvider.TryLoad(out apiKey))
                {
                    StartupError = "API 키가 필요합니다. 이동과 버튼 조작은 채팅으로 명령할 수 있습니다.";
                    return false;
                }
            }
            catch (Exception)
            {
                StartupError = "OpenAI API 키를 불러오지 못했습니다. 키 설정을 확인해 주세요.";
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
            _pendingCommand = null;
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
            if (!EnsureApiReady())
            {
                Say(StartupError);
                return;
            }
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
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(text)) return false;
            if (AgentCommandRules.IsStop(text)) { RequestStop(); return true; }
            if (AgentCommandRules.IsResumeExploration(text)) { ResumeExploration(); return true; }
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
            if (TrySubmitBoxPuzzleCommand(id, text) || TrySubmitCarryCommand(id, text)) return Task.CompletedTask;
            var pending = _pendingCommand;
            if (pending != null)
            {
                if (SceneCommandTargeting.IsSelectionReply(text, _stage.BuildForCommand()))
                {
                    ResolveCommandTarget(id, pending, text);
                    return Task.CompletedTask;
                }
                _pendingCommand = null;
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
            if (SceneCommandTargeting.TryParseTargetCommand(text, out var verb, out var pickupSelector))
            {
                if (pickupSelector.Length > 0 && commandRouting == AgentCommandRouting.PreferLocal &&
                    !SceneCommandTargeting.IsSelectionReply(pickupSelector, _stage.BuildForCommand()) && EnsureApiReady())
                    return false;
                ResolveCommandTarget(id, CreatePending(new[] { verb }, pickupSelector), pickupSelector);
                return true;
            }
            if (SceneCommandTargeting.TryParseDoorOpen(text, out string doorSelector))
            {
                var context = _stage.BuildForCommand();
                _runner.PopulateCommandContext(context, _previousAction);
                if (doorSelector.Length > 0 && commandRouting == AgentCommandRouting.PreferLocal &&
                    SceneCommandTargeting.ResolveButtonSelection(doorSelector, context, doorsOnly: true).Count == 0 &&
                    EnsureApiReady()) return false;
                ResolveCommandTarget(id, CreatePending(new[] { AgentVocabulary.Press }, doorSelector, doorsOnly: true),
                    doorSelector, useButtonContext: true);
                return true;
            }
            if (!SceneCommandTargeting.TryParseButtonPlan(text, out string selector, out bool useSelected, out var verbs))
                return false;

            if (!useSelected && selector.Length == 0 && _stage.BuildForCommand().Objects.Any(item => item.ButtonRoomId > 0))
            {
                ResolveCommandTarget(id, CreatePending(verbs, selector, buttonsOnly: true), selector, useButtonContext: true);
                return true;
            }
            var target = useSelected ? _runner.SelectedTarget : null;
            string error;
            if (useSelected)
                error = target != null && target.Button != null ? null : "어느 버튼을 누를까요? 이름이나 번호를 알려 주세요.";
            else
            {
                error = _stage.ResolveButton(selector, out target, out bool noMatch);
                if (noMatch && selector.Length > 0)
                {
                    // Complex descriptions can resemble a local button command. Let the API interpret them.
                    if (commandRouting == AgentCommandRouting.PreferLocal && EnsureApiReady()) return false;
                    var context = _stage.BuildForCommand();
                    var buttonIds = context.Objects.Where(SceneCommandTargeting.IsButton).Select(item => item.Id).ToArray();
                    _runner.PopulateCommandContext(context, _previousAction);
                    var matches = SceneCommandTargeting.ResolveButtonSelection(selector, context, buttonIds);
                    if (matches.Count > 0)
                    {
                        ResolveCommandTarget(id, CreatePending(verbs, selector, buttonsOnly: true), selector, useButtonContext: true);
                        return true;
                    }
                }
            }
            if (error != null)
            {
                // The tag resolver also sees unconfigured tagged objects. Preserve that configuration error
                // if the scene index has no competing registered candidate.
                var command = CreatePending(verbs, selector, buttonsOnly: true);
                ResolveCommandTarget(id, command, useSelected ? "그거" : selector, error);
                return true;
            }

            SubmitTarget(id, verbs, target, _stage.BuildForCommand());
            return true;
        }

        private bool TrySubmitBoxPuzzleCommand(AgentCommandId id, string text)
        {
            var placement = _stage.boxPlacement;
            if (placement == null) return false;
            if (SceneCommandTargeting.TryParseDoorOpen(text, out string doorSelector) &&
                (SceneCommandTargeting.IsSecondDoorSelector(doorSelector) ||
                    (doorSelector.Length == 0 && placement.RoomId > 0 &&
                        _stage.BuildForCommand().ButtonRoomId == placement.RoomId)))
            {
                _pendingCommand = null;
                RejectTarget(id, "초록색 버튼으로 상자를 떨어뜨린 뒤 상자를 문 여는 발판 위에 놓아 주세요.");
                return true;
            }
            if (!SceneCommandTargeting.TryParseBoxPuzzleCommand(text, out var mode, out string boxSelector, out string padSelector))
            {
                if (!SceneCommandTargeting.TryParseTargetCommand(text, out string verb, out string selector) ||
                    !selector.Contains("상자")) return false;
                ResolveCommandTarget(id, CreatePending(new[] { verb }, selector), selector);
                return true;
            }

            var held = CurrentHeldObject;
            if (mode == SceneCommandTargeting.BoxPuzzleAction.Release && held != null) return false;

            _pendingCommand = null;
            var context = _stage.BuildForCommand();
            _runner.PopulateCommandContext(context, _previousAction);
            string error = placement.ConfigurationError();
            if (error != null) { RejectTarget(id, error); return true; }
            var box = placement.box;
            if (mode == SceneCommandTargeting.BoxPuzzleAction.Release)
            {
                if (!context.Objects.Any(item => item.Id == box.id))
                    context.Objects.Add(box.ToStageObject());
                var releaseScope = BoxIdsInRoom(context, placement.RoomId).Append(box.id).Distinct().ToArray();
                var matches = SceneCommandTargeting.ResolveSelection(boxSelector, context, releaseScope);
                var release = placement.releaseButton.GetComponent<AgentInteractable>();
                if (matches.Count != 1 || matches[0].Id != box.id || release == null ||
                    !_stage.TryResolve(release.id, out var registered) || registered != release)
                    RejectTarget(id, "떨어뜨릴 상자를 하나로 확인할 수 없습니다. 초록색 버튼의 이름을 알려 주세요.");
                else SubmitTarget(id, new[] { AgentVocabulary.Press }, release, context);
                return true;
            }

            if (context.ButtonRoomId != placement.RoomId)
            {
                RejectTarget(id, "상자와 발판이 있는 구역에 먼저 들어가 주세요.");
                return true;
            }
            var pads = SceneCommandTargeting.ResolveSelection(padSelector, context);
            if (pads.Count != 1 || !_stage.TryResolvePlacement(pads[0].Id, out var resolved) || resolved != placement)
            {
                RejectTarget(id, "상자를 놓을 발판을 하나로 확인할 수 없습니다. 발판의 정확한 이름을 알려 주세요.");
                return true;
            }

            bool pickUp = held == null && mode == SceneCommandTargeting.BoxPuzzleAction.PickUpAndPlace;
            if (!pickUp && held != box)
            {
                RejectTarget(id, held == null ? "발판으로 옮길 상자를 먼저 집어 주세요." : "들고 있는 물체를 내려놓고 지정된 상자를 집어 주세요.");
                return true;
            }

            if (!_stage.TryResolve(box.id, out var available) || available != box)
            {
                RejectTarget(id, "상자를 떨어뜨린 뒤 바닥에 멈추면 집을 수 있습니다.");
                return true;
            }
            var scope = held == null ? BoxIdsInRoom(context, context.ButtonRoomId) : new[] { held.id };
            if (held != null) context.SelectedTargetId = held.id;
            var boxes = SceneCommandTargeting.ResolveSelection(boxSelector, context, scope);
            if (boxes.Count != 1 || boxes[0].Id != box.id)
            {
                RejectTarget(id, "옮길 상자를 하나로 확인할 수 없습니다. 상자의 이름을 알려 주세요.");
                return true;
            }

            string padId = pads[0].Id;
            var actions = new List<AgentAction>();
            if (pickUp) actions.Add(new AgentAction { Verb = AgentVocabulary.PickUp, Target = box.id });
            actions.Add(new AgentAction { Verb = AgentVocabulary.MoveTo, Target = padId, PlacementTargetId = padId });
            if (mode != SceneCommandTargeting.BoxPuzzleAction.Travel)
                actions.Add(new AgentAction { Verb = AgentVocabulary.Drop, Target = box.id, PlacementTargetId = padId });
            var targets = new Dictionary<string, AgentInteractable>(StringComparer.Ordinal)
                { [box.id] = box, [padId] = placement.Interactable };
            foreach (var action in actions)
            {
                error = AgentResponseParser.CheckAction(action, context, Trust, checkInventory: false);
                if (error != null) { RejectTarget(id, error); return true; }
            }
            _runner.Submit(id, actions, null, targets, requiredHeldObject: held);
            return true;
        }

        private string[] BoxIdsInRoom(StageContext context, int roomId) => context.Objects.Where(item =>
            item.AllowedVerbs.Contains(AgentVocabulary.PickUp) && SceneCommandTargeting.IsBox(item) &&
            _stage.TryResolve(item.Id, out var target) && _stage.ButtonRoomAt(target.transform.position) == roomId)
            .Select(item => item.Id).ToArray();

        private bool TrySubmitCarryCommand(AgentCommandId id, string text)
        {
            if (SceneCommandTargeting.TryParseDropCommand(text, out string selector))
            {
                _pendingCommand = null;
                var held = CurrentHeldObject;
                if (held == null)
                {
                    RejectTarget(id, "현재 들고 있는 물체가 없습니다.");
                    return true;
                }

                var context = _stage.BuildForCommand();
                if (!string.IsNullOrEmpty(selector) &&
                    !SceneCommandTargeting.ResolveSelection(selector, context).Any(item => item.Id == held.id))
                {
                    RejectTarget(id, $"현재 들고 있는 물체는 {held.displayName}입니다. 요청한 물체는 들고 있지 않습니다.");
                    return true;
                }
                SubmitTarget(id, new[] { AgentVocabulary.Drop }, held, context);
                return true;
            }
            if (SceneCommandTargeting.IsRepickCommand(text))
            {
                _pendingCommand = null;
                var last = _runner.LastDroppedObject;
                if (last == null || !_stage.TryResolve(last.id, out var actual) || actual != last)
                    RejectTarget(id, "다시 집을 수 있는 최근에 내려놓은 물체를 찾지 못했습니다.");
                else
                    SubmitTarget(id, new[] { AgentVocabulary.PickUp }, last, _stage.BuildForCommand());
                return true;
            }
            if (!SceneCommandTargeting.TryParseCarryMove(text, out var actions)) return false;
            _pendingCommand = null;
            var carried = CurrentHeldObject;
            if (carried == null) RejectTarget(id, "현재 들고 있는 물체가 없습니다.");
            else _runner.Submit(id, actions, null, requiredHeldObject: carried);
            return true;
        }

        private static PendingCommand CreatePending(string[] verbs, string selector, bool buttonsOnly = false, bool doorsOnly = false)
        {
            return new PendingCommand
            {
                Verbs = verbs,
                ButtonsOnly = buttonsOnly || verbs.Any(verb => verb == AgentVocabulary.Press || verb == AgentVocabulary.Interact),
                DoorsOnly = doorsOnly,
                BoxesOnly = !string.IsNullOrEmpty(selector) &&
                    (selector.Contains("상자") || selector.IndexOf("box", StringComparison.OrdinalIgnoreCase) >= 0),
                CandidateIds = Array.Empty<string>()
            };
        }

        private void ResolveCommandTarget(AgentCommandId id, PendingCommand command, string selector, string lookupError = null,
            IReadOnlyDictionary<string, AgentInteractable> originalTargets = null, int? originalTrust = null, bool useButtonContext = false)
        {
            if (!_runner.IsCurrent(id)) return;
            var context = _stage.BuildForCommand();
            _runner.PopulateCommandContext(context, _previousAction);
            var boxScope = command.BoxesOnly && _stage.boxPlacement != null ?
                BoxIdsInRoom(context, context.ButtonRoomId) : null;
            var scope = context.Objects.Where(item =>
                (!command.ButtonsOnly || SceneCommandTargeting.IsButton(item)) &&
                (!command.DoorsOnly || item.DoorAliases.Count > 0) &&
                (!command.BoxesOnly || SceneCommandTargeting.IsBox(item)) &&
                (boxScope == null || boxScope.Contains(item.Id)) &&
                (command.CandidateIds.Length < 2 || selector.Contains("말고") || command.CandidateIds.Contains(item.Id)))
                .Select(item => item.Id).ToArray();
            var selected = useButtonContext ? SceneCommandTargeting.ResolveButtonSelection(selector, context, scope, command.DoorsOnly) :
                SceneCommandTargeting.ResolveSelection(selector, context, scope);
            if (lookupError == null && selected.Count == 1 && _stage.TryResolve(selected[0].Id, out var target))
            {
                if (originalTargets == null || (originalTargets.TryGetValue(target.id, out var original) && original == target))
                {
                    _pendingCommand = null;
                    SubmitTarget(id, command.Verbs, target, context, originalTrust);
                    return;
                }
                lookupError = "응답을 기다리는 동안 대상이 바뀌었습니다.";
            }

            if (selected.Count > 1) command.CandidateIds = selected.Select(item => item.Id).ToArray();
            _pendingCommand = command;
            string label = command.ButtonsOnly ? "버튼" : command.BoxesOnly ? "상자" : "대상";
            string message;
            if (selected.Count > 1)
            {
                var names = string.Join(", ", selected.Take(5).Select(item =>
                    $"{item.DisplayName ?? item.SceneName ?? item.Id} ({item.Id})"));
                message = $"일치하는 {label} 후보가 {selected.Count}개 있습니다. 어느 쪽인가요? {names}";
            }
            else
            {
                string missing = command.BoxesOnly ? "지정한 상자를 찾지 못했습니다." : $"지정한 {label}을 찾지 못했습니다.";
                message = (lookupError ?? missing) + " 이름이나 색상을 다시 알려 주세요.";
            }
            RejectTarget(id, message);
        }

        private void SubmitTarget(AgentCommandId id, string[] verbs, AgentInteractable target, StageContext context,
            int? originalTrust = null)
        {
            var actions = verbs.Select(verb => new AgentAction { Verb = verb, Target = target.id }).ToArray();
            foreach (var action in actions)
            {
                string error = AgentResponseParser.CheckAction(action, context,
                    Math.Min(Trust, originalTrust ?? Trust), checkInventory: false);
                if (error != null)
                {
                    RejectTarget(id, error);
                    return;
                }
            }

            var targets = new Dictionary<string, AgentInteractable>(StringComparer.Ordinal) { [target.id] = target };
            _runner.Submit(id, actions, null, targets);
        }

        private void RejectTarget(AgentCommandId id, string message)
        {
            Say(message);
            if (_runner.IsCurrent(id)) _runner.Submit(id, Array.Empty<AgentAction>(), message);
        }

        public void ResumeExploration()
        {
            _pendingCommand = null;
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
                var keywords = _stage.BuildForCommand().Objects.SelectMany(o => new[] { o.DisplayName }.Concat(o.Aliases))
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
                int requestTrust = Trust;
                var prepared = await _brain.PrepareTurnAsync(text, context, token);
                if (!IsCurrent(id, token)) return;
                // 최신성 검사와 상태 확정 사이에 await/외부 이벤트를 두지 않는다.
                var result = _brain.CommitTurn(prepared);
                result.CommandId = id;
                if (TryContinueApiTarget(id, text, result, knownTargets, requestTrust)) return;
                var actions = result.Actions.Select(AgentCommandRules.Copy).ToArray();
                string groundingError = result.Rejected.Count == 0
                    ? SceneCommandTargeting.CheckGrounding(text, context, actions) : null;
                string rejection = result.Rejected.Count == 0 ? groundingError : result.Rejected[0].Reason;
                if (rejection == null && actions.Any(action => action.Target != null && !knownTargets.ContainsKey(action.Target)))
                    rejection = "지정한 대상을 씬에서 찾지 못했습니다.";
                if (rejection != null)
                {
                    result.Response.Speech = rejection;
                    if (groundingError != null)
                    {
                        result.Response.Comprehension = AgentVocabulary.Asking;
                        if (actions.Any(action => action.Verb == AgentVocabulary.Press || action.Verb == AgentVocabulary.Interact))
                        {
                            var candidates = SceneCommandTargeting.PressCandidateIds(text, context);
                            if (candidates.Count > 1)
                                _pendingCommand = new PendingCommand { Verbs = new[] { AgentVocabulary.Press }, ButtonsOnly = true,
                                    CandidateIds = candidates.ToArray() };
                        }
                    }
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

        private bool TryContinueApiTarget(AgentCommandId id, string text, AgentTurnResult result,
            IReadOnlyDictionary<string, AgentInteractable> originalTargets, int originalTrust)
        {
            var response = result.Response;
            bool pickup = SceneCommandTargeting.TryParseTargetCommand(text, out string verb, out string selector);
            // Only recover the unresolved slot of a single explicit pickup. The API still owns other action plans.
            if (pickup && (response.Comprehension == AgentVocabulary.Understood &&
                    response.Actions.Count == 1 && IsBareTargetAction(response.Actions[0], verb) ||
                response.Comprehension == AgentVocabulary.Asking && response.Actions.Count == 0 &&
                    (response.ClarifyVerb == null || response.ClarifyVerb == verb)))
            {
                ResolveCommandTarget(id, CreatePending(new[] { verb }, selector), selector,
                    originalTargets: originalTargets, originalTrust: originalTrust);
                return true;
            }
            if (response.Comprehension != AgentVocabulary.Asking || response.Actions.Count != 0 ||
                !AgentVocabulary.TargetClarificationVerbs.Contains(response.ClarifyVerb))
                return false;

            var pending = CreatePending(new[] { response.ClarifyVerb }, "");
            var context = _stage.BuildForCommand();
            _runner.PopulateCommandContext(context, _previousAction);
            pending.CandidateIds = pending.ButtonsOnly ? SceneCommandTargeting.PressCandidateIds(text, context).ToArray() :
                context.Objects.Select(item => item.Id).ToArray();
            if (pending.ButtonsOnly && pending.CandidateIds.Length < 2) return false;
            _pendingCommand = pending;
            RejectTarget(id, pending.ButtonsOnly ? "어느 버튼인가요? 이름이나 번호를 알려 주세요." :
                "어느 대상인가요? 이름이나 색상을 알려 주세요.");
            return true;
        }

        private static bool IsBareTargetAction(AgentAction action, string verb) =>
            action != null && action.Verb == verb && action.Item == null && action.Direction == null &&
            !action.Steps.HasValue && !action.Distance.HasValue && !action.Angle.HasValue && action.Text == null;

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
