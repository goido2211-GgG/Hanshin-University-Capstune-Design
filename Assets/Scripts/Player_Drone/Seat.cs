using UnityEngine;

/// <summary>
/// ★ 새 파일: 앉을 수 있는 의자.
/// Interactable이 붙은 의자 오브젝트에 함께 붙인다.
///
/// 사용법
/// 1. 의자에 Interactable(Prompt Text: 앉기, Usable By: Player Only)과 이 Seat을 붙인다.
/// 2. 의자의 자식으로 빈 오브젝트(SeatPoint)를 만들어,
///    위치 = 앉았을 때 플레이어 발이 놓일 자리, 파란 화살표(Z축) = 앉았을 때 바라볼 방향으로 맞춘다.
/// 3. Seat의 Seat Point 칸에 SeatPoint를 넣는다. (Player 칸은 비워 두면 자동으로 찾는다)
/// 4. On Interact () 칸에는 아무것도 연결하지 않아도 된다.
///    F를 누르면 Interactable이 이 스크립트를 자동으로 찾아 OnInteract()를 불러 준다.
///
/// 플레이어가 앉아 있는 동안에는 이 의자의 Interactable을 꺼서 "[F] 앉기"와 외곽선이 뜨지 않게 한다.
/// </summary>
[RequireComponent(typeof(Interactable))]   // 이 스크립트를 붙이면 같은 오브젝트에 Interactable도 꼭 있어야 한다
public class Seat : MonoBehaviour, IInteractionReceiver   // ", IInteractionReceiver" = F 반응 이름표를 단다
{
    [Tooltip("앉힐 플레이어. 비워 두면 씬에서 PlayerController를 자동으로 찾는다.")]
    [SerializeField] private PlayerController player;

    [Tooltip("앉을 자리. 위치 = 플레이어 발 위치, 파란 화살표(Z축) = 바라볼 방향. 비워 두면 의자 자체 위치를 쓴다.")]
    [SerializeField] private Transform seatPoint;

    private Interactable interactable;   // 같은 오브젝트에 붙은 Interactable (앉아 있는 동안 끄고 켤 대상)

    private void Awake()
    {
        // GetComponent<T>(): 같은 오브젝트에 붙은 T 컴포넌트 하나를 찾는다.
        interactable = GetComponent<Interactable>();

        // 앉을 자리를 안 넣었으면 의자 자체 위치를 쓴다.
        if (seatPoint == null) seatPoint = transform;

        // 플레이어 칸이 비어 있으면 씬에서 찾는다.
        // FindFirstObjectByType<T>(): 씬 전체에서 T 컴포넌트를 하나 찾는다. (느린 편이라 Awake에서 한 번만)
        if (player == null) player = FindFirstObjectByType<PlayerController>();
    }

    private void OnEnable()
    {
        // += : 플레이어의 "앉음/일어남" 알림을 구독한다. 알림이 오면 OnSeatedChanged가 불린다.
        if (player != null) player.SeatedChanged += OnSeatedChanged;
    }

    private void OnDisable()
    {
        // -= : 구독 해제. 의자가 꺼지거나 사라질 때 꼭 풀어 줘야 사라진 의자로 알림이 가지 않는다.
        // (구독 += 와 해제 -= 는 항상 짝으로 쓴다)
        if (player != null) player.SeatedChanged -= OnSeatedChanged;
    }

    /// <summary>
    /// IInteractionReceiver 약속에 따라 만든 함수. F가 눌리면 Interactable이 불러 준다.
    /// </summary>
    public void OnInteract(InteractorRole who)
    {
        // 사람만 앉을 수 있다. (Interactable을 Player Only로 해 두면 드론은 애초에 여기까지 오지 않지만, 한 번 더 확인)
        if (who != InteractorRole.Player) return;

        if (player == null)
        {
            Debug.LogWarning("[Seat] 앉힐 플레이어를 찾지 못했습니다. Seat의 Player 칸을 확인해 주세요.", this);
            return;
        }

        player.SitDown(seatPoint);
    }

    /// <summary>플레이어가 앉거나 일어날 때 불린다. seated: true = 앉음, false = 일어남</summary>
    private void OnSeatedChanged(bool seated)
    {
        // 앉아 있으면 의자 상호작용을 끄고(안내 문구·외곽선 안 뜸), 일어나면 다시 켠다.
        // !seated : seated의 반대 (true → false, false → true)
        interactable.enabled = !seated;
    }
}
