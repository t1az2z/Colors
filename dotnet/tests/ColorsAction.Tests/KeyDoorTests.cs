using System.Collections.Generic;
using System.Linq;
using ColorsAction.Configs;
using ColorsAction.Inventory;
using ColorsAction.Level;
using ColorsAction.Level.Navigation;
using GameCore.Config;
using GameCore.Events;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>
    /// Двери на ключ: ключ от двери задаётся у врага на карте, выпадает из него, подбирается в инвентарь,
    /// открывает свою дверь и тратится; редакторы предупреждают о двери без ключа.
    /// </summary>
    public class KeyDoorTests
    {
        private static ConfigService Configs() =>
            new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());

        private static GameSession Session(EventBus events = null)
        {
            var configs = Configs();
            return new GameSession(configs, new ItemDatabase(configs), events ?? new EventBus());
        }

        private static LevelObject Enemy(string id, string key, string type = "club_cube") => new LevelObject
        {
            Id = id, Kind = LevelObjectKind.EnemySpawn, Cell = new Int3(1, 0, 1), Item = type, Key = key
        };

        private static LevelObject Door(string id, int x = 5) => new LevelObject
        {
            Id = id, Kind = LevelObjectKind.KeyDoor, Cell = new Int3(x, 0, 0), Height = 3
        };

        [Test]
        public void ItemsConfig_HasKey()
        {
            var key = Configs().LoadTable<ItemConfig>(ConfigKeys.Items).Get(KeyDoorRules.KeyItemId);
            Assert.That(key.Kind, Is.EqualTo(ItemKind.Key));
            Assert.That(key.IsKey, Is.True);
            Assert.That(key.IsStackable, Is.False, "каждый ключ — от своей двери");
            Assert.That(key.IsWeapon, Is.False);
        }

        [Test]
        public void PickedKey_GoesToGrid_OpensOnlyItsDoor_AndIsSpent()
        {
            var events = new EventBus();
            var picked = new List<Item>();
            events.Subscribe<ItemPickedUp>(e => picked.Add(e.Item));
            var session = Session(events);

            Assert.That(session.TryOpenKeyDoor("gate"), Is.False, "без ключа не открыть");
            Assert.That(session.PickUpKey("gate"), Is.True);
            var key = session.Inventory.FindKey("gate");
            Assert.That(key, Is.Not.Null);
            Assert.That(key.KeyFor, Is.EqualTo("gate"));
            Assert.That(session.Inventory.Find(key)?.Kind, Is.EqualTo(SlotKind.Grid));
            Assert.That(picked.Single(), Is.SameAs(key));

            Assert.That(session.HasKey("other"), Is.False);
            Assert.That(session.TryOpenKeyDoor("other"), Is.False, "ключ открывает только свою дверь");
            Assert.That(session.TryOpenKeyDoor("gate"), Is.True);
            Assert.That(session.OpenedKeyDoors, Does.Contain("gate"));
            Assert.That(session.HasKey("gate"), Is.False, "ключ потрачен");
            Assert.That(session.TryOpenKeyDoor("gate"), Is.True, "открытая дверь ключа не требует");
        }

        [Test]
        public void KeysForDifferentDoors_DoNotStack_AndKeyCantGoToQuickSlot()
        {
            var session = Session();
            Assert.That(session.PickUpKey("a"), Is.True);
            Assert.That(session.PickUpKey("b"), Is.True);
            var a = session.Inventory.FindKey("a");
            var b = session.Inventory.FindKey("b");
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(session.Inventory.Find(a), Is.Not.EqualTo(session.Inventory.Find(b)));
            Assert.That(session.Inventory.Move(session.Inventory.Find(a).Value, SlotRef.Quick(0)), Is.False);
            Assert.That(session.Inventory.EquipTargets(a), Is.Empty);
        }

        [Test]
        public void KeyIsNotGivenWithoutDoor()
        {
            var session = Session();
            Assert.That(session.PickUp(KeyDoorRules.KeyItemId), Is.False);
            Assert.That(session.GiveItem(KeyDoorRules.KeyItemId, false), Is.False);
            Assert.That(session.Inventory.CountOf(KeyDoorRules.KeyItemId), Is.EqualTo(0));
        }

        [Test]
        public void FullInventory_LeavesKeyOnGround()
        {
            var events = new EventBus();
            var full = 0;
            events.Subscribe<InventoryFull>(_ => full++);
            var session = Session(events);
            var cells = session.Inventory.GridSize;
            for (int i = 0; i < cells; i++)
                Assert.That(session.PickUpKey("door_" + i), Is.True);
            Assert.That(session.PickUpKey("last"), Is.False);
            Assert.That(full, Is.EqualTo(1));
            Assert.That(session.HasKey("last"), Is.False);
        }

        [Test]
        public void VoxelLevel_SavesKeyOnEnemies_AndMapsKeyDoors()
        {
            var level = new VoxelLevel();
            level.PutObject(Enemy("e1", "keydoor_1"));
            level.PutObject(Door("keydoor_1"));
            var data = VoxelLevelData.From(level);
            Assert.That(data.Objects.Single(o => o.Id == "e1").Key, Is.EqualTo("keydoor_1"));
            Assert.That(data.Objects.Single(o => o.Id == "keydoor_1").Key, Is.Null);

            var loaded = data.ToLevel();
            Assert.That(loaded.FindObject("e1").Key, Is.EqualTo("keydoor_1"));
            Assert.That(loaded.FindObject("keydoor_1").BlocksPath, Is.True);

            var skipped = new List<string>();
            var config = VoxelPlayLayout.ToLevelConfig(loaded, skipped);
            Assert.That(config.Enemies.Single().Key, Is.EqualTo("keydoor_1"));
            var door = config.KeyDoors.Single();
            Assert.That(door.Id, Is.EqualTo("keydoor_1"));
            Assert.That(door.Height, Is.EqualTo(3f));
            Assert.That(skipped.Where(s => s.Contains("keydoor")), Is.Empty);
        }

        [Test]
        public void Warnings_DoorWithoutKey_KeyOnDummy_MissingDoor_SeveralHolders()
        {
            var level = new VoxelLevel();
            level.PutObject(Door("lonely"));
            Assert.That(KeyDoorRules.Warnings(level).Single(), Does.Contain("lonely").And.Contain("нет ключа"));

            level.PutObject(Enemy("e1", "lonely"));
            Assert.That(KeyDoorRules.Warnings(level), Is.Empty);

            level.PutObject(Enemy("e2", "lonely"));
            Assert.That(KeyDoorRules.Warnings(level).Single(), Does.Contain("нескольких"));

            level.PutObject(Enemy("e2", "ghost"));
            Assert.That(KeyDoorRules.Warnings(level).Single(), Does.Contain("ghost"));

            level.PutObject(Enemy("e2", "lonely", VoxelPlayLayout.DummyType));
            Assert.That(KeyDoorRules.Warnings(level).Single(), Does.Contain("манекен"));
        }

        private static LevelKeyDoor KeyDoor(string id, int x, int z, float yaw, float y = 1f, float height = 3f) =>
            new LevelKeyDoor { Id = id, X = x, Z = z, Yaw = yaw, Y = y, Height = height };

        [Test]
        public void AdjacentKeyDoorsInLine_FormOneWideDoor_EitherFacing()
        {
            // Дверь на ключ двусторонняя: 0° и 180° — одна ось, соседи по X сливаются; через клетку — отдельно.
            var doors = new[] { KeyDoor("b", 6, 2, 180), KeyDoor("a", 5, 2, 0), KeyDoor("c", 7, 2, 0), KeyDoor("far", 9, 2, 0) };
            var groups = KeyDoorGroups.Build(doors);
            Assert.That(groups.Count, Is.EqualTo(2));
            var wide = groups.Single(g => g.Width == 3);
            Assert.That(wide.Doors.Select(i => doors[i].Id), Is.EqualTo(new[] { "a", "b", "c" }));
            Assert.That(wide.CenterX, Is.EqualTo(6f));
            Assert.That(KeyDoorGroups.Build(new[] { KeyDoor("a", 4, 1, 90), KeyDoor("b", 4, 2, 270) }).Single().Width, Is.EqualTo(2));
            // Разные оси, этажи, высоты и соседи «в глубину» остаются отдельными.
            Assert.That(KeyDoorGroups.Build(new[] { KeyDoor("a", 5, 2, 0), KeyDoor("b", 6, 2, 90) }).Count, Is.EqualTo(2));
            Assert.That(KeyDoorGroups.Build(new[] { KeyDoor("a", 5, 2, 0), KeyDoor("b", 6, 2, 0, y: 4) }).Count, Is.EqualTo(2));
            Assert.That(KeyDoorGroups.Build(new[] { KeyDoor("a", 5, 2, 0), KeyDoor("b", 6, 2, 0, height: 4) }).Count, Is.EqualTo(2));
            Assert.That(KeyDoorGroups.Build(new[] { KeyDoor("a", 5, 2, 0), KeyDoor("b", 5, 3, 0) }).Count, Is.EqualTo(2));
        }

        [Test]
        public void WideKeyDoor_OpensWithOneKeyOfAnyCell_AndMarksAllCells()
        {
            var session = Session();
            var door = new[] { "a", "b", "c" };
            Assert.That(session.HasKey(door), Is.False);
            Assert.That(session.TryOpenKeyDoor(door), Is.False);

            Assert.That(session.PickUpKey("b"), Is.True);
            Assert.That(session.PickUpKey("c"), Is.True);
            Assert.That(session.PickUpKey("other"), Is.True);
            Assert.That(session.HasKey(door), Is.True);
            Assert.That(session.TryOpenKeyDoor(door), Is.True);
            Assert.That(session.OpenedKeyDoors, Is.SupersetOf(door));
            Assert.That(session.HasKey("b") || session.HasKey("c"), Is.False, "ключи от клеток открытой двери больше не нужны");
            Assert.That(session.HasKey("other"), Is.True, "ключ от другой двери не трогается");
            Assert.That(session.PickUpKey("a"), Is.True, "ключ от открытой двери не кладётся в инвентарь");
            Assert.That(session.HasKey("a"), Is.False);
            Assert.That(session.TryOpenKeyDoor(door), Is.True);
        }

        [Test]
        public void Warnings_WideDoor_NeedsOneKeyForAllCells()
        {
            var level = new VoxelLevel();
            level.PutObject(Door("w1", 5));
            level.PutObject(Door("w2", 6));
            Assert.That(KeyDoorRules.Warnings(level).Single(), Does.Contain("w1").And.Contain("широкая").And.Contain("нет ключа"));

            level.PutObject(Enemy("e1", "w2"));
            Assert.That(KeyDoorRules.Warnings(level), Is.Empty, "ключа от одной клетки хватает на всю дверь");

            level.PutObject(Enemy("e2", "w1"));
            Assert.That(KeyDoorRules.Warnings(level).Single(), Does.Contain("нескольких"));

            KeyDoorRules.AssignHolder(level, "w1", "e2").ForEach(c => level.PutObject(c.after));
            Assert.That(level.FindObject("e1").Key, Is.Null, "ключ от соседней клетки той же двери снят");
            Assert.That(level.FindObject("e2").Key, Is.EqualTo("w1"));
            Assert.That(KeyDoorRules.Warnings(level), Is.Empty);
        }

        [Test]
        public void AssignHolder_MovesKeyBetweenEnemies()
        {
            var level = new VoxelLevel();
            level.PutObject(Door("gate"));
            level.PutObject(Enemy("e1", "gate"));
            level.PutObject(Enemy("e2", null));
            level.PutObject(Enemy("e3", "other"));

            var changes = KeyDoorRules.AssignHolder(level, "gate", "e2");
            Assert.That(changes.Select(c => c.after.Id), Is.EquivalentTo(new[] { "e1", "e2" }));
            var command = MarkupCommand.PutObjects("Ключ", changes);
            command.Do(level);
            Assert.That(KeyDoorRules.Holders(level, "gate").Select(o => o.Id), Is.EqualTo(new[] { "e2" }));
            Assert.That(level.FindObject("e3").Key, Is.EqualTo("other"), "чужой ключ не трогается");
            command.Undo(level);
            Assert.That(KeyDoorRules.Holders(level, "gate").Select(o => o.Id), Is.EqualTo(new[] { "e1" }));

            KeyDoorRules.AssignHolder(level, "gate", null).ForEach(c => level.PutObject(c.after));
            Assert.That(KeyDoorRules.Holders(level, "gate"), Is.Empty);
        }

        [Test]
        public void ClosedKeyDoor_BlocksEnemies_UntilOpened()
        {
            var level = new VoxelLevel();
            var blocks = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 10; x++)
                blocks.Add(new KeyValuePair<Int3, Block?>(new Int3(x, -1, 0), Block.Cube()));
            level.SetMany(blocks);
            level.PutObject(new LevelObject { Id = "gate", Kind = LevelObjectKind.KeyDoor, Cell = new Int3(5, 0, 0), Yaw = 90, Height = 3 });
            var walker = new MovementProfile { Id = "walker", StepHeight = 0.3f, JumpHeight = 0f, MaxDrop = 1f, Height = 1f };
            var open = new HashSet<string>();
            var reach = new EnemyReach(level, open.Contains, () => open.Count);
            Assert.That(reach.CanReach(walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.False);
            open.Add("gate");
            Assert.That(reach.CanReach(walker, 1.5f, 0.1f, 0.5f, 8.5f, 0.1f, 0.5f), Is.True);
        }
    }
}
