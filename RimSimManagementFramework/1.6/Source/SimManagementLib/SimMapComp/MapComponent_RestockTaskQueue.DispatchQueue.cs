using System.Collections.Generic;

namespace SimManagementLib.SimMapComp
{
    //补货派工队列模块，职责是分离普通与专业就绪请求，并按精确 tick 唤醒阻塞请求。
    public partial class MapComponent_RestockTaskQueue
    {
        //按请求类型把已经到期的需求加入去重派工队列。
        private void EnqueueReadyDispatch(RestockTaskKey key)
        {
            if (!requests.ContainsKey(key))
                return;
            if (key.Kind == RestockRequestKind.Unique)
                uniqueDispatchQueue.Add(key);
            else
                bulkDispatchQueue.Add(key);
        }

        //把未到重试时间的请求放入精确 tick 桶，避免每个 Pawn 反复轮询全部阻塞请求。
        private void ScheduleDispatch(RestockTaskKey key, int retryTick, int now)
        {
            if (!requests.ContainsKey(key))
                return;
            if (retryTick <= now)
            {
                RemoveDelayedDispatch(key);
                EnqueueReadyDispatch(key);
                return;
            }
            if (delayedDispatchTicks.TryGetValue(key, out int existingTick) && existingTick == retryTick)
                return;

            RemoveDelayedDispatch(key);
            RemoveReadyDispatch(key);
            delayedDispatchTicks[key] = retryTick;
            if (!delayedDispatchBuckets.TryGetValue(retryTick, out HashSet<RestockTaskKey> bucket))
            {
                bucket = new HashSet<RestockTaskKey>();
                delayedDispatchBuckets.Add(retryTick, bucket);
            }
            bucket.Add(key);
        }

        //把当前 tick 到期的延迟请求提升到对应类型的就绪队列。
        private void PromoteDueDispatches(int now)
        {
            if (!delayedDispatchBuckets.TryGetValue(now, out HashSet<RestockTaskKey> bucket))
                return;
            delayedDispatchBuckets.Remove(now);
            foreach (RestockTaskKey key in bucket)
            {
                if (!delayedDispatchTicks.TryGetValue(key, out int scheduledTick) || scheduledTick != now)
                    continue;
                delayedDispatchTicks.Remove(key);
                EnqueueReadyDispatch(key);
            }
        }

        //从指定类型或两类公平轮换队列中取出一个就绪请求键。
        private bool TryDequeueReadyDispatch(RestockRequestKind? kind, out RestockTaskKey key)
        {
            if (kind == RestockRequestKind.Bulk)
                return TryDequeueFromQueue(bulkDispatchQueue, out key);
            if (kind == RestockRequestKind.Unique)
                return TryDequeueFromQueue(uniqueDispatchQueue, out key);

            RestockReadyQueue first = dispatchUniqueNext ? uniqueDispatchQueue : bulkDispatchQueue;
            RestockReadyQueue second = dispatchUniqueNext ? bulkDispatchQueue : uniqueDispatchQueue;
            dispatchUniqueNext = !dispatchUniqueNext;
            return first.TryTake(out key) || second.TryTake(out key);
        }

        //从请求类型对应的就绪队列取出首个请求。
        private bool TryDequeueFromQueue(RestockReadyQueue queue, out RestockTaskKey key)
        {
            return queue.TryTake(out key);
        }

        //从普通或专业就绪队列立即移除请求。
        private void RemoveReadyDispatch(RestockTaskKey key)
        {
            if (key.Kind == RestockRequestKind.Unique)
                uniqueDispatchQueue.Remove(key);
            else
                bulkDispatchQueue.Remove(key);
        }

        //从延迟时间轮移除请求及其空桶，避免反复重算留下陈旧唤醒项。
        private void RemoveDelayedDispatch(RestockTaskKey key)
        {
            if (!delayedDispatchTicks.TryGetValue(key, out int scheduledTick))
                return;
            delayedDispatchTicks.Remove(key);
            if (!delayedDispatchBuckets.TryGetValue(scheduledTick, out HashSet<RestockTaskKey> bucket))
                return;
            bucket.Remove(key);
            if (bucket.Count <= 0)
                delayedDispatchBuckets.Remove(scheduledTick);
        }

        //返回两类就绪请求的总数。
        private int CountReadyDispatches()
        {
            return bulkDispatchQueue.Count + uniqueDispatchQueue.Count;
        }
    }
}
