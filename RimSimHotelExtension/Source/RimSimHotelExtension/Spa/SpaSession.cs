using SimManagementLib.SimZone;
using System.Linq;
using Verse;

namespace RimSimHotelExtension.Spa
{
    //表示泡汤流程中已经完成的步骤，读档后不会重新扣钱或重复脱衣。
    public enum SpaSessionStage
    {
        Queued, GoingToLocker, GoingToPool, Soaking, ReturningToLocker, Completed, Canceled
    }

    //保存一次泡汤的资源预约、托管票款和已使用时长。
    public sealed class SpaSession : IExposable
    {
        public int id;
        public int actionOrderId;
        public Pawn customer;
        public Building_HotelSpa spa;
        public Building_HotelLocker locker;
        public int shopZoneId;
        public int slot = -1;
        public IntVec3 poolCell = IntVec3.Invalid;
        public SpaSessionStage stage;
        public int createdTick;
        public int stageTick;
        public int completedTick;
        public int price;
        public int durationTicks;
        public int elapsedTicks;
        public float joyGain;
        public float satisfactionGain;
        public bool ticketPaid;
        public bool incomeCommitted;
        public bool clothesStored;
        public bool benefitGranted;
        public string endReason = "";

        public bool IsTerminal => stage == SpaSessionStage.Completed || stage == SpaSessionStage.Canceled;
        public bool HasReservedSlot => !IsTerminal && slot >= 0;

        //从地图恢复原商店，避免顾客切换商店后给另一家店记账。
        public Zone_Shop Shop(Map map)
        {
            return map?.zoneManager.AllZones.OfType<Zone_Shop>().FirstOrDefault(s => s.ID == shopZoneId);
        }

        //保存跨 Job 的完整状态和实际对象引用。
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref actionOrderId, "actionOrderId");
            Scribe_References.Look(ref customer, "customer");
            Scribe_References.Look(ref spa, "spa");
            Scribe_References.Look(ref locker, "locker");
            Scribe_Values.Look(ref shopZoneId, "shopZoneId");
            Scribe_Values.Look(ref slot, "slot", -1);
            Scribe_Values.Look(ref poolCell, "poolCell", IntVec3.Invalid);
            Scribe_Values.Look(ref stage, "stage");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref stageTick, "stageTick");
            Scribe_Values.Look(ref completedTick, "completedTick");
            Scribe_Values.Look(ref price, "price");
            Scribe_Values.Look(ref durationTicks, "durationTicks");
            Scribe_Values.Look(ref elapsedTicks, "elapsedTicks");
            Scribe_Values.Look(ref joyGain, "joyGain");
            Scribe_Values.Look(ref satisfactionGain, "satisfactionGain");
            Scribe_Values.Look(ref ticketPaid, "ticketPaid");
            Scribe_Values.Look(ref incomeCommitted, "incomeCommitted");
            Scribe_Values.Look(ref clothesStored, "clothesStored");
            Scribe_Values.Look(ref benefitGranted, "benefitGranted");
            Scribe_Values.Look(ref endReason, "endReason", "");
        }
    }

    //独立记录普通散客和住客的泡汤冷却，不能通过切店或重新落单绕过。
    public sealed class SpaCooldown : IExposable
    {
        public int pawnId;
        public int usedTick;
        public int localDay;

        //保存最近一次开始泡汤的时间。
        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId");
            Scribe_Values.Look(ref usedTick, "usedTick");
            Scribe_Values.Look(ref localDay, "localDay");
        }
    }
}
