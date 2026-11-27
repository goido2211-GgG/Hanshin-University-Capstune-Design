using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>실행 권한의 유일한 소유자. API/코루틴 완료 순서와 독립적으로 작업을 무효화한다.</summary>
    [DisallowMultipleComponent]
    public sealed class AgentActionRunner : MonoBehaviour
    {
        [Min(0f)] public float intentDelaySeconds = 1f;
        public AgentExecutionState State { get; private set; }
        public AgentCommandId CurrentCommand { get; private set; }
        public AgentCommandSource CurrentSource { get; private set; }
        public bool HasActiveCommand => _active;
        public bool ExplorationEnabled { get; private set; }
        public bool AutonomySuppressed { get; private set; }
        public event Action<AgentCommandId> CommandStarted;
        public event Action<bool> AutonomySuppressionChanged;
        public event Action<bool> ExplorationCapabilityChanged;
        public string PendingIntent { get; private set; } = "";
        public string LastResult { get; private set; } = "";
        public event Action<AgentExecutionState> ExecutionStateChanged;
        public event Action<string> IntentAnnounced;
        public event Action<AgentExecutionResult> ActionFinished;
        public event Action<AgentExecutionResult> CommandFinished;

        private readonly Guid _session = Guid.NewGuid();
        private readonly StageContext _validationContext = new StageContext();
        private long _sequence;
        private bool _active;
        private List<AgentAction> _actions;
        private int _index = -1;
        private float _delay;
        private VoiceAgent _owner;
        private SceneStageContext _stage;
        private AgentBodyController _body;
        private float _autonomousRadius;
        private IReadOnlyDictionary<string, AgentInteractable> _expectedTargets;
        private AgentInteractable _requiredHeldObject;
        private bool _requireHeldObject;
        private AgentInteractable _selectedTarget;
        private AgentAction _lastAction;
        private bool? _lastActionSucceeded;

        internal AgentInteractable SelectedTarget => _selectedTarget != null &&
            _stage.TryResolve(_selectedTarget.id, out var current) && current == _selectedTarget ? current : null;

        internal AgentAction CurrentActionForContext => CopyForContext(CurrentAction);
        public AgentInteractable CurrentHeldObject => _body == null ? null : _body.CurrentHeldObject;
        internal AgentInteractable LastDroppedObject => _body == null ? null : _body.LastDroppedObject;
        private AgentAction CurrentAction => _active && _actions != null && _index >= 0 && _index < _actions.Count
            ? _actions[_index] : null;

        internal void SelectTarget(AgentInteractable target) => _selectedTarget = target;

        internal void ClearCommandContext()
        {
            _selectedTarget = null;
            _lastAction = null;
            _lastActionSucceeded = null;
        }

        internal void PopulateCommandContext(StageContext context, AgentAction previousAction)
        {
            context.SelectedTargetId = SelectedTarget?.id;
            context.LastAction = CopyForContext(_lastAction);
            context.LastActionSucceeded = _lastActionSucceeded;
            context.CurrentAction = previousAction;
            if (_body == null || !_body.isActiveAndEnabled) return;
            Vector3 position = _body.transform.position;
            Vector3 forward = _body.transform.forward;
            context.AgentPositionX = position.x;
            context.AgentPositionY = position.y;
            context.AgentPositionZ = position.z;
            context.AgentHeading = (float)(Math.Atan2(forward.x, forward.z) * 180.0 / Math.PI);
        }

        private static AgentAction CopyForContext(AgentAction action)
        {
            var copy = AgentCommandRules.Copy(action);
            if (copy != null) copy.Text = null;
            return copy;
        }

        internal void Initialize(VoiceAgent owner, SceneStageContext stage) { _owner = owner; _stage = stage; }

        internal void AttachBody(AgentBodyController body)
        {
            if (_body != null && _body != body) throw new InvalidOperationException("한 VoiceAgent에는 하나의 Body만 연결할 수 있습니다.");
            _body = body;
            _stage.BindCarryBody(body);
        }

        internal void DetachBody(AgentBodyController body)
        {
            if (_body != body) return;
            Cancel("로봇이 비활성화되었습니다.");
            _body = null;
            _stage.BindCarryBody(null);
        }

        public bool IsCurrent(AgentCommandId id) => _active && CurrentCommand.Equals(id) && isActiveAndEnabled;

        internal AgentCommandId BeginCommand()
            => StartCommand(AgentCommandSource.Player);

        internal bool TryBeginAutonomous(float radius, out AgentCommandId id)
        {
            id = default;
            if (_active || !ExplorationEnabled || AutonomySuppressed || !isActiveAndEnabled || _owner == null ||
                !_owner.isActiveAndEnabled || _owner.State != VoiceAgentState.Idle ||
                _body == null || !_body.isActiveAndEnabled || radius <= 0f ||
                float.IsNaN(radius) || float.IsInfinity(radius)) return false;
            _autonomousRadius = radius;
            id = StartCommand(AgentCommandSource.Autonomous);
            return IsCurrent(id);
        }

        private AgentCommandId StartCommand(AgentCommandSource source)
        {
            var id = new AgentCommandId(_session, ++_sequence);
            Cancel("새 플레이어 명령으로 교체되었습니다.");
            if (_sequence != id.Sequence) return id;
            CurrentCommand = id;
            CurrentSource = source;
            _active = true;
            _index = -1;
            _actions = null;
            PendingIntent = "";
            SetState(AgentExecutionState.AwaitingInterpretation);
            if (IsCurrent(id)) AgentEventDispatch.Invoke(CommandStarted, id);
            return id;
        }

        internal void Submit(AgentCommandId id, IEnumerable<AgentAction> actions, string rejection,
            IReadOnlyDictionary<string, AgentInteractable> expectedTargets = null, AgentInteractable requiredHeldObject = null)
        {
            if (!IsCurrent(id) || State != AgentExecutionState.AwaitingInterpretation) return;
            if (rejection != null) { Finish(AgentExecutionOutcome.Rejected, rejection); return; }
            _expectedTargets = expectedTargets;
            _requiredHeldObject = requiredHeldObject;
            _requireHeldObject = requiredHeldObject != null;
            _actions = actions.Select(AgentCommandRules.Copy).ToList();
            if (CurrentSource == AgentCommandSource.Autonomous && _actions.Any(a =>
                a == null || (a.Verb != AgentVocabulary.MoveTo && a.Verb != AgentVocabulary.Inspect)))
            { Finish(AgentExecutionOutcome.Rejected, "자율 탐색은 이동·조사만 허용합니다."); return; }
            if (_actions.Any(action => action != null &&
                (action.Verb == AgentVocabulary.Press || action.Verb == AgentVocabulary.Interact) &&
                (action.Target == null || _expectedTargets == null || !_expectedTargets.ContainsKey(action.Target))))
            { Finish(AgentExecutionOutcome.Rejected, "어느 버튼을 누를까요? 이름이나 번호를 알려 주세요."); return; }
            foreach (var action in _actions)
                if (action?.Verb == AgentVocabulary.Interact) action.Verb = AgentVocabulary.Press;
            if (_actions.Count > AgentResponseParser.MaxActionsPerTurn)
            { Finish(AgentExecutionOutcome.Rejected, "한 턴 행동 수 초과"); return; }
            if (_actions.Count == 0) { Finish(AgentExecutionOutcome.Completed, "실행할 행동이 없습니다."); return; }
            _index = 0;
            PrepareAction(id);
        }

        private void PrepareAction(AgentCommandId id)
        {
            if (!IsCurrent(id)) return;
            if (_actions[_index]?.Verb == AgentVocabulary.Stop) { Stop(); return; }
            string error = ValidateCurrent();
            if (error != null) { CompleteStep(id, AgentExecutionOutcome.Rejected, error); return; }
            var action = CurrentAction;
            if (CurrentSource == AgentCommandSource.Player && action.Target != null &&
                !(action.Verb == AgentVocabulary.MoveTo && action.PlacementTargetId != null) &&
                _stage.TryResolve(action.Target, out var selected)) SelectTarget(selected);
            PendingIntent = Describe(action);
            SetState(AgentExecutionState.Announcing);
            if (!IsCurrent(id)) return;
            AgentEventDispatch.Invoke(IntentAnnounced, PendingIntent);
            if (!IsCurrent(id)) return;
            _delay = Mathf.Max(0f, intentDelaySeconds);
            SetState(AgentExecutionState.Waiting);
        }

        private void Update() => Tick(Time.deltaTime);

        // 명시적 dt를 받아 네트워크와 무관한 상태 전이 시험이 가능하다.
        internal void Tick(float deltaTime)
        {
            if (!_active || deltaTime <= 0f) return;
            var id = CurrentCommand;
            try
            {
                if (State == AgentExecutionState.Waiting)
                {
                    _delay -= deltaTime;
                    if (_delay > 0f) return;
                    string error = ValidateCurrent();
                    if (error != null) { CompleteStep(id, AgentExecutionOutcome.Rejected, error); return; }
                    error = _body.BeginAction(_actions[_index], _stage);
                    if (!IsCurrent(id)) return;
                    if (error != null) { CompleteStep(id, AgentExecutionOutcome.Failed, error); return; }
                    SetState(AgentExecutionState.Executing);
                    return;
                }
                if (State != AgentExecutionState.Executing || !IsCurrent(id)) return;
                string validation = ValidateCurrent();
                if (validation != null) { CompleteStep(id, AgentExecutionOutcome.Rejected, validation); return; }
                if (!_body.TickAction(deltaTime, _stage, out string message, out bool success)) return;
                CompleteStep(id, success ? AgentExecutionOutcome.Completed : AgentExecutionOutcome.Failed, message);
            }
            catch (Exception e)
            {
                if (IsCurrent(id)) CompleteStep(id, AgentExecutionOutcome.Failed, "행동을 실행하는 중 오류가 발생했습니다.");
                Debug.LogException(e);
            }
        }

        private void CompleteStep(AgentCommandId id, AgentExecutionOutcome outcome, string message)
        {
            if (!IsCurrent(id)) return;
            var action = CurrentAction;
            _body?.CancelCurrentAction();
            if (!IsCurrent(id)) return;
            if (CurrentSource == AgentCommandSource.Player)
            {
                _lastAction = CopyForContext(action);
                _lastActionSucceeded = outcome == AgentExecutionOutcome.Completed;
            }

            var result = Result(outcome, message);
            if (action?.Verb == AgentVocabulary.Speak && outcome == AgentExecutionOutcome.Completed)
                _owner.Say(message);
            // 대사에는 사용자 입력이 포함될 수 있으므로 다음 API 요청의 게임 기록에 넣지 않는다.
            if (!IsCurrent(id)) return;
            AgentEventDispatch.Invoke(ActionFinished, result);
            if (!IsCurrent(id)) return;
            if (outcome != AgentExecutionOutcome.Completed) { Finish(outcome, message); return; }
            if (++_index >= _actions.Count) { Finish(AgentExecutionOutcome.Completed, "명령을 완료했습니다."); return; }
            PrepareAction(id);
        }

        private string ValidateCurrent()
        {
            if (_body == null || !_body.isActiveAndEnabled) return "활성 AgentBodyController가 없습니다.";
            if (_requireHeldObject && (_requiredHeldObject == null || CurrentHeldObject != _requiredHeldObject))
                return "이동에 필요한 물체를 더 이상 들고 있지 않습니다.";
            var action = CurrentAction;
            if (action?.PlacementTargetId != null)
            {
                if (!_stage.TryResolvePlacement(action.PlacementTargetId, out var destination) ||
                    _expectedTargets == null ||
                    !_expectedTargets.TryGetValue(action.PlacementTargetId, out var expectedPad) ||
                    destination.Interactable != expectedPad)
                    return "지정한 발판이 변경되었거나 비활성화되었습니다.";
                string placementError = destination.ConfigurationError();
                if (placementError != null) return placementError;
                if (CurrentHeldObject != destination.box)
                    return "발판에 놓을 상자를 들고 있지 않습니다.";
            }
            _stage.CopyInventoryTo(_validationContext.Inventory);
            _validationContext.Objects.Clear();
            AgentInteractable actual = null;
            if (action?.Target != null)
            {
                if (!_stage.TryResolve(action.Target, out actual))
                    return "지정한 대상을 찾지 못했거나 대상 ID가 중복되었습니다.";
                if (_expectedTargets != null &&
                    (!_expectedTargets.TryGetValue(action.Target, out var expected) || expected != actual))
                    return "선택한 대상이 변경되었거나 비활성화되었습니다.";
                if (action.Verb == AgentVocabulary.Press && actual.Button == null)
                    return "누를 수 있는 버튼을 찾지 못했습니다.";
                _validationContext.Objects.Add(actual.ToStageObject());
            }
            string error = action == null ? null : _body.CheckCarryAction(action, actual);
            if (error != null) return error;
            error = AgentResponseParser.CheckAction(action, _validationContext, _owner.Trust);
            if (error != null) return error;
            if (CurrentSource == AgentCommandSource.Autonomous)
            {
                if (action.Verb != AgentVocabulary.MoveTo && action.Verb != AgentVocabulary.Inspect)
                    return "자율 탐색은 이동·조사만 허용합니다.";
                if (!_stage.TryResolve(action.Target, out var target) || !target.allowAutonomousExploration || target.HasBeenInspected ||
                    target == CurrentHeldObject || target.transform == _body.transform ||
                    _body.transform.IsChildOf(target.transform) || target.transform.IsChildOf(_body.transform))
                    return "현재 조사할 수 없는 탐색 대상입니다.";
                if ((target.transform.position - _body.transform.position).sqrMagnitude > _autonomousRadius * _autonomousRadius)
                    return "탐색 허용 범위를 벗어났습니다.";
            }
            return null;
        }

        private string Describe(AgentAction action)
        {
            string target = _stage.TryResolve(action.Target, out var item) ? item.displayName : "";
            switch (action.Verb)
            {
                case AgentVocabulary.MoveTo: return target + " 앞으로 이동하겠습니다.";
                case AgentVocabulary.Inspect: return target + "을(를) 살펴보겠습니다.";
                case AgentVocabulary.Press: return target + "을(를) 누르겠습니다.";
                case AgentVocabulary.PickUp: return target + "을(를) 집겠습니다.";
                case AgentVocabulary.Drop:
                    return target + (action.PlacementTargetId != null ? "을(를) 발판 위에 내려놓겠습니다." : "을(를) 내려놓겠습니다.");
                case AgentVocabulary.MoveDirection:
                    string direction = action.Direction == "back" ? "뒤로" : action.Direction == "left" ? "왼쪽으로" :
                        action.Direction == "right" ? "오른쪽으로" : "앞으로";
                    string distance = action.Distance.HasValue ? $"{action.Distance:0.##}미터" : $"{action.Steps ?? 1}걸음";
                    return direction + " " + distance + " 이동하겠습니다.";
                case AgentVocabulary.Rotate:
                    return (action.Angle > 0f ? "오른쪽" : "왼쪽") + "으로 돌겠습니다.";
                case AgentVocabulary.Speak: return "말하겠습니다.";
                default: return "1초 동안 기다리겠습니다.";
            }
        }

        public void Cancel(string reason = "멈췄습니다.")
        {
            if (_active) Finish(AgentExecutionOutcome.Cancelled, reason);
            else { _body?.CancelCurrentAction(); LastResult = reason; }
        }

        public void Stop()
        {
            AutonomySuppressed = true;
            AgentEventDispatch.Invoke(AutonomySuppressionChanged, true);
            Cancel("멈췄습니다. 자동 탐색은 명시적으로 재개해야 합니다.");
        }

        public void SetExplorationEnabled(bool enabled)
        {
            if (ExplorationEnabled == enabled) return;
            ExplorationEnabled = enabled;
            if (!enabled && _active && CurrentSource == AgentCommandSource.Autonomous)
                Cancel("자율 탐색 기능이 비활성화되었습니다.");
            AgentEventDispatch.Invoke(ExplorationCapabilityChanged, enabled);
        }

        public void ResumeExploration()
        {
            AutonomySuppressed = false;
            AgentEventDispatch.Invoke(AutonomySuppressionChanged, false);
        }

        internal void Fail(AgentCommandId id, string reason)
        { if (IsCurrent(id)) Finish(AgentExecutionOutcome.Failed, reason); }

        private AgentExecutionResult Result(AgentExecutionOutcome outcome, string message) => new AgentExecutionResult
        { CommandId = CurrentCommand, Source = CurrentSource, ActionIndex = _index, Outcome = outcome, Message = message };

        private void Finish(AgentExecutionOutcome outcome, string message)
        {
            if (!_active) return;
            var result = Result(outcome, message);
            if (outcome == AgentExecutionOutcome.Cancelled && CurrentSource == AgentCommandSource.Player &&
                State == AgentExecutionState.Executing && CurrentAction != null)
            {
                _lastAction = CopyForContext(CurrentAction);
                _lastActionSucceeded = false;
            }
            _active = false;
            _expectedTargets = null;
            _requiredHeldObject = null;
            _requireHeldObject = false;
            _actions = null;
            PendingIntent = "";
            LastResult = message;
            State = AgentExecutionState.Idle;
            _body?.CancelCurrentAction();
            if (!_active && CurrentCommand.Equals(result.CommandId))
                AgentEventDispatch.Invoke(ExecutionStateChanged, AgentExecutionState.Idle);
            AgentEventDispatch.Invoke(CommandFinished, result);
        }

        private void SetState(AgentExecutionState state)
        { State = state; AgentEventDispatch.Invoke(ExecutionStateChanged, state); }
        private void OnDisable() => Cancel("실행기가 비활성화되었습니다.");
    }

    internal static class AgentEventDispatch
    {
        internal static void Invoke<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }
    }
}
