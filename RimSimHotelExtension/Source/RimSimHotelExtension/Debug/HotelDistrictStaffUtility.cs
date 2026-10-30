using System;
using RimWorld;
using RimSimHotelExtension.Tool;
using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using Verse;
using Verse.AI;

namespace RimSimHotelExtension.Debug
{
    //为示例商店创建具备服务能力的殖民者，并保留正常睡眠与生活需求。
    internal static class HotelDistrictStaffUtility
    {
        //由原版生成校验筛选合格员工，不在外部循环生成弃用人物。
        internal static Pawn Spawn(Map map, Zone_Shop shop, Building_CashRegister register,
            Building_Bed staffBed, bool reception)
        {
            if (register == null || !register.Spawned || register.Map != map || !shop.ContainsCell(register.Position)
                || !register.InteractionCell.InBounds(map) || !register.InteractionCell.Standable(map))
                throw new InvalidOperationException(HotelText.T("RSH.Text.TheRegisterOrItsStaffInteractionCellIs"));
            if (staffBed == null || !staffBed.Spawned || staffBed.Map != map || staffBed.Medical
                || staffBed.ForPrisoners || staffBed.ForSlaves || staffBed.OwnersForReading.Count != 0)
                throw new InvalidOperationException(HotelText.T("RSH.Text.TheStaffBedIsUnavailable"));
            string roleName = reception ? "RSH_Role_Reception" : "SimShopRole_Cashier";
            ShopStaffRoleDef role = DefDatabase<ShopStaffRoleDef>.GetNamed(roleName);
            WorkTypeDef service = DefDatabase<WorkTypeDef>.GetNamed("Sim_ServiceWork");
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn: true, allowDead: false, allowDowned: false,
                canGeneratePawnRelations: false, allowAddictions: false, fixedBiologicalAge: 28f, fixedChronologicalAge: 28f,
                validatorPreGear: pawn => !pawn.WorkTypeIsDisabled(service)
                    && SimShopStaffApi.EvaluateEligibility(pawn, role).Eligible);
            Pawn staff = PawnGenerator.GeneratePawn(request);
            if (staff == null || !SimShopStaffApi.EvaluateEligibility(staff, role).Eligible)
                throw new InvalidOperationException(HotelText.T("RSH.Text.PawnGenerationDidNotProduceAnEligibleService"));
            GenSpawn.Spawn(staff, register.InteractionCell, map);
            shop.AddAssignedPawn(roleName, staff, role.MaxAssignedPawns);
            staff.workSettings.SetPriority(service, 1);
            staff.ownership.ClaimBedIfNonMedical(staffBed);
            for (int hour = 0; hour < 24; hour++)
            {
                TimeAssignmentDef assignment = hour >= 22 || hour < 6 ? TimeAssignmentDefOf.Sleep
                    : hour >= 10 && hour < 20 ? TimeAssignmentDefOf.Work : TimeAssignmentDefOf.Anything;
                staff.timetable.SetAssignment(hour, assignment);
            }
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Sim_ManCashRegister"), register);
            if (!staff.jobs.TryTakeOrderedJob(job, JobTag.MiscWork))
                throw new InvalidOperationException(HotelText.T("RSH.Text.TheEmployeeCouldNotStartCashierDuty")
                    + staff.LabelShortCap + " / " + shop.label);
            ShopDataUtility.NotifyBuildingChanged(map, register.Position);
            return staff;
        }
    }
}
