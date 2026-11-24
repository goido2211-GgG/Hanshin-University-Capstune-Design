using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AgentEscape.VoiceAI
{
    /// <summary>사람/드론 조작과 별도의 AI 명령 입력을 중복 없이 연결한다.</summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class VoiceAIIntegrationInput : MonoBehaviour
    {
        public ControlModeSwitcher modes;
        public VoiceAgent voice;
        public VoiceAgentDebugUI debugUI;
        [Min(0.01f)] public float interactionDistance = 3f;

        public event Action<AgentInteractable> InteractionRequested;
        public string LastInteractedId { get; private set; } = "";

        private VoiceAgentDebugUI _subscribedUI;
        private bool _oldModeExternal, _oldUIExternal;
        private bool _recordingFromKey;
        private Keyboard _imeKeyboard;
        private bool _imeComposing;
        private int _imeGuardFrame = -10;

        private void OnEnable()
        {
            if (modes == null) modes = GetComponent<ControlModeSwitcher>();
            if (voice == null && debugUI != null) voice = debugUI.GetComponent<VoiceAgent>();
            if (debugUI == null && voice != null) debugUI = voice.GetComponent<VoiceAgentDebugUI>();
            if (modes != null) { _oldModeExternal = modes.externalInput; modes.externalInput = true; }
            _subscribedUI = debugUI;
            if (_subscribedUI != null)
            {
                _oldUIExternal = _subscribedUI.externalInput;
                _subscribedUI.externalInput = true;
                _subscribedUI.ChatOpenChanged += OnChatOpenChanged;
                OnChatOpenChanged(_subscribedUI.IsChatOpen);
            }
        }

        private void OnDisable()
        {
            if (_recordingFromKey && voice != null) voice.RequestStop();
            _recordingFromKey = false;
            if (_subscribedUI != null)
            {
                _subscribedUI.CloseChat();
                _subscribedUI.ChatOpenChanged -= OnChatOpenChanged;
                _subscribedUI.externalInput = _oldUIExternal;
            }
            if (modes != null)
            {
                modes.SetInputSuppressed(false);
                modes.externalInput = _oldModeExternal;
            }
            ObserveKeyboard(null);
            _subscribedUI = null;
        }

        private void OnChatOpenChanged(bool open)
        {
            if (modes != null) modes.SetInputSuppressed(open);
            if (!open) { _imeComposing = false; _imeGuardFrame = -10; }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            ObserveKeyboard(keyboard);
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (voice != null) voice.RequestStop();
                _recordingFromKey = false;
                if (debugUI != null) debugUI.CloseChat();
                return;
            }

            // 채팅을 여는 사이 V를 놓아도 이미 시작한 녹음을 끝낼 수 있어야 한다.
            if (keyboard.vKey.wasReleasedThisFrame && _recordingFromKey)
            {
                _recordingFromKey = false;
                if (voice != null && voice.State == VoiceAgentState.Recording) voice.StopVoiceInput();
            }

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (debugUI == null) { Notice("채팅 UI 연결이 필요합니다."); return; }
                if (debugUI.IsChatOpen)
                {
                    if (ImeBlocksSubmission()) return;
                    debugUI.SubmitChatDraft();
                }
                else debugUI.BeginChat();
                return;
            }
            if (debugUI != null && debugUI.IsChatOpen) return;

            if (keyboard.vKey.wasPressedThisFrame && voice != null)
            {
                voice.StartVoiceInput();
                _recordingFromKey = voice.State == VoiceAgentState.Recording;
            }
            if (keyboard.tabKey.wasPressedThisFrame)
            {
                if (modes == null) Notice("조작 모드 연결이 필요합니다.");
                else if (!modes.TryToggleMode()) Notice(modes.LastDeniedReason);
            }
            if (keyboard.fKey.wasPressedThisFrame) InspectInFront();
        }

        private void ObserveKeyboard(Keyboard keyboard)
        {
            if (ReferenceEquals(_imeKeyboard, keyboard)) return;
            if (_imeKeyboard != null) _imeKeyboard.onIMECompositionChange -= OnImeCompositionChanged;
            _imeKeyboard = keyboard;
            _imeComposing = false;
            _imeGuardFrame = -10;
            if (_imeKeyboard != null) _imeKeyboard.onIMECompositionChange += OnImeCompositionChanged;
        }

        private void OnImeCompositionChanged(IMECompositionString composition)
        {
            bool wasComposing = _imeComposing;
            _imeComposing = composition.Count > 0;
            // 조합 종료와 Enter가 같은 프레임에 들어와도 전송하지 않는다.
            if (_imeComposing || wasComposing) _imeGuardFrame = Time.frameCount;
        }

        private bool ImeBlocksSubmission()
        {
            if (_imeComposing || Time.frameCount <= _imeGuardFrame + 1) return true;
            try { return !string.IsNullOrEmpty(Input.compositionString); }
            catch (InvalidOperationException) { return false; }
        }

        private void InspectInFront()
        {
            var camera = modes != null ? modes.ActiveCamera : null;
            if (camera == null || !camera.isActiveAndEnabled)
            { Notice("현재 조작 카메라 연결이 필요합니다."); return; }
            if (interactionDistance <= 0f || float.IsNaN(interactionDistance) || float.IsInfinity(interactionDistance))
            { Notice("상호작용 거리는 유한한 양수여야 합니다."); return; }
            var ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out var hit, interactionDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            { Notice("앞에 조사할 물체가 없습니다."); return; }
            var target = hit.collider.GetComponentInParent<AgentInteractable>();
            if (target == null || !target.isActiveAndEnabled || target.gameObject.scene != gameObject.scene)
            { Notice("앞에 조사할 시험 물체가 없습니다."); return; }
            LastInteractedId = target.id ?? "";
            string name = string.IsNullOrWhiteSpace(target.displayName) ? target.name : target.displayName;
            Notice(name + ": " + (target.description ?? "") + " / 상태: " + (target.state ?? ""));
            // 사람/드론의 F 관찰은 로봇 조사 기억이나 퍼즐 상태를 변경하지 않는다.
            AgentEventDispatch.Invoke(InteractionRequested, target);
        }

        private void Notice(string message)
        {
            if (debugUI != null) debugUI.ShowLocalNotice(message);
            else if (!string.IsNullOrWhiteSpace(message)) Debug.Log("[VoiceAI] " + message, this);
        }
    }
}
