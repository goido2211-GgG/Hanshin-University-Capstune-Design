using System.Collections.Generic;   // List<string> (드롭다운 항목 목록)
using TMPro;                        // TMP_Text, TMP_Dropdown
using UnityEngine;
using UnityEngine.UI;               // Slider

/// <summary>
/// 설정 팝업의 UI(슬라이더, 값 글자, 드롭다운)를 GameSettings와 연결한다.
/// Setting_Popup 오브젝트에 붙인다.
///
/// - 팝업이 열릴 때마다(OnEnable) 저장된 값으로 UI를 맞춘다.
/// - 슬라이더나 드롭다운을 바꾸면 바로 GameSettings에 반영한다.
/// - 팝업이 닫힐 때(OnDisable) 디스크에 저장한다.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    [Header("마우스 감도")]
    [SerializeField] private Slider sensitivitySlider;    // SensitivitySlider
    [SerializeField] private TMP_Text sensitivityValue;   // SensitivityValue (1.0 글자)

    [Header("마스터 볼륨")]
    [SerializeField] private Slider volumeSlider;         // VolumeSlider
    [SerializeField] private TMP_Text volumeValue;        // VolumeValue (80% 글자)

    [Header("마이크")]
    [SerializeField] private TMP_Dropdown micDropdown;    // MicDropdown

    // 드롭다운의 각 줄이 어떤 마이크인지 기억해 두는 배열.
    // 드롭다운은 "몇 번째 줄을 골랐는지(숫자)"만 알려주므로, 이 배열에서 이름을 찾는다.
    private string[] micDevices = new string[0];

    /// <summary>
    /// 처음 한 번 실행. (Setting_Popup이 꺼진 채로 시작하면, 처음 열릴 때 실행된다)
    /// 슬라이더 범위를 정하고, 값이 바뀔 때 부를 함수를 등록한다.
    /// </summary>
    private void Awake()
    {
        // 슬라이더 범위를 코드에서 정해 두면, Inspector에서 잘못 넣어도 항상 맞는 범위가 된다.
        sensitivitySlider.minValue = GameSettings.MinSensitivity;
        sensitivitySlider.maxValue = GameSettings.MaxSensitivity;
        sensitivitySlider.wholeNumbers = false;   // 소수 허용 (0.2, 0.3 ...)

        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 100f;
        volumeSlider.wholeNumbers = true;         // 정수만 (0, 1, 2 ... 100)

        // AddListener(함수): "값이 바뀌면 이 함수를 불러 줘"라고 등록한다.
        // 버튼의 On Click ()을 Inspector 대신 코드로 연결하는 것과 같다.
        sensitivitySlider.onValueChanged.AddListener(OnSensitivityChanged);
        volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        micDropdown.onValueChanged.AddListener(OnMicChanged);

        // 마이크 이름이 길어도 한 줄로 보이게 하고, 넘치는 부분은 "…"로 줄인다.
        // captionText: 드롭다운 겉에 보이는 글자, itemText: 펼쳤을 때 목록의 글자
        if (micDropdown.captionText != null)
        {
            micDropdown.captionText.textWrappingMode = TextWrappingModes.NoWrap;   // 줄바꿈 안 함
            micDropdown.captionText.overflowMode = TextOverflowModes.Ellipsis;    // 넘치면 … 표시
        }
        if (micDropdown.itemText != null)
        {
            micDropdown.itemText.textWrappingMode = TextWrappingModes.NoWrap;
            micDropdown.itemText.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    /// <summary>팝업이 켜질 때마다 실행. 저장된 값으로 UI를 맞춘다.</summary>
    private void OnEnable()
    {
        // SetValueWithoutNotify: 값을 바꾸되 onValueChanged는 부르지 않는다.
        // (UI를 맞추는 것뿐인데 "바뀌었다"고 다시 저장하는 낭비를 막음)
        sensitivitySlider.SetValueWithoutNotify(GameSettings.Sensitivity);
        volumeSlider.SetValueWithoutNotify(Mathf.Round(GameSettings.MasterVolume * 100f));

        RefreshSensitivityText();
        RefreshVolumeText();
        FillMicDropdown();
    }

    /// <summary>팝업이 꺼질 때 실행. 바꾼 값을 디스크에 저장한다.</summary>
    private void OnDisable()
    {
        GameSettings.Save();
    }

    // ───────── 값이 바뀌었을 때 ─────────

    private void OnSensitivityChanged(float value)
    {
        GameSettings.SetSensitivity(value);
        RefreshSensitivityText();
    }

    private void OnVolumeChanged(float value)
    {
        // 슬라이더는 0~100, GameSettings는 0~1을 쓰므로 100으로 나눈다.
        GameSettings.SetMasterVolume(value / 100f);
        RefreshVolumeText();
    }

    private void OnMicChanged(int index)
    {
        // 고른 줄 번호가 마이크 목록 범위 안일 때만 저장 ("마이크 없음" 줄은 무시)
        if (index >= 0 && index < micDevices.Length)
        {
            GameSettings.SetMicrophoneDevice(micDevices[index]);
        }
    }

    // ───────── 글자 갱신 ─────────

    private void RefreshSensitivityText()
    {
        // ToString("0.0"): 소수점 한 자리까지 표시 (1 → "1.0", 1.25 → "1.3")
        sensitivityValue.text = GameSettings.Sensitivity.ToString("0.0");
    }

    private void RefreshVolumeText()
    {
        // RoundToInt: 반올림해서 정수로 (0.8 * 100 = 80)
        volumeValue.text = Mathf.RoundToInt(GameSettings.MasterVolume * 100f) + "%";
    }

    // ───────── 마이크 목록 ─────────

    /// <summary>연결된 마이크 목록으로 드롭다운을 채우고, 저장된 마이크를 선택해 둔다.</summary>
    private void FillMicDropdown()
    {
        // Microphone.devices: Unity가 알려주는 "지금 연결된 마이크 이름" 배열
        micDevices = Microphone.devices;

        micDropdown.ClearOptions();   // 기존 항목(Option A 등) 모두 지우기

        // 마이크가 하나도 없을 때
        if (micDevices.Length == 0)
        {
            micDropdown.AddOptions(new List<string> { "마이크 없음" });
            micDropdown.interactable = false;   // 눌러도 안 열리게
            micDropdown.SetValueWithoutNotify(0);
            return;
        }

        micDropdown.interactable = true;
        micDropdown.AddOptions(new List<string>(micDevices));   // 배열을 List로 바꿔서 추가

        // 저장된 마이크가 목록의 몇 번째인지 찾는다. 없으면 -1이 나온다.
        int index = System.Array.IndexOf(micDevices, GameSettings.MicrophoneDevice);
        if (index < 0)
        {
            // 처음이거나, 저장했던 마이크가 지금 연결되어 있지 않으면 첫 번째 마이크로
            index = 0;
            GameSettings.SetMicrophoneDevice(micDevices[0]);
        }

        micDropdown.SetValueWithoutNotify(index);
        micDropdown.RefreshShownValue();   // 드롭다운에 보이는 글자를 새로 고침
    }
}