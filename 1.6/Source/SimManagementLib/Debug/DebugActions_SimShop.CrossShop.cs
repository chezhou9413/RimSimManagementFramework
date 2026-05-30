using LudeonTK;
using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using Verse;

namespace SimManagementLib.Debug
{
    /// <summary>
    /// 提供跨店购物测试场景调试入口，负责一键生成多店商圈和测试顾客。
    /// </summary>
    public static partial class DebugActions_SimShop
    {
        [DebugAction("SimShop", "生成跨店购物测试商圈（点选位置）", false, false, false, false, false, 0, false,
            actionType = DebugActionType.ToolMap,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SpawnCrossShopTestDistrictAtCell()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            IntVec3 clickCell = UI.MouseCell();
            if (!clickCell.InBounds(map)) return;

            ThingDef registerDef = DefDatabase<ThingDef>.GetNamedSilentFail("Sim_CashRegister");
            ThingDef storageDef = DefDatabase<ThingDef>.GetNamedSilentFail("MegaStorageBox")
                                  ?? DefDatabase<ThingDef>.AllDefsListForReading
                                      .FirstOrDefault(def => def?.thingClass != null && typeof(Building_SimContainer).IsAssignableFrom(def.thingClass));
            ThingDef wallDef = ThingDefOf.Wall;
            ThingDef doorDef = ThingDefOf.Door;
            if (registerDef == null || storageDef == null || wallDef == null || doorDef == null)
            {
                Messages.Message("生成跨店购物测试商圈失败：缺少收银台、货柜、墙或门 Def。", MessageTypeDefOf.RejectInput, false);
                return;
            }

            IntVec3[] centers =
            {
                clickCell + IntVec3.West * 11,
                clickCell,
                clickCell + IntVec3.East * 11
            };
            string[] goodsIds =
            {
                "Goods_Foods",
                "Goods_ApparelBasic",
                "Goods_Drugs"
            };

            for (int i = 0; i < centers.Length; i++)
            {
                if (!TrySpawnCrossShop(map, centers[i], registerDef, storageDef, wallDef, doorDef, goodsIds[i], out string reason))
                {
                    Messages.Message("生成跨店购物测试商圈失败：" + reason, MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }

            CustomerArrivalManager manager = map.GetComponent<CustomerArrivalManager>();
            string resultMessage = "";
            bool spawned = manager != null && manager.ForceSpawnOneWave(ignoreConditions: true, out resultMessage);
            Messages.Message(
                spawned
                    ? "已生成跨店购物测试商圈，并强制刷新顾客：" + resultMessage
                    : "已生成跨店购物测试商圈，但强制刷新顾客失败。",
                MessageTypeDefOf.TaskCompletion,
                false);
        }

        /// <summary>
        /// 生成一间跨店测试商店，负责按指定商品分类铺货并安排收银员。
        /// </summary>
        private static bool TrySpawnCrossShop(Map map, IntVec3 center, ThingDef registerDef, ThingDef storageDef, ThingDef wallDef, ThingDef doorDef, string goodsDefName, out string reason)
        {
            reason = "";
            CellRect outer = CellRect.CenteredOn(center, 4).ClipInsideMap(map);
            if (outer.Width < 7 || outer.Height < 7)
            {
                reason = "测试商店位置太靠近地图边缘。";
                return false;
            }

            CellRect inner = outer.ContractedBy(1);
            ClearAreaForShop(map, outer);
            BuildRoomShell(map, outer, wallDef, doorDef);
            SetConstructedRoof(map, inner);

            SimZone.Zone_Shop zone = CreateShopZone(map, inner);
            if (zone == null)
            {
                reason = "无法创建商店区。";
                return false;
            }

            Building_CashRegister register = SpawnRegister(map, registerDef, inner.CenterCell + IntVec3.South * 2);
            if (register == null)
            {
                reason = "无法放置收银台。";
                return false;
            }

            System.Collections.Generic.List<Building_SimContainer> storages = SpawnStorages(map, storageDef, inner, register.Position);
            if (storages.NullOrEmpty())
            {
                reason = "无法放置货柜。";
                return false;
            }

            SimDef.GoodsDef goodsDef = DefDatabase<SimDef.GoodsDef>.GetNamedSilentFail(goodsDefName)
                                      ?? DefDatabase<SimDef.GoodsDef>.AllDefsListForReading.FirstOrDefault(def => def != null && !def.GoodsList.NullOrEmpty());
            FillStoragesWithGoodsSet(storages, new[] { goodsDef });
            SpawnCashier(map, register);
            return true;
        }
    }
}
