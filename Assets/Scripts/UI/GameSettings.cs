using UnityEngine;

/// <summary>
/// 게임 설정 값(마우스 감도, 마스터 볼륨, 마이크)을 보관하고 저장하는 곳.
///
/// - static 클래스라서 오브젝트에 붙이지 않는다. 어디서든 GameSettings.Sensitivity 처럼 바로 쓴다.
/// - 값은 PlayerPrefs(Unity가 제공하는 간단한 저장소)에 저장되어, 게임을 껐다 켜도 유지된다.
/// - 게임이 시작될 때(첫 씬이 열리기 전) 저장된 값을 자동으로 불러온다.
/// </summary>
public static class GameSettings
{
    // ───────── 저장할 때 쓰는 이름표(키) ─────────
    // PlayerPrefs는 "이름표 → 값" 형태로 저장한다. 오타가 나지 않게 상수로 한 번만 적어 둔다.
    private const string SensitivityKey = "Settings.Sensitivity";
    private const string VolumeKey      = "Settings.MasterVolume";
    private const string MicKey         = "Settings.MicrophoneDevice";

    // ───────── 범위와 기본값 ─────────
    // public const: 다른 스크립트(SettingsMenu)도 같은 숫자를 쓸 수 있게 열어 둔 고정값
    public const float MinSensitivity     = 0.2f;   // 감도 배율 최소 (기본의 0.2배)
    public const float MaxSensitivity     = 3f;     // 감도 배율 최대 (기본의 3배)
    public const float DefaultSensitivity = 1f;     // 처음 값: 1배 (지금 감도 그대로)
    public const float DefaultVolume      = 0.8f;   // 처음 볼륨: 80%

    // ───────── 현재 값 ─────────
    // 읽기는 누구나, 바꾸기는 이 클래스 안의 Set 함수로만 (실수 방지)

    /// <summary>마우스 감도 배율. 플레이어·드론의 기본 감도에 곱해진다.</summary>
    public static float Sensitivity { get; private set; } = DefaultSensitivity;

    /// <summary>마스터 볼륨 (0 = 무음, 1 = 최대)</summary>
    public static float MasterVolume { get; private set; } = DefaultVolume;

    /// <summary>선택한 마이크 이름. 아직 고른 적 없으면 빈 문자열.</summary>
    public static string MicrophoneDevice { get; private set; } = "";

    /// <summary>
    /// 게임이 시작될 때 Unity가 자동으로 한 번 불러 주는 함수.
    /// BeforeSceneLoad: 첫 씬의 오브젝트들이 깨어나기(Awake) 전에 실행된다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Load()
    {
        // GetFloat(키, 기본값): 저장된 값이 있으면 그 값, 없으면 기본값을 돌려준다.
        // Mathf.Clamp(값, 최소, 최대): 값이 범위를 벗어나면 범위 안으로 맞춘다. (저장값이 이상해도 안전하게)
        Sensitivity = Mathf.Clamp(
            PlayerPrefs.GetFloat(SensitivityKey, DefaultSensitivity), MinSensitivity, MaxSensitivity);

        // Clamp01: 0~1 사이로 맞춘다.
        MasterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumeKey, DefaultVolume));

        MicrophoneDevice = PlayerPrefs.GetString(MicKey, "");

        // 불러온 볼륨을 바로 게임 전체 소리에 적용
        AudioListener.volume = MasterVolume;
    }

    /// <summary>감도 배율을 바꾸고 저장 목록에 적는다.</summary>
    public static void SetSensitivity(float value)
    {
        Sensitivity = Mathf.Clamp(value, MinSensitivity, MaxSensitivity);
        PlayerPrefs.SetFloat(SensitivityKey, Sensitivity);
    }

    /// <summary>마스터 볼륨(0~1)을 바꾸고, 게임 전체 소리에 바로 적용한다.</summary>
    public static void SetMasterVolume(float value01)
    {
        MasterVolume = Mathf.Clamp01(value01);
        AudioListener.volume = MasterVolume;   // AudioListener.volume: 게임 전체 소리 크기 (0~1)
        PlayerPrefs.SetFloat(VolumeKey, MasterVolume);
    }

    /// <summary>사용할 마이크 이름을 바꾼다. (음성인식 담당이 이 값을 읽어서 쓰면 된다)</summary>
    public static void SetMicrophoneDevice(string deviceName)
    {
        // ?? : 왼쪽이 null이면 오른쪽 값을 쓴다. (null 대신 빈 문자열을 저장)
        MicrophoneDevice = deviceName ?? "";
        PlayerPrefs.SetString(MicKey, MicrophoneDevice);
    }

    /// <summary>
    /// 적어 둔 값을 실제 디스크에 저장한다.
    /// Set 함수들은 메모리에만 적어 두고, 이 함수를 불러야 확실하게 파일로 저장된다.
    /// (슬라이더를 끌 때마다 디스크에 쓰면 느려지니, 팝업을 닫을 때 한 번만 부른다)
    /// </summary>
    public static void Save()
    {
        PlayerPrefs.Save();
    }
}