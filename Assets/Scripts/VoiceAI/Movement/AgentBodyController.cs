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

        public AgentInteractable CurrentHeldObject
        {
            get
            {
                ReconcileCarry();
                return !_releaseCarryRequested && IsAttached(_carry) ? _carry.Target : null;
            }
        }

        internal AgentInteractable LastDroppedObject => _lastDropped != null ? _lastDropped : null;

        private AgentAction _action;
        private AgentInteractable _target;
        private CarryAttachment _carry;
        private AgentInteractable _lastDropped;
        private bool _changingCarry;
        private bool _releaseCarryRequested;
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
            ReconcileCarry();
            if (voiceAgent != null) voiceAgent.RegisterBody(this);
            else Debug.LogError("[VoiceAI] Body의 VoiceAgent를 Inspector에서 지정해 주세요.", this);
        }

        private void OnDisable()
        {
            _releaseCarryRequested = true;
            CancelCurrentAction();
            ReconcileCarry();
            if (voiceAgent != null) voiceAgent.UnregisterBody(this);
        }

        private void Update() => ReconcileCarry();

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
            string carryError = CheckCarryAction(action, _target);
            if (carryError != null) return carryError;
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

        internal string CheckCarryAction(AgentAction action, AgentInteractable target)
        {
            if (action == null || (action.Verb != AgentVocabulary.PickUp && action.Verb != AgentVocabulary.Drop))
                return null;
            if (_changingCarry) return "물체를 옮기는 중입니다.";
            if (!isActiveAndEnabled) return "로봇이 비활성 상태입니다.";

            var held = CurrentHeldObject;
            if (action.Verb == AgentVocabulary.Drop)
            {
                if (held == null) return "현재 들고 있는 물체가 없습니다.";
                if (held != target) return $"현재 들고 있는 물체는 {held.displayName}입니다.";
                return IsFinite(stopDistance) ? null : "내려놓을 거리가 올바르지 않습니다.";
            }

            if (held != null)
                return held == target ? "이미 이 물체를 들고 있습니다." : $"{held.displayName}부터 내려놓아 주세요.";
            if (_carry != null) return "이전 물체의 부착을 정리하는 중입니다.";
            if (target == null || !target.isActiveAndEnabled) return "집을 수 있는 대상이 없습니다.";
            if (target.transform == transform || transform.IsChildOf(target.transform))
                return "로봇 자신이나 부모는 집을 수 없습니다.";
            if (target.GetComponentInParent<AgentBodyController>() != null) return "이미 로봇에 부착된 물체입니다.";
            return IsFinite(carryHeight) ? null : "물체를 들 높이가 올바르지 않습니다.";
        }

        private bool IsAttached(CarryAttachment attachment) => attachment != null &&
            attachment.Target != null && attachment.Target.isActiveAndEnabled &&
            attachment.Root != null && attachment.Root.parent == transform;

        private void ReconcileCarry()
        {
            if (_changingCarry) return;
            var attachment = _carry;
            if (attachment == null)
            {
                _releaseCarryRequested = false;
                return;
            }
            if (!_releaseCarryRequested && IsAttached(attachment)) return;

            _changingCarry = true;
            try
            {
                if (attachment.Root != null && attachment.Root.parent == transform)
                {
                    // A parent GameObject may be in the middle of its own activation change.
                    // Restore physics now and finish detaching on enable if the hierarchy is inactive.
                    if (!gameObject.activeInHierarchy)
                    {
                        attachment.RestorePhysics();
                        return;
                    }
                    attachment.Root.SetParent(null, true);
                    if (attachment.Root != null && attachment.Root.parent == transform) return;
                }
                attachment.RestorePhysics();
                _carry = null;
                _releaseCarryRequested = false;
            }
            finally { _changingCarry = false; }
        }

        private string TryPickUp(AgentAction action, AgentInteractable target)
        {
            string error = CheckCarryAction(action, target);
            if (error != null) return error;

            var attachment = new CarryAttachment(target);
            var previousParent = attachment.Root.parent;
            Vector3 previousPosition = attachment.Root.position;
            _changingCarry = true;
            try
            {
                attachment.SuspendPhysics();
                attachment.Root.SetParent(transform, true);
                if (_releaseCarryRequested || !isActiveAndEnabled || !IsAttached(attachment))
                    return "물체를 로봇에 부착하지 못했습니다.";
                attachment.Root.localPosition = Vector3.up * carryHeight;
                _carry = attachment;
                return null;
            }
            finally
            {
                try
                {
                    if (_carry != attachment)
                    {
                        if (attachment.Root != null && attachment.Root.parent == transform)
                        {
                            attachment.Root.SetParent(previousParent, true);
                            if (attachment.Root != null) attachment.Root.position = previousPosition;
                        }
                        if (attachment.Root != null && attachment.Root.parent == transform)
                        {
                            _carry = attachment;
                            _releaseCarryRequested = true;
                        }
                        else attachment.RestorePhysics();
                    }
                }
                finally
                {
                    _changingCarry = false;
                    if (_releaseCarryRequested) ReconcileCarry();
                }
            }
        }

        private string TryDrop(AgentAction action, AgentInteractable target)
        {
            string error = CheckCarryAction(action, target);
            if (error != null) return error;

            var attachment = _carry;
            Physics.SyncTransforms();
            float bodyRadius = IsFinite(obstacleRadius) ? Mathf.Max(0.05f, obstacleRadius) : 0.05f;
            foreach (var collider in GetComponentsInChildren<Collider>(false))
                bodyRadius = Mathf.Max(bodyRadius, PlanarExtent(collider, transform.position));
            float clearance = bodyRadius + attachment.PlanarRadius + 0.05f;
            if (!IsFinite(clearance)) return "내려놓을 위치를 계산하지 못했습니다.";
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (!IsFinite(forward.sqrMagnitude) || forward.sqrMagnitude < 0.0001f)
                return "로봇의 수평 방향을 확인할 수 없습니다.";
            Vector3 dropPosition = transform.position + forward.normalized * Mathf.Max(clearance, stopDistance);
            _changingCarry = true;
            try
            {
                attachment.Root.SetParent(null, true);
                if (attachment.Root == null || attachment.Root.parent != null || target == null || !target.isActiveAndEnabled)
                    return "물체를 내려놓지 못했습니다.";
                attachment.Root.position = dropPosition;
                attachment.RestorePhysics();
                _carry = null;
                _lastDropped = target;
                return null;
            }
            finally
            {
                _changingCarry = false;
                ReconcileCarry();
            }
        }

        private static float PlanarExtent(Collider collider, Vector3 reference)
        {
            if (collider == null || !collider.enabled || collider.isTrigger) return 0f;
            var bounds = collider.bounds;
            if (bounds.extents.sqrMagnitude == 0f) return 0f;
            Vector3 offset = bounds.center - reference;
            float side = System.Math.Abs(offset.x) + bounds.extents.x;
            float depth = System.Math.Abs(offset.z) + bounds.extents.z;
            return new Vector3(side, 0f, depth).magnitude;
        }

        private sealed class CarryAttachment
        {
            internal readonly AgentInteractable Target;
            internal readonly Transform Root;
            internal readonly float PlanarRadius;
            private readonly (Rigidbody Body, bool WasKinematic)[] _bodies;
            private readonly (Collider Collider, bool WasEnabled)[] _colliders;
            private bool _physicsSuspended;

            internal CarryAttachment(AgentInteractable target)
            {
                Target = target;
                Root = target.transform;
                var bodies = target.GetComponentsInChildren<Rigidbody>(true);
                _bodies = new (Rigidbody, bool)[bodies.Length];
                for (int i = 0; i < bodies.Length; i++) _bodies[i] = (bodies[i], bodies[i].isKinematic);
                var colliders = target.GetComponentsInChildren<Collider>(true);
                _colliders = new (Collider, bool)[colliders.Length];
                Physics.SyncTransforms();
                for (int i = 0; i < colliders.Length; i++)
                {
                    _colliders[i] = (colliders[i], colliders[i].enabled);
                    PlanarRadius = Mathf.Max(PlanarRadius, PlanarExtent(colliders[i], Root.position));
                }
            }

            internal void SuspendPhysics()
            {
                _physicsSuspended = true;
                foreach (var entry in _bodies)
                    if (entry.Body != null && !entry.WasKinematic) entry.Body.isKinematic = true;
                foreach (var entry in _colliders)
                    if (entry.Collider != null && entry.WasEnabled) entry.Collider.enabled = false;
            }

            internal void RestorePhysics()
            {
                if (!_physicsSuspended) return;
                foreach (var entry in _bodies)
                    if (entry.Body != null) entry.Body.isKinematic = entry.WasKinematic;
                foreach (var entry in _colliders)
                    if (entry.Collider != null) entry.Collider.enabled = entry.WasEnabled;
                _physicsSuspended = false;
            }
        }

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
                    string pickedName = _target.displayName;
                    message = TryPickUp(_action, _target);
                    if (message != null) return true;
                    message = pickedName + "을(를) 집었습니다.";
                    break;
                case AgentVocabulary.Drop:
                    string droppedName = _target.displayName;
                    message = TryDrop(_action, _target);
                    if (message != null) return true;
                    message = droppedName + "을(를) 내려놓았습니다.";
                    break;
                default: message = "지원하지 않는 행동입니다."; return true;
            }
            success = true;
            return true;
        }
    }
}
