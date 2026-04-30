using System;
using System.Text;
using Verse;

namespace SimManagementLib.SimThingClass
{
    public class Building_CashRegister : Building
    {
        private int storedSilver;
        private int pendingWithdrawSilver;

        public int StoredSilver => storedSilver;
        public int PendingWithdrawSilver => pendingWithdrawSilver;
        public int AvailableForWithdraw => Math.Max(0, storedSilver - pendingWithdrawSilver);

        // 获取当前正在这个收银台工作的殖民者
        public Pawn CurrentCashier
        {
            get
            {
                if (!Spawned) return null;

                // 检查交互点（InteractionCell，也就是小人站的位置）上有没有人
                Pawn pawn = Map.thingGrid.ThingAt<Pawn>(InteractionCell);

                // 如果有人，且他正在执行“收银”任务，那他就是收银员
                if (pawn != null && pawn.CurJobDef != null && pawn.CurJobDef.defName == "Sim_ManCashRegister")
                {
                    return pawn;
                }

                return null;
            }
        }

        public bool IsManned => CurrentCashier != null;

        public void DepositSilver(int amount)
        {
            if (amount <= 0) return;
            storedSilver += amount;
        }

        public int ReserveWithdrawSilver(int desiredCount)
        {
            if (desiredCount <= 0) return 0;

            int available = AvailableForWithdraw;
            if (available <= 0) return 0;

            int actual = Math.Min(desiredCount, available);
            pendingWithdrawSilver += actual;
            return actual;
        }

        public void CancelWithdrawReservation(int reservedCount)
        {
            if (reservedCount <= 0) return;
            pendingWithdrawSilver = Math.Max(0, pendingWithdrawSilver - reservedCount);
        }

        public int WithdrawReservedSilver(int reservedCount)
        {
            if (reservedCount <= 0) return 0;

            int actual = Math.Min(reservedCount, storedSilver);
            storedSilver -= actual;

            // 归还预约：按 reservedCount 扣除，避免预约残留。
            pendingWithdrawSilver = Math.Max(0, pendingWithdrawSilver - reservedCount);
            return actual;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref storedSilver, "storedSilver", 0);
            Scribe_Values.Look(ref pendingWithdrawSilver, "pendingWithdrawSilver", 0);
        }

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(baseText))
            {
                sb.Append(baseText);
            }

            if (sb.Length > 0) sb.AppendLine();
            sb.Append("白银库存: ").Append(storedSilver);

            if (pendingWithdrawSilver > 0)
            {
                sb.Append(" (待搬运 ").Append(pendingWithdrawSilver).Append(")");
            }

            return sb.ToString();
        }
    }
}
