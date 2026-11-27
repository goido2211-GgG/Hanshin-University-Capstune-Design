using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace AgentEscape.VoiceAI
{
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class FirstMapVoiceInput : MonoBehaviour
    {
        [SerializeField] private VoiceAgent voice;
        [SerializeField] private DroneController drone;
        [SerializeField] private Font hudFont;

        public static bool CapturesEscape { get; private set; }

        private AgentActionRunner runner;
        private GUIStyle labelStyle;
        private bool recordingFromKey;
        private bool chatOpen;
        private bool restoreDroneControl;
        private bool chatEnded;
        private string submittedCommand;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private string status;
        private EventSystem ownedEventSystem;
        private EventSystem chatEventSystem;
        private BaseInputModule chatModule;
        private GameObject previousSelection;
        private bool previousNavigationEvents;
        private bool previousDeselectOnBackgroundClick;
        private TMP_InputField field;
        private GameObject chatCanvas;

        private void OnEnable()
        {
            if (voice == null) return;
            runner = voice.Runner;
            runner.CommandFinished += ShowCommandResult;
            voice.LocalNotice += ShowStatus;
            voice.ErrorOccurred += ShowStatus;
            status = "AI 시스템 재가동 완료";
            if (EventSystem.current == null)
            {
                var input = new GameObject("FirstScene Chat EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                input.transform.SetParent(transform, false);
                ownedEventSystem = input.GetComponent<EventSystem>();
            }
        }

        private void OnDisable()
        {
            CloseChat();
            ReleaseInputCapture();
            if (runner != null) runner.CommandFinished -= ShowCommandResult;
            if (voice == null) return;
            voice.LocalNotice -= ShowStatus;
            voice.ErrorOccurred -= ShowStatus;
            if (recordingFromKey && voice.isActiveAndEnabled) voice.RequestStop();
            recordingFromKey = false;
        }

        private void OnDestroy()
        {
            // This object owns only the UI it creates as children.
            if (ownedEventSystem != null) Destroy(ownedEventSystem.gameObject);
            if (chatCanvas != null) Destroy(chatCanvas);
        }

        private void Update()
        {
            if (voice == null || !voice.isActiveAndEnabled)
            {
                CloseChat();
                recordingFromKey = false;
                return;
            }

            var keyboard = Keyboard.current;
            if (recordingFromKey && (keyboard == null || keyboard.vKey.wasReleasedThisFrame || PauseMenu.IsPaused))
            {
                recordingFromKey = false;
                if (PauseMenu.IsPaused || keyboard == null) voice.RequestStop();
                else voice.StopVoiceInput();
            }

            if (chatOpen)
            {
                if (PauseMenu.IsPaused || chatEventSystem == null ||
                    chatEventSystem != EventSystem.current || chatModule != chatEventSystem.currentInputModule)
                    CloseChat();
                return;
            }

            if (keyboard == null || PauseMenu.IsPaused) return;
            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (!recordingFromKey) OpenChat();
                return;
            }

            if (keyboard.vKey.wasPressedThisFrame)
            {
                voice.StartVoiceInput();
                recordingFromKey = voice.State == VoiceAgentState.Recording;
            }
        }

        private void OpenChat()
        {
            chatEventSystem = EventSystem.current;
            chatModule = chatEventSystem != null ? chatEventSystem.currentInputModule : null;
            if (chatModule == null)
            {
                ShowStatus("채팅 입력을 준비하고 있습니다. Enter를 다시 눌러 주세요.");
                return;
            }
            if (field == null) BuildChat();

            previousSelection = chatEventSystem.currentSelectedGameObject;
            previousNavigationEvents = chatEventSystem.sendNavigationEvents;
            // The module still updates the selected TMP field. Avoid a second Submit/Cancel
            // path from physical key presses while TMP handles Windows IME and text editing.
            chatEventSystem.sendNavigationEvents = false;
            if (chatModule is InputSystemUIInputModule inputSystemModule)
            {
                previousDeselectOnBackgroundClick = inputSystemModule.deselectOnBackgroundClick;
                inputSystemModule.deselectOnBackgroundClick = false;
            }
            chatOpen = true;
            CapturesEscape = true;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            restoreDroneControl = drone != null && drone.enabled;
            if (restoreDroneControl) drone.enabled = false;

            chatCanvas.SetActive(true);
            field.SetTextWithoutNotify("");
            // The stock field activates itself on selection and manages IME while focused.
            chatEventSystem.SetSelectedGameObject(field.gameObject);
        }

        private void CloseChat()
        {
            if (!chatOpen) return;
            chatOpen = false;
            chatEnded = false;
            submittedCommand = null;
            field.DeactivateInputField();
            if (chatEventSystem != null)
            {
                if (chatEventSystem.currentSelectedGameObject == field.gameObject)
                    chatEventSystem.SetSelectedGameObject(previousSelection);
                chatEventSystem.sendNavigationEvents = previousNavigationEvents;
            }
            chatCanvas.SetActive(false);
            if (chatModule is InputSystemUIInputModule inputSystemModule && inputSystemModule != null)
                inputSystemModule.deselectOnBackgroundClick = previousDeselectOnBackgroundClick;
            previousSelection = null;
        }

        private void LateUpdate()
        {
            // Close and send after TMP finishes its end-edit callbacks.
            if (chatEnded)
            {
                string command = submittedCommand?.Trim();
                CloseChat();
                if (!string.IsNullOrEmpty(command)) voice.SendChat(command);
            }

            // PauseMenu and the drone still see chat capture on the frame that closes it.
            if (!chatOpen && CapturesEscape) ReleaseInputCapture();
        }

        private void ReleaseInputCapture()
        {
            if (CapturesEscape && !PauseMenu.IsPaused)
            {
                Cursor.lockState = previousCursorLock;
                Cursor.visible = previousCursorVisible;
            }
            if (restoreDroneControl && drone != null) drone.enabled = true;
            restoreDroneControl = false;
            CapturesEscape = false;
        }

        private void BuildChat()
        {
            chatCanvas = new GameObject("FirstScene Command Chat", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            chatCanvas.SetActive(false);
            chatCanvas.transform.SetParent(transform, false);
            var canvas = chatCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = chatCanvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            var panel = new GameObject("Command", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)panel.transform;
            rect.SetParent(chatCanvas.transform, false);
            rect.anchorMin = new Vector2(0.1f, 0f);
            rect.anchorMax = new Vector2(0.9f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 24f);
            rect.sizeDelta = new Vector2(0f, 52f);
            panel.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.1f, 0.95f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect, rect);
            viewportRect.offsetMin = new Vector2(14f, 3f);
            viewportRect.offsetMax = new Vector2(-14f, -3f);
            var text = AddChatText("Text", viewportRect);
            var hint = AddChatText("Hint", viewportRect);
            hint.text = "예  첫 번째 문 버튼 눌러     Enter 전송 · Esc 취소";
            hint.color = new Color(0.77f, 0.83f, 0.87f);

            field = panel.AddComponent<TMP_InputField>();
            field.textViewport = viewportRect;
            field.textComponent = text;
            field.placeholder = hint;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.onFocusSelectAll = false;
            field.characterLimit = AgentBrain.MaxPlayerTextLength;
            field.richText = false;
            field.targetGraphic = panel.GetComponent<Image>();
            field.onSubmit.AddListener(value => submittedCommand = value);
            field.onEndEdit.AddListener(_ => chatEnded = chatOpen);
        }

        private static TextMeshProUGUI AddChatText(string objectName, RectTransform parent)
        {
            var text = new GameObject(objectName, typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            Stretch(text.rectTransform, parent);
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = 22f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.richText = false;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect, RectTransform parent)
        {
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void ShowCommandResult(AgentExecutionResult result) => ShowStatus(result.Message);
        private void ShowStatus(string message)
        {
            if (!string.IsNullOrWhiteSpace(message)) status = message;
        }

        private void OnGUI()
        {
            if (voice == null || !voice.isActiveAndEnabled || PauseMenu.IsPaused || chatOpen) return;
            labelStyle ??= new GUIStyle(GUI.skin.label) { font = hudFont, wordWrap = true };
            GUILayout.BeginArea(new Rect(12, Screen.height - 110, Mathf.Min(500, Screen.width - 24), 98), GUI.skin.box);
            GUILayout.Label(voice.commandRouting == AgentCommandRouting.LocalOnly ? "Enter 채팅" : "Enter 채팅 · V를 누른 채 말하기 (API 키 필요)", labelStyle);
            GUILayout.Label(voice.State == VoiceAgentState.Recording ? "녹음 중" : status, labelStyle);
            GUILayout.EndArea();
        }
    }
}
