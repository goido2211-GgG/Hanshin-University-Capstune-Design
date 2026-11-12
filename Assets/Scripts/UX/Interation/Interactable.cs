using UnityEngine;
using UnityEngine.Events;   // UnityEvent(Inspector에서 함수를 연결하는 칸)를 쓰기 위해 필요

/// <summary>
/// "이 물체는 F로 상호작용할 수 있다"는 표시이자 설정.
/// 상호작용할 물체(책상, 문, 상자 등)마다 이 컴포넌트를 붙인다.
///
/// 사용법
/// 1. 물체에 Collider가 있는지 확인한다. (조준할 때 Collider를 기준으로 찾음)
/// 2. Add Component → Interactable
/// 3. Prompt Text: 화면에 뜰 안내 문구 (예: "앉기" → 화면에는 "[F] 앉기")
/// 4. Usable By: 누가 쓸 수 있는지 (플레이어만 / 드론만 / 둘 다)
/// 5. On Interact (): F를 눌렀을 때 실행할 일. 버튼의 On Click ()과 같은 방식으로 연결한다.
/// </summary>
public class Interactable : MonoBehaviour
{
    // 누가 이 물체를 쓸 수 있는지 고르는 선택지.
    // enum(열거형)은 "정해진 몇 가지 중 하나"를 나타내는 자료형이다.
    // Inspector에서는 드롭다운 메뉴로 보인다.
    public enum Users
    {
        PlayerOnly,   // 플레이어만
        DroneOnly,    // 드론만
        Both          // 둘 다
    }

    [Tooltip("화면에 '[F] ○○' 형태로 뜰 문구. 예: 앉기, 살펴보기, 열기")]
    [SerializeField] private string promptText = "상호작용";

    [Tooltip("이 물체를 쓸 수 있는 쪽")]
    [SerializeField] private Users usableBy = Users.Both;

    [Tooltip("F를 눌렀을 때 실행할 일. + 를 눌러 오브젝트와 함수를 연결한다.")]
    [SerializeField] private UnityEvent onInteract;

    // 다른 스크립트(Interactor)가 안내 문구를 읽을 수 있게 열어 둔 통로.
    // "=>"는 "이 값을 돌려준다"는 짧은 표기다. 읽기만 가능하고 바꿀 수는 없다.
    public string PromptText => promptText;

    /// <summary>
    /// 이 물체를 role(플레이어 또는 드론)이 쓸 수 있는지 알려준다.
    /// </summary>
    public bool CanBeUsedBy(InteractorRole role)
    {
        // 이 컴포넌트를 Inspector에서 꺼 두었다면(체크 해제) 아무도 못 쓴다.
        // 나중에 "이미 연 상자"처럼 잠깐 상호작용을 막고 싶을 때 enabled = false로 끄면 된다.
        if (!enabled) return false;

        // switch: usableBy 값에 따라 다른 결과를 돌려준다.
        switch (usableBy)
        {
            case Users.PlayerOnly: return role == InteractorRole.Player;
            case Users.DroneOnly:  return role == InteractorRole.Drone;
            default:               return true;   // Both
        }
    }

    /// <summary>
    /// F를 눌렀을 때 Interactor가 부르는 함수.
    /// Inspector의 On Interact ()에 연결해 둔 일들을 차례로 실행한다.
    /// </summary>
    public void Interact()
    {
        // Console에 기록을 남겨서 상호작용이 실제로 일어났는지 확인할 수 있게 한다.
        // name은 이 오브젝트의 이름(Hierarchy에 보이는 이름)이다.
        Debug.Log($"[Interactable] {name} 상호작용");

        // Invoke(): 연결된 함수들을 모두 실행한다.
        onInteract.Invoke();
    }
}
