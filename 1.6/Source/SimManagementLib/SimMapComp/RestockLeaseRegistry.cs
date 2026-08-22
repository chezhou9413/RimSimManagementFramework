using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimMapComp
{
    //补货租约，职责是把一个 Job 的在途数量绑定到稳定的补货请求。
    internal sealed class RestockLease
    {
        public readonly int JobId;
        public readonly Pawn Pawn;
        public readonly Job Job;
        public readonly RestockTaskKey RequestKey;
        public readonly int Count;

        //创建补货租约。
        public RestockLease(Pawn pawn, Job job, RestockTaskKey requestKey, int count)
        {
            JobId = job?.loadID ?? -1;
            Pawn = pawn;
            Job = job;
            RequestKey = requestKey;
            Count = count;
        }
    }

    //补货租约登记器，职责是提供幂等预约、聚合在途数量和失效 Job 回收。
    internal sealed class RestockLeaseRegistry
    {
        private readonly Dictionary<int, RestockLease> leasesByJobId = new Dictionary<int, RestockLease>();
        private readonly Dictionary<RestockTaskKey, int> countsByRequest = new Dictionary<RestockTaskKey, int>();
        private readonly Dictionary<int, int> bulkCountsByStorage = new Dictionary<int, int>();
        private readonly List<int> staleJobIds = new List<int>();

        public int Count => leasesByJobId.Count;

        //清空所有运行时租约。
        public void Clear()
        {
            leasesByJobId.Clear();
            countsByRequest.Clear();
            bulkCountsByStorage.Clear();
            staleJobIds.Clear();
        }

        //为 Job 幂等取得租约，同一个 Job 重复预约时返回原数量。
        public bool TryAcquire(Pawn pawn, Job job, RestockTaskKey requestKey, int wanted, int available, out int reserved)
        {
            reserved = 0;
            if (pawn == null || job == null || job.loadID < 0 || wanted <= 0)
                return false;

            if (leasesByJobId.TryGetValue(job.loadID, out RestockLease existing))
            {
                if (!existing.RequestKey.Equals(requestKey)
                    || !ReferenceEquals(existing.Job, job)
                    || !ReferenceEquals(existing.Pawn, pawn)
                    || existing.Count <= 0)
                {
                    Log.Error($"补货 Job 与租约不一致，已清理旧租约：Job={job.loadID}，旧货柜={existing.RequestKey.StorageId}，新货柜={requestKey.StorageId}。");
                    Release(job.loadID, out _);
                }
                else
                {
                    reserved = existing.Count;
                    job.count = existing.Count;
                    return reserved > 0;
                }
            }

            int actual = Math.Min(wanted, available);
            if (actual <= 0)
                return false;

            RestockLease lease = new RestockLease(pawn, job, requestKey, actual);
            leasesByJobId.Add(job.loadID, lease);
            countsByRequest.TryGetValue(requestKey, out int current);
            countsByRequest[requestKey] = current + actual;
            if (requestKey.Kind == RestockRequestKind.Bulk)
            {
                bulkCountsByStorage.TryGetValue(requestKey.StorageId, out int storageCurrent);
                bulkCountsByStorage[requestKey.StorageId] = storageCurrent + actual;
            }
            job.count = actual;
            reserved = actual;
            return true;
        }

        //释放指定 Job 的租约并返回受影响的请求键。
        public bool Release(int jobId, out RestockTaskKey requestKey)
        {
            requestKey = default(RestockTaskKey);
            if (jobId < 0 || !leasesByJobId.TryGetValue(jobId, out RestockLease lease))
                return false;

            leasesByJobId.Remove(jobId);
            requestKey = lease.RequestKey;
            countsByRequest.TryGetValue(requestKey, out int current);
            int next = current - lease.Count;
            if (next < 0)
            {
                Log.Error($"补货租约计数为负：货柜={requestKey.StorageId}，Job={jobId}，当前={current}，释放={lease.Count}。");
                next = 0;
            }

            if (next <= 0)
                countsByRequest.Remove(requestKey);
            else
                countsByRequest[requestKey] = next;
            if (requestKey.Kind == RestockRequestKind.Bulk)
            {
                bulkCountsByStorage.TryGetValue(requestKey.StorageId, out int storageCurrent);
                int storageNext = storageCurrent - lease.Count;
                if (storageNext < 0)
                {
                    Log.Error($"货柜补货租约总数为负，已清零：货柜={requestKey.StorageId}，当前={storageCurrent}，释放={lease.Count}。");
                    storageNext = 0;
                }
                if (storageNext <= 0)
                    bulkCountsByStorage.Remove(requestKey.StorageId);
                else
                    bulkCountsByStorage[requestKey.StorageId] = storageNext;
            }
            return true;
        }

        //返回指定普通补货请求的在途数量。
        public int CountPending(int storageId, ThingDef thingDef)
        {
            if (storageId < 0 || thingDef == null)
                return 0;
            RestockTaskKey key = new RestockTaskKey(storageId, thingDef);
            if (!countsByRequest.TryGetValue(key, out int value))
                return 0;
            if (value >= 0)
                return value;
            Log.Error($"补货请求租约聚合为负，已清理：货柜={storageId}，商品={thingDef.defName}，数量={value}。");
            RemoveRequestLeases(key);
            return 0;
        }

        //返回指定货柜全部普通补货请求的在途数量。
        public int CountTotalPending(int storageId)
        {
            if (storageId < 0)
                return 0;
            if (!bulkCountsByStorage.TryGetValue(storageId, out int total))
                return 0;
            if (total >= 0)
                return total;
            Log.Error($"货柜补货租约聚合为负，已清理：货柜={storageId}，数量={total}。");
            RemoveStorage(storageId, null);
            return 0;
        }

        //判断指定请求当前是否已有有效租约。
        public bool HasLease(RestockTaskKey requestKey)
        {
            return countsByRequest.TryGetValue(requestKey, out int value) && value > 0;
        }

        //回收已经不在 Pawn 当前或排队工作中的租约。
        public void PruneStaleLeases(Action<RestockTaskKey> onReleased)
        {
            staleJobIds.Clear();
            foreach (KeyValuePair<int, RestockLease> entry in leasesByJobId)
            {
                if (!PawnStillOwnsJob(entry.Value.Pawn, entry.Value.Job))
                {
                    Log.Error($"补货租约与 Pawn 工作不一致，已回收：Job={entry.Key}，Pawn={entry.Value.Pawn?.LabelShort ?? "无"}，货柜={entry.Value.RequestKey.StorageId}。");
                    staleJobIds.Add(entry.Key);
                }
            }

            for (int i = 0; i < staleJobIds.Count; i++)
            {
                if (Release(staleJobIds[i], out RestockTaskKey key))
                    onReleased?.Invoke(key);
            }
            staleJobIds.Clear();
        }

        //移除目标货柜已经不存在的全部租约。
        public void RemoveStorage(int storageId, Action<RestockTaskKey> onReleased)
        {
            staleJobIds.Clear();
            foreach (KeyValuePair<int, RestockLease> entry in leasesByJobId)
            {
                if (entry.Value.RequestKey.StorageId == storageId)
                    staleJobIds.Add(entry.Key);
            }

            for (int i = 0; i < staleJobIds.Count; i++)
            {
                if (Release(staleJobIds[i], out RestockTaskKey key))
                    onReleased?.Invoke(key);
            }
            staleJobIds.Clear();
        }

        //清理指定请求的全部 Job 租约，职责是在聚合损坏时恢复一致运行态。
        private void RemoveRequestLeases(RestockTaskKey requestKey)
        {
            staleJobIds.Clear();
            foreach (KeyValuePair<int, RestockLease> entry in leasesByJobId)
            {
                if (entry.Value.RequestKey.Equals(requestKey))
                    staleJobIds.Add(entry.Key);
            }
            for (int i = 0; i < staleJobIds.Count; i++)
                Release(staleJobIds[i], out _);
            staleJobIds.Clear();
            countsByRequest.Remove(requestKey);
        }

        //判断 Pawn 是否仍持有指定 Job。
        private static bool PawnStillOwnsJob(Pawn pawn, Job job)
        {
            if (pawn == null || job == null || pawn.Destroyed || pawn.Dead || pawn.jobs == null)
                return false;
            if (ReferenceEquals(pawn.CurJob, job))
                return true;
            return pawn.jobs.jobQueue != null && pawn.jobs.jobQueue.Contains(job);
        }
    }
}
