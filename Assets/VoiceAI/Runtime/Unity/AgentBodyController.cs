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
        [Min(0.01f), Tooltip("한 칸 또는 한 걸음의 Unity unit 거리입니다.")]
        public float stepLength = 1f;
        public float stopDistance = 1.2f;
        public float carryHeight = 1f;
        [Min(1f)] public float actionTimeoutSeconds = 30f;

        [Header("이동 장애물 검사")]
        public LayerMask obstacleLayers = Physics.DefaultRaycastLayers;
        [Min(0.05f)] public float obstacleRadius = 0.35f;
        [Min(0.1f)] public float obstacleHeight = 2f;
        [Tooltip("로봇 루트에서 충돌 검사용 캡슐의 아래쪽까지의 높이입니다.")]
        public float obstacleBottomOffset = 0.05f;

        /// <summary>실제 위치 갱신 직후의 deltaTime. 0은 즉시 정지/기준점 초기화를 뜻한다.</summary>
        public event System.Action<float> MotionUpdated;
        public bool IsMoving => _action != null && _moving && isActiveAndEnabled;

        private AgentAction _action;
        private AgentInteractable _target;
        private Vector3 _destination;
        private AgentButton _button;
        private bool _moving;
        private float _remainingWait;
        private float _remainingAngle;
        private float _elapsed;
        private readonly Collider[] _overlaps = new Collider[16];
        private readonly RaycastHit[] _hits = new RaycastHit[16];

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
            _button = null;
            _moving = false;
            _remainingAngle = 0f;
            AgentEventDispatch.Invoke(MotionUpdated, 0f);
        }

        public string BeginAction(AgentAction action, SceneStageContext stage)
        {
            CancelCurrentAction();
            _action = AgentCommandRules.Copy(action);
            _elapsed = 0f;
            _remainingWait = 1f;
            if (action.Verb == AgentVocabulary.Wait || action.Verb == AgentVocabulary.Speak) return null;
            if (action.Verb == AgentVocabulary.Rotate)
            {
                _remainingAngle = action.Angle ?? 0f;
                if (!IsFinite(_remainingAngle) || _remainingAngle == 0f ||
                    System.Math.Abs(_remainingAngle) > AgentResponseParser.MaxRotationDegrees)
                    return "회전 각도가 허용 범위를 벗어났습니다.";
                return TurningError();
            }
            if (action.Verb == AgentVocabulary.MoveDirection)
            {
                // 각 단계가 시작되는 시점의 로봇 방향을 수평면에 투영한다.
                Vector3 axis = transform.forward;
                axis.y = 0f;
                if (axis.sqrMagnitude < 0.0001f) return "로봇의 수평 방향을 확인할 수 없습니다.";
                axis = axis.normalized;
                switch (action.Direction)
                {
                    case "forward": break;
                    case "back": axis = -axis; break;
                    case "left": axis = new Vector3(-axis.z, 0f, axis.x); break;
                    case "right": axis = new Vector3(axis.z, 0f, -axis.x); break;
                    default: return "이동 방향을 확인할 수 없습니다.";
                }
                float travel = action.Distance ?? stepLength * (action.Steps ?? 1);
                if (!IsFinite(travel) || travel <= 0f || travel > AgentResponseParser.MaxDistance)
                    return "이동 거리가 허용 범위를 벗어났습니다.";
                _destination = transform.position + axis * travel;
                _moving = true;
                return MovementError();
            }
            if (!stage.TryResolve(action.Target, out _target)) return "대상이 없거나 ID가 중복되었습니다: " + action.Target;
            if (action.Verb == AgentVocabulary.Drop) return null;
            _button = _target.Button;
            if (action.Verb == AgentVocabulary.Press)
            {
                if (_button == null) return "AIButton 어댑터가 연결되지 않았습니다: " + _target.name;
                string error = _button.ConfigurationError();
                if (error != null) { Debug.LogWarning("[VoiceAI] " + error, _button); return error; }
            }
            if (_button != null && _button.approachPoint != null &&
                (action.Verb == AgentVocabulary.MoveTo || action.Verb == AgentVocabulary.Press))
            {
                _destination = _button.approachPoint.position;
                _destination.y = transform.position.y;
                _moving = (_destination - transform.position).sqrMagnitude > 0.0001f;
                return _moving ? MovementError() : null;
            }
            Vector3 offset = _target.transform.position - transform.position;
            offset.y = 0f;
            float distance = Mathf.Max(0.05f, stopDistance);
            _destination = offset.magnitude > distance
                ? _target.transform.position - offset.normalized * distance : transform.position;
            _destination.y = transform.position.y;
            _moving = (_destination - transform.position).sqrMagnitude > 0.0001f;
            return _moving ? MovementError() : null;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private string MovementError()
        {
            if (!IsFinite(moveSpeed) || moveSpeed <= 0f) return "이동 속도는 유한한 양수여야 합니다.";
            if (!IsFinite(obstacleRadius) || !IsFinite(obstacleHeight) || !IsFinite(obstacleBottomOffset) ||
                obstacleRadius <= 0f || obstacleHeight < obstacleRadius * 2f)
                return "이동 장애물 검사 크기가 올바르지 않습니다.";
            return null;
        }

        private string TurningError() => !IsFinite(turnSpeed) || turnSpeed <= 0f
            ? "회전 속도는 유한한 양수여야 합니다." : null;

        private bool IsObstacle(Collider collider) => collider != null &&
            collider.transform != transform && !collider.transform.IsChildOf(transform);

        private bool IsPathBlocked(Vector3 displacement)
        {
            float distance = displacement.magnitude;
            if (distance <= 0f) return false;
            Vector3 bottom = transform.position + Vector3.up * (obstacleBottomOffset + obstacleRadius);
            Vector3 top = transform.position + Vector3.up * (obstacleBottomOffset + obstacleHeight - obstacleRadius);
            // 물리 틱 이후 이동한 씬 오브젝트의 위치를 검사에 반영한다.
            Physics.SyncTransforms();
            // 이미 겹친 장애물을 별도로 확인한 뒤 이번 프레임의 이동 구간을 검사한다.
            int overlaps = Physics.OverlapCapsuleNonAlloc(bottom, top, obstacleRadius, _overlaps,
                obstacleLayers, QueryTriggerInteraction.Ignore);
            if (overlaps == _overlaps.Length) return true;
            for (int index = 0; index < overlaps; index++)
                if (IsObstacle(_overlaps[index])) return true;

            int hits = Physics.CapsuleCastNonAlloc(bottom, top, obstacleRadius, displacement / distance, _hits,
                distance, obstacleLayers, QueryTriggerInteraction.Ignore);
            if (hits == _hits.Length) return true;
            for (int index = 0; index < hits; index++)
                if (IsObstacle(_hits[index].collider)) return true;
            return false;
        }

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
            if (_action.Verb == AgentVocabulary.Speak)
            {
                success = true;
                message = _action.Text;
                return true;
            }
            if (_action.Verb == AgentVocabulary.Rotate)
            {
                message = TurningError();
                if (message != null) return true;
                float remaining = Mathf.MoveTowards(_remainingAngle, 0f, turnSpeed * deltaTime);
                transform.rotation = Quaternion.AngleAxis(_remainingAngle - remaining, Vector3.up) * transform.rotation;
                _remainingAngle = remaining;
                if (_remainingAngle != 0f) return false;
                success = true;
                message = "회전을 마쳤습니다.";
                return true;
            }
            if (_action.Target != null && (_target == null || !stage.TryResolve(_action.Target, out var current) || current != _target))
            { message = "행동 중 대상이 변경되었습니다."; return true; }
            if (_moving)
            {
                message = MovementError();
                if (message != null) return true;
                Vector3 nextPosition = Vector3.MoveTowards(transform.position, _destination, moveSpeed * deltaTime);
                if (IsPathBlocked(nextPosition - transform.position))
                { message = "이동 경로에 장애물이 있습니다."; return true; }
                Vector3 direction = _destination - transform.position;
                if (direction.sqrMagnitude > 0.0001f && IsFinite(turnSpeed) && turnSpeed > 0f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(direction), turnSpeed * deltaTime);
                transform.position = nextPosition;
                AgentEventDispatch.Invoke(MotionUpdated, deltaTime);
                if ((_destination - transform.position).sqrMagnitude > 0.0001f) return false;
                _moving = false;
                // 조작은 다음 프레임에 수행하여 접근 직후 최신 검증을 한 번 더 거친다.
                return false;
            }
            if (_action.Verb == AgentVocabulary.Press ||
                (_action.Verb == AgentVocabulary.MoveTo && _button != null && _button.approachPoint != null))
            {
                if (_button == null || _target.Button != _button || _button.approachPoint == null ||
                    Vector3.Distance(transform.position, _button.approachPoint.position) > _button.interactionDistance)
                { message = "버튼 접근 지점의 조작 거리 안에 도착하지 못했습니다."; return true; }
            }
            else if (_target != null && _action.Verb != AgentVocabulary.Drop)
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
                case AgentVocabulary.Press:
                    string buttonName = _target.displayName;
                    message = _button.TryPress();
                    if (message != null) { Debug.LogWarning("[VoiceAI] " + message, this); return true; }
                    message = buttonName + " 버튼 이벤트를 실행했습니다.";
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
