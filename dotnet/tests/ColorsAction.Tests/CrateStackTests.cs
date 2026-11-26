using System.Collections.Generic;
using System.Linq;
using ColorsAction.Combat;
using ColorsAction.Configs;
using ColorsAction.Level;
using GameCore.Config;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Стопка ломаемых ящиков 2×1×2: форма, блокировка прохода, урон только мечом, загрузка из карты.</summary>
    public class CrateStackTests
    {
        [TestCase(0f, 1, 0)]
        [TestCase(90f, 0, -1)]
        [TestCase(180f, -1, 0)]
        [TestCase(270f, 0, 1)]
        [TestCase(-90f, 0, 1)]
        public void SecondCell_FollowsYaw(float yaw, int dx, int dz)
        {
            var anchor = new Int3(5, 2, 5);
            Assert.That(CrateStackShape.Cell(anchor, yaw, 0), Is.EqualTo(anchor));
            Assert.That(CrateStackShape.Cell(anchor, yaw, 1), Is.EqualTo(new Int3(5 + dx, 2, 5 + dz)));
        }

        [Test]
        public void Center_IsBetweenBothCells()
        {
            Assert.That(CrateStackShape.Center(3, 4, CrateStackShape.AlongX), Is.EqualTo((4.5f, 4.5f)));
            Assert.That(CrateStackShape.Center(3, 4, CrateStackShape.AlongZ), Is.EqualTo((3.5f, 5.5f)));
        }

        [Test]
        public void Object_CoversTwoColumns_AndItsHeight()
        {
            var o = new LevelObject { Kind = LevelObjectKind.CrateStack, Cell = new Int3(2, 1, 2), Yaw = CrateStackShape.AlongZ, Height = 2 };
            Assert.That(o.BlocksPath, Is.True);
            Assert.That(o.Covers(new Int3(2, 1, 2)), Is.True);
            Assert.That(o.Covers(new Int3(2, 2, 3)), Is.True, "second column, top crate");
            Assert.That(o.Covers(new Int3(2, 3, 3)), Is.False, "above the stack");
            Assert.That(o.Covers(new Int3(3, 1, 2)), Is.False, "beside the stack");
            Assert.That(o.Covers(new Int3(2, 0, 2)), Is.False, "below the stack");
        }

        [Test]
        public void Breakable_OnlyBySword()
        {
            var state = new BreakableState(20f, new[] { "sword" });
            Assert.That(state.Hit("shotgun", 100f), Is.EqualTo(BreakHitResult.Ignored));
            Assert.That(state.Hit("shield", 100f), Is.EqualTo(BreakHitResult.Ignored));
            Assert.That(state.Hit(null, 100f), Is.EqualTo(BreakHitResult.Ignored));
            Assert.That(state.Hp, Is.EqualTo(20f));
            Assert.That(state.Hit("SWORD", 7.5f), Is.EqualTo(BreakHitResult.Damaged), "id is case-insensitive");
            Assert.That(state.Hit("sword", 7.5f), Is.EqualTo(BreakHitResult.Damaged));
            Assert.That(state.Fraction, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(state.Hit("sword", 7.5f), Is.EqualTo(BreakHitResult.Broken));
            Assert.That(state.Broken, Is.True);
            Assert.That(state.Hit("sword", 7.5f), Is.EqualTo(BreakHitResult.Ignored), "already broken");
        }

        [Test]
        public void Breakable_EmptyList_AcceptsAnyWeapon()
        {
            var state = new BreakableState(5f, new string[0]);
            Assert.That(state.Accepts("fists"), Is.True);
            Assert.That(new BreakableState(5f, null, broken: true).Broken, Is.True);
        }

        [Test]
        public void CratesConfig_Loads_AndNeedsSeveralSwordHits()
        {
            var service = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            var crates = service.Load<CrateConfig>(ConfigKeys.Crates);
            var sword = service.LoadTable<ItemConfig>(ConfigKeys.Items).Get("sword");
            Assert.That(crates.BreakableBy, Does.Contain("sword"));
            Assert.That(crates.BreakableBy, Does.Not.Contain("shotgun"));
            Assert.That(crates.Hp, Is.GreaterThan(sword.Damage), "a single hit should not break the stack");
        }

        [Test]
        public void VoxelLevel_CrateObject_GoesToLevelConfig_AndBlocksPath()
        {
            var level = new VoxelLevel();
            var cells = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 4; x++)
            for (int z = 0; z < 4; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            level.SetMany(cells);
            level.PutObject(new LevelObject
            {
                Id = "crates_1", Kind = LevelObjectKind.CrateStack, Cell = new Int3(1, 1, 1), Yaw = CrateStackShape.AlongX, Height = 2
            });

            var skipped = new List<string>();
            var config = VoxelPlayLayout.ToLevelConfig(level, skipped);
            Assert.That(skipped.Any(s => s.Contains("crates_1")), Is.False);
            Assert.That(config.Crates, Has.Count.EqualTo(1));
            var crate = config.Crates[0];
            Assert.That((crate.Id, crate.X, crate.Z, crate.Height, crate.Y), Is.EqualTo(("crates_1", 1, 1, 2, (float?)1f)));
            Assert.That(level.BarrierAt(new Int3(2, 2, 1))?.Id, Is.EqualTo("crates_1"));

            // Файл карты: вид объекта и высота переживают сохранение и загрузку.
            var round = VoxelLevelData.From(level).ToLevel();
            var o = round.FindObject("crates_1");
            Assert.That(o.Kind, Is.EqualTo(LevelObjectKind.CrateStack));
            Assert.That(o.Height, Is.EqualTo(2));
        }
    }
}
