using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using ColorsAction.Localization;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    public class LocalizationTests
    {
        private static ConfigService Configs() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());

        [Test]
        public void Parse_SplitsTextAndActions()
        {
            var segments = PromptText.Parse("{action:interact} взять: Меч");
            Assert.That(segments, Is.EqualTo(new List<PromptSegment>
            {
                PromptSegment.Action("interact"),
                PromptSegment.Text(" взять: Меч")
            }));
        }

        [Test]
        public void Parse_EscapedBracesAndUnknownTokensStayText()
        {
            var segments = PromptText.Parse("{{x}} {foo} {action:jump}");
            Assert.That(segments, Is.EqualTo(new List<PromptSegment>
            {
                PromptSegment.Text("{x} {foo} "),
                PromptSegment.Action("jump")
            }));
        }

        [Test]
        public void Parse_IconTokens_AndUnblockableHintText()
        {
            var segments = PromptText.Parse("Атаки со знаком {icon:unblockable} нельзя блокировать");
            Assert.That(segments, Is.EqualTo(new List<PromptSegment>
            {
                PromptSegment.Text("Атаки со знаком "),
                PromptSegment.Icon(PromptText.UnblockableIcon),
                PromptSegment.Text(" нельзя блокировать")
            }));
            Assert.That(PromptText.Plain("{icon:unblockable} {icon:other}{action:block}", a => "ПКМ"), Is.EqualTo("! [ПКМ]"));
        }

        [Test]
        public void Substitute_ReplacesArgsButKeepsActionTokens()
        {
            var text = PromptText.Substitute("{action:interact} {0} +{1}", new object[] { "Металл", 3 });
            Assert.That(text, Is.EqualTo("{action:interact} Металл +3"));
            Assert.That(PromptText.Plain(text, a => a == "interact" ? "F" : a), Is.EqualTo("[F] Металл +3"));
        }

        [Test]
        public void Strings_FallbackChainAndMissingKey()
        {
            var ru = new LocalizationConfig { Language = "ru", Strings = { ["a"] = "А", ["b"] = "Б" } };
            var en = new LocalizationConfig { Language = "en", Fallback = "ru", Strings = { ["a"] = "A" } };
            var strings = new Strings(en, ru);
            Assert.That(strings.Get("a"), Is.EqualTo("A"));
            Assert.That(strings.Get("b"), Is.EqualTo("Б"));
            Assert.That(strings.Get("missing.key"), Is.EqualTo("missing.key"));
            Assert.That(strings.Get(new LocText("x {0}", 5)), Is.EqualTo("x 5"), "literal text without a key still formats");
        }

        [Test]
        public void ControlLabel_FamilyOverridesGeneric()
        {
            var strings = Strings.Load(Configs(), "ru");
            Assert.That(strings.ControlLabel("<Gamepad>/buttonSouth"), Is.EqualTo("A"));
            Assert.That(strings.ControlLabel("<Gamepad>/buttonSouth", "ps"), Is.EqualTo("✕"));
            Assert.That(strings.ControlLabel("<Gamepad>/buttonSouth", "xbox"), Is.EqualTo("A"));
            Assert.That(strings.ControlLabel("<Keyboard>/f"), Is.Null, "single letters come from Input System");
        }

        [Test]
        public void UnknownLanguage_FallsBackToRussian()
        {
            var strings = Strings.Load(Configs(), "xx");
            Assert.That(strings.Language, Is.EqualTo("ru"));
            Assert.That(strings.Get("prompt.bonfire"), Is.Not.EqualTo("prompt.bonfire"));
        }

        /// <summary>Все ключи, на которые ссылается код (строковые литералы вида "hint.*", "prompt.*"…), есть в ru.json.</summary>
        [Test]
        public void Russian_HasEveryKeyUsedInCode()
        {
            var strings = Strings.Load(Configs(), "ru");
            var root = Directory.GetParent(TestPaths.ConfigsRoot())!.Parent!.FullName; // Assets
            var pattern = new Regex(@"""((?:hint|prompt|hud|toast|bonfire|inventory)\.[A-Za-z.]+)""");
            var missing = new List<string>();
            foreach (var file in Directory.GetFiles(Path.Combine(root, "Game"), "*.cs", SearchOption.AllDirectories))
                foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                    if (!strings.Has(m.Groups[1].Value) && !missing.Contains(m.Groups[1].Value))
                        missing.Add(m.Groups[1].Value);
            Assert.That(missing, Is.Empty);
        }

        /// <summary>Подсказки на полу из карт: для каждого действия есть строка "hint.&lt;действие&gt;".</summary>
        [Test]
        public void Russian_HasTextForEveryLevelHintAction()
        {
            var strings = Strings.Load(Configs(), "ru");
            var actions = new HashSet<string>();
            var pattern = new Regex(@"""action""\s*:\s*""([a-z][A-Za-z]*)""");
            foreach (var file in Directory.GetFiles(TestPaths.ConfigsRoot(), "*.json", SearchOption.AllDirectories))
                foreach (Match m in pattern.Matches(File.ReadAllText(file)))
                    actions.Add(m.Groups[1].Value);
            var missing = new List<string>();
            foreach (var a in actions)
                if (!strings.Has("hint." + a))
                    missing.Add(a);
            Assert.That(missing, Is.Empty);
        }
    }
}
