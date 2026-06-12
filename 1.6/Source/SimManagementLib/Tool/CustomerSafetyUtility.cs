using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace SimManagementLib.Tool
{
    // 提供顾客安全相关判断，负责处理大袭击避险、顾客专用寻路和紧急丢弃商品。
    public static class CustomerSafetyUtility
    {
        private const float LargeRaidCombatPowerThreshold = 1000f;

        // 判断 Pawn 是否属于模拟经营顾客，负责让门禁和安全逻辑只作用在顾客身上。
        public static bool IsCustomerPawn(Pawn pawn)
        {
            LordJob lordJob = pawn?.lord?.LordJob;
            return lordJob is LordJob_CustomerVisit
                || lordJob is LordJob_VendingMachineVisit;
        }

        // 判断地图上是否存在超过阈值的敌对袭击，负责阻止刷客和触发顾客紧急离店。
        public static bool IsLargeHostileRaidActive(Map map)
        {
            if (map?.lordManager?.lords == null)
                return false;

            float totalCombatPower = 0f;
            for (int i = 0; i < map.lordManager.lords.Count; i++)
            {
                Lord lord = map.lordManager.lords[i];
                if (!IsHostileRaidLord(lord))
                    continue;

                totalCombatPower += CountActiveRaidCombatPower(lord);
                if (totalCombatPower > LargeRaidCombatPowerThreshold)
                    return true;
            }

            return false;
        }

        // 判断顾客是否可以到达目标，负责额外避开玩家手动禁用的门。
        public static bool CanCustomerReach(Pawn customer, LocalTargetInfo target, PathEndMode pathEndMode, Danger danger)
        {
            if (customer == null || !customer.Spawned || customer.Map == null || !target.IsValid)
                return false;

            if (!customer.CanReach(target, pathEndMode, danger))
                return false;

            return !PathUsesForbiddenPlayerDoor(customer, target, pathEndMode, danger);
        }

        // 判断顾客是否可以到达指定格，负责复用 LocalTargetInfo 版本的完整规则。
        public static bool CanCustomerReach(Pawn customer, IntVec3 cell, PathEndMode pathEndMode, Danger danger)
        {
            return cell.IsValid && CanCustomerReach(customer, new LocalTargetInfo(cell), pathEndMode, danger);
        }

        // 判断顾客是否可以到达指定 Thing，负责复用 LocalTargetInfo 版本的完整规则。
        public static bool CanCustomerReach(Pawn customer, Thing thing, PathEndMode pathEndMode, Danger danger)
        {
            return thing != null && CanCustomerReach(customer, new LocalTargetInfo(thing), pathEndMode, danger);
        }

        // 丢弃顾客当前手持的物品，负责让紧急离店不继续携带商品。
        public static void DropCarriedThing(Pawn customer)
        {
            if (customer?.carryTracker?.CarriedThing == null || customer.MapHeld == null)
                return;

            customer.carryTracker.TryDropCarriedThing(customer.PositionHeld, ThingPlaceMode.Near, out _);
        }

        // 按购买记录丢弃已交付到顾客身上的商品，负责避免顾客在大袭击中带走店内商品。
        public static void DropDeliveredItems(Pawn customer, List<CustomerCartItem> deliveredItems)
        {
            if (customer == null)
                return;

            DropCarriedThing(customer);
            if (deliveredItems.NullOrEmpty())
                return;

            for (int i = 0; i < deliveredItems.Count; i++)
            {
                CustomerCartItem item = deliveredItems[i];
                if (item == null || item.def == null || item.count <= 0)
                    continue;

                DropInventoryCount(customer, item.def, item.count);
            }
        }

        // 判断 Lord 是否属于当前地图上的敌对袭击流程。
        private static bool IsHostileRaidLord(Lord lord)
        {
            if (lord == null || lord.faction == null || lord.faction == Faction.OfPlayer)
                return false;
            if (!lord.faction.HostileTo(Faction.OfPlayer))
                return false;

            string jobName = lord.LordJob?.GetType().Name ?? "";
            return jobName.Contains("Assault")
                || jobName.Contains("Siege")
                || jobName.Contains("StageThenAttack");
        }

        // 统计袭击 Lord 当前仍在地图上的战斗力。
        private static float CountActiveRaidCombatPower(Lord lord)
        {
            if (lord?.ownedPawns == null)
                return 0f;

            float total = 0f;
            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                    continue;

                total += pawn.kindDef?.combatPower ?? 0f;
            }

            return total;
        }

        // 判断生成路径是否会经过玩家禁用门。
        private static bool PathUsesForbiddenPlayerDoor(Pawn customer, LocalTargetInfo target, PathEndMode pathEndMode, Danger danger)
        {
            using (PawnPath path = customer.Map.pathFinder.FindPathNow(
                customer.Position,
                target,
                TraverseParms.For(customer, danger, TraverseMode.ByPawn),
                null,
                pathEndMode))
            {
                if (path == null || !path.Found)
                    return true;

                List<IntVec3> nodes = path.NodesReversed;
                for (int i = 0; i < nodes.Count; i++)
                {
                    Building_Door door = nodes[i].GetDoor(customer.Map);
                    if (door != null && IsPlayerForbiddenDoor(door))
                        return true;
                }
            }

            return false;
        }

        // 判断门是否是玩家拥有并手动禁用的门。
        public static bool IsPlayerForbiddenDoor(Building_Door door)
        {
            return door != null
                && door.Faction == Faction.OfPlayer
                && door.IsForbidden(Faction.OfPlayer);
        }

        // 从顾客背包里丢出指定数量的已购买物品。
        private static void DropInventoryCount(Pawn customer, ThingDef def, int count)
        {
            if (customer?.inventory?.innerContainer == null || def == null || count <= 0)
                return;

            int remaining = count;
            for (int i = customer.inventory.innerContainer.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Thing thing = customer.inventory.innerContainer[i];
                if (thing == null || thing.Destroyed || thing.def != def || thing.stackCount <= 0)
                    continue;

                int dropCount = System.Math.Min(remaining, thing.stackCount);
                customer.inventory.innerContainer.TryDrop(
                    thing,
                    customer.PositionHeld,
                    customer.MapHeld,
                    ThingPlaceMode.Near,
                    dropCount,
                    out _);
                remaining -= dropCount;
            }
        }
    }
}
