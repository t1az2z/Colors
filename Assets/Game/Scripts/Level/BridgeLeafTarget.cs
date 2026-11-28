using ColorsAction.Level;
using UnityEngine;

namespace ColorsAction.Unity
{
    /// <summary>
    /// Поднятая створка разводного моста (<see cref="DrawBridge"/>) как цель: попадание по ней — то же, что по цепям
    /// (<see cref="BridgeChain"/>). Так залп дробовика опускает мост и без фиксации цели, если пули упёрлись в створку,
    /// а не долетели до цепей. Другое оружие только раскачивает цепи. Снимается, когда цепи порваны — дальше створка
    /// просто настил.
    /// </summary>
    public sealed class BridgeLeafTarget : MonoBehaviour, IHittable, IWeaponGate
    {
        private BridgeChain chain;

        public void Init(BridgeChain bridgeChain) => chain = bridgeChain;

        private bool Live => chain != null && !chain.Broken;

        public string ColorId => chain != null ? chain.ColorId : "red";

        /// <summary>Середина створки — к ней дробовик без фиксации наклоняет залп.</summary>
        public Vector3 HitPoint => transform.TransformPoint(new Vector3(DrawBridgeShape.LeafLength * 0.5f, 0f, 0f));

        public bool ShowsDamageNumbers => false;

        public bool Accepts(string weaponId) => !Live || chain.Accepts(weaponId);

        public void Deflect(Vector3 direction, Color weaponColor)
        {
            if (Live)
                chain.Deflect(direction, weaponColor);
        }

        public void ReceiveHit(HitInfo hit)
        {
            if (Live)
                chain.ReceiveHit(hit);
        }
    }
}
