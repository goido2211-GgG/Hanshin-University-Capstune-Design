using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private const string DoorOpenRequest = @"(?:열어|개방\s*(?:해|시켜))" +
            @"(?:\s*(?:줘|주세요|줄래|주시겠어요|주실래요|줄\s*수\s*있(?:어|겠어|을까)))?";
        private static readonly Regex DoorOpen = new Regex(
            @"^(?:(?:이제|그럼|먼저)\s+)?(?<descriptor>(?<door>.*?)\s*(?:출입문|문))(?:을)?\s*(?:좀\s*)?" +
            DoorOpenRequest + "$");
        private static readonly Regex DoorOpeningIntent = new Regex(
            @"문(?:을)?\s*(?:좀\s*)?" + DoorOpenRequest + @"(?:\s*(?:[,.]\s*)?(?:그리고|그다음|그 다음)\s|$)");
        private static readonly Regex GenericDoorClause = new Regex(
            @"(?:^|[,.;]\s*|(?:게|도록)\s+)(?:저\s*|이\s*)?(?:출입문|문)(?:을)?\s*(?:좀\s*)?" + DoorOpenRequest + "$");
        private static readonly Regex NegatedDoorOpening = new Regex(@"열지|열면\s*안|개방하지|개방시키지|열어도\s*(?:되|돼)");
        private static readonly Regex Pickup = new Regex(
            @"^(?<selector>.*?)\s*(?:을|를)?\s*(?:좀\s*)?(?:(?:집어|주워)(?:\s*줘|주세요|라)?|집으세요|주우세요)$");
        private const string DropPrefix = @"^(?:(?:이제|그럼)\s+)?(?<selector>.*?)\s*(?:을|를)?\s*(?:(?:바닥에|여기에?|이 자리에|제자리에|손에서)\s*)?(?:좀\s*)?";
        private static readonly Regex Drop = new Regex(DropPrefix +
            @"(?:(?:내려\s*(?:놔|놓아)|떨어뜨려)(?:\s*(?:줘|주세요|라))?|내려\s*놓으세요|떨어뜨리세요)$");
        private static readonly Regex HeldPlacement = new Regex(DropPrefix +
            @"(?:(?:두어|둬|놔|놓아|내려\s*(?:놔|놓아))(?:\s*(?:줘|주세요|줄래|줄 수 있어))?|두세요|놓으세요)$");
        private static readonly Regex ImpliedDrop = new Regex(
            @"^(?:상자|그거|그걸|그것(?:을)?|들고\s*있는\s*(?:상자|거|것|물건)|손에\s*(?:든|쥔|잡은|들고\s*있는|쥐고\s*있는)\s*(?:상자|거|것|물건))$");
        private static readonly Regex Repick = new Regex(
            @"^(?:이제\s+)?다시\s*(?:집어|주워)(?:\s*(?:줘|주세요))?$");
        private static readonly Regex CarryMove = new Regex(
            @"^(?:(?:이제|그럼)\s+)?(?:(?:그거|그걸|그것을)\s*)?들고\s+(?<move>.+)$");
        private const string PuzzlePrefix = @"^(?:(?:이제|그럼|먼저)\s+)?";
        private const string PuzzleBox = @"(?<box>[^,.;]*?(?:상자|그거|그걸|그것))(?:을|를)?\s*";
        private const string PuzzlePad = @"(?<pad>[^,.;]*?발판)\s*(?:위\s*)?(?:까지|으로|로|에)\s*";
        private const string PuzzleEnding = @"(?:\s*(?:줘|주세요|줄래|봐))?$";
        private const string PuzzleSetDown = @"(?:올려\s*(?:놔|놓아)|내려\s*(?:놔|놓아)|놓아|놔)";
        private static readonly Regex PuzzleRelease = new Regex(
            PuzzlePrefix + PuzzleBox + @"(?:좀\s*)?떨어\s*뜨려" + PuzzleEnding);
        private static readonly Regex PuzzleTravel = new Regex(
            PuzzlePrefix + "(?:" + PuzzleBox + ")?" + PuzzlePad + @"(?:좀\s*)?(?:옮겨|가)" + PuzzleEnding);
        private static readonly Regex PuzzlePlace = new Regex(
            PuzzlePrefix + "(?:" + PuzzleBox + ")?" + PuzzlePad + @"(?:좀\s*)?" + PuzzleSetDown + PuzzleEnding);
        private static readonly Regex PuzzleCompound = new Regex(
            PuzzlePrefix + PuzzleBox + @"(?:가져다가?|집어서|집고|주워서)\s*" + PuzzlePad +
            @"(?:좀\s*)?" + PuzzleSetDown + PuzzleEnding);
        private static readonly Regex PickupIntent = new Regex(
            @"(?:집어|집고|집으세요|집을래|주워|줍고|주우세요|들어\s*올려|들고|회수해|가져와)(?=$|[\s,.!?。！？]|줘|주세요|줄래|라)");
        private static readonly Regex NegatedPickup = new Regex(
            @"집지|줍지|집으면\s*안|주우면\s*안|들지\s*말|들어\s*올리지|회수하지|손대지|건드리지");
        private static readonly Regex ConditionalAction = new Regex(@"만약|있으면|가능하면|필요하면|필요하다면|라면|다음에");
        private static readonly Regex NegatedDrop = new Regex(
            @"내려\s*놓지|내려\s*놓으면\s*안|떨어뜨리지|떨어뜨리면\s*안");
        private static readonly Regex SimpleCategory = new Regex(
            @"^(?:(?:[가-힣]+|[a-z0-9]+)\s*)?(?:상자|버튼|box|button)(?:\s*[a-z0-9]+(?:번)?)?(?:이요|을|를|요)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex AdditionalAction = new Regex(
            @"가서|가고|이동해|돌아|회전해|살펴봐|살펴보고|조사해|조사하고|확인해|확인하고|찾아서|눌러|누르고|놓고|내려놓|집은\s*뒤|멈춰|기다려|(?:집어|집고|주워|줍고|집으세요|주우세요)(?=$|[\s,.!?])");
        private static readonly Regex SelectionPrefix = new Regex(
            @"^(?:아니\s*[,，]?\s*|(?:부탁인데|먼저|요원아)\s*[,，]?\s+|저쪽(?:에\s*(?:있는\s*)?)?\s+|저기(?:에\s*(?:있는\s*)?)?\s+|(?:저|이|그)\s+)+");
        private static readonly Regex ReferenceSelector = new Regex(@"^(?:그거|그걸|그것(?:을)?|(?<kind>그\s+(?:버튼|상자)))$");
        private static readonly Regex RelationSelector = new Regex(@"(?:옆|근처)(?:에)?(?:\s*있는)?(?:\s*(?:거|것|버튼|상자))?$");
        private static readonly Regex ColorPhrase = new Regex(
            @"(?<![\p{L}\p{N}_])(?:[가-힣]+(?:\s*색)?\s*(?:상자|버튼)|[a-z]+[\s_-]*(?:box|button))" +
            @"(?=$|[\s,.;!?，。！？]|(?:으로|에서|에게|에|을|를|이|가|과|와|도|만)(?=$|[\s,.;!?，。！？]))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Dictionary<string, string> Colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["red"] = "red", ["빨강"] = "red", ["빨강색"] = "red", ["빨간"] = "red", ["빨간색"] = "red",
            ["붉은"] = "red", ["붉은색"] = "red", ["적색"] = "red",
            ["blue"] = "blue", ["파랑"] = "blue", ["파랑색"] = "blue", ["파란"] = "blue", ["파란색"] = "blue",
            ["푸른"] = "blue", ["푸른색"] = "blue", ["청색"] = "blue",
            ["green"] = "green", ["초록"] = "green", ["초록색"] = "green", ["녹색"] = "green",
            ["yellow"] = "yellow", ["노랑"] = "yellow", ["노란"] = "yellow", ["노란색"] = "yellow", ["황색"] = "yellow",
            ["white"] = "white", ["흰"] = "white", ["흰색"] = "white", ["하얀"] = "white", ["하얀색"] = "white", ["백색"] = "white",
            ["black"] = "black", ["검정"] = "black", ["검정색"] = "black", ["검은"] = "black", ["검은색"] = "black", ["흑색"] = "black"
        };

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

        internal static bool TryParseDoorOpen(string text, out string selector)
        {
            string normalized = Normalize(text);
            var match = DoorOpen.Match(normalized);
            if (!match.Success || !HasDoorOpeningIntent(normalized) ||
                AdditionalAction.IsMatch(match.Groups["door"].Value))
            {
                selector = null;
                return false;
            }
            string door = match.Groups["door"].Value.Trim();
            selector = door is "" or "저" or "이" ? "" : match.Groups["descriptor"].Value;
            return true;
        }

        private static bool HasDoorOpeningIntent(string normalized) => DoorOpeningIntent.IsMatch(normalized) &&
            !NegatedDoorOpening.IsMatch(normalized) && !ConditionalAction.IsMatch(normalized);

        internal static bool IsSecondDoorSelector(string selector) =>
            Regex.IsMatch(MatchKey(selector), @"^(?:두번째|둘째|2번째|2번|2)(?:출입문|문)$");

        private static bool HasButtonPressIntent(string normalized) => PressRequest.IsMatch(normalized) &&
            !NegatedPress.IsMatch(normalized) && !ConditionalAction.IsMatch(normalized);

        internal static bool IsNeighborCorrection(string text) => NeighborCorrection.IsMatch(Normalize(text));

        internal static bool TryParseTargetCommand(string text, out string verb, out string selector)
        {
            verb = null;
            selector = null;
            string normalized = Normalize(text);
            if (normalized.Length > 160 || HasQuestionMark(text) || NegatedPickup.IsMatch(normalized) ||
                ConditionalAction.IsMatch(normalized)) return false;
            var match = Pickup.Match(normalized);
            if (!match.Success || AdditionalAction.IsMatch(match.Groups["selector"].Value)) return false;
            selector = match.Groups["selector"].Value.Trim();
            verb = AgentVocabulary.PickUp;
            return true;
        }

        internal static bool TryParseDropCommand(string text, out string selector)
        {
            selector = null;
            string normalized = Normalize(text);
            if (normalized.Length > 160 || HasQuestionMark(text) || NegatedDrop.IsMatch(normalized) ||
                ConditionalAction.IsMatch(normalized)) return false;
            return TryReadDropSelector(Drop.Match(normalized), out selector);
        }

        internal static bool IsImplicitDropReference(string selector) =>
            string.IsNullOrWhiteSpace(selector) || ImpliedDrop.IsMatch(Normalize(selector));

        private static bool TryReadDropSelector(Match match, out string selector)
        {
            selector = null;
            if (!match.Success) return false;
            string descriptor = match.Groups["selector"].Value.Trim();
            if (AdditionalAction.IsMatch(descriptor) || Drop.IsMatch(descriptor)) return false;
            selector = !IsImplicitDropReference(descriptor) ? descriptor :
                Regex.IsMatch(descriptor, @"상자(?:를)?$") ? "상자" : "";
            return true;
        }

        internal static bool IsRepickCommand(string text) => !HasQuestionMark(text) && Repick.IsMatch(Normalize(text));

        internal enum BoxPuzzleAction { Release, Travel, Place, PickUpAndPlace }

        internal static bool TryParseBoxPuzzleCommand(string text, out BoxPuzzleAction action,
            out string boxSelector, out string padSelector)
        {
            action = default;
            boxSelector = padSelector = null;
            string normalized = Normalize(text);
            if (normalized.Length > 160 || HasQuestionMark(text) || ConditionalAction.IsMatch(normalized) ||
                NegatedPickup.IsMatch(normalized) || NegatedDrop.IsMatch(normalized)) return false;

            var match = PuzzleRelease.Match(normalized);
            if (!match.Success)
            {
                action = BoxPuzzleAction.PickUpAndPlace;
                match = PuzzleCompound.Match(normalized);
            }
            if (!match.Success)
            {
                action = BoxPuzzleAction.Travel;
                match = PuzzleTravel.Match(normalized);
            }
            if (!match.Success)
            {
                action = BoxPuzzleAction.Place;
                match = PuzzlePlace.Match(normalized);
            }
            if (!match.Success) return false;
            boxSelector = match.Groups["box"].Value.Trim();
            padSelector = match.Groups["pad"].Value.Trim();
            return !AdditionalAction.IsMatch(boxSelector) && !AdditionalAction.IsMatch(padSelector);
        }

        internal static bool TryParseCarryMove(string text, out AgentAction[] actions)
        {
            actions = Array.Empty<AgentAction>();
            string normalized = Normalize(text);
            if (normalized.Length > 160 || HasQuestionMark(text)) return false;
            var match = CarryMove.Match(normalized);
            if (!match.Success || !AgentCommandRules.TryParseMotionCommand(match.Groups["move"].Value, out var motion) ||
                motion.Length == 0 || motion[0].Verb != AgentVocabulary.MoveDirection) return false;
            actions = motion;
            return true;
        }

        internal static bool HasPickupIntent(string text)
        {
            string normalized = Normalize(text);
            return PickupIntent.IsMatch(normalized) && !NegatedPickup.IsMatch(normalized);
        }

        internal static bool IsSelectionReply(string text, StageContext context = null)
        {
            string normalized = Normalize(text);
            if (normalized.Length == 0 || normalized.Length > 80 || HasQuestionMark(text) ||
                HasPickupIntent(normalized) || NegatedPickup.IsMatch(normalized) || AdditionalAction.IsMatch(normalized) ||
                PressRequest.IsMatch(normalized) || AgentCommandRules.IsStop(normalized) ||
                TryParseDropCommand(text, out _) || TryParseCarryMove(text, out _) ||
                AgentCommandRules.TryParseMotionCommand(normalized, out _)) return false;

            string descriptor = SelectionText(normalized);
            return ReferenceSelector.IsMatch(normalized) || TryReadColor(descriptor, out _, out _) ||
                RelationSelector.IsMatch(descriptor) || SideTarget.IsMatch(descriptor) ||
                SimpleCategory.IsMatch(descriptor) ||
                Regex.IsMatch(descriptor, @"^\d+\s*번$") ||
                (context != null && ResolveSelection(normalized, context).Count > 0);
        }

        internal static IReadOnlyList<StageObject> ResolveSelection(string selector, StageContext context,
            IReadOnlyCollection<string> allowedIds = null)
        {
            var items = context.Objects.Where(item => item != null &&
                (allowedIds == null || allowedIds.Contains(item.Id))).ToArray();
            string normalized = Normalize(selector);
            if (normalized.Length == 0) return items;

            var reference = ReferenceSelector.Match(normalized);
            if (reference.Success)
            {
                string kind = reference.Groups["kind"].Value;
                return items.Where(item => item.Id == context.SelectedTargetId &&
                    (kind.Length == 0 || IsCategory(item, kind.EndsWith("버튼", StringComparison.Ordinal) ? "버튼" : "상자"))).ToArray();
            }

            string descriptor = SelectionText(normalized);
            if (descriptor.Length == 0) return Array.Empty<StageObject>();
            var side = SideTarget.Match(descriptor);
            if (side.Success && side.Length == descriptor.Length)
                return OnSide(items, context, side).ToArray();

            var matches = ResolveDescriptor(items, descriptor);
            if (matches.Count != 0) return matches;
            string withoutParticle = Regex.Replace(descriptor, @"(?:이요|을|를|요)$", "").TrimEnd();
            return withoutParticle == descriptor || withoutParticle.Length == 0
                ? matches : ResolveDescriptor(items, withoutParticle);
        }

        private static IReadOnlyList<StageObject> ResolveDescriptor(StageObject[] items, string descriptor)
        {
            string key = MatchKey(descriptor);
            if (key.Length == 0) return Array.Empty<StageObject>();
            // An authored technical ID is sufficient when two objects share the same display descriptor.
            if (descriptor.Contains("_") || descriptor.Contains("-"))
            {
                var ids = items.Where(item => string.Equals(item.Id, descriptor, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (ids.Length > 0) return ids;
            }
            string kind = CategoryWord(key);
            bool colorMatch = TryReadColor(descriptor, out string color, out string colorKind);
            if (colorMatch && colorKind == null) return items.Where(item => HasColor(item, color)).ToArray();
            string relation = RelationKey(descriptor);
            return items.Where(item => (kind != null && IsCategory(item, kind)) ||
                (colorMatch && IsCategory(item, colorKind) && HasColor(item, color)) ||
                Names(item).Any(name => MatchKey(name) == key || (relation != null && RelationKey(name) == relation)) ||
                item.DoorAliases.Any(alias => MatchKey(alias) == key)).ToArray();
        }

        internal static IReadOnlyList<StageObject> ResolveButtonSelection(string selector, StageContext context,
            IReadOnlyCollection<string> allowedIds = null, bool doorsOnly = false)
        {
            var items = context.Objects.Where(item => IsButton(item) && (!doorsOnly || item.DoorAliases.Count > 0) &&
                (allowedIds == null || allowedIds.Contains(item.Id))).ToArray();
            if (!string.IsNullOrWhiteSpace(selector))
            {
                if (doorsOnly)
                {
                    string key = MatchKey(selector);
                    if (key == "그문" || key == "그출입문")
                        return items.Where(item => item.Id == context.SelectedTargetId).ToArray();
                    return items.Where(item => item.DoorAliases.Any(alias => MatchKey(alias) == key)).ToArray();
                }
                var ids = items.Select(item => item.Id).ToArray();
                var matches = ResolveSelection(selector, context, ids);
                return matches.Count > 0 ? matches : ResolveSelection(selector + " 버튼", context, ids);
            }
            if (items.Length <= 1) return items;

            var sameRoom = context.ButtonRoomId == 0 ? items :
                items.Where(item => item.ButtonRoomId == context.ButtonRoomId).ToArray();
            if (sameRoom.Length == 1) return sameRoom;
            if (sameRoom.Length == 0) return items;
            // Unconfigured scenes retain explicit clarification and recent-target behavior.
            var ranked = sameRoom.Where(item => item.ButtonRoomId > 0 && item.CanInteract == true && item.ButtonApproachDistance.HasValue)
                .OrderBy(item => item.ButtonApproachDistance.Value).ToArray();
            if (ranked.Length == sameRoom.Length)
            {
                var nearest = ranked[0];
                float distance = nearest.ButtonApproachDistance.Value;
                bool recentlySelected = context.SelectedTargetId == nearest.Id;
                if (distance <= (recentlySelected ? 3f : 1.5f) && ranked[1].ButtonApproachDistance.Value - distance >= 1.5f)
                    return new[] { nearest };
            }
            return sameRoom;
        }

        internal static string CheckGrounding(string text, StageContext context, AgentAction[] actions)
        {
            string normalized = Normalize(text);
            var targeted = actions.Where(action => action.Target != null).ToArray();
            if (targeted.Length == 0) return null;

            var press = targeted.Where(action => action.Verb == AgentVocabulary.Press || action.Verb == AgentVocabulary.Interact).ToArray();
            if (targeted.Any(action => action.Verb == AgentVocabulary.PickUp) &&
                (NegatedPickup.IsMatch(normalized) || (HasQuestionMark(text) && !HasPickupIntent(text))))
                return "물체를 집으라는 지시인지 확인해 주세요.";
            if (targeted.Any(action => action.Verb == AgentVocabulary.Drop) && NegatedDrop.IsMatch(normalized))
                return "물체를 내려놓으라는 지시인지 확인해 주세요.";
            if (actions.Length == 1 && targeted[0].Verb == AgentVocabulary.Drop && context.Inventory.Count == 1 &&
                context.Inventory[0] == targeted[0].Target && context.Objects.Any(item => item.Id == targeted[0].Target) &&
                (TryParseDropCommand(normalized, out string dropSelector) ||
                    TryReadDropSelector(HeldPlacement.Match(normalized), out dropSelector)))
                return dropSelector.Length == 0 || ResolveSelection(dropSelector, context, context.Inventory).Count == 1
                    ? null : "지정한 물체를 들고 있지 않습니다.";
            bool doorOpening = HasDoorOpeningIntent(normalized);
            if (press.Length > 0 && !doorOpening && !HasButtonPressIntent(normalized))
                return "버튼을 누르라는 명령인지 확인해 주세요.";
            if (press.Length > 1)
                return "한 번에 누를 버튼 하나의 이름이나 번호를 알려 주세요.";

            bool hasNamedTargets = false;
            var candidates = press.Length > 0 ? PressCandidateIds(normalized, context) :
                CandidateIds(normalized, context, out hasNamedTargets);
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

        internal static IReadOnlyList<string> PressCandidateIds(string text, StageContext context)
        {
            string normalized = Normalize(text);
            if (HasDoorOpeningIntent(normalized))
            {
                if (TryParseDoorOpen(normalized, out string doorSelector))
                {
                    var simple = ResolveButtonSelection(doorSelector, context, doorsOnly: true);
                    if (simple.Count > 0 || doorSelector.Length == 0) return simple.Select(item => item.Id).ToArray();
                }
                var named = context.Objects.Where(item => IsButton(item) && item.DoorAliases.Any(alias => Mentions(normalized, alias)))
                    .Select(item => item.Id).ToArray();
                if (named.Length > 0 || !GenericDoorClause.IsMatch(normalized)) return named;
                return ResolveButtonSelection("", context, doorsOnly: true).Select(item => item.Id).ToArray();
            }
            if (!HasButtonPressIntent(normalized)) return Array.Empty<string>();

            if (TryParseButtonPlan(normalized, out string selector, out bool useSelected, out _))
            {
                var simple = ResolveButtonSelection(useSelected ? "그 버튼" : selector, context);
                if (simple.Count > 0 || selector.Length == 0) return simple.Select(item => item.Id).ToArray();
            }
            if (Reference.IsMatch(normalized))
                return CandidateIds(normalized, context, out _).Where(id => context.Objects.Any(item => item.Id == id && IsButton(item))).ToArray();

            var generic = GenericTarget.Match(normalized);
            if (generic.Success && generic.Groups["kind"].Value == "버튼" &&
                Regex.IsMatch(normalized.Substring(generic.Length), @"^\s*(?:좀\s*)?(?:앞(?:까지|으로)?\s*가서\s*)?" +
                    @"(?:눌러(?:\s*(?:줘|주세요|줄래|봐))?|누르(?:세요|자)|작동\s*(?:시켜|해))$"))
                return ResolveButtonSelection("", context).Select(item => item.Id).ToArray();

            return CandidateIds(normalized, context, out _).Where(id => context.Objects.Any(item => item.Id == id && IsButton(item))).ToArray();
        }

        private static IReadOnlyList<string> CandidateIds(string text, StageContext context, out bool hasDistinctNames)
        {
            if (TryParseTargetCommand(text, out _, out string selector) && selector.Length != 0)
            {
                var resolved = ResolveSelection(selector, context);
                if (resolved.Count > 0 || IsSelectionReply(selector))
                {
                    hasDistinctNames = resolved.Count == 1 && CategoryWord(MatchKey(SelectionText(selector))) == null;
                    return resolved.Select(item => item.Id).ToArray();
                }
            }
            var colorLabels = ColorPhrase.Matches(text).Cast<Match>()
                .Select(match => match.Value)
                .Where(value => TryReadColor(value, out _, out string kind) && kind != null);
            var namedMatches = context.Objects.SelectMany(Names).Where(name => Mentions(text, name))
                .Concat(colorLabels).GroupBy(MatchKey).Select(group => ResolveSelection(group.First(), context))
                .Where(matches => matches.Count > 0).ToArray();
            hasDistinctNames = namedMatches.Length > 0 && namedMatches.All(matches => matches.Count == 1);
            if (namedMatches.Length > 0)
                return namedMatches.SelectMany(matches => matches).Select(item => item.Id).Distinct().ToArray();

            var reference = Reference.Match(text);
            if (reference.Success)
            {
                var selected = context.Objects.FirstOrDefault(item => item.Id == context.SelectedTargetId);
                return selected == null || (reference.Groups["button"].Success && !IsButton(selected))
                    ? Array.Empty<string>() : new[] { selected.Id };
            }
            var side = SideTarget.Match(text);
            if (side.Success)
                return OnSide(context.Objects, context, side).Select(item => item.Id).ToArray();
            var generic = GenericTarget.Match(text);
            if (generic.Success)
            {
                string kind = generic.Groups["kind"].Value;
                return context.Objects.Where(item => IsCategory(item, kind)).Select(item => item.Id).ToArray();
            }
            return ImplicitObject.IsMatch(text) ? context.Objects.Select(item => item.Id).ToArray() : Array.Empty<string>();
        }

        private static IEnumerable<StageObject> OnSide(IEnumerable<StageObject> items, StageContext context, Match side)
        {
            if (!context.AgentHeading.HasValue || !context.AgentPositionX.HasValue || !context.AgentPositionZ.HasValue)
                return Array.Empty<StageObject>();
            double heading = context.AgentHeading.Value * Math.PI / 180.0;
            double sign = side.Groups["side"].Value == "오른쪽" ? 1.0 : -1.0;
            double rightX = sign * Math.Cos(heading);
            double rightZ = -sign * Math.Sin(heading);
            string kind = side.Groups["kind"].Value;
            return items.Where(item => IsCategory(item, kind) &&
                (item.PositionX - context.AgentPositionX) * rightX +
                (item.PositionZ - context.AgentPositionZ) * rightZ > 0.15);
        }

        internal static bool IsButton(StageObject item) => item.CanInteract.HasValue;

        internal static bool IsBox(StageObject item) => Names(item).Any(IsBoxName);

        private static bool IsCategory(StageObject item, string category) => category == "버튼" ? IsButton(item) : IsBox(item);

        private static bool IsBoxName(string name) => !string.IsNullOrWhiteSpace(name) &&
            (Regex.IsMatch(name, @"상자(?=$|\s|\d)") ||
             Regex.IsMatch(name, @"(?:^|[\s_-])box(?=$|[\s_\d-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
             Regex.IsMatch(name, @"Box(?=$|[\s_\d-])") ||
             (TryReadColor(Regex.Replace(name, @"\d+$", ""), out _, out string kind) && kind == "상자"));

        private static IEnumerable<string> Names(StageObject item)
        {
            yield return item.Id;
            yield return item.DisplayName;
            yield return item.SceneName;
            if (item.Aliases != null)
                foreach (string alias in item.Aliases) yield return alias;
        }

        private static bool HasColor(StageObject item, string color)
        {
            if (!string.IsNullOrWhiteSpace(item.Color))
                return TryReadColor(item.Color, out string explicitColor, out _) && explicitColor == color;
            return Names(item).Any(name => !string.IsNullOrWhiteSpace(name) &&
                TryReadColor(Regex.Replace(name, @"\d+$", ""), out string namedColor, out _) && namedColor == color);
        }

        private static bool TryReadColor(string text, out string color, out string kind)
        {
            string key = MatchKey(text);
            kind = null;
            if (Colors.TryGetValue(key, out color)) return true;
            foreach (string word in new[] { "상자", "버튼", "box", "button" })
            {
                if (!key.EndsWith(word, StringComparison.Ordinal) || !Colors.TryGetValue(key.Substring(0, key.Length - word.Length), out color)) continue;
                kind = CategoryWord(word);
                return true;
            }
            return false;
        }

        private static string CategoryWord(string key) => key == "상자" || key == "box" ? "상자" :
            key == "버튼" || key == "button" ? "버튼" : null;

        private static string RelationKey(string text)
        {
            string normalized = Normalize(text);
            if (!RelationSelector.IsMatch(normalized)) return null;
            return MatchKey(Regex.Replace(normalized, @"(?<place>옆|근처)(?:에)?(?:\s*있는)?(?:\s*(?:거|것|버튼|상자))?$", "${place}"));
        }

        private static string SelectionText(string text)
        {
            string[] choices = Regex.Split(text, @"\s+(?:말고|대신)\s*");
            return SelectionPrefix.Replace(choices[choices.Length - 1], "").Trim();
        }

        private static string MatchKey(string text) =>
            Regex.Replace(Normalize(text).Normalize(NormalizationForm.FormKC).ToLowerInvariant(), @"[\s_-]+", "");

        private static bool HasQuestionMark(string text) => !string.IsNullOrEmpty(text) &&
            (text.Contains("?") || text.Contains("？"));

        private static bool Mentions(string text, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return false;
            string key = MatchKey(label);
            if (key.Length == 0 || CategoryWord(key) != null || Colors.ContainsKey(key)) return false;
            string pattern = string.Join(@"[\s_-]*", key.Select(character => Regex.Escape(character.ToString())));
            return Regex.IsMatch(text, @"(?<![\p{L}\p{N}_])" + pattern +
                @"(?=$|[\s,.;!?，。！？]|(?:으로|에서|에게|에|을|를|이|가|과|와|도|만)(?=$|[\s,.;!?，。！？]))",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string Normalize(string text) =>
            Regex.Replace((text ?? "").Trim().TrimEnd('.', '!', '?', '。', '！', '？').Trim(), @"\s+", " ");
    }
}
