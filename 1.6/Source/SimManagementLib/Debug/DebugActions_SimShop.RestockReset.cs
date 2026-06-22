using LudeonTK;
using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimWorkGiver;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.Debug
{
    //补货调试工具，职责是强制清空补货扫描缓存并校正货柜预约状态。
    public static partial class DebugActions_SimShop
    {
        [DebugAction("SimShop", "重置补货缓存（当前地图）", false, false, false, false, false, 0, false,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ResetRestockCachesForCurrentMap()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("重置补货缓存失败：当前没有地图。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            WorkGiver_RestockMegaStorage.ClearRestockCandidateCaches();
            RestockSupplySearchStateCache.ClearSupplySearchStates();
            RestockWorkTickBudget.ClearBudgets();
            WorkGiverThingQueryCache.Clear();

            int storageCount = ReconcileMapStorageReservations(map);
            int queueCount = map.GetComponent<MapComponent_RestockTaskQueue>()?.ResetAndRebuildAll("调试重置补货缓存") ?? 0;
            Messages.Message($"已重置补货缓存，并重建补货队列：{queueCount} 个货柜，校正预约：{storageCount} 个货柜。", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("SimShop", "输出补货诊断日志（当前地图）", false, false, false, false, false, 0, false,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void LogRestockDebugReportForCurrentMap()
        {
            Map map = Find.CurrentMap;
            string report = RestockDebugReportBuilder.Build(map);
            Log.Message(report);
            GUIUtility.systemCopyBuffer = report;
            Messages.Message("已输出补货诊断日志，并复制到剪贴板。", MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction("SimShop", "打开补货队列调试面板（当前地图）", false, false, false, false, false, 0, false,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void OpenRestockQueueDebugWindow()
        {
            Map map = Find.CurrentMap;
            if (map == null)
            {
                Messages.Message("打开补货队列调试面板失败：当前没有地图。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_RestockQueueDebug(map));
        }

        //校正当前地图所有商店货柜预约，职责是清理中断任务留下的待入库和待出库数量。
        private static int ReconcileMapStorageReservations(Map map)
        {
            if (map?.listerBuildings?.allBuildingsColonist == null)
                return 0;

            int count = 0;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_SimContainer storage = buildings[i] as Building_SimContainer;
                if (storage == null || storage.Destroyed || !storage.Spawned)
                    continue;

                storage.ReconcilePendingReservations();
                count++;
            }

            return count;
        }
    }
}
