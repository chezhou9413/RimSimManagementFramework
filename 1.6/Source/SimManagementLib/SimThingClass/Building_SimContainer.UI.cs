using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingComp;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    public partial class Building_SimContainer
    {
        private void DropStoredContentsIfNeeded(Map map, IntVec3 dropSpot, DestroyMode mode)
        {
            if (contentsDropped) return;
            if (mode == DestroyMode.WillReplace) return;
            if (virtualStorage == null || virtualStorage.Count == 0) return;
            if (map == null || !dropSpot.IsValid) return;

            contentsDropped = true;
            virtualStorage.TryDropAll(dropSpot, map, ThingPlaceMode.Near);
            pendingIn?.Clear();
            pendingOut?.Clear();
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            DropStoredContentsIfNeeded(MapHeld, PositionHeld, mode);
            base.DeSpawn(mode);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            DropStoredContentsIfNeeded(MapHeld, PositionHeld, mode);
            base.Destroy(mode);
        }

        public override string GetInspectString()
        {
            string baseStr = base.GetInspectString();
            StringBuilder sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(customName))
                sb.Append("名称: ").Append(customName).Append("\n");

            sb.Append($"总容量: {CountTotalStored()}/{MaxTotalCapacity}");
            int pending = CountTotalPendingIn();
            if (pending > 0)
                sb.Append($" (+{pending} 途中)");
            sb.Append($"\n目标总量: {CountConfiguredTargets()}");

            foreach (ThingDef thingDef in ActiveDefs)
            {
                int target = GetTargetCount(thingDef);
                if (target <= 0) continue;
                sb.Append("\n");
                sb.Append($"{thingDef.LabelCap}: {CountStored(thingDef)}/{target}");
                int reserved = CountPending(thingDef);
                if (reserved > 0) sb.Append($" (+{reserved}途中)");
            }

            if (string.IsNullOrEmpty(baseStr)) return sb.ToString();
            return baseStr + "\n" + sb;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = "重命名货柜",
                defaultDesc = "为该货柜设置一个便于快速识别和定位的名称。",
                icon = TexButton.Rename,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_RenameBuildingStorage(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "货柜管理",
                defaultDesc = "打开货柜管理面板，配置上架商品、目标库存与价格。",
                icon = ContentFinder<Texture2D>.Get("UI/Buttons/Copy", true),
                action = delegate
                {
                    ThingComp_GoodsData comp = GoodsComp;
                    if (comp == null) return;
                    Find.WindowStack.Add(new Dialog_GoodsManager(comp));
                }
            };
        }
    }
}
