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
                sb.Append(SimTranslation.T("RSMF.Container.Inspect.Name", customName.Named("name"))).Append("\n");

            sb.Append(SimTranslation.T("RSMF.Container.Inspect.TotalCapacity", CountTotalStored().Named("stored"), MaxTotalCapacity.Named("max")));
            if (Tool.VendingMachineUtility.IsVendingMachine(this))
                sb.Append("\n").Append(SimTranslation.T("RSMF.Container.Inspect.VendingMachineType"));
            int pending = CountTotalPendingIn();
            if (pending > 0)
                sb.Append(" ").Append(SimTranslation.T("RSMF.Container.Inspect.PendingIn", pending.Named("pending")));
            sb.Append("\n").Append(SimTranslation.T("RSMF.Container.Inspect.TargetTotal", CountConfiguredTargets().Named("target")));

            foreach (ThingDef thingDef in ActiveDefs)
            {
                int target = GetTargetCount(thingDef);
                if (target <= 0) continue;
                sb.Append("\n");
                sb.Append($"{thingDef.LabelCap}: {CountStored(thingDef)}/{target}");
                int reserved = CountPending(thingDef);
                if (reserved > 0) sb.Append(" ").Append(SimTranslation.T("RSMF.Container.Inspect.PendingIn", reserved.Named("pending")));
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
