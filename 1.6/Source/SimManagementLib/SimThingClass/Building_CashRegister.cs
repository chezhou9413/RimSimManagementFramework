using SimManagementLib.SimThingComp;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 商店收银台建筑，负责提供收银员识别、结账存银和取现兼容入口。
    /// </summary>
    public class Building_CashRegister : Building
    {
        private ThingComp_CashStorage CashStorage => this.GetComp<ThingComp_CashStorage>();

        /// <summary>
        /// 返回收银台内部已经收取但尚未取出的白银数量。
        /// </summary>
        public int StoredSilver => CashStorage?.StoredSilver ?? 0;

        /// <summary>
        /// 返回已经被搬运工作预约但尚未实际取出的白银数量。
        /// </summary>
        public int PendingWithdrawSilver => CashStorage?.PendingWithdrawSilver ?? 0;

        /// <summary>
        /// 返回当前还可以被新工作预约取出的白银数量。
        /// </summary>
        public int AvailableForWithdraw => CashStorage?.AvailableForWithdraw ?? 0;

        /// <summary>
        /// 获取当前正在这个收银台工作的殖民者。
        /// </summary>
        public Pawn CurrentCashier
        {
            get
            {
                if (!Spawned) return null;

                // 检查交互点，也就是收银员应该站立的位置上是否有人。
                Pawn pawn = Map.thingGrid.ThingAt<Pawn>(InteractionCell);

                // 只有正在执行收银任务的小人才算正在值班。
                if (pawn != null && pawn.CurJobDef != null && pawn.CurJobDef.defName == "Sim_ManCashRegister")
                {
                    return pawn;
                }

                return null;
            }
        }

        public bool IsManned => CurrentCashier != null;

        /// <summary>
        /// 把顾客结账支付的白银存入收银台现金库存。
        /// </summary>
        public void DepositSilver(int amount)
        {
            ThingComp_CashStorage cash = CashStorage;
            if (cash != null)
                cash.DepositSilver(amount);
        }

        /// <summary>
        /// 为搬运工作预约指定数量的收银台白银。
        /// </summary>
        public int ReserveWithdrawSilver(int desiredCount)
        {
            return CashStorage?.ReserveWithdrawSilver(desiredCount) ?? 0;
        }

        /// <summary>
        /// 取消已经预约但未完成的收银台取现数量。
        /// </summary>
        public void CancelWithdrawReservation(int reservedCount)
        {
            CashStorage?.CancelWithdrawReservation(reservedCount);
        }

        /// <summary>
        /// 从收银台现金库存取出已经预约的白银数量。
        /// </summary>
        public int WithdrawReservedSilver(int reservedCount)
        {
            return CashStorage?.WithdrawReservedSilver(reservedCount) ?? 0;
        }
    }
}
