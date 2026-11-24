/// <summary>
/// ★ 새 파일: "F 상호작용에 반응하는 스크립트"라는 이름표(인터페이스).
///
/// 인터페이스는 "이 함수를 꼭 가지고 있겠다"는 약속이다.
/// 이 약속을 지키는 스크립트를 Interactable과 같은 오브젝트에 붙여 두면,
/// F가 눌렸을 때 Interactable이 그 스크립트를 자동으로 찾아서 OnInteract()를 불러 준다.
///
/// 예) 의자: Interactable + Seat(이 약속을 지킴) → F 누르면 Seat.OnInteract() 실행 → 앉기
///     문:   Interactable + Door(이 약속을 지킴) → F 누르면 Door.OnInteract() 실행 → 문 열기
///
/// 그래서 물체 종류가 늘어나도 Interactable이나 Interactor는 고칠 필요가 없다.
/// 새 물체용 스크립트를 만들 때 이 이름표만 달면 된다.
/// </summary>
public interface IInteractionReceiver
{
    /// <summary>
    /// F가 눌렸을 때 불리는 함수.
    /// who: 누가 눌렀는지 (InteractorRole.Player = 사람, InteractorRole.Drone = 드론)
    /// </summary>
    void OnInteract(InteractorRole who);
}
