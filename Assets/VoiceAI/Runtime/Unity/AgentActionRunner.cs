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
        public bool AutonomySuppressed { get; private set; }
        public event Action<AgentCommandId> CommandStarted;
        public event Action<bool> AutonomySuppressionChanged;
        public string PendingIntent { get; private set; } = "";
        public string LastResult { get; private set; } = "";
        public event Action<AgentExecutionState> ExecutionStateChanged;
        public event Action<string> IntentAnnounced;
        public event Action<AgentExecutionResult> ActionFinished;
        public event Action<AgentExecutionResult> CommandFinished;

        private readonly Guid _session = Guid.NewGuid();
        private long _sequence;
        private bool _active;
        private List<AgentAction> _actions;
        private int _index = -1;
        private float _delay;
        private VoiceAgent _owner;
        private SceneStageContext _stage;
        private AgentBodyController _body;
        private float _autonomousRadius;

        internal void Initialize(VoiceAgent owner, SceneStageContext stage) { _owner = owner; _stage = stage; }

        internal void AttachBody(AgentBodyController body)
        {
            if (_body != null && _body != body) throw new InvalidOperationException("한 VoiceAgent에는 하나의 Body만 연결할 수 있습니다.");
            _body = body;
        }

        internal void DetachBody(AgentBodyController body)
        {
            if (_body != body) return;
            Cancel("로봇이 비활성화되었습니다.");
            _body = null;
        }

        public bool IsCurrent(AgentCommandId id) => _active && CurrentCommand.Equals(id) && isActiveAndEnabled;

        internal AgentCommandId BeginCommand()
            => StartCommand(AgentCommandSource.Player);

        internal bool TryBeginAutonomous(float radius, out AgentCommandId id)
        {
            id = default;
            if (_active || AutonomySuppressed || !isActiveAndEnabled || _owner == null ||
                !_owner.isActiveAndEnabled || _owner.State != VoiceAgentState.Idle ||
                _body == null || !_body.isActiveAndEnabled || radius <= 0f ||
                float.IsNaN(radius) || float.IsInfinity(radius)) return false;
            _autonomousRadius = radius;
            id = StartCommand(AgentCommandSource.Autonomous);
            return IsCurrent(id);
        }

        private AgentCommandId StartCommand(AgentCommandSource source)
        {
            Cancel("새 플레이어 명령으로 교체되었습니다.");
            var id = new AgentCommandId(_session, ++_sequence);
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

        internal void Submit(AgentCommandId id, IEnumerable<AgentAction> actions, string rejection)
        {
            if (!IsCurrent(id) || State != AgentExecutionState.AwaitingInterpretation) return;
            if (rejection != null) { Finish(AgentExecutionOutcome.Rejected, rejection); return; }
            _actions = actions.Select(AgentCommandRules.Copy).ToList();
            if (CurrentSource == AgentCommandSource.Autonomous && _actions.Any(a =>
                a == null || (a.Verb != AgentVocabulary.MoveTo && a.Verb != AgentVocabulary.Inspect)))
            { Finish(AgentExecutionOutcome.Rejected, "자율 탐색은 이동·조사만 허용합니다."); return; }
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
            if (error != null) { Finish(AgentExecutionOutcome.Rejected, error); return; }
            PendingIntent = Describe(_actions[_index]);
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
                    if (error != null) { Finish(AgentExecutionOutcome.Rejected, error); return; }
                    error = _body.BeginAction(_actions[_index], _stage);
                    if (error != null) { Finish(AgentExecutionOutcome.Failed, error); return; }
                    SetState(AgentExecutionState.Executing);
                    return;
                }
                if (State != AgentExecutionState.Executing || !IsCurrent(id)) return;
                string validation = ValidateCurrent();
                if (validation != null) { Finish(AgentExecutionOutcome.Rejected, validation); return; }
                if (!_body.TickAction(deltaTime, _stage, out string message, out bool success)) return;
                _body.CancelCurrentAction();
                if (!IsCurrent(id)) return;
                var result = Result(success ? AgentExecutionOutcome.Completed : AgentExecutionOutcome.Failed, message);
                _owner.ReportEvent(message);
                AgentEventDispatch.Invoke(ActionFinished, result);
                if (!IsCurrent(id)) return;
                if (!success) { Finish(AgentExecutionOutcome.Failed, message); return; }
                if (++_index >= _actions.Count) { Finish(AgentExecutionOutcome.Completed, "명령을 완료했습니다."); return; }
                PrepareAction(id);
            }
            catch (Exception e)
            {
                if (IsCurrent(id)) Finish(AgentExecutionOutcome.Failed, "행동 실행 오류: " + e.Message);
            }
        }

        private string ValidateCurrent()
        {
            if (_body == null || !_body.isActiveAndEnabled) return "활성 AgentBodyController가 없습니다.";
            var action = _actions[_index];
            string error = AgentResponseParser.CheckAction(action, _stage.Build(), _owner.Trust);
            if (error != null) return error;
            if (action.Target != null && !_stage.TryResolve(action.Target, out _))
                return "실제 대상이 없거나 ID가 중복되었습니다: " + action.Target;
            if (CurrentSource == AgentCommandSource.Autonomous)
            {
                if (action.Verb != AgentVocabulary.MoveTo && action.Verb != AgentVocabulary.Inspect)
                    return "자율 탐색은 이동·조사만 허용합니다.";
                if (!_stage.TryResolve(action.Target, out var target) || !target.allowAutonomousExploration || target.HasBeenInspected ||
                    _stage.inventory.Contains(action.Target) || target.transform == _body.transform ||
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
                case AgentVocabulary.PickUp: return target + "을(를) 집겠습니다.";
                case AgentVocabulary.Drop: return target + "을(를) 내려놓겠습니다.";
                case AgentVocabulary.MoveDirection:
                    string direction = action.Direction == "back" ? "뒤" : action.Direction == "left" ? "왼쪽" : action.Direction == "right" ? "오른쪽" : "앞";
                    return direction + "으로 " + (action.Steps ?? 1) + "걸음 이동하겠습니다.";
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
            _active = false;
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
