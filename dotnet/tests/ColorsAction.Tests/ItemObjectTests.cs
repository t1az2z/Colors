using System.Collections.Generic;
using ColorsAction.Level;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Объект «Предмет» веб-редактора (фляга на земле) попадает в уровень так же, как оружие на постаменте.</summary>
    public class ItemObjectTests
    {
        [Test]
        public void ItemObject_GoesToLevelItems()
        {
            var level = new VoxelLevel();
            var cells = new List<KeyValuePair<Int3, Block?>>();
            for (int x = 0; x < 3; x++)
            for (int z = 0; z < 3; z++)
                cells.Add(new KeyValuePair<Int3, Block?>(new Int3(x, 0, z), Block.Cube()));
            level.SetMany(cells);
            level.PutObject(new LevelObject { Id = "item_1", Kind = LevelObjectKind.Item, Cell = new Int3(1, 1, 1), Item = "flask" });
            level.PutObject(new LevelObject { Id = "item_2", Kind = LevelObjectKind.Item, Cell = new Int3(2, 1, 2) });

            var skipped = new List<string>();
            var config = VoxelPlayLayout.ToLevelConfig(level, skipped);
            Assert.That(config.Items, Has.Count.EqualTo(1));
            Assert.That((config.Items[0].Item, config.Items[0].X, config.Items[0].Z), Is.EqualTo(("flask", 1, 1)));
            Assert.That(skipped, Has.Some.Contains("item_2"));
        }
    }
}
