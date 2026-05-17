using System;
using System.Collections.Generic;
using System.Text;
using RimWorld;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义建筑现金库存的默认取现阈值和玩家是否允许调整阈值。
    /// </summary>
    public class CompProperties_CashStorage : CompProperties
    {
        public int defaultWithdrawThreshold = 100;
        public int maxWithdrawThreshold = 1000;
        public bool showThresholdGizmos = true;

        /// <summary>
        /// 负责绑定建筑现金库存组件类型。
        /// </summary>
        public CompProperties_CashStorage()
        {
            compClass = typeof(ThingComp_CashStorage);
        }
    }

    /// <summary>
    /// 挂在收银台、自动售货机等建筑上的现金库存组件，负责保存白银金额、取现预约和取现阈值。
    /// </summary>
    public class ThingComp_CashStorage : ThingComp
    {
        private int storedSilver;
        private int pendingWithdrawSilver;
        private int withdrawThreshold = -1;
        private bool silverLeavingsHandled;

        private CompProperties_CashStorage CashProps => props as CompProperties_CashStorage;

        /// <summary>
        /// 返回建筑内部已经收取但尚未取出的白银数量。
        /// </summary>
        public int StoredSilver => storedSilver;

        /// <summary>
        /// 返回已经被搬运工作预约但尚未实际取出的白银数量。
        /// </summary>
        public int PendingWithdrawSilver => pendingWithdrawSilver;

        /// <summary>
        /// 返回当前还可以被新工作预约取出的白银数量。
        /// </summary>
        public int AvailableForWithdraw => Math.Max(0, storedSilver - pendingWithdrawSilver);

        /// <summary>
        /// 返回当前用于自动取现的可预约白银数量，避免单次任务超过白银堆叠上限。
        /// </summary>
        public int AutoWithdrawAmount => ShouldAutoWithdraw() ? Math.Min(AvailableForWithdraw, Mathf.Max(1, ThingDefOf.Silver.stackLimit)) : 0;

        /// <summary>
        /// 返回触发自动取现工作的最小可取白银数量。
        /// </summary>
        public int WithdrawThreshold => Mathf.Clamp(withdrawThreshold > 0 ? withdrawThreshold : DefaultWithdrawThreshold, 1, MaxWithdrawThreshold);

        /// <summary>
        /// 返回现金阈值滑条使用的最大显示和设置上限。
        /// </summary>
        public int MaxWithdrawThreshold => Mathf.Max(1, CashProps?.maxWithdrawThreshold ?? 1000);

        /// <summary>
        /// 返回 XML 中配置的默认取现阈值。
        /// </summary>
        private int DefaultWithdrawThreshold => Mathf.Max(1, CashProps?.defaultWithdrawThreshold ?? 100);

        /// <summary>
        /// 保存和读取建筑现金库存、取现预约和玩家设置的取现阈值。
        /// </summary>
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref storedSilver, "storedSilver", 0);
            Scribe_Values.Look(ref pendingWithdrawSilver, "pendingWithdrawSilver", 0);
            Scribe_Values.Look(ref withdrawThreshold, "withdrawThreshold", -1);
            Scribe_Values.Look(ref silverLeavingsHandled, "silverLeavingsHandled", false);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                SanitizeState();
        }

        /// <summary>
        /// 建筑生成后补齐默认阈值并清理非法状态。
        /// </summary>
        public override void PostPostMake()
        {
            base.PostPostMake();
            SanitizeState();
        }

        /// <summary>
        /// 在建筑检查面板中显示当前现金库存和自动取现阈值。
        /// </summary>
        public override string CompInspectStringExtra()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(SimTranslation.T("RSMF.CashStorage.StoredSilver", storedSilver.Named("stored")));
            if (pendingWithdrawSilver > 0)
                sb.Append(" ").Append(SimTranslation.T("RSMF.CashStorage.PendingWithdraw", pendingWithdrawSilver.Named("pending")));
            sb.AppendLine();
            sb.Append(SimTranslation.T("RSMF.CashStorage.WithdrawThresholdInspect", WithdrawThreshold.Named("threshold")));
            return sb.ToString();
        }

        /// <summary>
        /// 提供玩家调整自动取现阈值的建筑操作按钮。
        /// </summary>
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (CashProps?.showThresholdGizmos == false)
                yield break;

            yield return new Gizmo_CashWithdrawThreshold(this);
        }

        /// <summary>
        /// 提供选中小人右键经营建筑时强制取出白银的浮动菜单选项。
        /// </summary>
        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            if (selPawn == null || parent == null)
                yield break;

            string label = SimTranslation.T("RSMF.CashStorage.ForceWithdraw");
            if (AvailableForWithdraw <= 0)
            {
                yield return new FloatMenuOption(SimTranslation.T("RSMF.CashStorage.ForceWithdrawNoSilver", label.Named("label")), null);
                yield break;
            }

            if (!selPawn.CanReach(parent, PathEndMode.Touch, Danger.Deadly))
            {
                yield return new FloatMenuOption(SimTranslation.T("RSMF.CashStorage.ForceWithdrawUnreachable", label.Named("label")), null);
                yield break;
            }

            yield return new FloatMenuOption(SimTranslation.T("RSMF.CashStorage.ForceWithdrawAmount", label.Named("label"), AvailableForWithdraw.Named("amount")), delegate
            {
                TryStartForceWithdrawJob(selPawn);
            });
        }

        /// <summary>
        /// 建筑拆除或破坏时额外掉落内部白银，负责避免存银随建筑消失。
        /// </summary>
        public override IEnumerable<ThingDefCountClass> GetAdditionalLeavings(Map map, DestroyMode mode)
        {
            if (storedSilver <= 0)
                yield break;

            yield return new ThingDefCountClass(ThingDefOf.Silver, storedSilver);
            silverLeavingsHandled = true;
            storedSilver = 0;
            pendingWithdrawSilver = 0;
        }

        /// <summary>
        /// 建筑被原版不产生额外掉落的模式销毁时兜底吐出白银，负责保护现金库存不被拆除吞掉。
        /// </summary>
        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            TryDropStoredSilver(previousMap);
        }

        /// <summary>
        /// 把指定数量的白银收入存入建筑内部现金库存。
        /// </summary>
        public void DepositSilver(int amount)
        {
            if (amount <= 0) return;
            storedSilver += amount;
        }

        /// <summary>
        /// 为搬运工作预约指定数量的白银，并返回实际预约数量。
        /// </summary>
        public int ReserveWithdrawSilver(int desiredCount)
        {
            if (desiredCount <= 0) return 0;

            int available = AvailableForWithdraw;
            if (available <= 0) return 0;

            int actual = Math.Min(desiredCount, available);
            pendingWithdrawSilver += actual;
            return actual;
        }

        /// <summary>
        /// 取消已经预约但没有完成的取现数量。
        /// </summary>
        public void CancelWithdrawReservation(int reservedCount)
        {
            if (reservedCount <= 0) return;
            pendingWithdrawSilver = Math.Max(0, pendingWithdrawSilver - reservedCount);
        }

        /// <summary>
        /// 从建筑现金库存中取出已经预约的白银数量。
        /// </summary>
        public int WithdrawReservedSilver(int reservedCount)
        {
            if (reservedCount <= 0) return 0;

            int actual = Math.Min(reservedCount, storedSilver);
            storedSilver -= actual;
            pendingWithdrawSilver = Math.Max(0, pendingWithdrawSilver - reservedCount);
            return actual;
        }

        /// <summary>
        /// 判断当前现金库存是否达到自动取现条件。
        /// </summary>
        public bool ShouldAutoWithdraw()
        {
            return AvailableForWithdraw >= WithdrawThreshold;
        }

        /// <summary>
        /// 为玩家指定的小人立即创建取现搬运任务，强制操作不受自动阈值限制。
        /// </summary>
        private void TryStartForceWithdrawJob(Pawn pawn)
        {
            if (pawn == null || parent == null || AvailableForWithdraw <= 0)
                return;

            int batchSize = Mathf.Max(1, ThingDefOf.Silver.stackLimit);
            int desired = Mathf.Min(AvailableForWithdraw, batchSize);

            JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail("Sim_CollectCashRegisterSilver");
            if (jobDef == null)
                return;

            Job job = JobMaker.MakeJob(jobDef, parent);
            job.count = desired;
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        /// <summary>
        /// 清理现金库存和阈值中的非法值，避免读档或 XML 缺省导致负数状态。
        /// </summary>
        private void SanitizeState()
        {
            storedSilver = Math.Max(0, storedSilver);
            pendingWithdrawSilver = Mathf.Clamp(pendingWithdrawSilver, 0, storedSilver);
            if (withdrawThreshold <= 0)
                withdrawThreshold = DefaultWithdrawThreshold;
            withdrawThreshold = Mathf.Clamp(withdrawThreshold, 1, MaxWithdrawThreshold);
            silverLeavingsHandled = false;
        }

        /// <summary>
        /// 设置当前建筑的自动取现阈值。
        /// </summary>
        public void SetWithdrawThreshold(int threshold)
        {
            withdrawThreshold = Mathf.Clamp(threshold, 1, MaxWithdrawThreshold);
        }

        /// <summary>
        /// 将内部白银生成到建筑附近，负责在非标准销毁模式下兜底返还库存。
        /// </summary>
        private void TryDropStoredSilver(Map map)
        {
            if (silverLeavingsHandled || storedSilver <= 0 || map == null || parent == null)
                return;

            Thing silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = storedSilver;
            GenPlace.TryPlaceThing(silver, parent.PositionHeld, map, ThingPlaceMode.Near);
            storedSilver = 0;
            pendingWithdrawSilver = 0;
            silverLeavingsHandled = true;
        }
    }
}
