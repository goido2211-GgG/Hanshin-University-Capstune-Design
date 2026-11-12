using System;
using System.Collections.Generic;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>LLM을 호출하지 않는 이동·조사 전용 제안자. 실행·선점 권한은 Runner에 있다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentBodyController))]
    public sealed class RobotExplorationController : MonoBehaviour
    {
        public AgentBodyController body;
        [Min(0f)] public float idleDelaySeconds = 3f;
        [Min(0.1f)] public float explorationRadius = 10f;
        public string Status { get; private set; } = "탐색 준비 중";
        public string CurrentTargetId { get; private set; } = "";
        public float RemainingDelay => _remaining;
        public int FailedTargetCount => _failedIds.Count;

        private readonly HashSet<string> _failedIds = new HashSet<string>(StringComparer.Ordinal);
        private VoiceAgent _voice;
        private AgentActionRunner _runner;
        private SceneStageContext _stage;
        private AgentCommandId _command;
        private bool _inFlight;
        private bool _scheduled;
        private float _remaining;

        private void OnEnable() { Connect(); }
        private void Update() => Tick(Time.deltaTime);

        private bool Connect()
        {
            if (_runner != null) return true;
            if (body == null) body = GetComponent<AgentBodyController>();
            if (body == null || body.voiceAgent == null) { Status = "Body/VoiceAgent 연결 대기"; return false; }
            _voice = body.voiceAgent;
            if (body.gameObject.scene != _voice.gameObject.scene) { Status = "동일 씬의 VoiceAgent가 필요합니다."; return false; }
            _stage = _voice.GetComponent<SceneStageContext>();
            _runner = _voice.Runner;
            _runner.CommandStarted += OnCommandStarted;
            _runner.CommandFinished += OnCommandFinished;
            _runner.AutonomySuppressionChanged += OnSuppressionChanged;
            Schedule();
            return true;
        }

        private void Schedule()
        {
            _remaining = Mathf.Max(0f, idleDelaySeconds);
            _scheduled = !_runner.AutonomySuppressed && !_runner.HasActiveCommand;
            Status = _runner.AutonomySuppressed ? "명시적 중지 — 탐색 재개 필요" : "탐색 대기";
        }

        private void OnCommandStarted(AgentCommandId id)
        {
            _scheduled = false;
            _remaining = Mathf.Max(0f, idleDelaySeconds);
            Status = _runner.CurrentSource == AgentCommandSource.Player ? "플레이어 명령 우선" : "자율 탐색 수행 중";
        }

        private void OnSuppressionChanged(bool suppressed)
        {
            if (suppressed) { _scheduled = false; Status = "명시적 중지 — 탐색 재개 필요"; }
            else Schedule();
        }

        private void OnCommandFinished(AgentExecutionResult result)
        {
            if (result.Source == AgentCommandSource.Player)
            {
                if (result.Outcome == AgentExecutionOutcome.Completed) Schedule();
                else { _scheduled = false; Status = "플레이어 작업 미완료 — 재개 요청 대기"; }
                return;
            }
            // 이전 작업의 종료 알림이 새 플레이어 작업/새 탐색을 갱신하지 못한다.
            if (!_inFlight || !_command.Equals(result.CommandId)) return;
            if (result.Outcome == AgentExecutionOutcome.Failed || result.Outcome == AgentExecutionOutcome.Rejected)
                _failedIds.Add(CurrentTargetId);
            _inFlight = false;
            CurrentTargetId = "";
            Schedule();
        }

        internal void Tick(float deltaTime)
        {
            if (!isActiveAndEnabled || !Connect() || deltaTime <= 0f) return;
            if (_runner.AutonomySuppressed) { Status = "명시적 중지 — 탐색 재개 필요"; return; }
            if (_runner.HasActiveCommand || !_runner.isActiveAndEnabled || !_voice.isActiveAndEnabled ||
                !body.isActiveAndEnabled || _voice.State != VoiceAgentState.Idle) return;
            if (!_scheduled) return;
            _remaining = Mathf.Max(0f, _remaining - deltaTime);
            if (_remaining > 0f) return;
            var target = ChooseTarget();
            if (target == null) { Status = "조사할 실제 대상 없음 — Idle"; return; }
            if (!_runner.TryBeginAutonomous(explorationRadius, out var id)) return;
            _command = id;
            _inFlight = true;
            CurrentTargetId = target.id;
            // 반드시 실제 고유 대상에서 만든 ID만 사용한다. StageContext의 가상 데모 데이터는 참조하지 않는다.
            _runner.Submit(id, new[]
            {
                new AgentAction { Verb = AgentVocabulary.MoveTo, Target = target.id },
                new AgentAction { Verb = AgentVocabulary.Inspect, Target = target.id }
            }, null);
        }

        private AgentInteractable ChooseTarget()
        {
            if (explorationRadius <= 0f || float.IsNaN(explorationRadius) || float.IsInfinity(explorationRadius))
            { Status = "탐색 반경은 유한한 양수여야 합니다."; return null; }
            AgentInteractable best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (var item in AgentInteractable.Active)
            {
                if (item == null || !item.isActiveAndEnabled || !item.allowAutonomousExploration || item.HasBeenInspected ||
                    string.IsNullOrWhiteSpace(item.id) || item.gameObject.scene != _stage.gameObject.scene ||
                    _failedIds.Contains(item.id) || _stage.inventory.Contains(item.id) ||
                    item.transform == body.transform || item.transform.IsChildOf(body.transform) || body.transform.IsChildOf(item.transform)) continue;
                if (!_stage.TryResolve(item.id, out var unique) || unique != item) continue;
                float distance = (item.transform.position - body.transform.position).sqrMagnitude;
                if (float.IsNaN(distance) || distance > explorationRadius * explorationRadius) continue;
                if (best == null || distance < bestDistance ||
                    (distance == bestDistance && StringComparer.Ordinal.Compare(item.id, best.id) < 0))
                { best = item; bestDistance = distance; }
            }
            return best;
        }

        // 실패 대상을 다시 시험할 때만 명시적으로 호출한다. 조사 완료 기억은 지우지 않는다.
        [ContextMenu("Reset Failed Exploration Targets")]
        public void ResetFailedTargets() { _failedIds.Clear(); }

        private void OnDisable()
        {
            if (_runner != null)
            {
                _runner.CommandStarted -= OnCommandStarted;
                _runner.CommandFinished -= OnCommandFinished;
                _runner.AutonomySuppressionChanged -= OnSuppressionChanged;
                if (_inFlight && _runner.IsCurrent(_command)) _runner.Cancel("탐색 컴포넌트가 비활성화되었습니다.");
            }
            _runner = null;
            _inFlight = false;
            _scheduled = false;
            CurrentTargetId = "";
            Status = "탐색 컴포넌트 비활성";
        }
    }
}
