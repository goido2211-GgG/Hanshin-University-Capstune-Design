using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// AI가 보낸 행동을 실제 움직임으로 실행하는 시작용 요원 컨트롤러. 요원 오브젝트에 붙인다.
    /// 물리와 길 찾기 없이 직선으로 움직이므로, 실제 요원을 만들 때 NavMeshAgent 등으로 교체한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AgentBodyController : MonoBehaviour
    {
        [Tooltip("비워 두면 씬에서 자동으로 찾는다.")]
        public VoiceAgent voiceAgent;
        public float moveSpeed = 3f;
        public float turnSpeed = 540f;
        [Tooltip("1걸음의 거리")]
        public float stepLength = 1f;
        [Tooltip("오브젝트 앞에서 멈추는 거리")]
        public float stopDistance = 1.2f;
        [Tooltip("집은 물건을 머리 위 얼마나 높이 들지")]
        public float carryHeight = 1f;

        private SceneStageContext _stage;

        private void Awake()
        {
            if (voiceAgent == null) voiceAgent = FindAnyObjectByType<VoiceAgent>();
            if (voiceAgent == null)
            {
                Debug.LogError("[VoiceAI] 씬에서 VoiceAgent를 찾을 수 없습니다.", this);
                return;
            }
            _stage = voiceAgent.GetComponent<SceneStageContext>();
        }

        private void OnEnable()
        {
            if (voiceAgent != null) voiceAgent.TurnCompleted += HandleTurn;
        }

        private void OnDisable()
        {
            if (voiceAgent != null) voiceAgent.TurnCompleted -= HandleTurn;
        }

        // 새 명령이 오면 진행 중인 행동을 멈추고 새 행동을 시작한다.
        private void HandleTurn(AgentTurnResult turn)
        {
            if (turn.Actions.Count == 0) return;
            StopAllCoroutines();
            StartCoroutine(RunActions(turn.Actions));
        }

        private IEnumerator RunActions(List<AgentAction> actions)
        {
            foreach (AgentAction action in actions)
            {
                yield return Execute(action);
            }
        }

        private IEnumerator Execute(AgentAction action)
        {
            switch (action.Verb)
            {
                case AgentVocabulary.MoveDirection:
                    yield return MoveInDirection(action.Direction, action.Steps ?? 1);
                    break;
                case AgentVocabulary.MoveTo:
                    yield return WalkToTarget(action.Target, true);
                    break;
                case AgentVocabulary.Inspect:
                    yield return WalkToTarget(action.Target, false);
                    AgentInteractable inspected = Find(action.Target);
                    if (inspected != null) Report(inspected.displayName + "을(를) 살펴봤다: " + inspected.description);
                    break;
                case AgentVocabulary.PickUp:
                    yield return WalkToTarget(action.Target, false);
                    PickUp(Find(action.Target));
                    break;
                case AgentVocabulary.Drop:
                    Drop(Find(action.Target));
                    break;
                case AgentVocabulary.Wait:
                    yield return new WaitForSeconds(1f);
                    break;
                case AgentVocabulary.Stop:
                    break;
                default:
                    // open, press, use 등은 스테이지 기믹에 맞춰 여기에 추가한다.
                    Debug.Log("[VoiceAI] 아직 구현되지 않은 행동: " + action);
                    Report(action.Verb + " 행동을 시도했지만 아무 일도 일어나지 않았다.");
                    break;
            }
        }

        private IEnumerator MoveInDirection(string direction, int steps)
        {
            Vector3 axis;
            switch (direction)
            {
                case "back": axis = -transform.forward; break;
                case "left": axis = -transform.right; break;
                case "right": axis = transform.right; break;
                default: axis = transform.forward; break;
            }

            yield return MoveTo(transform.position + axis * (stepLength * steps), false);
            SetLocation("이동한 곳");
            Report("요원이 " + direction + " 방향으로 " + steps + "걸음 이동했다.");
        }

        private IEnumerator WalkToTarget(string id, bool reportArrival)
        {
            AgentInteractable target = Find(id);
            if (target == null)
            {
                Debug.LogWarning("[VoiceAI] 씬에 없는 대상: " + id, this);
                Report(id + "을(를) 찾지 못했다.");
                yield break;
            }

            Vector3 toTarget = target.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.magnitude > stopDistance)
            {
                yield return MoveTo(target.transform.position - toTarget.normalized * stopDistance, true);
            }

            SetLocation(target.displayName + " 앞");
            if (reportArrival) Report("요원이 " + target.displayName + " 앞에 도착했다.");
        }

        private IEnumerator MoveTo(Vector3 destination, bool faceDirection)
        {
            destination.y = transform.position.y;
            while ((destination - transform.position).sqrMagnitude > 0.0001f)
            {
                if (faceDirection)
                {
                    Quaternion look = Quaternion.LookRotation(destination - transform.position);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
                }
                transform.position = Vector3.MoveTowards(transform.position, destination, moveSpeed * Time.deltaTime);
                yield return null;
            }
        }

        private void PickUp(AgentInteractable target)
        {
            if (target == null || _stage.inventory.Contains(target.id)) return;
            target.transform.SetParent(transform, true);
            target.transform.localPosition = Vector3.up * carryHeight;
            target.state = "요원이 들고 있음";
            _stage.inventory.Add(target.id);
            Report("요원이 " + target.displayName + "을(를) 집었다.");
        }

        private void Drop(AgentInteractable target)
        {
            if (target == null || !_stage.inventory.Contains(target.id)) return;
            target.transform.SetParent(null, true);
            target.transform.position = transform.position + transform.forward * stopDistance;
            target.state = "바닥에 있음";
            _stage.inventory.Remove(target.id);
            Report("요원이 " + target.displayName + "을(를) 내려놓았다.");
        }

        private static AgentInteractable Find(string id)
        {
            foreach (AgentInteractable item in AgentInteractable.Active)
            {
                if (item.id == id) return item;
            }
            return null;
        }

        private void SetLocation(string location)
        {
            if (_stage != null) _stage.agentLocation = location;
        }

        private void Report(string description)
        {
            voiceAgent.ReportEvent(description);
        }
    }
}
