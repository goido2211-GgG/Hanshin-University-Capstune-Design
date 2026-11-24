using UnityEngine;

/// <summary>
/// ★ 새 파일: "의자에 앉아 있을 때만 드론으로 전환할 수 있다"는 규칙.
///
/// 사용법
/// 1. 빈 오브젝트(예: DroneAccessPolicy)를 만들고 이 스크립트만 붙인다.
/// 2. ModeSwitcher 오브젝트의 Control Mode Switcher → Drone Access Policy 칸에 그 오브젝트를 끌어다 놓는다.
/// 3. 같은 컴포넌트의 Laptop 칸은 비워 둔다. (노트북 거리 조건과 겹치지 않게)
///
/// 동작
/// - Tab을 누르면 ControlModeSwitcher가 이 스크립트의 CanEnterDrone()을 불러 "드론으로 가도 돼?"를 묻는다.
/// - 플레이어가 앉아 있으면(PlayerController.IsSeated) 허락, 아니면 거절하고 이유를 돌려준다.
/// - 드론 → 사람으로 돌아가는 건 이 규칙과 상관없이 항상 된다. (ControlModeSwitcher가 그렇게 만들어져 있음)
///
/// IDroneAccessPolicy는 팀원이 ControlModeSwitcher.cs 안에 만들어 둔 "드론 진입 규칙" 약속(인터페이스)이다.
/// 이 약속을 지키는 스크립트라면 무엇이든 Drone Access Policy 칸에 넣을 수 있어서,
/// ControlModeSwitcher.cs는 한 줄도 고치지 않아도 된다.
/// </summary>
public class SeatedDroneAccess : MonoBehaviour, IDroneAccessPolicy   // ", IDroneAccessPolicy" = 드론 진입 규칙 이름표
{
    [Tooltip("드론 진입을 거절할 때 돌려줄 문구. 팀원의 음성/채팅 UI가 연결되어 있으면 화면에 이 문구가 뜬다.")]
    [SerializeField] private string deniedMessage = "의자에 앉아 있을 때만 드론을 조종할 수 있어요.";

    /// <summary>
    /// IDroneAccessPolicy 약속에 따라 만든 함수. Tab을 누를 때 ControlModeSwitcher가 부른다.
    /// 반환값: true = 드론으로 가도 됨, false = 안 됨
    /// out string reason: 안 될 때 그 이유. (ControlModeSwitcher.LastDeniedReason에 저장된다)
    /// </summary>
    public bool CanEnterDrone(PlayerController player, out string reason)
    {
        // player가 있고(null 아님) 앉아 있으면 허락
        if (player != null && player.IsSeated)
        {
            reason = "";      // out 변수는 함수가 끝나기 전에 반드시 값을 채워야 해서, 허락할 때도 빈 문자열을 넣는다
            return true;
        }

        // 그 외에는 거절
        reason = deniedMessage;
        return false;
    }
}
