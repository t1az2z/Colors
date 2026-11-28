using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Спрайты нового интерфейса для того, что строится кодом во время игры (полосы над врагами).
    /// Ассет — Resources/UI/UiAtlas (собирает UiPrefabBuilder из Art/UI/Atlas; сами PNG режет Tools/UI/make_ui_sprites.py).
    /// HUD и колесо оружия ссылаются на спрайты из своих префабов напрямую.
    /// </summary>
    [CreateAssetMenu(menuName = "ColorsAction/UI Atlas", fileName = "UiAtlas")]
    public sealed class UiAtlas : ScriptableObject
    {
        public const string Resource = "UI/UiAtlas";

        [Header("Полосы над врагом")]
        public Sprite EnemyHealthFrame;
        public Sprite EnemyHealthFill;
        public Sprite EnemyPoiseFrame;
        public Sprite EnemyPoiseFill;

        private static UiAtlas cached;
        private static bool loaded;

        /// <summary>Атлас из Resources (null, если ассета нет — тогда вызывающий рисует запасной вариант).</summary>
        public static UiAtlas Instance
        {
            get
            {
                if (!loaded)
                {
                    cached = Resources.Load<UiAtlas>(Resource);
                    loaded = true;
                }

                return cached;
            }
        }
    }
}
