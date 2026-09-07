using SimManagementLib.SimThingClass;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //补货租约接口模块，职责是在 Job 预预约、完成和中断阶段维护唯一在途占用。
    public partial class MapComponent_RestockTaskQueue
    {
        //为普通补货 Job 幂等取得在途租约。
        public bool TryAcquireBulkLease(
            Pawn pawn,
            Job job,
            Building_SimContainer storage,
            ThingDef thingDef,
            int wanted,
            out int reserved)
        {
            reserved = 0;
            if (!IsValidStorage(storage) || storage is Building_UniqueGoodsContainer || thingDef == null || job == null)
                return false;
            EnsureRuntimeInitialized();
            RestockTaskKey key = new RestockTaskKey(storage.thingIDNumber, thingDef);
            activeRestockCycles.Add(key);
            int available = CalculateBulkAvailable(storage, thingDef);
            if (!leases.TryAcquire(pawn, job, key, wanted, available, out reserved))
                return false;
            EnsureBulkRequest(storage, thingDef, "普通补货租约取得");
            MarkRequestDirty(key, "普通补货租约取得");
            return true;
        }

        //为专业补货 Job 幂等取得精确槽位租约。
        public bool TryAcquireUniqueLease(Pawn pawn, Job job, Building_UniqueGoodsContainer storage, int sourceThingId)
        {
            if (!IsValidStorage(storage) || job == null || sourceThingId < 0)
                return false;
            EnsureRuntimeInitialized();
            if (!storage.TryGetPendingSlotIndex(sourceThingId, out int slotIndex))
                return false;
            RestockTaskKey key = RestockTaskKey.ForUnique(storage.thingIDNumber, slotIndex, sourceThingId);
            EnsureUniqueRequest(storage, slotIndex, sourceThingId, "专业补货租约取得");
            int available = leases.HasLease(key) ? 0 : 1;
            if (!leases.TryAcquire(pawn, job, key, 1, available, out _))
                return false;
            MarkRequestDirty(key, "专业补货租约取得");
            return true;
        }

        //释放 Job 租约并让原请求立即重新计算。
        public void ReleaseLease(Job job, string reason)
        {
            if (job == null)
                return;
            if (leases.Release(job.loadID, out RestockTaskKey key))
                MarkReleasedRequestDirty(key, reason);
        }

        //完成 Job 租约并让库存变化后的请求立即重新计算。
        public void CompleteLease(Job job, string reason)
        {
            ReleaseLease(job, reason);
        }
    }
}
