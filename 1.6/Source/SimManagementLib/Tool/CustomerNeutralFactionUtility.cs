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
        internal const string CustomerFactionDefName = "SimShop_NeutralCustomerFaction";

        /// <summary>
        /// 判断指定派系是否为商店顾客专用中立派系，供生成、战斗和兼容补丁统一识别顾客身份。
        /// </summary>
        public static bool IsCustomerFaction(Faction faction)
        {
            return faction?.def != null && faction.def.defName == CustomerFactionDefName;
        }

        /// <summary>
        /// 获取或创建商店顾客中立派系，并确保它与当前世界全部派系保持非敌对关系。
        /// </summary>
        public static Faction GetOrCreateCustomerFaction()
        {
            FactionDef factionDef = DefDatabase<FactionDef>.GetNamedSilentFail(CustomerFactionDefName);
            if (factionDef == null)
            {
                Log.Error("[SimShop] " + SimTranslation.T("RSMF.CustomerFaction.MissingDef", CustomerFactionDefName.Named("defName")));
                return null;
            }

            Faction faction = Find.FactionManager.AllFactionsListForReading
                .FirstOrDefault(f => f != null && f.def == factionDef);
            if (faction == null)
            {
                faction = CreateCustomerFactionWithoutLeader(factionDef);
                faction.Name = SimTranslation.T("RSMF.CustomerFaction.Name");
                faction.hidden = true;
                faction.temporary = true;
                Find.FactionManager.Add(faction);
            }

            EnsureNeutralRelations(faction);
            return faction;
        }

        /// <summary>
        /// 判断 Pawn 是否是不能被转入顾客派系的真实世界派系领袖。
        /// </summary>
        public static bool IsProtectedFactionLeader(Pawn pawn)
        {
            if (pawn == null || !PawnUtility.IsFactionLeader(pawn)) return false;

            Faction leaderFaction = PawnUtility.GetFactionLeaderFaction(pawn);
            return leaderFaction != null && !IsCustomerFaction(leaderFaction);
        }

        /// <summary>
        /// 创建顾客专用隐藏派系，负责避开原版生成派系领袖时的亲属关系生成流程。
        /// </summary>
        private static Faction CreateCustomerFactionWithoutLeader(FactionDef factionDef)
        {
            Faction faction = new Faction
            {
                def = factionDef,
                loadID = Find.UniqueIDsManager.GetNextFactionID(),
                colorFromSpectrum = 0.5f,
                hidden = true,
                temporary = true
            };

            if (factionDef.humanlikeFaction)
            {
                faction.ideos = new FactionIdeosTracker(faction);
                if (ModsConfig.IdeologyActive)
                    faction.ideos.ChooseOrGenerateIdeo(new IdeoGenerationParms(factionDef, hidden: true, requiredPreceptsOnly: true));
            }

            return faction;
        }

        /// <summary>
        /// 将指定 Pawn 转入商店顾客中立派系，清空会干扰顾客访问的敌对和战斗状态。
        /// </summary>
        public static bool ConvertPawnToCustomerFaction(Pawn pawn, out Faction customerFaction)
        {
            customerFaction = GetOrCreateCustomerFaction();
            if (pawn == null || customerFaction == null) return false;

            if (IsProtectedFactionLeader(pawn))
            {
                Log.Warning("[SimShop] 跳过真实派系领袖顾客转换，避免原派系领袖被替换：" + pawn.LabelShortCap);
                return false;
            }

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
