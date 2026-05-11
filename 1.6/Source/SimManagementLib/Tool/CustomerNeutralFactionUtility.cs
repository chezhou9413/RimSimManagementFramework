using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 管理商店顾客专用的隐藏中立派系，负责运行时创建、关系维护和顾客身份转换。
    /// </summary>
    public static class CustomerNeutralFactionUtility
    {
        private const string CustomerFactionDefName = "SimShop_NeutralCustomerFaction";

        /// <summary>
        /// 获取或创建商店顾客中立派系，并确保它与当前世界全部派系保持非敌对关系。
        /// </summary>
        public static Faction GetOrCreateCustomerFaction()
        {
            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(CustomerFactionDefName);
            if (factionDef == null)
            {
                Log.Error("[SimShop] 缺少商店顾客中立派系 Def: " + CustomerFactionDefName);
                return null;
            }

            Faction faction = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => f != null && f.def == factionDef);
            if (faction == null)
            {
                faction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(factionDef, default, true));
                faction.Name = "商店顾客";
                faction.hidden = true;
                faction.temporary = true;
                Find.FactionManager.Add(faction);
            }

            EnsureNeutralRelations(faction);
            return faction;
        }

        /// <summary>
        /// 将指定 Pawn 转入商店顾客中立派系，清空会干扰顾客访问的敌对和战斗状态。
        /// </summary>
        public static bool ConvertPawnToCustomerFaction(Pawn pawn, out Faction customerFaction)
        {
            customerFaction = GetOrCreateCustomerFaction();
            if (pawn == null || customerFaction == null) return false;

            if (pawn.Faction != customerFaction)
                pawn.SetFaction(customerFaction);

            pawn.mindState?.Reset(clearInspiration: false, clearMentalState: true);
            pawn.jobs?.StopAll();
            pawn.ClearAllReservations();
            if (pawn.drafter != null)
                pawn.drafter.Drafted = false;
            pawn.mindState.enemyTarget = null;
            pawn.mindState.lastAttackedTarget = null;
            pawn.mindState.lastAttackTargetTick = -999999;
            return true;
        }

        /// <summary>
        /// 确保商店顾客派系和所有现有派系都有明确的中立关系，避免沿用敌对来源派系的关系。
        /// </summary>
        private static void EnsureNeutralRelations(Faction customerFaction)
        {
            if (customerFaction == null) return;

            List<Faction> factions = Find.FactionManager.AllFactionsListForReading;
            for (int i = 0; i < factions.Count; i++)
            {
                Faction other = factions[i];
                if (other == null || other == customerFaction) continue;

                FactionRelation relation = customerFaction.RelationWith(other, allowNull: true);
                if (relation == null)
                {
                    customerFaction.SetRelation(new FactionRelation(other, FactionRelationKind.Neutral) { baseGoodwill = 0 });
                    continue;
                }

                relation.kind = FactionRelationKind.Neutral;
                relation.baseGoodwill = 0;

                FactionRelation reverse = other.RelationWith(customerFaction, allowNull: true);
                if (reverse == null)
                    other.SetRelation(new FactionRelation(customerFaction, FactionRelationKind.Neutral) { baseGoodwill = 0 });
                else
                {
                    reverse.kind = FactionRelationKind.Neutral;
                    reverse.baseGoodwill = 0;
                }
            }
        }
    }
}
