using RimWorld;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class MainTabWindow_BusinessManager
    {
        /// <summary>
        /// 绘制自动售卖页，列出不依赖商店区域的自动售货机货柜。
        /// </summary>
        private void DrawVendingMachinePage(Rect rect)
        {
            List<Building_SimContainer> machines = CollectAllVendingMachines();
            if (machines.NullOrEmpty())
            {
                Widgets.NoneLabel(rect.center.y, rect.width, SimTranslation.T("RSMF.Business.Empty.NoVendingMachines"));
                return;
            }

            float viewWidth = rect.width - 18f;
            float rowHeight = 126f;
            Rect viewRect = new Rect(0f, 0f, viewWidth, machines.Count * rowHeight);
            Widgets.BeginScrollView(rect, ref vendingScrollPos, viewRect);

            for (int i = 0; i < machines.Count; i++)
            {
                Building_SimContainer machine = machines[i];
                DrawVendingMachineRow(new Rect(0f, i * rowHeight, viewWidth, rowHeight - 6f), machine, i);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制单台自动售货机的状态和操作按钮。
        /// </summary>
        private void DrawVendingMachineRow(Rect row, Building_SimContainer machine, int index)
        {
            ThingComp_VendingMachine comp = machine.GetComp<ThingComp_VendingMachine>();
            bool usable = VendingMachineUtility.IsUsableVendingMachine(machine);
            int activeCustomers = VendingMachineUtility.CountActiveCustomers(machine.Map, machine);
            int sellableKinds = machine.ActiveDefs.Count(def => def != null && machine.GetTargetCount(def) > 0);
            int stockedKinds = machine.ActiveDefs.Count(def => def != null && machine.GetTargetCount(def) > 0 && machine.CountStored(def) > 0);

            Widgets.DrawBoxSolid(row, index % 2 == 0 ? CPanelAlt : new Color(0f, 0f, 0f, 0.08f));
            DrawBorder(row, new Color(1f, 1f, 1f, 0.12f));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 10f, row.y + 6f, 420f, 26f), machine.StorageDisplayLabel);

            Text.Font = GameFont.Tiny;
            GUI.color = CDim;
            Widgets.Label(new Rect(row.x + 10f, row.y + 30f, 560f, 20f), SimTranslation.T("RSMF.Business.MapPositionLine",
                (machine.Map.info?.parent?.LabelCap ?? machine.Map.ToString()).Named("map"),
                machine.Position.ToString().Named("position")));

            GUI.color = usable ? COk : CWarn;
            Widgets.Label(new Rect(row.x + 10f, row.y + 50f, 360f, 20f), usable ? SimTranslation.T("RSMF.Business.Status.VendingUsable") : SimTranslation.T("RSMF.Business.Status.VendingDisabled"));

            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 260f, row.y + 50f, 500f, 20f), SimTranslation.T("RSMF.Business.VendingInventoryLine",
                stockedKinds.Named("stockedKinds"),
                sellableKinds.Named("sellableKinds"),
                machine.CountTotalStored().Named("stored"),
                machine.MaxTotalCapacity.Named("capacity"),
                activeCustomers.Named("customers"),
                (comp?.MaxSimultaneousCustomers ?? 1).Named("maxCustomers")));

            GUI.color = CDim;
            Widgets.Label(new Rect(row.x + 10f, row.y + 72f, row.width - 220f, 38f), SimTranslation.T("RSMF.Business.GoodsPreview", BuildVendingGoodsPreview(machine).Named("preview")));
            ResetText();

            float btnW = 92f;
            float btnH = 28f;
            float bx = row.xMax - btnW - 10f;
            float by = row.y + 10f;

            if (SimUiStyle.DrawPrimaryButton(new Rect(bx, by, btnW, btnH), comp != null && comp.enabled ? SimTranslation.T("RSMF.Business.PauseBusiness") : SimTranslation.T("RSMF.Business.StartBusiness"), comp != null, GameFont.Tiny))
            {
                comp.enabled = !comp.enabled;
            }

            by += btnH + 6f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Gizmo.ContainerManagement.Label"), true, GameFont.Tiny))
            {
                OpenStorageManager(machine);
            }

            by += btnH + 6f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), SimTranslation.T("RSMF.Common.Locate"), true, GameFont.Tiny))
            {
                CameraJumper.TryJump(machine);
            }
        }

        /// <summary>
        /// 收集所有玩家地图上的自动售货机货柜。
        /// </summary>
        private static List<Building_SimContainer> CollectAllVendingMachines()
        {
            List<Building_SimContainer> result = new List<Building_SimContainer>();
            if (Find.Maps == null) return result;

            foreach (Map map in Find.Maps)
            {
                if (map == null || !map.IsPlayerHome) continue;
                result.AddRange(VendingMachineUtility.GetAllVendingMachines(map));
            }

            return result
                .OrderBy(m => m.Map.Index)
                .ThenBy(m => m.thingIDNumber)
                .ToList();
        }

        /// <summary>
        /// 构建自动售货机商品库存预览文本。
        /// </summary>
        private static string BuildVendingGoodsPreview(Building_SimContainer machine)
        {
            List<string> parts = new List<string>();
            foreach (ThingDef def in machine.ActiveDefs)
            {
                if (def == null || machine.GetTargetCount(def) <= 0) continue;
                parts.Add($"{def.LabelCap.RawText} {machine.CountStored(def)}/{machine.GetTargetCount(def)}");
                if (parts.Count >= 4) break;
            }

            if (parts.Count <= 0) return SimTranslation.T("RSMF.Business.UnconfiguredGoods");
            if (machine.ActiveDefs.Count(def => def != null && machine.GetTargetCount(def) > 0) > parts.Count)
                parts.Add("...");
            return string.Join("、", parts);
        }
    }
}
