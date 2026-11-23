using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// StarterAssetsThirdPerson 전용 표현 계층. Body의 실제 수평 변위를 관찰하며 Transform은 쓰지 않는다.
    /// Controller의 Idle/Walk/Run 임계값은 0/2/6, MotionSpeed는 locomotion 상태의 속도 배수다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RobotAnimatorBridge : MonoBehaviour
    {
        public AgentBodyController body;
        [Tooltip("StarterAssetsThirdPerson Controller가 연결된 KyleRobot의 Animator를 지정하세요.")]
        public Animator targetAnimator;
        [Min(0.01f), Tooltip("걷기 클립 1배속에 대응할 월드 이동 속도. 2는 초기 보정값입니다.")]
        public float walkReferenceSpeed = 2f;
        [Min(0f), Tooltip("Idle에서 Walk로 가중치를 올리는 시간. 정지/취소 시에는 즉시 0으로 초기화합니다.")]
        public float walkBlendSeconds = 0.12f;
        [Min(0f)] public float stationaryThreshold = 0.01f;
        public float MeasuredSpeed { get; private set; }

        private const float WalkThreshold = 2f;
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int MotionSpeed = Animator.StringToHash("MotionSpeed");
        private static readonly int Grounded = Animator.StringToHash("Grounded");
        private static readonly int Jump = Animator.StringToHash("Jump");
        private static readonly int FreeFall = Animator.StringToHash("FreeFall");
        private AgentBodyController _subscribedBody;
        private Animator _checkedAnimator;
        private RuntimeAnimatorController _checkedController;
        private bool _validParameters;
        private bool _warned;
        private Vector3 _previousPosition;
        private float _blend;

        private void OnEnable()
        {
            if (body == null) body = GetComponent<AgentBodyController>();
            _subscribedBody = body;
            if (_subscribedBody != null)
            {
                _previousPosition = _subscribedBody.transform.position;
                _subscribedBody.MotionUpdated += OnMotionUpdated;
            }
            ForceIdle();
        }

        private void OnDisable()
        {
            if (_subscribedBody != null) _subscribedBody.MotionUpdated -= OnMotionUpdated;
            ForceIdle();
            _subscribedBody = null;
        }

        private void LateUpdate()
        {
            // Body 비활성화·대기·timeScale=0에서 마지막 걷기 값이 남지 않게 한다.
            if (_subscribedBody == null || !_subscribedBody.IsMoving || Time.deltaTime <= 0f) ForceIdle();
        }

        private void OnMotionUpdated(float deltaTime)
        {
            if (!isActiveAndEnabled || _subscribedBody == null) return;
            Vector3 position = _subscribedBody.transform.position;
            Vector3 displacement = position - _previousPosition;
            _previousPosition = position;
            if (deltaTime <= 0f || !_subscribedBody.IsMoving) { ForceIdle(); return; }
            displacement.y = 0f;
            MeasuredSpeed = displacement.magnitude / deltaTime;
            if (float.IsNaN(MeasuredSpeed) || float.IsInfinity(MeasuredSpeed) ||
                MeasuredSpeed <= Mathf.Max(0f, stationaryThreshold)) { ForceIdle(); return; }
            if (!CanWrite()) return;

            _blend = walkBlendSeconds > 0f
                ? Mathf.MoveTowards(_blend, WalkThreshold, WalkThreshold * deltaTime / walkBlendSeconds)
                : WalkThreshold;
            // 실제 속도가 3, 6이어도 Run 임계값 6으로 올리지 않는다. Walk의 재생 배수만 보정한다.
            targetAnimator.SetFloat(Speed, _blend);
            targetAnimator.SetFloat(MotionSpeed, Mathf.Clamp(MeasuredSpeed / Mathf.Max(0.01f, walkReferenceSpeed), 0.1f, 3f));
            SetGroundLocomotion();
        }

        private void ForceIdle()
        {
            MeasuredSpeed = 0f;
            _blend = 0f;
            if (_subscribedBody != null) _previousPosition = _subscribedBody.transform.position;
            if (!CanWrite()) return;
            // damping으로 잔여 걷기를 남기지 않는다. Animator.speed=0은 Idle까지 얼리므로 사용하지 않는다.
            targetAnimator.SetFloat(Speed, 0f);
            targetAnimator.SetFloat(MotionSpeed, 1f);
            SetGroundLocomotion();
        }

        private void SetGroundLocomotion()
        {
            targetAnimator.SetBool(Grounded, true);
            targetAnimator.SetBool(Jump, false);
            targetAnimator.SetBool(FreeFall, false);
        }

        private bool CanWrite()
        {
            if (targetAnimator == null) return false;
            // 실행 중에만 적용한다. 원본 에셋·씬을 쓰거나 위치를 움직이지 않는다.
            if (targetAnimator.applyRootMotion) targetAnimator.applyRootMotion = false;
            var controller = targetAnimator.runtimeAnimatorController;
            if (_checkedAnimator != targetAnimator || _checkedController != controller)
            {
                _checkedAnimator = targetAnimator;
                _checkedController = controller;
                _warned = false;
                _validParameters = controller != null &&
                    HasParameter(Speed, AnimatorControllerParameterType.Float) &&
                    HasParameter(MotionSpeed, AnimatorControllerParameterType.Float) &&
                    HasParameter(Grounded, AnimatorControllerParameterType.Bool) &&
                    HasParameter(Jump, AnimatorControllerParameterType.Bool) &&
                    HasParameter(FreeFall, AnimatorControllerParameterType.Bool);
            }
            if (!_validParameters && !_warned)
            {
                _warned = true;
                Debug.LogWarning("[VoiceAI] Kyle Animator에 Speed/MotionSpeed(Float), Grounded/Jump/FreeFall(Bool)이 필요합니다. Controller와 참조를 확인하세요.", this);
            }
            return _validParameters && targetAnimator.isActiveAndEnabled;
        }

        private bool HasParameter(int hash, AnimatorControllerParameterType type)
        {
            foreach (var parameter in targetAnimator.parameters)
                if (parameter.nameHash == hash && parameter.type == type) return true;
            return false;
        }
    }
}
