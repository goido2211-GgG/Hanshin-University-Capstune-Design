using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AgentEscape.VoiceAI
{
    // Models propose IDs. Text, an explicit reference, or a unique unqualified target authorizes them.
    internal static class SceneCommandTargeting
    {
        private static readonly Regex ButtonMotion = new Regex(
            @"^(?<selector>.*?)\s*버튼\s*(?:으로\s*가(?:세요)?|앞(?:까지|으로)?\s*가서\s*(?<press>눌러)(?:\s*줘|주세요)?)$");
        private static readonly Regex ReferencedPress = new Regex(
            @"^(?:(?:이제|그럼)\s+)?(?:그거|그걸|그것을|그 버튼(?:을)?)\s*(?:좀\s*)?눌러(?:\s*줘|주세요)?$");
        private static readonly Regex NeighborCorrection = new Regex(
            @"^(?:아니\s*[,，]?\s*)?옆에\s*있는\s*버튼$");
        private static readonly Regex Reference = new Regex(
            @"^(?:(?:이제|그럼)\s+)?(?:그거|그걸|그것(?:을|에|으로)?|(?<button>그 버튼)(?:을|으로)?)(?=\s|$)");
        private static readonly Regex GenericTarget = new Regex(
            @"^(?:(?:이제|그럼|먼저|저기)\s+)?(?:(?:저|그|이)\s+)?(?<kind>버튼|상자)(?:으로|으로는|에|을|를)?(?=\s|$)");
        private static readonly Regex SideTarget = new Regex(
            @"^(?:(?:이제|그럼|먼저)\s+)?(?<side>왼쪽|오른쪽)(?:\s*에(?:\s*있는)?)?\s*(?<kind>버튼|상자)(?:으로|에|을|를)?(?=\s|$)");
        private static readonly Regex ImplicitObject = new Regex(
            @"^(?:(?:이제|그럼)\s+)?(?:(?:살펴\s*봐|조사해|확인해|집어|집고\s*내려놓아|내려놔|눌러)(?:\s*줘|주세요)?)$");
        private static readonly Regex PressRequest = new Regex(
            @"(?:눌러(?:\s*(?:줘|주세요|줄래|봐))?|누르(?:세요|자)|작동\s*(?:시켜|해))(?:\s*(?:[,.]\s*)?(?:그리고|그다음|그 다음)\s|$)");
        private static readonly Regex NegatedPress = new Regex(@"누르지|누르면\s*안|작동하지|작동시키지");

        internal static bool TryParseButtonPlan(string text, out string selector, out bool useSelected, out string[] verbs)
        {
            string normalized = Normalize(text);
            useSelected = ReferencedPress.IsMatch(normalized);
            selector = "";
            if (useSelected)
            {
                verbs = new[] { AgentVocabulary.Press };
                return true;
            }
            var match = ButtonMotion.Match(normalized);
            if (match.Success)
            {
                selector = match.Groups["selector"].Value.Trim();
                if (selector == "저" || selector == "이" || selector == "그") selector = "";
                verbs = match.Groups["press"].Success ? new[] { AgentVocabulary.MoveTo, AgentVocabulary.Press } :
                    new[] { AgentVocabulary.MoveTo };
                return true;
            }
            if (AgentCommandRules.TryParseButtonCommand(normalized, out string verb, out selector))
            {
                verbs = new[] { verb };
                return true;
            }
            verbs = null;
            return false;
        }

        internal static bool IsNeighborCorrection(string text) => NeighborCorrection.IsMatch(Normalize(text));

        internal static string CheckGrounding(string text, StageContext context, AgentAction[] actions)
        {
            string normalized = Normalize(text);
            var targeted = actions.Where(action => action.Target != null).ToArray();
            if (targeted.Length == 0) return null;

            var press = targeted.Where(action => action.Verb == AgentVocabulary.Press || action.Verb == AgentVocabulary.Interact).ToArray();
            if (press.Length > 0 && (!PressRequest.IsMatch(normalized) || NegatedPress.IsMatch(normalized)))
                return "버튼을 누르라는 명령인지 확인해 주세요.";
            if (press.Length > 1)
                return "한 번에 누를 버튼 하나의 이름이나 번호를 알려 주세요.";

            var candidates = CandidateIds(normalized, context, out bool hasNamedTargets);
            foreach (var action in targeted)
            {
                bool pressing = action.Verb == AgentVocabulary.Press || action.Verb == AgentVocabulary.Interact;
                bool canUseSeveralNames = hasNamedTargets && press.Length == 0 &&
                    (action.Verb == AgentVocabulary.MoveTo || action.Verb == AgentVocabulary.Inspect);
                if ((!canUseSeveralNames && candidates.Count != 1) || !candidates.Contains(action.Target))
                    return pressing ? "어느 버튼을 누를까요? 이름이나 번호를 알려 주세요." :
                        "어느 대상을 말씀하시는 건가요? 이름을 알려 주세요.";
            }
            return null;
        }

        private static IReadOnlyList<string> CandidateIds(string text, StageContext context, out bool hasNamedTargets)
        {
            var named = context.Objects.Where(item =>
                Mentions(text, item.Id) || Mentions(text, item.DisplayName) || Mentions(text, item.SceneName) ||
                item.Aliases.Any(alias => Mentions(text, alias))).Select(item => item.Id).ToList();
            hasNamedTargets = named.Count > 0;
            if (hasNamedTargets) return named;

            var reference = Reference.Match(text);
            if (reference.Success)
            {
                var selected = context.Objects.FirstOrDefault(item => item.Id == context.SelectedTargetId);
                return selected == null || (reference.Groups["button"].Success && !IsButton(selected))
                    ? Array.Empty<string>() : new[] { selected.Id };
            }
            var side = SideTarget.Match(text);
            if (side.Success && context.AgentHeading.HasValue && context.AgentPositionX.HasValue && context.AgentPositionZ.HasValue)
            {
                double heading = context.AgentHeading.Value * Math.PI / 180.0;
                double sideSign = side.Groups["side"].Value == "오른쪽" ? 1.0 : -1.0;
                double rightX = sideSign * Math.Cos(heading);
                double rightZ = -sideSign * Math.Sin(heading);
                string kind = side.Groups["kind"].Value;
                return context.Objects.Where(item => IsCategory(item, kind) &&
                    (item.PositionX - context.AgentPositionX) * rightX +
                    (item.PositionZ - context.AgentPositionZ) * rightZ > 0.15)
                    .Select(item => item.Id).ToArray();
            }
            var generic = GenericTarget.Match(text);
            if (generic.Success)
            {
                string kind = generic.Groups["kind"].Value;
                return context.Objects.Where(item => IsCategory(item, kind)).Select(item => item.Id).ToArray();
            }
            return ImplicitObject.IsMatch(text) ? context.Objects.Select(item => item.Id).ToArray() : Array.Empty<string>();
        }

        private static bool IsButton(StageObject item) => item.CanInteract.HasValue;

        private static bool IsCategory(StageObject item, string category) => category == "버튼" ? IsButton(item) :
            IsCategory(item.DisplayName, category) || IsCategory(item.SceneName, category);

        private static bool IsCategory(string name, string category) =>
            !string.IsNullOrWhiteSpace(name) && name.Trim().EndsWith(category, StringComparison.Ordinal);

        private static bool Mentions(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            label = Normalize(label);
            if (label == "버튼" || label == "상자") return false;
            return Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(label) +
                @"(?=$|[\s,.;!?，。！？]|(?:으로|에서|에게|에|을|를|이|가|과|와|도|만)(?=$|[\s,.;!?，。！？]))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string Normalize(string text) =>
            Regex.Replace((text ?? "").Trim().TrimEnd('.', '!', '?', '。', '！', '？').Trim(), @"\s+", " ");
    }
}
