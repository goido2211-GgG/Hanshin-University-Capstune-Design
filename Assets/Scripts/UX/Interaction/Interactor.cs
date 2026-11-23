using System.Collections.Generic; // ★ List<T>(크기가 늘었다 줄었다 하는 목록)를 쓰기 위해 필요
using TMPro;                    // 안내 문구(TextMeshPro 텍스트)를 바꾸기 위해 필요
using UnityEngine;
using UnityEngine.InputSystem;  // 새 Input System (Keyboard.current)
using UnityEngine.Rendering;    // ★ ShadowCastingMode(그림자 설정)를 쓰기 위해 필요

/// <summary>
/// 상호작용을 "하는 쪽". 상호작용하는 주체를 나타내는 선택지.
/// Interactable도 이 값을 보고 "누가 쓰려고 하는지" 판단한다.
/// </summary>
public enum InteractorRole
{
    Player,
    Drone
}

/// <summary>
/// 상호작용을 하는 쪽(플레이어, 드론)에 붙이는 스크립트.
/// 매 프레임 카메라가 바라보는 방향 근처에서 Interactable을 찾고,
/// 찾으면 화면에 "[F] ○○"를 띄우며, F를 누르면 그 물체의 Interact()를 부른다.
///
/// 조준 방식(조준 보정)
/// - 화면 정가운데에 정확히 맞추지 않아도, 가운데에서 maxAngle도 안쪽에 있으면 고른다.
/// - 여러 개가 있으면 가운데에 가장 가까운(각도가 가장 작은) 것을 고른다.
/// - 벽 같은 다른 물체에 가려져 있으면 고르지 않는다.
///
/// 언제 동작하나?
/// - 이 오브젝트의 카메라가 켜져 있을 때만 동작한다.
///   (ControlModeSwitcher가 Tab으로 카메라를 바꾸므로, 지금 조작 중인 쪽만 동작하게 된다)
/// - 일시정지 중, 채팅 입력 중에는 동작하지 않는다.
///
/// ★ 외곽선 하이라이트
/// - 조준 중인 물체(current)의 메시를 외곽선 머티리얼로 "매 프레임 한 번 더" 그린다. (LateUpdate)
/// - 물체의 원래 머티리얼·렌더러는 전혀 바꾸지 않는다.
/// - current가 null이 되면 그리기를 멈추므로 다음 프레임에 외곽선이 저절로 사라진다.
///   (대상 변경, 일시정지, 채팅, 플레이어↔드론 전환 모두 current가 바뀌거나 null이 되는 흐름을 탄다)
/// </summary>
public class Interactor : MonoBehaviour
{
    [Header("누가 상호작용하나")]
    [Tooltip("플레이어에 붙이면 Player, 드론에 붙이면 Drone")]
    [SerializeField] private InteractorRole role = InteractorRole.Player;

    [Tooltip("이 쪽의 카메라. 플레이어면 플레이어 카메라, 드론이면 드론 카메라")]
    [SerializeField] private Camera viewCamera;

    [Header("조준")]
    [Tooltip("상호작용할 수 있는 최대 거리 (미터)")]
    [SerializeField] private float range = 2.5f;

    [Tooltip("화면 가운데에서 이 각도(도) 안쪽에 있으면 조준한 것으로 본다. 클수록 대충 봐도 잡힌다.")]
    [SerializeField] private float maxAngle = 15f;

    [Tooltip("검사할 레이어. 기본값 Everything이면 모든 물체를 검사한다.")]
    [SerializeField] private LayerMask hitLayers = ~0;   // ~0 은 "모든 레이어"라는 뜻

    [Header("UI")]
    [Tooltip("화면에 '[F] ○○'를 띄울 TextMeshPro 텍스트. 플레이어와 드론이 같은 것을 함께 쓴다.")]
    [SerializeField] private TMP_Text promptLabel;

    [Tooltip("(선택) 문구 뒤의 배경 상자. 넣으면 글자 대신 이 상자를 켜고 끈다. 글자는 이 상자의 자식이어야 한다.")]
    [SerializeField] private GameObject promptBox;

    [Header("다른 시스템 (선택)")]
    [Tooltip("Hierarchy의 ModeSwitcher 오브젝트. 채팅 입력 중인지 확인하는 데 쓴다. 비워 두면 채팅 확인을 건너뛴다.")]
    [SerializeField] private ControlModeSwitcher modeSwitcher;

    // ★ 외곽선 설정 칸
    [Header("외곽선 하이라이트")]
    [Tooltip("PS/Outline_InvertedHull 셰이더로 만든 머티리얼. 비워 두면 외곽선을 그리지 않는다.")]
    [SerializeField] private Material outlineMaterial;

    [Tooltip("외곽선 색")]
    [SerializeField] private Color outlineColor = new Color(1f, 0.85f, 0.2f, 1f);   // 노란색

    // Range(최소, 최대): Inspector에서 슬라이더로 보인다.
    [Tooltip("외곽선 두께 (미터). 0.01 = 1cm. 가까이 가면 더 두꺼워 보인다.")]
    [SerializeField, Range(0f, 0.05f)] private float outlineWidth = 0.01f;

    // ★ 외곽선을 그릴 "조각" 하나. 메시(모양)와 렌더러(켜져 있는지 확인용)를 한 쌍으로 묶는다.
    // struct(구조체)는 값 몇 개를 묶어 두는 작은 상자라고 생각하면 된다.
    private struct OutlinePart
    {
        public MeshFilter filter;      // 모양(메시)을 가진 컴포넌트
        public MeshRenderer renderer;  // 실제로 화면에 그리는 컴포넌트
    }

    // ★ 지금 조준 중인 물체(와 그 자식들)의 조각 목록. 대상이 바뀔 때만 다시 모은다.
    private readonly List<OutlinePart> outlineParts = new List<OutlinePart>();

    // ★ 머티리얼 에셋을 바꾸지 않고 색·두께만 "이번 그리기에만" 덮어쓰는 상자.
    private MaterialPropertyBlock outlineProps;

    // ★ 셰이더 속성 이름을 숫자 ID로 바꿔 둔 것. (매번 문자열로 찾는 것보다 빠름)
    //    static readonly: 모든 Interactor가 같이 쓰고, 한 번 정하면 바뀌지 않는 값
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");

    // 실제로 켜고 끌 대상.
    // 배경 상자를 넣었으면 상자를, 안 넣었으면 글자 오브젝트를 돌려준다.
    // "조건 ? A : B" 는 조건이 참이면 A, 거짓이면 B를 돌려주는 짧은 if 문(삼항 연산자)이다.
    // 상자를 끄면 자식인 글자도 같이 꺼지므로, 상자가 있으면 상자만 켜고 끄면 된다.
    private GameObject PromptObject => promptBox != null ? promptBox : promptLabel.gameObject;

    // 지금 조준 중인 물체. 없으면 null.
    private Interactable current;

    // 지난 프레임에 이 Interactor가 동작 중이었는지. (꺼지는 순간을 알아채기 위해)
    private bool wasActive;

    // 안내 문구는 플레이어와 드론이 하나를 같이 쓴다.
    // static이라 모든 Interactor가 이 값 하나를 공유한다.
    // "지금 문구를 띄운 주인"을 기록해서, 주인이 아닌 쪽이 실수로 문구를 끄지 못하게 한다.
    private static Interactor promptOwner;

    private void Awake()
    {
        // ★ MaterialPropertyBlock은 new로 한 번만 만들어 두고 계속 재사용한다.
        outlineProps = new MaterialPropertyBlock();
    }

    private void Start()
    {
        // 시작할 때는 안내 문구(와 배경 상자)를 숨겨 둔다.
        if (promptLabel != null) PromptObject.SetActive(false);
    }

    private void Update()
    {
        // 1. 지금 이 Interactor가 동작해야 하는지 확인
        if (!IsActive())
        {
            // 방금 전까지 동작하다가 이번에 꺼졌다면, 조준 대상을 비우고 문구를 끈다.
            if (wasActive) SetTarget(null);
            wasActive = false;
            return;   // 조기 반환: 아래 코드는 실행하지 않음
        }
        wasActive = true;

        // 2. 바라보는 방향 근처에서 상호작용할 물체를 찾고, 안내 문구를 갱신
        SetTarget(FindTarget());

        // 3. 대상이 있고 F가 이번 프레임에 눌렸으면 상호작용 실행
        Keyboard keyboard = Keyboard.current;
        if (current != null && keyboard != null && keyboard.fKey.wasPressedThisFrame)
        {
            current.Interact();
        }
    }

    /// <summary>
    /// 지금 이 Interactor가 동작해야 하면 true.
    /// </summary>
    private bool IsActive()
    {
        if (PauseMenu.IsPaused) return false;                                     // 일시정지 중이면 X
        if (modeSwitcher != null && modeSwitcher.InputSuppressed) return false;   // 채팅 입력 중이면 X
        if (viewCamera == null) return false;                                     // 카메라 칸이 비어 있으면 X
        return viewCamera.enabled;                                                // 내 카메라가 켜져 있을 때만 O
    }

    /// <summary>
    /// 카메라 주변에서 "가장 화면 가운데에 가까운" 상호작용 물체를 찾는다. 없으면 null.
    /// </summary>
    private Interactable FindTarget()
    {
        Transform cam = viewCamera.transform;

        // 1) 카메라 위치를 중심으로 반지름 range 안에 있는 Collider를 모두 모은다.
        //    QueryTriggerInteraction.Collide: "트리거(통과 가능한) Collider도 포함"
        Collider[] nearby = Physics.OverlapSphere(
            cam.position, range, hitLayers, QueryTriggerInteraction.Collide);

        Interactable best = null;        // 지금까지 찾은 것 중 가장 좋은 후보
        float bestAngle = maxAngle;      // 그 후보의 각도. 처음엔 허용 최대치로 시작

        // 2) 모은 Collider를 하나씩 검사한다.
        foreach (Collider col in nearby)
        {
            // Collider가 붙은 오브젝트나 그 부모에서 Interactable을 찾는다.
            // (물체가 여러 조각으로 되어 있어도 부모에 하나만 붙이면 되게 하려고 InParent를 씀)
            Interactable candidate = col.GetComponentInParent<Interactable>();

            // Interactable이 없거나, 나(플레이어/드론)는 못 쓰는 물체면 건너뛴다.
            if (candidate == null || !candidate.CanBeUsedBy(role)) continue;

            // 카메라에서 물체 중심까지의 방향
            Vector3 toTarget = col.bounds.center - cam.position;

            // 카메라 정면(화면 가운데)과 그 방향 사이의 각도.
            // 0도면 정확히 가운데, 클수록 화면 가장자리 쪽이다.
            float angle = Vector3.Angle(cam.forward, toTarget);

            // 지금 후보보다 각도가 크면(덜 가운데면) 건너뛴다.
            if (angle > bestAngle) continue;

            // 다른 물체(벽 등)에 가려져 있으면 건너뛴다.
            if (!HasLineOfSight(cam.position, toTarget, candidate)) continue;

            // 여기까지 오면 새 최고 후보
            best = candidate;
            bestAngle = angle;
        }

        return best;
    }

    /// <summary>
    /// 카메라에서 후보 물체까지 다른 물체에 가리지 않고 보이면 true.
    /// </summary>
    private bool HasLineOfSight(Vector3 from, Vector3 toTarget, Interactable candidate)
    {
        // Raycast: from에서 toTarget 방향으로 보이지 않는 광선을 쏴서, 처음 부딪힌 것을 hit에 담는다.
        // QueryTriggerInteraction.Ignore: 트리거 Collider는 가리는 물체로 치지 않는다.
        bool hitSomething = Physics.Raycast(
            from, toTarget.normalized, out RaycastHit hit,
            toTarget.magnitude, hitLayers, QueryTriggerInteraction.Ignore);

        // 아무것에도 안 부딪혔으면 가리는 게 없다는 뜻
        if (!hitSomething) return true;

        // 처음 부딪힌 게 바로 그 후보(또는 후보의 일부)면 보이는 것
        return hit.collider.GetComponentInParent<Interactable>() == candidate;
    }

    /// <summary>
    /// 조준 대상을 바꾸고, 바뀌었을 때만 안내 문구를 갱신한다.
    /// (매 프레임 글자를 새로 쓰면 TextMeshPro가 불필요하게 다시 그리므로, 바뀔 때만 바꿈)
    /// </summary>
    private void SetTarget(Interactable target)
    {
        if (target == current) return;   // 그대로면 할 일 없음
        current = target;

        // ★ 대상이 바뀌었으니 외곽선을 그릴 조각 목록도 새로 모은다. (null이면 목록만 비움)
        CollectOutlineParts(current);

        if (current == null) HidePrompt();
        else ShowPrompt("[F] " + current.PromptText);
    }

    /// <summary>
    /// ★ target 물체와 그 자식들에서 외곽선을 그릴 메시 조각을 모두 모아 outlineParts에 담는다.
    /// </summary>
    private void CollectOutlineParts(Interactable target)
    {
        outlineParts.Clear();            // 이전 물체의 목록은 버린다
        if (target == null) return;

        // GetComponentsInChildren: 자기 자신 + 모든 자식(손자 포함)에서 MeshFilter를 전부 찾는다.
        // → Chair1처럼 조각마다 MeshRenderer가 있어도 모두 잡힌다.
        foreach (MeshFilter filter in target.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null) continue;               // 메시가 비어 있으면 그릴 게 없음

            MeshRenderer meshRenderer = filter.GetComponent<MeshRenderer>();
            if (meshRenderer == null) continue;                    // 렌더러 없는 메시는 원래도 안 보이는 것

            // Static(Batching Static)이 켜진 물체는 Play 중에 메시가 합쳐져서 외곽선이 엉뚱하게 나온다.
            if (meshRenderer.isPartOfStaticBatch)
            {
                Debug.LogWarning($"[Interactor] {filter.name}은(는) Static이 켜져 있어 외곽선을 그릴 수 없습니다. " +
                                 "Inspector 오른쪽 위 Static 체크를 꺼 주세요.", filter);
                continue;
            }

            outlineParts.Add(new OutlinePart { filter = filter, renderer = meshRenderer });
        }
    }

    /// <summary>
    /// ★ LateUpdate: 모든 Update가 끝난 뒤 매 프레임 실행된다.
    /// 물체가 이번 프레임에 움직였어도 움직인 "뒤" 위치에 외곽선을 그리기 위해 Update가 아니라 여기서 그린다.
    /// </summary>
    private void LateUpdate()
    {
        // 조준 대상이 없거나, 머티리얼 칸이 비었거나, 그릴 조각이 없으면 아무것도 안 그린다.
        // → 이게 곧 "외곽선 끄기"다. 따로 지우는 코드가 필요 없다.
        if (current == null || outlineMaterial == null || outlineParts.Count == 0) return;

        // Inspector의 색·두께를 이번 그리기에 넘긴다. (머티리얼 에셋 자체는 그대로)
        outlineProps.SetColor(OutlineColorId, outlineColor);
        outlineProps.SetFloat(OutlineWidthId, outlineWidth);

        // RenderParams: "어떻게 그릴지" 설정 묶음
        RenderParams renderParams = new RenderParams(outlineMaterial)
        {
            camera = viewCamera,                        // 내 카메라에만 그린다 (다른 카메라엔 안 보임)
            matProps = outlineProps,                    // 위에서 정한 색·두께
            shadowCastingMode = ShadowCastingMode.Off,  // 외곽선이 그림자를 만들지 않게
            receiveShadows = false                      // 외곽선에 그림자가 드리우지 않게
        };

        foreach (OutlinePart part in outlineParts)
        {
            // 조각이 삭제됐거나, 꺼져 있거나, 오브젝트가 비활성이면 건너뛴다. (원래 안 보이는 조각)
            if (part.filter == null || part.renderer == null) continue;
            if (!part.renderer.enabled || !part.renderer.gameObject.activeInHierarchy) continue;

            // 조각과 같은 레이어로 그린다 → 카메라의 Culling Mask 설정을 똑같이 따른다.
            renderParams.layer = part.renderer.gameObject.layer;

            Mesh mesh = part.filter.sharedMesh;
            // localToWorldMatrix: 이 조각의 위치·회전·크기를 한 번에 담은 행렬
            Matrix4x4 matrix = part.filter.transform.localToWorldMatrix;

            // 서브메시: 한 메시 안에서 머티리얼 칸별로 나뉜 부분. 전부 그려야 모양 전체에 외곽선이 생긴다.
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                // 이번 프레임에 한 번만 그려지는 그리기 요청. 다음 프레임에 또 부르지 않으면 사라진다.
                Graphics.RenderMesh(renderParams, mesh, i, matrix);
            }
        }
    }

    private void ShowPrompt(string text)
    {
        if (promptLabel == null) return;
        promptOwner = this;                         // 이제 내가 문구의 주인
        promptLabel.text = text;
        PromptObject.SetActive(true);               // 상자(또는 글자)를 켠다
    }

    private void HidePrompt()
    {
        if (promptLabel == null) return;
        if (promptOwner != this) return;            // 내가 띄운 문구가 아니면 건드리지 않음
        promptOwner = null;
        PromptObject.SetActive(false);              // 상자(또는 글자)를 끈다
    }

    private void OnDisable()
    {
        // 이 컴포넌트나 오브젝트가 꺼질 때 문구가 화면에 남지 않게 정리
        current = null;
        outlineParts.Clear();   // ★ 외곽선 조각 목록도 비운다
        wasActive = false;
        HidePrompt();
    }
}