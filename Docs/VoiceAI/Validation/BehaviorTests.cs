using System;
using System.Collections.Generic;
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
        public Fixture()
        {
            UnityEngine.Object.ResetWorld();
            OpenAIClient.Response = (r, t) => Task.FromResult(Reply(0, Move()));
            OpenAIClient.Transcription = () => Task.FromResult("앞으로 가");
            var host = new GameObject();
            Stage = host.AddComponent<SceneStageContext>();
            Stage.useDemoObjectsWhenEmpty = false;
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
        public RobotExplorationController AttachExplorer()
        {
            Box.allowAutonomousExploration = true;
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

    private static async Task Test(string name, Func<Task> run)
    { await run(); ++_passed; Console.WriteLine("PASS " + name); }
    private static Task Sync(Action run) { run(); return Task.CompletedTask; }

    public static async Task Main()
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
            var f = new Fixture(); f.Box.allowedVerbs.Add("press"); f.Submit(new AgentAction { Verb = "press", Target = "red_box" });
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
        await Test("Kyle controller asset matches exact parameters and blend thresholds", () => Sync(() =>
        {
            string path = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
            string controller = System.IO.File.ReadAllText(path);
            foreach (string parameter in new[] { "Speed", "MotionSpeed" })
                Check(System.Text.RegularExpressions.Regex.IsMatch(controller, "m_Name: " + parameter + @"\r?\n\s+m_Type: 1"), "float parameter mismatch");
            foreach (string parameter in new[] { "Grounded", "Jump", "FreeFall" })
                Check(System.Text.RegularExpressions.Regex.IsMatch(controller, "m_Name: " + parameter + @"\r?\n\s+m_Type: 4"), "bool parameter mismatch");
            Check(controller.Contains("m_Name: Idle Walk Run Blend") && controller.Contains("m_SpeedParameter: MotionSpeed"), "state multiplier mismatch");
            foreach (string threshold in new[] { "0", "2", "6" }) Check(controller.Contains("m_Threshold: " + threshold), "blend threshold mismatch");
        }));
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
            f.Box.transform.position = new Vector3(0, 0, 2); f.Stage.inventory.Add(f.Box.id); e.Tick(1);
            Check(!f.Runner.HasActiveCommand, "held selected");
            f.Stage.inventory.Clear(); var duplicate = new GameObject().AddComponent<AgentInteractable>(); duplicate.id = f.Box.id;
            e.Tick(1); Check(!f.Runner.HasActiveCommand, "duplicate selected");
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
        Console.WriteLine("TOTAL: " + _passed + " passed; Unity Play Mode NOT executed.");
    }
}
