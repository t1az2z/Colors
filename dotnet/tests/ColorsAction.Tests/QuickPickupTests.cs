using ColorsAction.Inventory;
using GameCore.Config;
using GameCore.Events;
using GameCore.Newtonsoft;
using NUnit.Framework;

namespace ColorsAction.Tests
{
    /// <summary>Подобранная фляга сразу встаёт в быстрый слот 1 (крестовина вверх), если он пуст.</summary>
    public class QuickPickupTests
    {
        private static GameSession Session()
        {
            var configs = new ConfigService(new FileConfigSource(TestPaths.ConfigsRoot()), new NewtonsoftConfigSerializer());
            return new GameSession(configs, new ItemDatabase(configs), new EventBus());
        }

        [Test]
        public void PickUpFlask_GoesToDpadUpSlot()
        {
            var session = Session();
            Assert.That(session.PickUp("flask"), Is.True);
            Assert.That(session.Inventory.Get(SlotRef.Quick(0))?.Id, Is.EqualTo("flask"));
            Assert.That(session.Inventory.Get(SlotRef.Grid(0)), Is.Null);
        }

        [Test]
        public void PickUpSecondFlask_StaysInGrid_WhenDpadUpTaken()
        {
            var session = Session();
            session.PickUp("flask");
            Assert.That(session.PickUp("flask"), Is.True);
            Assert.That(session.Inventory.Get(SlotRef.Quick(0))?.Id, Is.EqualTo("flask"));
            Assert.That(session.Inventory.Get(SlotRef.Grid(0))?.Id, Is.EqualTo("flask"));
        }
    }
}
