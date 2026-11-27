using System.Collections;
using AgentEscape.VoiceAI;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FirstMapContainmentController : MonoBehaviour
{
    [SerializeField] private CapsuleLift capsuleLift;
    [SerializeField] private Collider capsuleCollider;
    [SerializeField] private GameObject robotSystems;
    [SerializeField] private AgentBodyController robotBody;
    [SerializeField] private Animator robotAnimator;

    private bool releaseStarted;

    private void Awake()
    {
        if (capsuleLift == null || capsuleCollider == null || robotSystems == null || robotBody == null)
        {
            Debug.LogError("[FirstScene] 격리 캡슐과 AI 참조를 확인하세요.", this);
            enabled = false;
            return;
        }

        robotBody.enabled = false;
        if (robotAnimator != null) robotAnimator.enabled = false;
        robotSystems.SetActive(false);
    }

    public bool TryBeginRelease()
    {
        if (!isActiveAndEnabled || releaseStarted) return false;

        releaseStarted = true;
        StartCoroutine(ReleaseRobot());
        return true;
    }

    private IEnumerator ReleaseRobot()
    {
        capsuleLift.LiftCapsule();
        Debug.Log("[FirstScene] AI 전원 복구. 격리 캡슐 상승 중.", this);

        yield return new WaitUntil(() => capsuleLift.HasFinished);

        capsuleCollider.enabled = false;
        if (robotAnimator != null) robotAnimator.enabled = true;
        robotSystems.SetActive(true);
        robotBody.enabled = true;
        Debug.Log("[FirstScene] 격리 캡슐 해제. AI 시스템 재가동 완료.", this);
    }
}
