using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 商店收银台建筑，负责提供收银员识别、结账存银和取现兼容入口。
    /// </summary>
    public class Building_CashRegister : Building
    {
        private const int CashierCacheTicks = 15;
        private ThingComp_CashStorage CashStorage => this.GetComp<ThingComp_CashStorage>();
        private Pawn cachedCashier;
        private int nextCashierCheckTick = -1;

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
                if (!Spawned)
                {
                    cachedCashier = null;
                    return null;
                }

                int now = Find.TickManager?.TicksGame ?? 0;
                if (now < nextCashierCheckTick)
                {
                    if (cachedCashier == null || IsValidCashier(cachedCashier))
                        return cachedCashier;
                }

                cachedCashier = Map.thingGrid.ThingAt<Pawn>(InteractionCell);
                if (!IsValidCashier(cachedCashier))
                    cachedCashier = null;
                nextCashierCheckTick = now + CashierCacheTicks;
                return cachedCashier;
            }
        }

        public bool IsManned => CurrentCashier != null;

        //注册收银台到商店设施缓存，职责是让岗位和经营指标立即看到新建筑。
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            ShopDataUtility.NotifyBuildingChanged(map, Position);
        }

        //从商店设施缓存移除收银台，职责是避免拆除后继续使用失效建筑引用。
        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map oldMap = MapHeld;
            IntVec3 oldPosition = PositionHeld;
            base.DeSpawn(mode);
            ShopDataUtility.NotifyBuildingChanged(oldMap, oldPosition);
        }

        //判断 Pawn 是否正在当前收银台交互格执行值班任务，职责是防止同类 Job 被误认成当前收银员。
        private bool IsValidCashier(Pawn pawn)
        {
            return pawn != null
                && pawn.Spawned
                && pawn.Map == Map
                && pawn.Position == InteractionCell
                && pawn.CurJobDef?.defName == "Sim_ManCashRegister"
                && pawn.CurJob?.targetA.Thing == this;
        }

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
