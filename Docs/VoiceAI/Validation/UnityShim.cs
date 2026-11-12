// 테스트 전용 대체 환경. Unity의 물리·렌더링·IMGUI·프레임 순서를 재현하지 않는다.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace UnityEngine
{
    public class Object
    {
        internal static readonly List<Component> Components = new List<Component>();
        public string name = "test";
        public static T[] FindObjectsByType<T>(FindObjectsSortMode mode) where T : Component =>
            Components.OfType<T>().Where(c => !(c is Behaviour b) || b.isActiveAndEnabled).ToArray();
        internal static void Call(object target, string name) => target.GetType().GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(target, null);
        public static void ResetWorld()
        {
            foreach (var behaviour in Components.OfType<Behaviour>().ToArray()) behaviour.enabled = false;
            Components.Clear();
        }
    }
    public enum FindObjectsSortMode { None }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class Behaviour : Component
    {
        private bool _enabled = true;
        public bool enabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; Call(this, value ? "OnEnable" : "OnDisable"); }
        }
        public bool isActiveAndEnabled => enabled;
    }
    public class MonoBehaviour : Behaviour { }
    public class GameObject : Object
    {
        private readonly List<Component> _components = new List<Component>();
        public int scene = 1;
        public readonly Transform transform = new Transform();
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            _components.Add(component); Components.Add(component);
            Call(component, "Awake"); Call(component, "OnEnable");
            return component;
        }
        public T GetComponent<T>() where T : Component => _components.OfType<T>().FirstOrDefault();
    }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        private Transform _parent;
        public Vector3 localPosition { set { position = (_parent?.position ?? new Vector3()) + value; } }
        public Vector3 forward => rotation.Forward.sqrMagnitude > 0f ? rotation.Forward : new Vector3(0, 0, 1);
        public Vector3 right => new Vector3(forward.z, 0, -forward.x);
        public void SetParent(Transform parent, bool worldPositionStays) => _parent = parent;
        public bool IsChildOf(Transform target) => _parent != null && (_parent == target || _parent.IsChildOf(target));
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0, 1, 0);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this * (1f / magnitude) : this;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => a * -1;
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float amount) =>
            (b - a).magnitude <= amount ? b : a + (b - a).normalized * amount;
    }
    public struct Quaternion
    {
        public Vector3 Forward;
        public static Quaternion LookRotation(Vector3 direction) => new Quaternion { Forward = direction.normalized };
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float amount) => b;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));
        public static float MoveTowards(float value, float target, float delta) =>
            Math.Abs(target - value) <= delta ? target : value + Math.Sign(target - value) * delta;
    }
    public enum AnimatorControllerParameterType { Float = 1, Bool = 4 }
    public class AnimatorControllerParameter
    {
        public string name;
        public int nameHash => Animator.StringToHash(name);
        public AnimatorControllerParameterType type;
    }
    public class RuntimeAnimatorController : Object { }
    public class Animator : Behaviour
    {
        public bool applyRootMotion;
        public RuntimeAnimatorController runtimeAnimatorController = new RuntimeAnimatorController();
        public AnimatorControllerParameter[] parameters = new[]
        {
            new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "MotionSpeed", type = AnimatorControllerParameterType.Float },
            new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "Jump", type = AnimatorControllerParameterType.Bool },
            new AnimatorControllerParameter { name = "FreeFall", type = AnimatorControllerParameterType.Bool }
        };
        private readonly Dictionary<int, float> _floats = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> _bools = new Dictionary<int, bool>();
        public static int StringToHash(string name) => name.GetHashCode();
        public void SetFloat(int hash, float value) => _floats[hash] = value;
        public void SetBool(int hash, bool value) => _bools[hash] = value;
        public float GetFloat(string name) => _floats.TryGetValue(StringToHash(name), out var value) ? value : 0f;
        public bool GetBool(string name) => _bools.TryGetValue(StringToHash(name), out var value) && value;
    }
    public static class Time { public static float deltaTime = 0.02f; }
    public static class Debug
    {
        public static void LogError(object message, Object context = null) { }
        public static void LogWarning(object message, Object context = null) { }
        public static void LogException(Exception error) { }
    }
    public class TextAsset { public string text = "테스트 프롬프트"; }
    public static class Resources { public static T Load<T>(string path) where T : new() => new T(); }
    [AttributeUsage(AttributeTargets.Class)] public class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class ContextMenu : Attribute { public ContextMenu(string name) { } }
    [AttributeUsage(AttributeTargets.Class)] public class RequireComponent : Attribute { public RequireComponent(Type type) { } }
    [AttributeUsage(AttributeTargets.Field)] public class Min : Attribute { public Min(float value) { } }
    [AttributeUsage(AttributeTargets.Field)] public class Range : Attribute { public Range(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public class Tooltip : Attribute { public Tooltip(string value) { } }
    [AttributeUsage(AttributeTargets.Field)] public class Header : Attribute { public Header(string value) { } }
    [AttributeUsage(AttributeTargets.Field)] public class TextArea : Attribute { public TextArea(int min = 0, int max = 0) { } }
}

namespace AgentEscape.VoiceAI
{
    // 실제 키 파일·마이크·네트워크에는 접근하지 않는다.
    public static class OpenAIKeyProvider { public static string Load() => "unused-test-placeholder"; }
    public sealed class RecordedAudio { public byte[] Wav; public float Seconds; public float Rms; }
    public sealed class MicrophoneRecorder
    {
        public MicrophoneRecorder(int seconds) { }
        public void Start() { }
        public void Cancel() { }
        public RecordedAudio Stop() => new RecordedAudio { Wav = new byte[1], Seconds = 1f, Rms = 0.5f };
    }
    public sealed class OpenAIClient
    {
        public static Func<JObject, CancellationToken, Task<JObject>> Response;
        public static Func<Task<string>> Transcription;
        public OpenAIClient(string key) { }
        public Task<JObject> CreateResponseAsync(JObject request, CancellationToken cancellation) => Response(request, cancellation);
        public Task<string> TranscribeAsync(byte[] wav, string model, string language, string prompt, string[] keywords, CancellationToken cancellation) => Transcription();
    }
}
