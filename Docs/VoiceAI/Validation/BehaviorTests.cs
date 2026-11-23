using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentEscape.VoiceAI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class BehaviorTests
{
    private static int _passed;
    private static int _failed;
    private static int _skipped;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static AgentAction Move(int steps = 5) => new AgentAction { Verb = "move_direction", Direction = "forward", Steps = steps };
    private static JObject Reply(int delta = 0, params AgentAction[] actions) => new JObject
    {
        ["status"] = "completed",
        ["output"] = new JArray(new JObject
        {
            ["type"] = "message", ["content"] = new JArray(new JObject
            {
                ["type"] = "output_text", ["text"] = JsonConvert.SerializeObject(new AgentResponse
                { Speech = "테스트 응답", TrustDelta = delta, Actions = actions.ToList() })
            })
        })
    };

    private sealed class Fixture
    {
        public VoiceAgent Voice;
        public AgentBodyController Body;
        public SceneStageContext Stage;
        public AgentInteractable Box;
        public AgentActionRunner Runner => Voice.Runner;
        public readonly List<AgentExecutionResult> Endings = new List<AgentExecutionResult>();
        public readonly List<AgentExecutionResult> Actions = new List<AgentExecutionResult>();
        public Fixture(bool apiAvailable = true)
        {
            UnityEngine.Object.ResetWorld();
            OpenAIClient.Response = (r, t) => Task.FromResult(Reply(0, Move()));
            OpenAIClient.Transcription = () => Task.FromResult("앞으로 가");
            var host = new GameObject();
            Stage = host.AddComponent<SceneStageContext>();
            Stage.useDemoObjectsWhenEmpty = false;
            OpenAIKeyProvider.Available = apiAvailable;
            Voice = host.AddComponent<VoiceAgent>();
            Body = new GameObject().AddComponent<AgentBodyController>();
            Box = new GameObject().AddComponent<AgentInteractable>();
            Box.id = "red_box"; Box.displayName = "빨간 상자"; Box.allowedVerbs.AddRange(new[] { "pick_up", "drop" });
            Box.transform.position = new Vector3(0, 0, 5);
            Runner.CommandFinished += Endings.Add;
            Runner.ActionFinished += Actions.Add;
        }
        public AgentCommandId Submit(params AgentAction[] actions)
        {
            var id = Runner.BeginCommand(); Runner.Submit(id, actions, null); return id;
        }
        public void Tick(float seconds, int frames = 1)
        { for (int i = 0; i < frames; ++i) Runner.Tick(seconds / frames); }
        public RobotAnimatorBridge AttachAnimator(out Animator animator)
        {
            animator = new GameObject().AddComponent<Animator>();
            var bridge = Body.gameObject.AddComponent<RobotAnimatorBridge>();
            bridge.targetAnimator = animator;
            UnityEngine.Object.Call(bridge, "OnDisable");
            UnityEngine.Object.Call(bridge, "OnEnable");
            return bridge;
        }
        public RobotExplorationController AttachExplorer(bool enableCapability = true)
        {
            Box.allowAutonomousExploration = true;
            if (enableCapability) Voice.SetExplorationEnabled(true);
            return Body.gameObject.AddComponent<RobotExplorationController>();
        }
        public void Explore(RobotExplorationController explorer, float seconds, int frames = 1)
        {
            for (int i = 0; i < frames; ++i)
            {
                Runner.Tick(seconds / frames);
                explorer.Tick(seconds / frames);
            }
        }
    }

    private sealed class ButtonReceiver : MonoBehaviour, IAgentButtonReceiver
    {
        public int PressCount;
        public void Press() => PressCount++;
        public bool TryPress() { Press(); return true; }
    }

    private static AgentButton CreateButton(string id, string displayName, Vector3 approach, params string[] aliases)
    {
        var obj = new GameObject { tag = AgentButton.Tag, name = id + "_object" };
        var item = obj.AddComponent<AgentInteractable>();
        item.id = id;
        item.displayName = displayName;
        var button = obj.AddComponent<AgentButton>();
        button.approachPoint = new GameObject().transform;
        button.approachPoint.position = approach;
        button.aliases.AddRange(aliases);
        return button;
    }

    private static async Task Test(string name, Func<Task> run)
    {
        try
        {
            await run();
            ++_passed;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            ++_failed;
            Console.Error.WriteLine($"FAIL {name}{Environment.NewLine}{error}");
        }
    }

    private static Task Sync(Action run) { run(); return Task.CompletedTask; }

    private static string FindProjectRoot(string start)
    {
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ProjectSettings", "ProjectVersion.txt")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Assets", "VoiceAI")))
                return directory.FullName;
        }
        return null;
    }

    public static async Task<int> Main()
    {
        await Test("intent uses real target; one second delay", () => Sync(() =>
        {
            var f = new Fixture(); string intent = null; f.Runner.IntentAnnounced += s => intent = s;
            f.Submit(new AgentAction { Verb = "move_to", Target = "red_box" });
            Check(intent.Contains("빨간 상자") && f.Runner.State == AgentExecutionState.Waiting, "missing validated intent");
            f.Tick(0.99f); Check(f.Body.transform.position.z == 0, "moved before delay");
            f.Tick(0.02f); Check(f.Runner.State == AgentExecutionState.Executing, "did not begin");
            f.Tick(0.1f); Check(f.Body.transform.position.z > 0, "did not move");
        }));
        await Test("cancel during wait; finish once", () => Sync(() =>
        {
            var f = new Fixture(); f.Submit(Move()); f.Tick(0.5f); f.Voice.RequestStop(); f.Voice.RequestStop(); f.Tick(10);
            Check(f.Body.transform.position.z == 0 && f.Endings.Count == 1 && f.Endings[0].Outcome == AgentExecutionOutcome.Cancelled, "wait cancellation failed");
        }));
        await Test("cancel during movement; no follow-up", () => Sync(() =>
        {
            var f = new Fixture(); f.Submit(Move(), new AgentAction { Verb = "pick_up", Target = "red_box" });
            f.Tick(1); f.Tick(0.2f); float position = f.Body.transform.position.z;
            f.Voice.RequestStop(); f.Tick(30, 300);
            Check(f.Body.transform.position.z == position && f.Stage.inventory.Count == 0 && f.Actions.Count == 0, "movement continued");
        }));
        await Test("stop terminates remaining list", () => Sync(() =>
        {
            var f = new Fixture(); f.Submit(new AgentAction { Verb = "stop" }, Move()); f.Tick(5);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Cancelled && f.Body.transform.position.z == 0, "stop continued");
        }));
        await Test("target disabled after intention rejects execution", () => Sync(() =>
        {
            var f = new Fixture(); f.Submit(new AgentAction { Verb = "pick_up", Target = "red_box" });
            f.Box.enabled = false; f.Tick(1.1f);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected && f.Stage.inventory.Count == 0, "stale target used");
        }));
        await Test("permission change during approach blocks pickup", () => Sync(() =>
        {
            var f = new Fixture(); f.Submit(new AgentAction { Verb = "pick_up", Target = "red_box" });
            f.Tick(1); f.Tick(0.1f); f.Box.allowedVerbs.Clear(); f.Tick(5);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected && f.Stage.inventory.Count == 0, "stale permission used");
        }));
        await Test("pickup then drop uses updated inventory once", () => Sync(() =>
        {
            var f = new Fixture(); f.Box.transform.position = new Vector3(0, 0, 0.5f);
            f.Submit(new AgentAction { Verb = "pick_up", Target = "red_box" }, new AgentAction { Verb = "drop", Target = "red_box" });
            f.Tick(5, 100);
            Check(f.Actions.Count == 2 && f.Stage.inventory.Count == 0 && f.Endings.Single().Outcome == AgentExecutionOutcome.Completed, "sequential state failed");
        }));
        await Test("zero speed fails and blocks remaining action", () => Sync(() =>
        {
            var f = new Fixture(); f.Body.moveSpeed = 0;
            f.Submit(Move(), new AgentAction { Verb = "pick_up", Target = "red_box" }); f.Tick(2);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Failed && f.Stage.inventory.Count == 0, "invalid speed did not fail");
        }));
        await Test("duplicate ID rejects real target", () => Sync(() =>
        {
            var f = new Fixture(); var duplicate = new GameObject().AddComponent<AgentInteractable>(); duplicate.id = "red_box";
            f.Submit(new AgentAction { Verb = "move_to", Target = "red_box" });
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected, "duplicate accepted");
        }));
        await Test("unimplemented verb rejected", () => Sync(() =>
        {
            var f = new Fixture(); f.Box.allowedVerbs.Add("open"); f.Submit(new AgentAction { Verb = "open", Target = "red_box" });
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected, "unsupported accepted");
        }));
        await Test("local stop makes no API call", () => Sync(() =>
        {
            var f = new Fixture(); int requests = 0; OpenAIClient.Response = (r, t) => { ++requests; return Task.FromResult(Reply()); };
            f.Submit(Move()); Check(f.Voice.SendChat("중지!"), "stop not accepted");
            Check(requests == 0 && f.Endings.Single().Outcome == AgentExecutionOutcome.Cancelled, "stop used API");
            Check(!AgentCommandRules.IsStop("멈추지 말고 가"), "substring stop");
        }));
        await Test("new chat replaces running command", () => Sync(() =>
        {
            var f = new Fixture(); var previous = f.Submit(Move());
            f.Voice.SendChat("앞으로 가"); Check(!f.Runner.IsCurrent(previous) && f.Runner.State == AgentExecutionState.Waiting, "new chat did not replace");
            f.Tick(5, 100); Check(f.Endings.Count == 2 && f.Actions.Count == 1, "duplicate execution");
        }));
        await Test("late LLM response after stop changes no trust/history", async () =>
        {
            var f = new Fixture(); var response = new TaskCompletionSource<JObject>();
            OpenAIClient.Response = (r, t) => response.Task;
            int turns = 0; f.Voice.TurnCompleted += t => ++turns;
            f.Voice.SendChat("오래된 명령"); f.Voice.SendChat("중지"); response.SetResult(Reply(10, Move()));
            await Task.Delay(30);
            Check(turns == 0 && f.Voice.Trust == 50 && f.Runner.State == AgentExecutionState.Idle, "late response committed");
            JObject nextRequest = null; OpenAIClient.Response = (r, t) => { nextRequest = r; return Task.FromResult(Reply()); };
            f.Voice.SendChat("새 명령");
            Check(!nextRequest["input"].ToString().Contains("오래된 명령"), "cancelled history leaked");
        });
        await Test("reverse LLM completion preserves newest command", async () =>
        {
            var f = new Fixture(); var old = new TaskCompletionSource<JObject>(); int calls = 0;
            OpenAIClient.Response = (r, t) => ++calls == 1 ? old.Task : Task.FromResult(Reply(2, Move(1)));
            f.Voice.SendChat("A"); f.Voice.SendChat("B"); var current = f.Runner.CurrentCommand;
            old.SetResult(Reply(10, Move(10))); await Task.Delay(30);
            Check(f.Runner.IsCurrent(current) && f.Voice.Trust == 52, "old result changed new command");
        });
        await Test("late error leaves newest state intact", async () =>
        {
            var f = new Fixture(); var old = new TaskCompletionSource<JObject>(); int calls = 0, errors = 0;
            f.Voice.ErrorOccurred += e => ++errors;
            OpenAIClient.Response = (r, t) => ++calls == 1 ? old.Task : Task.FromResult(Reply(0, Move()));
            f.Voice.SendChat("A"); f.Voice.SendChat("B"); old.SetException(new Exception("old error")); await Task.Delay(30);
            Check(errors == 0 && f.Runner.State == AgentExecutionState.Waiting, "old error replaced state");
        });
        await Test("late STT ignored after chat replacement", async () =>
        {
            var f = new Fixture(); var stt = new TaskCompletionSource<string>(); int calls = 0;
            OpenAIClient.Transcription = () => stt.Task;
            OpenAIClient.Response = (r, t) => { ++calls; return Task.FromResult(Reply(0, Move())); };
            f.Voice.StartVoiceInput(); f.Voice.StopVoiceInput(); f.Voice.SendChat("B"); stt.SetResult("A"); await Task.Delay(30);
            Check(calls == 1 && f.Runner.State == AgentExecutionState.Waiting, "late STT dispatched");
        });
        await Test("voice normal path and stop while transcribing", async () =>
        {
            var f = new Fixture(); f.Voice.StartVoiceInput(); f.Voice.StopVoiceInput();
            Check(f.Runner.State == AgentExecutionState.Waiting, "voice regression"); f.Voice.RequestStop();
            var stt = new TaskCompletionSource<string>(); OpenAIClient.Transcription = () => stt.Task;
            f.Voice.StartVoiceInput(); f.Voice.StopVoiceInput(); f.Voice.RequestStop(); stt.SetResult("가"); await Task.Delay(30);
            Check(f.Runner.State == AgentExecutionState.Idle, "STT cancellation failed");
        });
        await Test("reset and disable invalidate pending response", async () =>
        {
            var f = new Fixture(); var old = new TaskCompletionSource<JObject>(); OpenAIClient.Response = (r, t) => old.Task;
            f.Voice.SendChat("A"); f.Voice.ResetConversation(false); f.Voice.enabled = false;
            old.SetResult(Reply(10, Move())); await Task.Delay(30);
            Check(f.Voice.Trust == 50 && f.Runner.State == AgentExecutionState.Idle, "reset failed");
        });
        await Test("prepared turn has no side effects; commit once", async () =>
        {
            var brain = new AgentBrain("test", new LlmSettings(), (r, t) => Task.FromResult(Reply(10)));
            var prepared = await brain.PrepareTurnAsync("A", new StageContext(), CancellationToken.None);
            Check(brain.Trust == 50, "prepare changed trust"); brain.CommitTurn(prepared); Check(brain.Trust == 60, "commit missing");
            bool rejected = false; try { brain.CommitTurn(prepared); } catch (OperationCanceledException) { rejected = true; }
            Check(rejected, "double commit accepted");
        });
        await Test("cancelled transport result cannot mutate brain", async () =>
        {
            var late = new TaskCompletionSource<JObject>(); var cts = new CancellationTokenSource();
            var brain = new AgentBrain("test", new LlmSettings(), (r, t) => late.Task);
            var task = brain.RunTurnAsync("A", new StageContext(), cts.Token); cts.Cancel(); late.SetResult(Reply(10));
            bool cancelled = false; try { await task; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && brain.Trust == 50, "cancelled brain changed");
        });
        await Test("intent subscriber cancellation prevents action", () => Sync(() =>
        {
            var f = new Fixture(); f.Runner.IntentAnnounced += s => f.Voice.RequestStop(); f.Submit(Move()); f.Tick(5);
            Check(f.Runner.State == AgentExecutionState.Idle && f.Body.transform.position.z == 0, "reentrant cancel failed");
        }));
        await Test("turn observer exception does not duplicate execution", () => Sync(() =>
        {
            var f = new Fixture(); f.Voice.TurnCompleted += t => throw new Exception("observer");
            f.Voice.SendChat("가"); f.Tick(5, 100);
            Check(f.Actions.Count == 1 && f.Endings.Count == 1, "observer disrupted execution");
        }));
        await Test("prepared response cancelled before commit is rejected", async () =>
        {
            var cts = new CancellationTokenSource();
            var brain = new AgentBrain("test", new LlmSettings(), (r, t) => Task.FromResult(Reply(10)));
            var prepared = await brain.PrepareTurnAsync("A", new StageContext(), cts.Token);
            cts.Cancel();
            bool cancelled = false; try { brain.CommitTurn(prepared); } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && brain.Trust == 50, "cancelled prepared response committed");
        });
        await Test("refused response cannot execute; malformed action rejected", () => Sync(() =>
        {
            var accepted = new List<AgentAction>(); var rejected = new List<RejectedAction>();
            AgentResponseParser.Validate(new AgentResponse { Comprehension = "refused", Actions = new List<AgentAction> { Move() } },
                new StageContext(), 50, accepted, rejected);
            Check(accepted.Count == 0 && rejected.Count == 1, "refused action accepted");
            Check(AgentResponseParser.CheckAction(null, new StageContext(), 50) != null, "null action accepted");
        }));
        await Test("body disabled while moving cancels and prevents resumption", () => Sync(() =>
        {
            var f = new Fixture(); f.Submit(Move()); f.Tick(1); f.Tick(0.1f); float position = f.Body.transform.position.z;
            f.Body.enabled = false; f.Body.enabled = true; f.Tick(5);
            Check(f.Body.transform.position.z == position && f.Endings.Single().Outcome == AgentExecutionOutcome.Cancelled, "body resumed");
        }));
        const string controllerAsset = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        string projectRoot = FindProjectRoot(AppContext.BaseDirectory) ?? FindProjectRoot(Directory.GetCurrentDirectory());
        string controllerPath = projectRoot == null ? null : Path.Combine(projectRoot, controllerAsset);
        if (!File.Exists(controllerPath))
        {
            ++_skipped;
            Console.WriteLine("SKIP Kyle controller asset is unavailable: " + (controllerPath ?? controllerAsset));
        }
        else
        {
            await Test("Kyle controller asset matches exact parameters and blend thresholds", () => Sync(() =>
            {
                string controller = File.ReadAllText(controllerPath);
                foreach (string parameter in new[] { "Speed", "MotionSpeed" })
                    Check(System.Text.RegularExpressions.Regex.IsMatch(controller, "m_Name: " + parameter + @"\r?\n\s+m_Type: 1"), "float parameter mismatch");
                foreach (string parameter in new[] { "Grounded", "Jump", "FreeFall" })
                    Check(System.Text.RegularExpressions.Regex.IsMatch(controller, "m_Name: " + parameter + @"\r?\n\s+m_Type: 4"), "bool parameter mismatch");
                Check(controller.Contains("m_Name: Idle Walk Run Blend") && controller.Contains("m_SpeedParameter: MotionSpeed"), "state multiplier mismatch");
                foreach (string threshold in new[] { "0", "2", "6" }) Check(controller.Contains("m_Threshold: " + threshold), "blend threshold mismatch");
            }));
        }
        await Test("animator idle and intent wait have zero locomotion", () => Sync(() =>
        {
            var f = new Fixture(); f.AttachAnimator(out var animator);
            Check(animator.GetFloat("Speed") == 0 && animator.GetFloat("MotionSpeed") == 1 && animator.GetBool("Grounded"), "idle not initialized");
            f.Submit(Move()); f.Tick(0.8f);
            Check(animator.GetFloat("Speed") == 0 && !animator.GetBool("Jump") && !animator.GetBool("FreeFall"), "walked in wait");
        }));
        await Test("actual movement drives walk only and adjusts playback rate", () => Sync(() =>
        {
            var f = new Fixture(); var bridge = f.AttachAnimator(out var animator);
            f.Submit(Move()); f.Tick(1); f.Tick(0.2f);
            Check(Math.Abs(bridge.MeasuredSpeed - 3f) < 0.001f && animator.GetFloat("Speed") == 2f, "walk speed not measured");
            Check(Math.Abs(animator.GetFloat("MotionSpeed") - 1.5f) < 0.001f, "playback ratio wrong");
            f.Body.moveSpeed = 6f; f.Tick(0.1f);
            Check(animator.GetFloat("Speed") == 2f && animator.GetFloat("MotionSpeed") == 3f, "run selected");
        }));
        await Test("completed movement resets animation to idle", () => Sync(() =>
        {
            var f = new Fixture(); var bridge = f.AttachAnimator(out var animator);
            f.Submit(Move(1)); f.Tick(4, 80);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Completed && animator.GetFloat("Speed") == 0f && bridge.MeasuredSpeed == 0, "completed walk persisted");
        }));
        await Test("emergency stop resets walk immediately without another frame", () => Sync(() =>
        {
            var f = new Fixture(); f.AttachAnimator(out var animator);
            f.Submit(Move()); f.Tick(1); f.Tick(0.2f); Check(animator.GetFloat("Speed") > 0f, "not walking");
            float z = f.Body.transform.position.z; f.Voice.RequestStop();
            Check(animator.GetFloat("Speed") == 0f && animator.GetFloat("MotionSpeed") == 1f, "cancel retained walk");
            f.Tick(5); Check(f.Body.transform.position.z == z, "cancel moved root");
        }));
        await Test("cancel during intent wait never enables walk", () => Sync(() =>
        {
            var f = new Fixture(); f.AttachAnimator(out var animator);
            f.Submit(Move()); f.Tick(0.5f); f.Voice.RequestStop(); f.Tick(3);
            Check(animator.GetFloat("Speed") == 0f && f.Body.transform.position.z == 0f, "cancelled wait animated");
        }));
        await Test("missing or incompatible animator does not stop movement", () => Sync(() =>
        {
            var f = new Fixture(); f.Body.gameObject.AddComponent<RobotAnimatorBridge>();
            f.Submit(Move(1)); f.Tick(3, 60);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Completed, "missing animator broke movement");
            f = new Fixture(); var bridge = f.AttachAnimator(out var animator);
            animator.runtimeAnimatorController = new RuntimeAnimatorController();
            animator.parameters = new[] { new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Bool } };
            f.Submit(Move(1)); f.Tick(3, 60);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Completed, "wrong animator contract broke movement");
        }));
        await Test("root motion disabled and bridge disable clears animation", () => Sync(() =>
        {
            var f = new Fixture(); var bridge = f.AttachAnimator(out var animator);
            animator.applyRootMotion = true;
            f.Submit(Move()); f.Tick(1); f.Tick(0.2f);
            Check(!animator.applyRootMotion, "root motion enabled");
            bridge.enabled = false; Check(animator.GetFloat("Speed") == 0f, "disabled bridge retained walk");
        }));
        await Test("back and left directions rotate toward actual travel", () => Sync(() =>
        {
            var f = new Fixture(); var back = Move(); back.Direction = "back";
            f.Submit(back); f.Tick(1); f.Tick(0.1f);
            Check(f.Body.transform.position.z < 0 && f.Body.transform.forward.z < 0, "back direction not faced");
            f = new Fixture(); var left = Move(); left.Direction = "left";
            f.Submit(left); f.Tick(1); f.Tick(0.1f);
            Check(f.Body.transform.position.x < 0 && f.Body.transform.forward.x < 0, "left direction not faced");
        }));
        await Test("chat pickup and drop keep inventory with animator attached", () => Sync(() =>
        {
            var f = new Fixture(); f.AttachAnimator(out var animator); f.Box.transform.position = new Vector3(0, 0, 0.5f);
            OpenAIClient.Response = (r, t) => Task.FromResult(Reply(0,
                new AgentAction { Verb = "pick_up", Target = "red_box" }, new AgentAction { Verb = "drop", Target = "red_box" }));
            f.Voice.SendChat("집고 내려놓아"); f.Tick(5, 100);
            Check(f.Actions.Count == 2 && f.Stage.inventory.Count == 0 && animator.GetFloat("Speed") == 0f, "pickup regression");
        }));
        await Test("exploration starts after three seconds without API requests", () => Sync(() =>
        {
            var f = new Fixture(); int calls = 0;
            OpenAIClient.Response = (r, t) => { ++calls; return Task.FromResult(Reply()); };
            var e = f.AttachExplorer(); e.Tick(2.9f); Check(!f.Runner.HasActiveCommand, "exploration started early");
            e.Tick(0.11f);
            Check(f.Runner.CurrentSource == AgentCommandSource.Autonomous && f.Runner.State == AgentExecutionState.Waiting && calls == 0, "autonomous admission failed");
            Check(f.Body.transform.position.z == 0, "intent delay skipped");
        }));
        await Test("exploration moves inspects once and then remains idle", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Explore(e, 12f, 600);
            Check(f.Box.HasBeenInspected && f.Body.transform.position.z > 0 && f.Actions.Count == 2, "exploration did not inspect");
            int count = f.Endings.Count; f.Explore(e, 30f, 600);
            Check(f.Endings.Count == count && !f.Runner.HasActiveCommand && f.Stage.inventory.Count == 0, "repeated inspection or changed puzzle");
        }));
        await Test("eligible candidates use distance then ordinal ID deterministically", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Box.transform.position = new Vector3(1, 0, 0);
            var other = new GameObject().AddComponent<AgentInteractable>();
            other.id = "aaa"; other.allowAutonomousExploration = true; other.transform.position = new Vector3(-1, 0, 0);
            e.Tick(3); Check(e.CurrentTargetId == "aaa", "ordinal tie break failed");
            f.Explore(e, 12, 600);
            Check(other.HasBeenInspected && f.Box.HasBeenInspected, "uninspected candidates not preferred");
            f = new Fixture(); e = f.AttachExplorer(); f.Box.transform.position = new Vector3(0, 0, 8);
            other = new GameObject().AddComponent<AgentInteractable>(); other.id = "zzz"; other.allowAutonomousExploration = true;
            other.transform.position = new Vector3(0, 0, 2);
            e.Tick(3); Check(e.CurrentTargetId == "zzz", "distance not prioritized over ID");
        }));
        await Test("opt-out inactive out-of-range duplicate and held targets excluded", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Box.allowAutonomousExploration = false;
            e.Tick(3); Check(!f.Runner.HasActiveCommand, "opt-out selected");
            f.Box.allowAutonomousExploration = true; f.Box.enabled = false; e.Tick(1);
            Check(!f.Runner.HasActiveCommand, "inactive selected");
            f.Box.enabled = true; f.Box.transform.position = new Vector3(0, 0, 20); e.Tick(1);
            Check(!f.Runner.HasActiveCommand, "range violated");
            f.Box.transform.position = new Vector3(0, 0, 2);
            f.Submit(new AgentAction { Verb = AgentVocabulary.PickUp, Target = f.Box.id }); f.Tick(5, 100); e.Tick(3);
            Check(f.Body.CurrentHeldObject == f.Box, "fixture failed to pick up target");
            Check(!f.Runner.HasActiveCommand, "held selected");
            f.Submit(new AgentAction { Verb = AgentVocabulary.Drop, Target = f.Box.id }); f.Tick(5, 100);
            var duplicate = new GameObject().AddComponent<AgentInteractable>(); duplicate.id = f.Box.id;
            e.Tick(3); Check(!f.Runner.HasActiveCommand, "duplicate selected");
        }));
        await Test("no physical candidates never uses fake stage objects", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Box.enabled = false; f.Stage.useDemoObjectsWhenEmpty = true;
            Check(f.Stage.Build().Objects.Count > 0, "fake fixture missing");
            f.Explore(e, 30, 300);
            Check(!f.Runner.HasActiveCommand && f.Endings.Count == 0 && f.Body.transform.position.z == 0, "fake objects explored");
        }));
        await Test("player command preempts exploration and blocks auto while thinking", async () =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); e.Tick(3); var old = f.Runner.CurrentCommand;
            f.Runner.Tick(1); f.Runner.Tick(0.1f);
            var response = new TaskCompletionSource<JObject>(); OpenAIClient.Response = (r, t) => response.Task;
            f.Voice.SendChat("플레이어 명령"); e.Tick(100);
            Check(!f.Runner.IsCurrent(old) && f.Runner.CurrentSource == AgentCommandSource.Player && f.Voice.State == VoiceAgentState.Thinking, "player did not preempt");
            response.SetResult(Reply(0, Move(1))); await Task.Delay(30);
            Check(f.Runner.CurrentSource == AgentCommandSource.Player, "late exploration replaced player");
        });
        await Test("autonomous requests cannot preempt active player command", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Voice.SendChat("가"); var player = f.Runner.CurrentCommand;
            bool admitted = f.Runner.TryBeginAutonomous(10, out _); e.Tick(100);
            Check(!admitted && f.Runner.IsCurrent(player), "autonomous preempted player");
        }));
        await Test("explicit stop persists across ordinary completed commands", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); e.Tick(3); f.Runner.Tick(1); f.Runner.Tick(0.1f);
            f.Voice.RequestStop(); float z = f.Body.transform.position.z; f.Explore(e, 10, 100);
            Check(f.Runner.AutonomySuppressed && f.Body.transform.position.z == z && !f.Runner.HasActiveCommand, "stop resumed");
            f.Voice.SendChat("일반 명령"); f.Explore(e, 15, 300);
            Check(f.Runner.AutonomySuppressed && f.Endings.Count(r => r.Source == AgentCommandSource.Autonomous) == 1, "ordinary completion cleared stop");
        }));
        await Test("explicit local resume schedules auto without LLM", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); int calls = 0;
            OpenAIClient.Response = (r, t) => { ++calls; return Task.FromResult(Reply()); };
            f.Voice.SendChat("중지"); e.Tick(10); f.Voice.SendChat("탐색 재개");
            e.Tick(2.9f); Check(!f.Runner.HasActiveCommand, "resume too early"); e.Tick(0.11f);
            Check(!f.Runner.AutonomySuppressed && f.Runner.CurrentSource == AgentCommandSource.Autonomous && calls == 0, "resume used API");
        }));
        await Test("ordinary player completion schedules fresh three second delay", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); e.Tick(2.5f);
            f.Voice.SendChat("앞으로 가"); f.Runner.Tick(1); f.Runner.Tick(2); f.Runner.Tick(0.02f);
            Check(!f.Runner.HasActiveCommand && f.Endings.Single().Source == AgentCommandSource.Player, "player did not complete");
            e.Tick(2.9f); Check(!f.Runner.HasActiveCommand, "old startup timer reused"); e.Tick(0.11f);
            Check(f.Runner.CurrentSource == AgentCommandSource.Autonomous && f.Runner.HasActiveCommand, "no restart after completion");
        }));
        await Test("autonomous whitelist rejects every mutating verb before execution", () => Sync(() =>
        {
            foreach (string verb in new[] { "pick_up", "drop", "open", "close", "press", "use", "push", "pull", "stop", "move_direction" })
            {
                var f = new Fixture(); f.Box.allowAutonomousExploration = true;
                f.Voice.SetExplorationEnabled(true);
                Check(f.Runner.TryBeginAutonomous(10, out var id), "autonomous start failed");
                f.Runner.Submit(id, new[] { new AgentAction { Verb = verb, Target = f.Box.id } }, null);
                Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected && f.Stage.inventory.Count == 0, "autonomous mutation allowed: " + verb);
            }
        }));
        await Test("autonomous target permission and range rechecked after intention", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); e.Tick(3); f.Box.allowAutonomousExploration = false; f.Runner.Tick(1);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected && !f.Box.HasBeenInspected, "permission not rechecked");
            f = new Fixture(); e = f.AttachExplorer(); e.Tick(3); f.Box.transform.position = new Vector3(0, 0, 20); f.Runner.Tick(1);
            Check(f.Endings.Single().Outcome == AgentExecutionOutcome.Rejected, "range not rechecked");
        }));
        await Test("failed exploration target not retried indefinitely", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Body.moveSpeed = 0; e.Tick(3); f.Runner.Tick(1);
            Check(e.FailedTargetCount == 1 && f.Endings.Single().Outcome == AgentExecutionOutcome.Failed, "failure not tracked");
            f.Explore(e, 30, 300); Check(f.Endings.Count == 1 && !f.Box.HasBeenInspected, "failed target repeated");
        }));
        await Test("player inspection removes candidate without changing puzzle state", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); f.Box.transform.position = new Vector3(0, 0, 0.5f); f.Box.state = "퍼즐 원본";
            OpenAIClient.Response = (r, t) => Task.FromResult(Reply(0, new AgentAction { Verb = "inspect", Target = f.Box.id }));
            f.Voice.SendChat("살펴봐"); f.Runner.Tick(1); f.Runner.Tick(0.1f); e.Tick(10);
            Check(f.Box.HasBeenInspected && f.Box.state == "퍼즐 원본" && !f.Runner.HasActiveCommand, "inspection mutated puzzle or repeated");
        }));
        await Test("exploration animation and stop use existing body bridge", () => Sync(() =>
        {
            var f = new Fixture(); f.AttachAnimator(out var animator); var e = f.AttachExplorer(); e.Tick(3);
            Check(animator.GetFloat("Speed") == 0, "walk during intention"); f.Runner.Tick(1); f.Runner.Tick(0.2f);
            Check(animator.GetFloat("Speed") > 0, "no exploration walk"); f.Voice.RequestStop();
            Check(animator.GetFloat("Speed") == 0 && f.Runner.AutonomySuppressed, "exploration stop retained animation");
        }));
        await Test("disabling explorer cancels only its own work", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); e.Tick(3); e.enabled = false;
            Check(!f.Runner.HasActiveCommand, "own work continued"); e.enabled = true; f.Voice.SendChat("가");
            var id = f.Runner.CurrentCommand; e.enabled = false; Check(f.Runner.IsCurrent(id), "disabling explorer cancelled player");
        }));
        await Test("stale autonomous completion cannot change newer player timer", () => Sync(() =>
        {
            var f = new Fixture(); var e = f.AttachExplorer(); e.Tick(3); var old = f.Runner.CurrentCommand;
            f.Voice.SendChat("가"); var current = f.Runner.CurrentCommand;
            typeof(RobotExplorationController).GetMethod("OnCommandFinished", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(e, new object[] { new AgentExecutionResult { CommandId = old, Source = AgentCommandSource.Autonomous, Outcome = AgentExecutionOutcome.Completed } });
            e.Tick(10); Check(f.Runner.IsCurrent(current) && !f.Box.HasBeenInspected, "stale completion altered current work");
        }));
        await Test("offline capability defaults off and can be explicitly enabled", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var explorer = f.AttachExplorer(enableCapability: false);
            f.Voice.SendChat("탐색 재개");
            f.Explore(explorer, 10, 200);
            Check(!f.Voice.ApiReady && !f.Runner.HasActiveCommand && !f.Box.HasBeenInspected &&
                f.Body.transform.position.sqrMagnitude == 0, "exploration started without capability");
            f.Voice.SetExplorationEnabled(true);
            f.Explore(explorer, 10, 200);
            Check(f.Box.HasBeenInspected, "enabled exploration did not inspect");
        }));
        await Test("offline button aliases, separate approach, exactly one event", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 4), "1번", "문 옆에 있는");
            var redReceiver = red.gameObject.AddComponent<ButtonReceiver>();
            red.onPress.SetTestPersistentListener(redReceiver.Press);
            var blue = CreateButton("blue_button", "파란 버튼", new Vector3(4, 0, 0), "2번");
            var blueReceiver = blue.gameObject.AddComponent<ButtonReceiver>();
            blue.receiver = blueReceiver;

            f.Voice.SendChat("저 버튼 눌러");
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "ambiguous command accepted");
            f.Voice.SendChat("없는 버튼 눌러");
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "missing button accepted");
            f.Voice.SendChat("1번 버튼 앞까지 가"); f.Tick(5, 100);
            Check(f.Body.transform.position.z == 4 && redReceiver.PressCount == 0 &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Completed, "approach pressed or failed");
            f.Voice.SendChat("문 옆에 있는 버튼 눌러"); f.Tick(10, 200);
            Check(redReceiver.PressCount == 1 && blueReceiver.PressCount == 0 &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Completed && !f.Voice.ApiReady, "wrong press");
            f.Voice.SendChat("2번 버튼을 눌러"); f.Tick(10, 200);
            Check(redReceiver.PressCount == 1 && blueReceiver.PressCount == 1, "wrong numbered button");
        }));
        await Test("button without listener fails and a moving press can be cancelled", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 5));
            f.Voice.SendChat("빨간 버튼 눌러"); f.Tick(3, 60);
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Failed &&
                f.Body.transform.position.sqrMagnitude == 0, "disconnected button reported success");
            var listener = red.gameObject.AddComponent<ButtonReceiver>();
            red.receiver = listener;
            var explorer = f.AttachExplorer();
            explorer.Tick(3);
            Check(f.Runner.CurrentSource == AgentCommandSource.Autonomous, "missing autonomous work");
            f.Voice.SendChat("빨간 버튼 눌러"); f.Tick(1.5f, 30);
            Check(f.Runner.CurrentSource == AgentCommandSource.Player && f.Body.IsMoving, "player failed to preempt");
            f.Voice.SetExplorationEnabled(false);
            Check(f.Body.IsMoving, "capability disable cancelled player work");
            f.Voice.SendChat("멈춰"); f.Explore(explorer, 10, 200);
            Check(listener.PressCount == 0 && !f.Body.IsMoving && !f.Runner.HasActiveCommand &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Cancelled, "cancelled press fired");
        }));
        await Test("moved approach point and replaced target never activate", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var button = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 5));
            var listener = button.gameObject.AddComponent<ButtonReceiver>(); button.receiver = listener;
            f.Voice.SendChat("빨간 버튼 눌러"); f.Tick(1.1f, 11);
            button.approachPoint.position = new Vector3(0, 0, 9);
            f.Tick(10, 200);
            Check(listener.PressCount == 0 && f.Endings.Last().Outcome == AgentExecutionOutcome.Failed, "out of range press");
            f.Voice.SendChat("빨간 버튼 눌러");
            button.GetComponent<AgentInteractable>().enabled = false;
            var replacement = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 5));
            var replacementListener = replacement.gameObject.AddComponent<ButtonReceiver>(); replacement.receiver = replacementListener;
            f.Tick(10, 200);
            Check(replacementListener.PressCount == 0 && f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "stale target replacement");
        }));
        await Test("voice transcript shares local button routing without LLM", () => Sync(() =>
        {
            var f = new Fixture();
            var button = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 2));
            var listener = button.gameObject.AddComponent<ButtonReceiver>();
            button.receiver = listener;
            int llmCalls = 0;
            OpenAIClient.Response = (r, t) => { llmCalls++; return Task.FromResult(Reply()); };
            OpenAIClient.Transcription = () => Task.FromResult("저 버튼 눌러");
            f.Voice.StartVoiceInput(); f.Voice.StopVoiceInput();
            f.Tick(5, 100);
            Check(listener.PressCount == 1 && llmCalls == 0, "voice bypassed local routing");
        }));
        await Test("free-form API output cannot pick and press a button", () => Sync(() =>
        {
            var f = new Fixture();
            var button = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 2));
            var listener = button.gameObject.AddComponent<ButtonReceiver>();
            button.receiver = listener;
            OpenAIClient.Response = (r, t) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.Press, Target = "red_button" }));
            f.Voice.SendChat("아무거나 해");
            f.Tick(5, 100);
            Check(listener.PressCount == 0 && f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "API pressed without explicit command");
        }));
        await Test("offline Korean movement respects grid size and metric distances", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Voice.commandRouting = AgentCommandRouting.LocalOnly;
            f.Voice.SendChat("앞으로 한 칸 가"); f.Tick(5, 100);
            Check(Math.Abs(f.Body.transform.position.z - 1f) < 0.001f, "default grid size was not one");
            f.Body.stepLength = 0.5f;
            f.Voice.SendChat("세 걸음 앞으로 가"); f.Tick(5, 100);
            Check(Math.Abs(f.Body.transform.position.z - 2.5f) < 0.001f, "three steps ignored Inspector grid size");
            f.Voice.SendChat("앞으로 2미터 가"); f.Tick(5, 100);
            Check(Math.Abs(f.Body.transform.position.z - 4.5f) < 0.001f, "metric move was scaled by grid");
            f.Voice.SendChat("조금 뒤로 가"); f.Tick(5, 100);
            Check(Math.Abs(f.Body.transform.position.z - 4f) < 0.001f && !f.Voice.ApiReady, "short backwards move failed offline");
        }));
        await Test("local right and left rotation and movement use body-relative axes", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Voice.SendChat("오른쪽으로 90도 돌아"); f.Tick(5, 100);
            Check(f.Body.transform.forward.x > 0.99f && f.Body.transform.position.sqrMagnitude == 0f, "right turn failed");
            f.Voice.SendChat("앞으로 한 칸 가"); f.Tick(5, 100);
            Check(f.Body.transform.position.x > 0.99f && Math.Abs(f.Body.transform.position.z) < 0.001f, "forward used world axis");
            f.Voice.SendChat("왼쪽으로 90도 돌아"); f.Tick(5, 100);
            Check(f.Body.transform.forward.z > 0.99f, "left turn failed");
        }));
        await Test("Korean compound move then rotate and immediate mid-move stop", () => Sync(() =>
        {
            var f = new Fixture();
            OpenAIClient.Response = (r, t) => throw new Exception("local command called API");
            f.Voice.SendChat("앞으로 두 칸 가서 오른쪽으로 돌아"); f.Tick(8, 160);
            Check(f.Actions.Count == 2 && f.Body.transform.position.z == 2f && f.Body.transform.forward.x > 0.99f,
                "local sequence did not run in order");
            f.Voice.SendChat("앞으로 8미터 가"); f.Tick(1.5f, 30);
            var stoppedAt = f.Body.transform.position;
            Check(f.Body.IsMoving, "stop fixture never moved");
            f.Voice.SendChat("앞으로 가다가 멈춰"); f.Tick(5, 100);
            Check(Vector3.Distance(stoppedAt, f.Body.transform.position) < 0.001f && f.Runner.AutonomySuppressed &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Cancelled, "stop waited or movement resumed");
        }));
        await Test("button approach sequence fails at an obstacle before pressing", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var button = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 4));
            var receiver = button.gameObject.AddComponent<ButtonReceiver>(); button.receiver = receiver;
            var wall = new GameObject().AddComponent<Collider>();
            Physics.CapsuleCast = (bottom, top, radius, direction, distance) => wall;
            f.Voice.SendChat("버튼 앞으로 가서 눌러"); f.Tick(8, 160);
            Check(f.Actions.Count == 1 && receiver.PressCount == 0 && f.Body.transform.position.sqrMagnitude == 0f &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Failed && f.Endings.Last().Message.Contains("장애물"), "blocked move pressed or passed");
            Physics.CapsuleCast = null;
            f.Voice.SendChat("버튼 앞으로 가서 눌러"); f.Tick(8, 160);
            Check(f.Actions.Count == 3 && receiver.PressCount == 1 &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Completed, "reach then press sequence failed");
        }));
        await Test("target selection, pronoun and unique neighboring correction are grounded", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 4));
            var redReceiver = red.gameObject.AddComponent<ButtonReceiver>(); red.receiver = redReceiver;
            red.transform.position = new Vector3(0, 0, 5);
            var blue = CreateButton("blue_button", "파란 버튼", new Vector3(2, 0, 4));
            var blueReceiver = blue.gameObject.AddComponent<ButtonReceiver>(); blue.receiver = blueReceiver;
            blue.transform.position = new Vector3(2, 0, 5);

            f.Voice.SendChat("저 버튼으로 가");
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "ambiguous demonstrative moved");
            f.Voice.SendChat("이제 그거 눌러");
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "unresolved pronoun pressed");
            f.Voice.SendChat("빨간 버튼으로 가"); f.Tick(5, 100);
            f.Voice.SendChat("이제 그거 눌러"); f.Tick(5, 100);
            Check(redReceiver.PressCount == 1 && blueReceiver.PressCount == 0, "pronoun selected wrong button");
            f.Voice.SendChat("아니, 옆에 있는 버튼");
            Check(redReceiver.PressCount == 1 && blueReceiver.PressCount == 0, "correction triggered a press");
            f.Voice.SendChat("이제 그거 눌러"); f.Tick(5, 100);
            Check(redReceiver.PressCount == 1 && blueReceiver.PressCount == 1, "neighbor context was not updated");
        }));
        await Test("free-form API route sends strict actions and executes returned motion with metrics", () => Sync(() =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            JObject request = null;
            OpenAIClient.Response = (body, token) =>
            {
                request = body;
                var response = Reply(0,
                    new AgentAction { Verb = AgentVocabulary.MoveDirection, Direction = "forward", Distance = 2f },
                    new AgentAction { Verb = AgentVocabulary.Rotate, Angle = -90f });
                response["usage"] = new JObject { ["input_tokens"] = 143, ["output_tokens"] = 40, ["total_tokens"] = 183 };
                return Task.FromResult(response);
            };
            f.Voice.SendChat("두 미터쯤 직진하고 반시계 방향으로 직각만큼 돌아 줘");
            f.Tick(8, 160);
            Check(request != null && (bool)request["text"]["format"]["strict"] && !(bool)request["store"],
                "structured output or storage setting was absent");
            Check(f.Actions.Count == 2 && f.Body.transform.position.z == 2f && f.Body.transform.forward.x < -0.99f,
                "mocked structured API actions did not execute");
            Check(f.Voice.LastApiMetrics?.TotalTokens == 183 &&
                f.Voice.LastApiMetrics.Outcome == AgentApiOutcome.Completed, "API usage metrics were not captured");
        }));
        await Test("APIs can select a real box and receive sanitized command context", () => Sync(() =>
        {
            var f = new Fixture();
            var requests = new List<JObject>();
            f.Box.gameObject.name = "BoxSceneName";
            OpenAIClient.Response = (request, token) =>
            {
                requests.Add(request);
                return Task.FromResult(requests.Count == 1 ? Reply(0, new AgentAction
                { Verb = AgentVocabulary.MoveTo, Target = f.Box.id }) : Reply());
            };
            f.Voice.SendChat("저 상자 앞까지 가"); f.Tick(5, 100);
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Completed && f.Body.transform.position.z > 3f,
                "API target movement failed");
            f.Voice.SendChat("장면 상태를 알려 줘");
            string context = (string)requests[1]["input"][0]["content"];
            Check(context.Contains("BoxSceneName") && context.Contains("월드 위치") &&
                context.Contains("직전에 선택한 대상 ID: red_box") && context.Contains("마지막 행동") && context.Contains("성공") &&
                !requests[1]["input"].ToString().Contains("저 상자 앞까지 가"), "scene metadata or action context missing");
        }));
        await Test("partially matching local grammar falls through to grounded API interaction", () => Sync(() =>
        {
            var f = new Fixture();
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 3));
            var redReceiver = red.gameObject.AddComponent<ButtonReceiver>(); red.receiver = redReceiver;
            int calls = 0;
            OpenAIClient.Response = (request, token) =>
            {
                ++calls;
                return Task.FromResult(Reply(0, new AgentAction { Verb = AgentVocabulary.MoveTo, Target = "red_button" },
                    new AgentAction { Verb = AgentVocabulary.Interact, Target = "red_button" }));
            };
            f.Voice.SendChat("부탁인데 빨간 버튼 눌러"); f.Tick(8, 160);
            Check(calls == 1 && redReceiver.PressCount == 1 && f.Actions.Count == 2, "local miss did not reach safe API execution");
        }));
        await Test("API rejects ambiguous, invented and out-of-bounds plans atomically", () => Sync(() =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 4));
            var redReceiver = red.gameObject.AddComponent<ButtonReceiver>(); red.receiver = redReceiver;
            var blue = CreateButton("blue_button", "파란 버튼", new Vector3(2, 0, 4));
            blue.receiver = blue.gameObject.AddComponent<ButtonReceiver>();
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.Interact, Target = "red_button" }));
            f.Voice.SendChat("버튼을 눌러 줄래"); f.Tick(5, 100);
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected && redReceiver.PressCount == 0,
                "API guessed between buttons");
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                Move(1), new AgentAction { Verb = AgentVocabulary.Interact, Target = "invented_button" }));
            f.Voice.SendChat("없는 버튼 눌러"); f.Tick(5, 100);
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected && f.Body.transform.position.sqrMagnitude == 0,
                "invented target let a partial sequence execute");
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.MoveDirection, Direction = "forward", Distance = 50f },
                new AgentAction { Verb = AgentVocabulary.Rotate, Angle = 200f }));
            f.Voice.SendChat("길게 직진한 뒤 여러 바퀴 돌아"); f.Tick(5, 100);
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected && f.Actions.Count == 0,
                "unsafe distance or rotation executed");
        }));
        await Test("API errors and missing key have distinct notices and no movement", () => Sync(() =>
        {
            var missing = new Fixture(apiAvailable: false);
            missing.Voice.SendChat("오늘 임무의 상황을 알려 줄래?");
            Check(missing.Endings.Last().Message.Contains("키") && !missing.Voice.ApiReady, "missing API key was not distinguished");

            var f = new Fixture();
            OpenAIClient.Response = (request, token) =>
                Task.FromException<JObject>(new AgentApiException("OpenAI 요청 한도에 도달했습니다."));
            f.Voice.SendChat("오늘 임무의 상황을 알려 줄래?");
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Failed &&
                f.Endings.Last().Message.Contains("한도") && f.Voice.LastApiMetrics?.Outcome == AgentApiOutcome.Failed &&
                !f.Runner.ExplorationEnabled && f.Body.transform.position.sqrMagnitude == 0,
                "failed API call was reported as a successful action");
        }));
        await Test("API spatial selection must agree with the robot heading and live scene", () => Sync(() =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(-2, 0, 3));
            var redReceiver = red.gameObject.AddComponent<ButtonReceiver>(); red.receiver = redReceiver;
            red.transform.position = new Vector3(-2, 0, 4);
            var blue = CreateButton("blue_button", "파란 버튼", new Vector3(2, 0, 3));
            var blueReceiver = blue.gameObject.AddComponent<ButtonReceiver>(); blue.receiver = blueReceiver;
            blue.transform.position = new Vector3(2, 0, 4);
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.Interact, Target = "red_button" }));
            f.Voice.SendChat("오른쪽에 있는 버튼 좀 눌러"); f.Tick(5, 100);
            Check(redReceiver.PressCount == 0 && f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected,
                "wrong side button was allowed");
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.Interact, Target = "blue_button" }));
            f.Voice.SendChat("오른쪽에 있는 버튼 좀 눌러"); f.Tick(5, 100);
            Check(blueReceiver.PressCount == 1 && f.Endings.Last().Outcome == AgentExecutionOutcome.Completed,
                "unique right-side scene button was not pressed");
        }));
        await Test("replacement API request records the interrupted action and orders wait then speak", () => Sync(() =>
        {
            var f = new Fixture();
            f.Voice.SendChat("앞으로 5미터 가"); f.Tick(1.5f, 30);
            Check(f.Body.IsMoving, "context fixture did not move");
            string state = null;
            var notices = new List<string>();
            f.Voice.LocalNotice += notices.Add;
            f.Runner.intentDelaySeconds = 0f;
            OpenAIClient.Response = (request, token) =>
            {
                state = (string)request["input"][0]["content"];
                return Task.FromResult(Reply(0, new AgentAction { Verb = AgentVocabulary.Wait },
                    new AgentAction { Verb = AgentVocabulary.Speak, Text = "준비됐습니다." }));
            };
            f.Voice.SendChat("잠시 기다렸다 준비됐다고 말해 줘");
            f.Tick(0.5f, 10);
            Check(notices.Count == 0 && f.Actions.Count == 0, "speak ran before wait");
            f.Tick(2, 40);
            Check(f.Actions.Count == 2 && notices.Single().Contains("준비됐습니다") &&
                state.Contains("진행 중인 행동: move_direction") && state.Contains("마지막 행동: move_direction") &&
                state.Contains("/ 실패"), "interrupted context or wait/speak ordering failed");
        }));
        await Test("six red box labels resolve to the same registered pickup", () => Sync(() =>
        {
            foreach (string label in new[] { "빨간 상자", "빨간상자", "빨간색 상자", "붉은 상자", "RedBox", "red box" })
            {
                var f = new Fixture(apiAvailable: false);
                f.Box.gameObject.name = "RedBox";
                f.Voice.SendChat(label + " 집어."); f.Tick(8, 160);
                Check(f.Stage.inventory.SequenceEqual(new[] { "red_box" }) &&
                    f.Endings.Last().Outcome == AgentExecutionOutcome.Completed, "label did not resolve: " + label);
            }
        }));
        await Test("color-only answer resumes ambiguous pickup and can correct its color", () => Sync(() =>
        {
            foreach (string reply in new[] { "빨간색.", "파란색 말고 빨간색." })
            {
                var f = new Fixture(apiAvailable: false);
                var blue = new GameObject().AddComponent<AgentInteractable>();
                blue.id = "blue_box"; blue.displayName = "파란 상자";
                blue.allowedVerbs.Add(AgentVocabulary.PickUp);
                f.Voice.SendChat("상자 집어.");
                Check(f.Voice.IsAwaitingTarget && f.Endings.Last().Message.Contains("2개") &&
                    f.Stage.inventory.Count == 0, "generic pickup did not ask");
                f.Voice.SendChat(reply); f.Tick(8, 160);
                Check(!f.Voice.IsAwaitingTarget && f.Stage.inventory.SequenceEqual(new[] { "red_box" }),
                    "follow-up did not resume original pickup: " + reply);
            }
        }));
        await Test("remembered target and authored metadata support pickup", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Box.displayName = "보급 상자";
            f.Box.id = "supply_a";
            f.Box.color = "red";
            f.Box.aliases.Add("보급품 A");
            f.Voice.SendChat("빨간색 상자 집어."); f.Tick(8, 160);
            Check(f.Stage.inventory.Contains("supply_a"), "explicit color was not respected");
            f.Submit(new AgentAction { Verb = AgentVocabulary.Drop, Target = "supply_a" }); f.Tick(5, 100);
            f.Voice.SendChat("그거 집어."); f.Tick(8, 160);
            Check(f.Stage.inventory.Contains("supply_a"), "reference did not reuse selected object");
        }));
        await Test("missing and duplicate boxes ask without guessing, and pickup permission is explicit", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Voice.SendChat("초록 상자 집어.");
            Check(f.Voice.IsAwaitingTarget && f.Endings.Last().Message.Contains("찾지 못했습니다"), "missing box misreported");
            f.Voice.SendChat("저쪽에 있는 빨간 상자."); f.Tick(8, 160);
            Check(f.Stage.inventory.Contains(f.Box.id), "deictic clarification did not recover");
            f = new Fixture(apiAvailable: false);
            var duplicate = new GameObject().AddComponent<AgentInteractable>();
            duplicate.id = "other_red"; duplicate.displayName = "빨간 상자";
            f.Voice.SendChat("빨간 상자 집어.");
            f.Voice.SendChat("빨간색.");
            Check(f.Voice.IsAwaitingTarget && f.Endings.Last().Message.Contains("2개") &&
                f.Stage.inventory.Count == 0, "duplicate box was guessed");
            f.Voice.SendChat("other_red.");
            Check(!f.Voice.IsAwaitingTarget && f.Endings.Last().Message.Contains("집을 수 없는") &&
                f.Stage.inventory.Count == 0, "pickup permissions were skipped");
        }));
        await Test("button clarification uses authored door alias", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var red = CreateButton("red_button", "빨간 버튼", new Vector3(0, 0, 3), "문 옆에 있는");
            var redReceiver = red.gameObject.AddComponent<ButtonReceiver>(); red.receiver = redReceiver;
            var blue = CreateButton("blue_button", "파란 버튼", new Vector3(2, 0, 3));
            var blueReceiver = blue.gameObject.AddComponent<ButtonReceiver>(); blue.receiver = blueReceiver;
            f.Voice.SendChat("버튼 눌러.");
            Check(f.Voice.IsAwaitingTarget, "button intent was dropped");
            f.Voice.SendChat("문 옆에 있는 거."); f.Tick(8, 160);
            Check(redReceiver.PressCount == 1 && blueReceiver.PressCount == 0, "door alias selected wrong button");
        }));
        await Test("cancel and replacement discard a remembered unresolved pickup", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Voice.SendChat("초록 상자 집어.");
            f.Voice.RequestStop();
            f.Voice.SendChat("빨간색."); f.Tick(5, 100);
            Check(!f.Voice.IsAwaitingTarget && f.Stage.inventory.Count == 0, "stopped pickup revived");
            f.Voice.SendChat("초록 상자 집어.");
            f.Voice.SendChat("오른쪽으로 돌아."); f.Tick(5, 100);
            f.Voice.SendChat("빨간색."); f.Tick(5, 100);
            Check(!f.Voice.IsAwaitingTarget && f.Stage.inventory.Count == 0, "replaced pickup revived");
        }));
        await Test("Unity resolves API pickup intent to a real ID before execution", () => Sync(() =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.PickUp, Target = "RedBox" }));
            f.Voice.SendChat("빨간색 상자 집어."); f.Tick(8, 160);
            Check(f.Stage.inventory.SequenceEqual(new[] { "red_box" }), "raw model label was treated as an actual ID");
        }));
        await Test("free-form API pickup still accepts model intent with registered evidence", () => Sync(() =>
        {
            var f = new Fixture();
            int calls = 0;
            OpenAIClient.Response = (request, token) =>
            {
                ++calls;
                return Task.FromResult(Reply(0, new AgentAction { Verb = AgentVocabulary.PickUp, Target = "red_box" }));
            };
            f.Voice.SendChat("손에 들 수 있게 붉은 상자를 들어 올려."); f.Tick(8, 160);
            Check(calls == 1 && f.Stage.inventory.SequenceEqual(new[] { "red_box" }),
                "free-form API pickup required exact local pickup wording");
        }));
        await Test("API pickup repair keeps original target identity and permissions", async () =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            f.Box.minTrust = 60;
            var proposal = new AgentAction { Verb = AgentVocabulary.PickUp, Target = "red_box" };
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(10, proposal));
            f.Voice.SendChat("빨간 상자 집어."); f.Tick(8, 160);
            Check(f.Stage.inventory.Count == 0 && f.Endings.Last().Message.Contains("신뢰도"),
                "repair bypassed trust at interpretation");

            var delayed = new TaskCompletionSource<JObject>();
            f.Box.minTrust = 0;
            OpenAIClient.Response = (request, token) => delayed.Task;
            f.Voice.SendChat("빨간 상자 집어.");
            f.Box.enabled = false;
            var replacement = new GameObject().AddComponent<AgentInteractable>();
            replacement.id = "red_box"; replacement.displayName = "빨간 상자";
            replacement.allowedVerbs.Add(AgentVocabulary.PickUp);
            delayed.SetResult(Reply(0, proposal));
            await Task.Yield();
            f.Tick(8, 160);
            Check(f.Stage.inventory.Count == 0 && f.Voice.IsAwaitingTarget &&
                f.Endings.Last().Message.Contains("대상이 바뀌었습니다"), "late repair rebound to a new instance");
        });
        await Test("typed API clarification is resumable and late clarification cannot revive", async () =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            var answer = Reply();
            answer["output"][0]["content"][0]["text"] = JsonConvert.SerializeObject(new AgentResponse
            { Comprehension = AgentVocabulary.Asking, ClarifyVerb = AgentVocabulary.PickUp });
            var late = new TaskCompletionSource<JObject>();
            OpenAIClient.Response = (request, token) => late.Task;
            f.Voice.SendChat("손에 들 물건을 정하기 전에 먼저 물어봐 줘.");
            f.Voice.RequestStop();
            late.SetResult(answer);
            await Task.Delay(30);
            Check(!f.Voice.IsAwaitingTarget && f.Stage.inventory.Count == 0, "late response recreated pending selection");
            OpenAIClient.Response = (request, token) => Task.FromResult(answer);
            f.Voice.SendChat("가까이 가져와서 손에 들어 줘.");
            Check(f.Voice.IsAwaitingTarget, "valid typed clarification was ignored");
            f.Voice.SendChat("빨간 상자."); f.Tick(8, 160);
            Check(f.Stage.inventory.Contains("red_box"), "typed API clarification did not resume");
        });
        await Test("voice transcription and chat share the pending target", () => Sync(() =>
        {
            var f = new Fixture();
            var blue = new GameObject().AddComponent<AgentInteractable>();
            blue.id = "blue_box"; blue.displayName = "파란 상자";
            blue.allowedVerbs.Add(AgentVocabulary.PickUp);
            f.Voice.SendChat("상자 집어.");
            OpenAIClient.Transcription = () => Task.FromResult("빨간색.");
            f.Voice.StartVoiceInput();
            f.Voice.StopVoiceInput();
            f.Tick(8, 160);
            Check(!f.Voice.IsAwaitingTarget && f.Stage.inventory.SequenceEqual(new[] { "red_box" }),
                "voice follow-up discarded the chat pickup intent");
        }));
        await Test("repeated pickup reuses the same instance and does not apply carry twice", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            int instances = AgentInteractable.Active.Count;
            var physics = f.Box.gameObject.AddComponent<Rigidbody>();
            var collider = f.Box.gameObject.AddComponent<Collider>();
            f.Voice.SendChat("빨간 상자 집어");
            f.Voice.SendChat("빨간 상자 집어");
            f.Tick(8, 160);
            var heldPosition = f.Box.transform.position;
            Check(ReferenceEquals(f.Body.CurrentHeldObject, f.Box) && f.Box.transform.parent == f.Body.transform &&
                physics.isKinematic && !collider.enabled && f.Stage.inventory.SequenceEqual(new[] { f.Box.id }),
                "pickup did not attach and own the original object");
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(5, 100);
            Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected &&
                f.Actions.Count(action => action.Outcome == AgentExecutionOutcome.Completed) == 1 &&
                AgentInteractable.Active.Count == instances && Vector3.Distance(f.Box.transform.position, heldPosition) < 0.001f,
                "repeated pickup cloned, moved or reattached the box");
        }));
        await Test("occupied slot rejects another pickup and a mismatched explicit drop", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var blue = new GameObject().AddComponent<AgentInteractable>();
            blue.id = "blue_box"; blue.displayName = "파란 상자";
            blue.allowedVerbs.AddRange(new[] { AgentVocabulary.PickUp, AgentVocabulary.Drop });
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(8, 160);
            f.Voice.SendChat("파란 상자 집어"); f.Tick(5, 100);
            Check(f.Endings.Last().Message.Contains("내려놓") && blue.transform.parent == null &&
                f.Body.CurrentHeldObject == f.Box, "second pickup overwrote the occupied slot");
            f.Voice.SendChat("파란 상자 내려놔"); f.Tick(5, 100);
            Check(f.Endings.Last().Message.Contains("들고 있지") && f.Body.CurrentHeldObject == f.Box &&
                f.Box.transform.parent == f.Body.transform, "explicit mismatch dropped the wrong object");
        }));
        await Test("all held-object drop forms release the same box and restore physics", () => Sync(() =>
        {
            foreach (string text in new[] { "상자 내려놔", "들고 있는 상자 내려놔", "그거 내려놔",
                "내려놔", "손에 든 거 내려놔", "빨간 상자 내려놔" })
            {
                var f = new Fixture(apiAvailable: false);
                var physics = f.Box.gameObject.AddComponent<Rigidbody>();
                var collider = f.Box.gameObject.AddComponent<Collider>();
                var child = new GameObject();
                child.transform.SetParent(f.Box.transform, false);
                var disabledCollider = child.AddComponent<Collider>();
                disabledCollider.enabled = false;
                f.Voice.SendChat("빨간 상자 집어"); f.Tick(8, 160);
                f.Voice.SendChat(text); f.Tick(5, 100);
                Check(f.Endings.Last().Outcome == AgentExecutionOutcome.Completed && f.Body.CurrentHeldObject == null &&
                    f.Stage.inventory.Count == 0 && f.Box.transform.parent == null &&
                    !physics.isKinematic && collider.enabled && !disabledCollider.enabled,
                    "held slot or physics was not restored: " + text);
            }
        }));
        await Test("carry move uses physical possession and repick uses the last actual drop", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Voice.SendChat("내려놔");
            Check(f.Endings.Last().Message.Contains("현재 들고 있는 물체가 없습니다"), "empty drop did not explain");
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(8, 160);
            float start = f.Body.transform.position.z;
            f.Voice.SendChat("그거 들고 앞으로 두 칸 가"); f.Tick(6, 120);
            Check(Math.Abs(f.Body.transform.position.z - start - 2f) < 0.001f &&
                f.Body.CurrentHeldObject == f.Box, "carry move re-picked or failed to move");
            f.Voice.SendChat("이제 내려놔"); f.Tick(5, 100);
            Check(f.Body.CurrentHeldObject == null && f.Body.LastDroppedObject == f.Box, "successful drop was not remembered");
            var decoy = new GameObject().AddComponent<AgentInteractable>(); decoy.id = "decoy";
            f.Runner.SelectTarget(decoy);
            f.Voice.SendChat("다시 집어"); f.Tick(8, 160);
            Check(f.Body.CurrentHeldObject == f.Box && decoy.transform.parent == null,
                "repick used conversation selection instead of actual recent drop");
        }));
        await Test("cancel and failed pickup or drop preserve the physical state", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(1.4f, 28); f.Voice.RequestStop(); f.Tick(5, 100);
            Check(f.Body.CurrentHeldObject == null && f.Box.transform.parent == null, "cancelled pickup attached");
            f.Body.moveSpeed = 0;
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(5, 100);
            Check(f.Body.CurrentHeldObject == null && f.Endings.Last().Outcome == AgentExecutionOutcome.Failed, "failed pickup occupied slot");
            f.Body.moveSpeed = 3;
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(8, 160);
            f.Voice.SendChat("내려놔"); f.Tick(0.5f, 10); f.Voice.RequestStop(); f.Tick(5, 100);
            Check(f.Body.CurrentHeldObject == f.Box && f.Box.transform.parent == f.Body.transform, "cancelled drop released");
            f.Box.allowedVerbs.Remove(AgentVocabulary.Drop);
            f.Voice.SendChat("내려놔"); f.Tick(5, 100);
            Check(f.Body.CurrentHeldObject == f.Box && f.Stage.inventory.SequenceEqual(new[] { f.Box.id }) &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected, "failed drop lost the held object");
        }));
        await Test("disabled carried target is detached and removed from the projected inventory", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            var physics = f.Box.gameObject.AddComponent<Rigidbody>();
            var collider = f.Box.gameObject.AddComponent<Collider>();
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(8, 160);
            Check(f.Body.CurrentHeldObject == f.Box, "fixture failed to carry");
            f.Voice.SendChat("그거 들고 앞으로 두 칸 가"); f.Tick(1.4f, 28);
            f.Box.enabled = false;
            Vector3 stoppedAt = f.Body.transform.position;
            f.Tick(5, 100);
            Check(f.Stage.BuildForCommand().Inventory.Count == 0 && f.Body.CurrentHeldObject == null &&
                f.Box.transform.parent == null && collider.enabled && !physics.isKinematic &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Rejected &&
                Vector3.Distance(f.Body.transform.position, stoppedAt) < 0.001f,
                "disabled target left a ghost inventory or altered physics");
        }));
        await Test("API drop uses the held ID rather than selected target", () => Sync(() =>
        {
            var f = new Fixture();
            f.Voice.SendChat("빨간 상자 집어"); f.Tick(8, 160);
            var decoy = new GameObject().AddComponent<AgentInteractable>();
            decoy.id = "blue_box"; decoy.displayName = "파란 상자"; decoy.allowedVerbs.Add(AgentVocabulary.Drop);
            f.Runner.SelectTarget(decoy);
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            string state = null;
            OpenAIClient.Response = (request, token) =>
            {
                state = (string)request["input"][0]["content"];
                return Task.FromResult(Reply(0, new AgentAction { Verb = AgentVocabulary.Drop }));
            };
            f.Voice.SendChat("손에 쥔 물건을 바닥에 두세요"); f.Tick(6, 120);
            Check(state != null && state.Contains("현재 들고 있는 물체 ID: red_box") &&
                state.Contains("직전에 선택한 대상 ID: blue_box") && f.Body.CurrentHeldObject == null &&
                f.Body.LastDroppedObject == f.Box && f.Endings.Last().Outcome == AgentExecutionOutcome.Completed,
                "API and physical ownership diverged");
        }));
        await Test("reverse-order duplicate API carry requests mutate ownership only once", async () =>
        {
            var f = new Fixture();
            f.Voice.commandRouting = AgentCommandRouting.ApiInterpretation;
            var old = new TaskCompletionSource<JObject>();
            int calls = 0;
            var pickup = new AgentAction { Verb = AgentVocabulary.PickUp, Target = "red_box" };
            OpenAIClient.Response = (request, token) => ++calls == 1 ? old.Task : Task.FromResult(Reply(0, pickup));
            f.Voice.SendChat("빨간 상자 집어");
            f.Voice.SendChat("빨간 상자 집어");
            f.Tick(8, 160);
            old.SetResult(Reply(0, pickup));
            await Task.Yield();
            f.Tick(5, 100);
            Check(calls == 2 && f.Body.CurrentHeldObject == f.Box &&
                f.Actions.Count(action => action.Outcome == AgentExecutionOutcome.Completed) == 1 &&
                f.Stage.inventory.Count == 1, "stale transport reapplied pickup");
        });
        await Test("held category mismatch and disabled robot preserve physical ownership invariants", () => Sync(() =>
        {
            var f = new Fixture(apiAvailable: false);
            f.Box.id = "keycard"; f.Box.displayName = "카드키";
            var collider = f.Box.gameObject.AddComponent<Collider>();
            f.Voice.SendChat("카드키 집어"); f.Tick(8, 160);
            Check(f.Body.CurrentHeldObject == f.Box, "fixture failed to carry a non-box");
            f.Voice.SendChat("상자 내려놔"); f.Tick(5, 100);
            Check(f.Body.CurrentHeldObject == f.Box && !collider.enabled, "box command dropped the held keycard");
            f.Body.enabled = false;
            Check(f.Body.CurrentHeldObject == null && f.Stage.inventory.Count == 0 &&
                f.Box.transform.parent == null && collider.enabled, "disabled robot retained a kinematic attachment");
        }));
        await Test("API sequence fills a bare drop from its preceding pickup", () => Sync(() =>
        {
            var f = new Fixture();
            OpenAIClient.Response = (request, token) => Task.FromResult(Reply(0,
                new AgentAction { Verb = AgentVocabulary.PickUp, Target = "red_box" },
                new AgentAction { Verb = AgentVocabulary.Drop }));
            f.Voice.SendChat("빨간 상자를 집고 나서 내려놓아"); f.Tick(10, 200);
            Check(f.Actions.Count == 2 && f.Body.CurrentHeldObject == null && f.Body.LastDroppedObject == f.Box &&
                f.Endings.Last().Outcome == AgentExecutionOutcome.Completed, "API lost carry state between steps");
        }));
        Console.WriteLine($"TOTAL: {_passed} passed, {_failed} failed, {_skipped} skipped. Unity Play Mode NOT executed.");
        return _failed == 0 ? 0 : 1;
    }
}
