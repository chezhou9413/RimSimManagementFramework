using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 提供货柜拆除掉落、检查面板文本和操作按钮相关能力。
    /// </summary>
    public partial class Building_SimContainer
    {
        private const int InspectPreviewLineLimit = 8;

        /// <summary>
        /// 在货柜被移除前掉落内部虚拟库存，负责避免拆除或摧毁时吞掉商品。
        /// </summary>
        private void DropStoredContentsIfNeeded(Map map, IntVec3 dropSpot, DestroyMode mode)
        {
            if (contentsDropped) return;
            if (mode == DestroyMode.WillReplace) return;
            if (virtualStorage == null || virtualStorage.Count == 0) return;
            if (map == null || !dropSpot.IsValid) return;

            contentsDropped = true;
            virtualStorage.TryDropAll(dropSpot, map, ThingPlaceMode.Near);
            MarkStoredCountCacheDirty();
            pendingIn?.Clear();
            pendingOut?.Clear();
        }

        /// <summary>
        /// 反生成货柜时处理内部库存，负责在地图移除前把商品退回地图。
        /// </summary>
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            DropStoredContentsIfNeeded(MapHeld, PositionHeld, mode);
            base.DeSpawn(mode);
        }

        /// <summary>
        /// 摧毁货柜时处理内部库存，负责在建筑消失前把商品退回地图。
        /// </summary>
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            DropStoredContentsIfNeeded(MapHeld, PositionHeld, mode);
            base.Destroy(mode);
        }

        /// <summary>
        /// 返回货柜检查面板文本，负责展示汇总库存和少量预览，避免大量商品逐行拖慢检查面板。
        /// </summary>
        public override string GetInspectString()
        {
            string baseStr = base.GetInspectString();
            StringBuilder sb = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(customName))
                sb.Append(SimTranslation.T("RSMF.Container.Inspect.Name", customName.Named("name"))).Append("\n");

            sb.Append(SimTranslation.T("RSMF.Container.Inspect.TotalCapacity", CountTotalStored().Named("stored"), MaxTotalCapacity.Named("max")));
            if (Tool.VendingMachineUtility.IsVendingMachine(this))
                sb.Append("\n").Append(SimTranslation.T("RSMF.Container.Inspect.VendingMachineType"));
            int pending = CountTotalPendingIn(forceReconcile: true);
            if (pending > 0)
                sb.Append(" ").Append(SimTranslation.T("RSMF.Container.Inspect.PendingIn", pending.Named("pending")));
            sb.Append("\n").Append(SimTranslation.T("RSMF.Container.Inspect.TargetTotal", CountConfiguredTargets().Named("target")));

            int shown = 0;
            int hidden = 0;
            foreach (ThingDef thingDef in ActiveDefs)
            {
                int target = GetTargetCount(thingDef);
                if (target <= 0) continue;
                if (shown < InspectPreviewLineLimit)
                {
                    sb.Append("\n");
                    sb.Append($"{thingDef.LabelCap}: {CountStored(thingDef)}/{target}");
                    int reserved = CountPending(thingDef, forceReconcile: false);
                    if (reserved > 0) sb.Append(" ").Append(SimTranslation.T("RSMF.Container.Inspect.PendingIn", reserved.Named("pending")));
                    shown++;
                }
                else
                {
                    hidden++;
                }
            }
            if (hidden > 0)
                sb.Append("\n").Append(SimTranslation.T("RSMF.Container.Inspect.HiddenGoods", hidden.Named("count")));

            if (Prefs.DevMode && !string.IsNullOrWhiteSpace(lastPendingReservationDebug))
                sb.Append("\n").Append("补货调试: ").Append(lastPendingReservationDebug);

            if (string.IsNullOrEmpty(baseStr)) return sb.ToString();
            return baseStr + "\n" + sb;
        }

        /// <summary>
        /// 返回货柜操作按钮，负责提供改名和商品管理入口。
        /// </summary>
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
            {
                yield return gizmo;
            }

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.Gizmo.RenameContainer.Label"),
                defaultDesc = SimTranslation.T("RSMF.Gizmo.RenameContainer.Desc"),
                icon = TexButton.Rename,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_RenameBuildingStorage(this));
                }
            };

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.Gizmo.ContainerManagement.Label"),
                defaultDesc = SimTranslation.T("RSMF.Gizmo.ContainerManagement.Desc"),
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
