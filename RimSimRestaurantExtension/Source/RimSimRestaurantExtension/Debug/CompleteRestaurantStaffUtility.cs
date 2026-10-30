using RimWorld;
using SimManagementLib.Api;
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
                && DefDatabase<ShopStaffRoleDef>.GetNamedSilentFail("SimShopRole_Restocker") != null
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
            var restockRole = DefDatabase<ShopStaffRoleDef>.GetNamed("SimShopRole_Restocker");
            Pawn cashier = SpawnStaffPawn(map, inner, register.InteractionCell, cashierRole);
            Pawn chef = SpawnStaffPawn(map, inner, stove.InteractionCell, chefRole, ChefCookingLevel);
            Pawn waiter = SpawnStaffPawn(map, inner, inner.CenterCell + IntVec3.South * 2, waiterRole);
            Pawn restocker = SpawnStaffPawn(map, inner, inner.CenterCell + IntVec3.West, restockRole);
            if (cashier == null || chef == null || waiter == null || restocker == null)
            {
                failReason = "无法生成全部餐厅员工";
                return false;
            }

            AssignStaff(shop, cashier, cashierRole);
            AssignStaff(shop, chef, chefRole);
            AssignStaff(shop, waiter, waiterRole);
            AssignStaff(shop, restocker, restockRole);
            Job cashierJob = JobMaker.MakeJob(cashierJobDef, register);
            if (!cashier.jobs.TryTakeOrderedJob(cashierJob, JobTag.MiscWork))
            {
                failReason = "收银员无法开始值班";
                return false;
            }
            return true;
        }

        //分配岗位并优先执行该岗位工作，使测试餐厅能立即接待顾客。
        private static void AssignStaff(Zone_Shop shop, Pawn pawn, ShopStaffRoleDef role)
        {
            shop.AddAssignedPawn(role.defName, pawn, role.maxAssignedPawns);
            if (role.requiredWorkType != null) pawn.workSettings.SetPriority(role.requiredWorkType, 1);
        }

        //通过原版生成校验筛选能承担岗位的员工，厨师保证默认菜单所需技能。
        private static Pawn SpawnStaffPawn(Map map, CellRect inner, IntVec3 preferredCell,
            ShopStaffRoleDef role, int cookingLevel = 0)
        {
            IntVec3 spawnCell = preferredCell;
            if (!inner.Contains(spawnCell) || !spawnCell.Standable(map))
            {
                if (!CellFinder.TryFindRandomCellNear(inner.CenterCell, map, 6,
                        cell => inner.Contains(cell) && cell.Standable(map), out spawnCell))
                    return null;
            }
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, allowAddictions: false, fixedBiologicalAge: 28f, fixedChronologicalAge: 28f,
                validatorPreGear: candidate => SimShopStaffApi.EvaluateEligibility(candidate, role).Eligible);
            Pawn pawn = PawnGenerator.GeneratePawn(request);
            if (pawn == null || !SimShopStaffApi.EvaluateEligibility(pawn, role).Eligible) return null;
            if (cookingLevel > 0)
            {
                SkillRecord cooking = pawn.skills.GetSkill(SkillDefOf.Cooking);
                cooking.levelInt = Mathf.Max(cooking.levelInt, cookingLevel);
            }
            return GenSpawn.Spawn(pawn, spawnCell, map) as Pawn;
        }
    }
}
