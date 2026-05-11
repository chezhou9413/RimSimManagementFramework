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
                Widgets.NoneLabel(rect.center.y, rect.width, "(当前没有自动售货机货柜)");
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
            Widgets.Label(new Rect(row.x + 10f, row.y + 30f, 560f, 20f), $"地图: {machine.Map.info?.parent?.LabelCap ?? machine.Map.ToString()}   位置: {machine.Position}");

            GUI.color = usable ? COk : CWarn;
            string status = usable ? "状态: 可售卖" : "状态: 停用、断电、关机或无库存";
            Widgets.Label(new Rect(row.x + 10f, row.y + 50f, 360f, 20f), status);

            GUI.color = Color.white;
            Widgets.Label(new Rect(row.x + 260f, row.y + 50f, 500f, 20f), $"商品: {stockedKinds}/{sellableKinds} 有库存   容量: {machine.CountTotalStored()}/{machine.MaxTotalCapacity}   顾客: {activeCustomers}/{comp?.MaxSimultaneousCustomers ?? 1}");

            GUI.color = CDim;
            Widgets.Label(new Rect(row.x + 10f, row.y + 72f, row.width - 220f, 38f), "商品预览: " + BuildVendingGoodsPreview(machine));
            ResetText();

            float btnW = 92f;
            float btnH = 28f;
            float bx = row.xMax - btnW - 10f;
            float by = row.y + 10f;

            if (SimUiStyle.DrawPrimaryButton(new Rect(bx, by, btnW, btnH), comp != null && comp.enabled ? "暂停营业" : "开始营业", comp != null, GameFont.Tiny))
            {
                comp.enabled = !comp.enabled;
            }

            by += btnH + 6f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), "货柜管理", true, GameFont.Tiny))
            {
                OpenStorageManager(machine);
            }

            by += btnH + 6f;
            if (SimUiStyle.DrawSecondaryButton(new Rect(bx, by, btnW, btnH), "定位", true, GameFont.Tiny))
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

            if (parts.Count <= 0) return "未配置商品";
            if (machine.ActiveDefs.Count(def => def != null && machine.GetTargetCount(def) > 0) > parts.Count)
                parts.Add("...");
            return string.Join("、", parts);
        }
    }
}
