using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>단일 행동만 수행한다. 작업·대기·종료 이벤트의 소유자는 Runner다.</summary>
    [DisallowMultipleComponent]
    public sealed class AgentBodyController : MonoBehaviour
    {
        public VoiceAgent voiceAgent;
        public float moveSpeed = 3f;
        public float turnSpeed = 540f;
        public float stepLength = 1f;
        public float stopDistance = 1.2f;
        public float carryHeight = 1f;
        [Min(1f)] public float actionTimeoutSeconds = 30f;

        /// <summary>실제 위치 갱신 직후의 deltaTime. 0은 즉시 정지/기준점 초기화를 뜻한다.</summary>
        public event System.Action<float> MotionUpdated;
        public bool IsMoving => _action != null && _moving && isActiveAndEnabled;

        private AgentAction _action;
        private AgentInteractable _target;
        private Vector3 _destination;
        private bool _moving;
        private float _remainingWait;
        private float _elapsed;

        private void Awake()
        {
            if (voiceAgent == null)
            {
                // 기존 단일 로봇 씬 호환. 여러 후보일 때 임의 선택하지 않는다.
                foreach (var candidate in FindObjectsByType<VoiceAgent>(FindObjectsSortMode.None))
                {
                    if (candidate.gameObject.scene != gameObject.scene) continue;
                    if (voiceAgent != null) { voiceAgent = null; break; }
                    voiceAgent = candidate;
                }
            }
        }

        private void OnEnable()
        {
            if (voiceAgent != null) voiceAgent.RegisterBody(this);
            else Debug.LogError("[VoiceAI] Body의 VoiceAgent를 Inspector에서 지정해 주세요.", this);
        }

        private void OnDisable()
        {
            CancelCurrentAction();
            if (voiceAgent != null) voiceAgent.UnregisterBody(this);
        }

        public void CancelCurrentAction()
        {
            _action = null;
            _target = null;
            _moving = false;
            AgentEventDispatch.Invoke(MotionUpdated, 0f);
        }

        public string BeginAction(AgentAction action, SceneStageContext stage)
        {
            CancelCurrentAction();
            _action = AgentCommandRules.Copy(action);
            _elapsed = 0f;
            _remainingWait = 1f;
            if (action.Verb == AgentVocabulary.Wait) return null;
            if (action.Verb == AgentVocabulary.MoveDirection)
            {
                Vector3 axis = action.Direction == "back" ? -transform.forward :
                    action.Direction == "left" ? -transform.right :
                    action.Direction == "right" ? transform.right : transform.forward;
                _destination = transform.position + axis * (Mathf.Max(0.01f, stepLength) * (action.Steps ?? 1));
                _moving = true;
                return MovementError();
            }
            if (!stage.TryResolve(action.Target, out _target)) return "대상이 없거나 ID가 중복되었습니다: " + action.Target;
            if (action.Verb == AgentVocabulary.Drop) return null;
            Vector3 offset = _target.transform.position - transform.position;
            offset.y = 0f;
            float distance = Mathf.Max(0.05f, stopDistance);
            _destination = offset.magnitude > distance
                ? _target.transform.position - offset.normalized * distance : transform.position;
            _destination.y = transform.position.y;
            _moving = (_destination - transform.position).sqrMagnitude > 0.0001f;
            return _moving ? MovementError() : null;
        }

        private string MovementError() => moveSpeed <= 0f || float.IsNaN(moveSpeed) || float.IsInfinity(moveSpeed)
            ? "이동 속도는 유한한 양수여야 합니다." : null;

        // Runner가 최신 상태 검증 후 프레임마다 한 번 호출한다. true면 행동 종료.
        public bool TickAction(float deltaTime, SceneStageContext stage, out string message, out bool success)
        {
            message = null;
            success = false;
            if (_action == null) { message = "실행 중인 행동이 없습니다."; return true; }
            _elapsed += deltaTime;
            if (_elapsed > Mathf.Max(1f, actionTimeoutSeconds)) { message = "행동 시간이 초과되었습니다."; return true; }
            if (_action.Verb == AgentVocabulary.Wait)
            {
                _remainingWait -= deltaTime;
                if (_remainingWait > 0f) return false;
                success = true; message = "기다리기를 마쳤습니다."; return true;
            }
            if (_action.Target != null && (_target == null || !stage.TryResolve(_action.Target, out var current) || current != _target))
            { message = "행동 중 대상이 변경되었습니다."; return true; }
            if (_moving)
            {
                message = MovementError();
                if (message != null) return true;
                Vector3 direction = _destination - transform.position;
                if (direction.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), Mathf.Max(0f, turnSpeed) * deltaTime);
                transform.position = Vector3.MoveTowards(transform.position, _destination, moveSpeed * deltaTime);
                AgentEventDispatch.Invoke(MotionUpdated, deltaTime);
                if ((_destination - transform.position).sqrMagnitude > 0.0001f) return false;
                _moving = false;
                // 조작은 다음 프레임에 수행하여 접근 직후 최신 검증을 한 번 더 거친다.
                return false;
            }
            if (_target != null && _action.Verb != AgentVocabulary.Drop)
            {
                Vector3 offset = _target.transform.position - transform.position;
                offset.y = 0f;
                if (offset.magnitude > Mathf.Max(0.05f, stopDistance) + 0.05f)
                { message = "대상이 이동하여 조작 거리를 벗어났습니다."; return true; }
            }
            switch (_action.Verb)
            {
                case AgentVocabulary.MoveDirection:
                    stage.agentLocation = "이동한 곳";
                    message = "방향 이동을 마쳤습니다.";
                    break;
                case AgentVocabulary.MoveTo:
                    stage.agentLocation = _target.displayName + " 앞";
                    message = stage.agentLocation + "에 도착했습니다.";
                    break;
                case AgentVocabulary.Inspect:
                    stage.agentLocation = _target.displayName + " 앞";
                    _target.MarkInspected();
                    message = _target.displayName + ": " + _target.description;
                    break;
                case AgentVocabulary.PickUp:
                    if (_target.transform == transform || transform.IsChildOf(_target.transform))
                    { message = "로봇 자신이나 부모는 집을 수 없습니다."; return true; }
                    _target.transform.SetParent(transform, true);
                    _target.transform.localPosition = Vector3.up * carryHeight;
                    _target.state = "요원이 들고 있음";
                    stage.inventory.Add(_target.id);
                    message = _target.displayName + "을(를) 집었습니다.";
                    break;
                case AgentVocabulary.Drop:
                    _target.transform.SetParent(null, true);
                    _target.transform.position = transform.position + transform.forward * Mathf.Max(0.05f, stopDistance);
                    _target.state = "바닥에 있음";
                    stage.inventory.Remove(_target.id);
                    message = _target.displayName + "을(를) 내려놓았습니다.";
                    break;
                default: message = "지원하지 않는 행동입니다."; return true;
            }
            success = true;
            return true;
        }
    }
}
