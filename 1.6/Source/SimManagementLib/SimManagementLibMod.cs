using HarmonyLib;
using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.SimZone;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib
{
    public class SimManagementLibSettings : ModSettings
    {
        public bool showCustomerArrivalMessage = true;
        public bool showCustomerInspectDetails = true;
        public int customerArrivalCheckIntervalTicks = 500;
        public int maxFinanceBillRecords = 2000;
        public int financeLogPageSize = 30;
        public string debugForcedCustomerKindId = "";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showCustomerArrivalMessage, "showCustomerArrivalMessage", true);
            Scribe_Values.Look(ref showCustomerInspectDetails, "showCustomerInspectDetails", true);
            Scribe_Values.Look(ref customerArrivalCheckIntervalTicks, "customerArrivalCheckIntervalTicks", 500);
            Scribe_Values.Look(ref maxFinanceBillRecords, "maxFinanceBillRecords", 2000);
            Scribe_Values.Look(ref financeLogPageSize, "financeLogPageSize", 30);
            Scribe_Values.Look(ref debugForcedCustomerKindId, "debugForcedCustomerKindId", "");

            customerArrivalCheckIntervalTicks = Mathf.Clamp(customerArrivalCheckIntervalTicks, 120, 5000);
            maxFinanceBillRecords = Mathf.Clamp(maxFinanceBillRecords, 200, 50000);
            financeLogPageSize = Mathf.Clamp(financeLogPageSize, 10, 200);
            if (debugForcedCustomerKindId == null)
                debugForcedCustomerKindId = "";
        }
    }

    public class SimManagementLibMod : Mod
    {
        public static SimManagementLibSettings Settings { get; private set; } = new SimManagementLibSettings();
        public static ModContentPack ActiveContentPack { get; private set; }

        public SimManagementLibMod(ModContentPack content) : base(content)
        {
            ActiveContentPack = content;
            Settings = GetSettings<SimManagementLibSettings>();
        }

        public override string SettingsCategory()
        {
            return "边缘模拟经营框架";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);

            list.Label("顾客系统");
            list.CheckboxLabeled("显示顾客到访提醒", ref Settings.showCustomerArrivalMessage, "关闭后不再显示“有顾客正在前往商店”的提示。");
            list.CheckboxLabeled("显示顾客检查详情", ref Settings.showCustomerInspectDetails, "关闭后不再在角色检查面板中附加顾客运行时参数。");
            list.Label($"顾客刷新检查间隔: {Settings.customerArrivalCheckIntervalTicks} ticks");
            Settings.customerArrivalCheckIntervalTicks = (int)list.Slider(Settings.customerArrivalCheckIntervalTicks, 120f, 5000f);
            DrawDebugForcedCustomerKindSelector(list);

            list.GapLine();
            list.Label("财务与日志");
            list.Label($"财务日志最大保留条数: {Settings.maxFinanceBillRecords}");
            Settings.maxFinanceBillRecords = (int)list.Slider(Settings.maxFinanceBillRecords, 200f, 50000f);
            list.Label($"统计面板每页条数: {Settings.financeLogPageSize}");
            Settings.financeLogPageSize = (int)list.Slider(Settings.financeLogPageSize, 10f, 200f);

            list.GapLine();
            Rect customGoodsButtonRect = list.GetRect(40f);
            if (Widgets.ButtonText(customGoodsButtonRect, "打开自定义商品注册面板"))
                Find.WindowStack.Add(new Dialog_CustomGoodsRegistry());

            Rect customCustomerButtonRect = list.GetRect(40f);
            if (Widgets.ButtonText(customCustomerButtonRect, "打开自定义顾客注册面板"))
                Find.WindowStack.Add(new Dialog_CustomCustomerRegistry());

            Rect resetRect = list.GetRect(36f);
            if (Widgets.ButtonText(resetRect, "恢复默认设置"))
            {
                Settings.showCustomerArrivalMessage = true;
                Settings.showCustomerInspectDetails = true;
                Settings.customerArrivalCheckIntervalTicks = 500;
                Settings.maxFinanceBillRecords = 2000;
                Settings.financeLogPageSize = 30;
                Settings.debugForcedCustomerKindId = "";
            }

            list.End();
            Settings.Write();
        }

        /// <summary>
        /// 绘制用于测试刷客的强制顾客组选择控件。
        /// </summary>
        private static void DrawDebugForcedCustomerKindSelector(Listing_Standard list)
        {
            CustomerCatalog.EnsureInitialized();
            RuntimeCustomerKind selected = CustomerCatalog.GetKind(Settings.debugForcedCustomerKindId);
            string label = selected != null
                ? $"{selected.label} / {selected.kindId}"
                : (string.IsNullOrEmpty(Settings.debugForcedCustomerKindId) ? "关闭" : "已失效: " + Settings.debugForcedCustomerKindId);

            Rect rect = list.GetRect(34f);
            if (Widgets.ButtonText(rect, "Debug 强制顾客组: " + label.Truncate(rect.width - 170f)))
            {
                List<FloatMenuOption> options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("关闭", () => Settings.debugForcedCustomerKindId = "")
                };

                foreach (RuntimeCustomerKind kind in CustomerCatalog.Kinds
                    .Where(k => k != null)
                    .OrderBy(k => k.sourceDef != null ? 0 : 1)
                    .ThenBy(k => k.label))
                {
                    string optionLabel = $"{kind.label} / {kind.kindId}";
                    options.Add(new FloatMenuOption(optionLabel, () => Settings.debugForcedCustomerKindId = kind.kindId));
                }

                Find.WindowStack.Add(new FloatMenu(options));
            }

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.76f, 0.82f, 1f);
            list.Label("开启后自然刷客和强制刷新会优先使用该顾客组；为空时恢复正常权重随机。");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }

    [StaticConstructorOnStartup]
    public static class SimManagementLibBootstrap
    {
        static SimManagementLibBootstrap()
        {
            Harmony harmony = new Harmony("com.Chezhou.simmanagementlib");
            harmony.PatchAll();
            EnsureShopDesignatorRegistered();
        }

        private static void EnsureShopDesignatorRegistered()
        {
            DesignationCategoryDef zoneCategory = DefDatabase<DesignationCategoryDef>.GetNamedSilentFail("Zone");
            if (zoneCategory == null)
                return;

            if (zoneCategory.specialDesignatorClasses == null)
                zoneCategory.specialDesignatorClasses = new List<Type>();

            Type shopDesignatorType = typeof(Designator_ZoneAdd_Shop);
            if (!zoneCategory.specialDesignatorClasses.Contains(shopDesignatorType))
                zoneCategory.specialDesignatorClasses.Add(shopDesignatorType);
        }
    }
}
