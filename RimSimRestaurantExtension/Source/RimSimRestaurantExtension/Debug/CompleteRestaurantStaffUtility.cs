using RimWorld;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimSimRestaurantExtension.Debug
{
    //创建完整餐厅需要的四类员工，职责是生成、分配岗位并让收银员立即值班。
    internal static class CompleteRestaurantStaffUtility
    {
        private const int ChefCookingLevel = 12;

        //解析收银员、厨师、服务员岗位与收银 Job，职责是在改动地图前完成前置校验。
        public static bool TryResolveDefinitions(out ShopStaffRoleDef cashierRole,
            out ShopStaffRoleDef chefRole, out ShopStaffRoleDef waiterRole,
            out JobDef cashierJobDef, out string failReason)
        {
            cashierRole = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("SimShopRole_Cashier");
            chefRole = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("RSR_Role_RestaurantChef");
            waiterRole = DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("RSR_Role_RestaurantWaiter");
            cashierJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Sim_ManCashRegister");
            failReason = "";
            if (cashierRole != null && chefRole != null && waiterRole != null && cashierJobDef != null
                && DefOfRefs.RSR_TakeRestaurantOrder != null && DefOfRefs.RSR_BringRestaurantMealToPass != null
                && DefOfRefs.RSR_WorkGiver_TakeRestaurantOrder != null && DefOfRefs.RSR_WorkGiver_BringRestaurantMealToPass != null)
                return true;
            failReason = "缺少餐厅岗位、接单、出餐或收银任务定义";
            return false;
        }

        //生成并分配完整餐厅员工，职责是确保厨师满足全部默认菜单的技能门槛。
        public static bool TrySpawnAndAssign(Map map, CellRect inner, Zone_Shop shop,
            Building_CashRegister register, Building_WorkTable stove,
            ShopStaffRoleDef cashierRole, ShopStaffRoleDef chefRole, ShopStaffRoleDef waiterRole,
            JobDef cashierJobDef, out string failReason)
        {
            failReason = "";
            Pawn cashier = SpawnStaffPawn(map, inner, register.InteractionCell, 0);
            Pawn chef = SpawnStaffPawn(map, inner, stove.InteractionCell, ChefCookingLevel);
            Pawn waiter = SpawnStaffPawn(map, inner, inner.CenterCell + IntVec3.South * 2, 0);
            Pawn restocker = SpawnStaffPawn(map, inner, inner.CenterCell + IntVec3.West, 0);
            if (cashier == null || chef == null || waiter == null || restocker == null)
            {
                failReason = "无法生成全部餐厅员工";
                return false;
            }

            shop.AddAssignedPawn(cashierRole.defName, cashier, cashierRole.maxAssignedPawns);
            shop.AddAssignedPawn(chefRole.defName, chef, chefRole.maxAssignedPawns);
            shop.AddAssignedPawn(waiterRole.defName, waiter, waiterRole.maxAssignedPawns);
            var restockRole = DefDatabase<ShopStaffRoleDef>.GetNamed("SimShopRole_Restocker");
            shop.AddAssignedPawn(restockRole.defName, restocker, restockRole.maxAssignedPawns);
            Job cashierJob = JobMaker.MakeJob(cashierJobDef, register);
            if (!cashier.jobs.TryTakeOrderedJob(cashierJob, JobTag.MiscWork))
            {
                failReason = "收银员无法开始值班";
                return false;
            }
            return true;
        }

        //生成一名测试员工，职责是选择可站立位置并按岗位需要保证最低烹饪技能。
        private static Pawn SpawnStaffPawn(Map map, CellRect inner, IntVec3 preferredCell, int cookingLevel)
        {
            Pawn pawn = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Pawn candidate = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                SkillRecord cooking = candidate.skills?.GetSkill(SkillDefOf.Cooking);
                if (cookingLevel > 0 && (cooking == null || cooking.TotallyDisabled)) continue;
                if (cookingLevel > 0)
                    cooking.levelInt = Mathf.Max(cooking.levelInt, cookingLevel);
                pawn = candidate;
                break;
            }
            if (pawn == null) return null;

            IntVec3 spawnCell = preferredCell;
            if (!inner.Contains(spawnCell) || !spawnCell.Standable(map))
            {
                if (!CellFinder.TryFindRandomCellNear(inner.CenterCell, map, 6,
                        cell => inner.Contains(cell) && cell.Standable(map), out spawnCell))
                    return null;
            }
            return GenSpawn.Spawn(pawn, spawnCell, map) as Pawn;
        }
    }
}
