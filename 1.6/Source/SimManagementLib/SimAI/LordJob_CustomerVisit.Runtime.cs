using SimManagementLib.Pojo;
using SimManagementLib.SimService;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        private const int DefaultMaxConsumptionActions = 3;

        public Pojo.RuntimeCustomerKind RuntimeCustomerKind => CustomerCatalog.GetKind(customerKindId);

        public void AddCartItem(int pawnId, ThingDef def, int count)
        {
            if (pawnId <= 0 || def == null || count <= 0) return;

            if (!cartItems.TryGetValue(pawnId, out List<CustomerCartItem> list))
            {
                list = new List<CustomerCartItem>();
                cartItems[pawnId] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                CustomerCartItem item = list[i];
                if (item == null || item.def != def) continue;
                item.count += count;
                return;
            }

            list.Add(new CustomerCartItem { def = def, count = count });
        }

        public void AddCartItemsFromCombo(int pawnId, List<ComboItem> comboItems)
        {
            if (comboItems == null) return;
            for (int i = 0; i < comboItems.Count; i++)
            {
                ComboItem comboItem = comboItems[i];
                if (comboItem == null || comboItem.def == null || comboItem.count <= 0) continue;
                AddCartItem(pawnId, comboItem.def, comboItem.count);
            }
        }

        public List<CustomerCartItem> GetCartItems(int pawnId)
        {
            return cartItems.TryGetValue(pawnId, out List<CustomerCartItem> list) ? list : null;
        }

        /// <summary>
        /// 为顾客添加一条服务订单，并按订单编号保持可追踪性。
        /// </summary>
        public void AddServiceOrder(int pawnId, CustomerServiceOrder order)
        {
            if (pawnId <= 0 || order == null) return;
            if (order.orderId <= 0) order.orderId = nextServiceOrderId++;
            else if (order.orderId >= nextServiceOrderId) nextServiceOrderId = order.orderId + 1;

            if (!serviceOrders.TryGetValue(pawnId, out List<CustomerServiceOrder> list))
            {
                list = new List<CustomerServiceOrder>();
                serviceOrders[pawnId] = list;
            }

            list.Add(order);
        }

        /// <summary>
        /// 返回指定顾客的服务订单列表。
        /// </summary>
        public List<CustomerServiceOrder> GetServiceOrders(int pawnId)
        {
            return serviceOrders.TryGetValue(pawnId, out List<CustomerServiceOrder> list) ? list : null;
        }

        /// <summary>
        /// 按订单编号查找指定顾客的服务订单。
        /// </summary>
        public CustomerServiceOrder GetServiceOrder(int pawnId, int orderId)
        {
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return null;
            return list.FirstOrDefault(o => o != null && o.orderId == orderId);
        }

        /// <summary>
        /// 统计指定服务建筑和服务 Def 当前正在占用并发名额的订单数量。
        /// </summary>
        public int CountActiveServiceOrders(int providerThingId, string serviceDefName)
        {
            if (providerThingId < 0 || string.IsNullOrEmpty(serviceDefName)) return 0;
            int count = 0;
            foreach (List<CustomerServiceOrder> list in serviceOrders.Values)
            {
                if (list.NullOrEmpty()) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    CustomerServiceOrder order = list[i];
                    if (order == null) continue;
                    if (order.providerThingId != providerThingId || order.serviceDefName != serviceDefName) continue;
                    if (order.state == ServiceOrderState.AwaitingPayment
                        || order.state == ServiceOrderState.ReadyToUse
                        || order.state == ServiceOrderState.TicketIssued
                        || order.state == ServiceOrderState.InUse
                        || order.state == ServiceOrderState.UsedAwaitingPayment)
                        count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 清理顾客未完成的服务订单，已使用未付款服务会标记为结账失败。
        /// </summary>
        public void ResolveServiceOrdersOnCheckoutFailure(int pawnId)
        {
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return;

            for (int i = 0; i < list.Count; i++)
            {
                CustomerServiceOrder order = list[i];
                if (order == null) continue;
                if (order.HasBeenUsed)
                {
                    if (order.state != ServiceOrderState.Completed)
                        order.state = ServiceOrderState.CheckoutFailed;
                }
                else
                {
                    order.state = ServiceOrderState.Canceled;
                }
            }
        }

        /// <summary>
        /// 付款完成后更新服务订单状态，并把需要付款后执行的服务 Job 加入购后队列。
        /// </summary>
        public void ResolveServiceOrdersOnCheckoutPaid(Pawn pawn, Zone_Shop shopZone)
        {
            if (pawn == null) return;
            int pawnId = pawn.thingIDNumber;
            List<CustomerServiceOrder> list = GetServiceOrders(pawnId);
            if (list.NullOrEmpty()) return;

            List<Job> followUps = new List<Job>();
            for (int i = 0; i < list.Count; i++)
            {
                CustomerServiceOrder order = list[i];
                if (order == null) continue;

                Thing provider = ShopServiceUtility.FindProviderByOrder(pawn.Map, order);
                ShopServiceDef serviceDef = DefDatabase<ShopServiceDef>.GetNamedSilentFail(order.serviceDefName);

                if (order.state == ServiceOrderState.AwaitingPayment)
                {
                    order.paidTick = Find.TickManager.TicksGame;
                    if (order.billingMode == ServiceBillingMode.UseBeforePay)
                    {
                        order.state = ServiceOrderState.Completed;
                        serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                        continue;
                    }

                    order.state = order.billingMode == ServiceBillingMode.TicketBeforeUse
                        ? ServiceOrderState.TicketIssued
                        : ServiceOrderState.ReadyToUse;
                    serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);

                    Job job = ShopServiceUtility.MakeServiceUseJob(pawn, order);
                    if (job != null) followUps.Add(job);
                }
                else if (order.state == ServiceOrderState.UsedAwaitingPayment)
                {
                    order.paidTick = Find.TickManager.TicksGame;
                    order.state = ServiceOrderState.Completed;
                    serviceDef?.Worker.NotifyServicePaid(pawn, provider, order);
                }
            }

            QueuePostCheckoutJobs(pawnId, followUps);
        }

        public void ClearCustomerCart(int pawnId)
        {
            cartItems.Remove(pawnId);
            cartValues[pawnId] = 0f;
            consumptionActionCounts.Remove(pawnId);
            checkoutOrder.Remove(pawnId);
            browseWaitStartTick.Remove(pawnId);
        }

        /// <summary>
        /// 清除顾客的服务订单，用于购后服务全部完成或无需继续保留订单时释放运行状态。
        /// </summary>
        public void ClearCustomerServiceOrders(int pawnId)
        {
            if (pawnId <= 0) return;
            serviceOrders.Remove(pawnId);
        }

        /// <summary>
        /// 记录一次商品或服务消费动作，并在达到上限时要求顾客去结账。
        /// </summary>
        public bool RegisterConsumptionActionAndShouldCheckout(int pawnId)
        {
            if (pawnId <= 0) return true;
            int count = consumptionActionCounts.TryGetValue(pawnId, out int old) ? old + 1 : 1;
            consumptionActionCounts[pawnId] = count;
            return count >= DefaultMaxConsumptionActions;
        }

        /// <summary>
        /// 判断顾客是否已经达到本次访问允许的最大消费动作数量。
        /// </summary>
        public bool HasReachedConsumptionLimit(int pawnId)
        {
            return pawnId > 0
                && consumptionActionCounts.TryGetValue(pawnId, out int count)
                && count >= DefaultMaxConsumptionActions;
        }

        public void MarkPawnReadyForCheckout(int pawnId)
        {
            if (pawnId <= 0) return;
            if (!readyForCheckout.Contains(pawnId))
                readyForCheckout.Add(pawnId);

            if (AreAllActivePawnsReadyForCheckout())
                lord?.ReceiveMemo("Customer_ReadyToCheckout");
        }

        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            if (pawnId <= 0 || settings == null) return;
            pawnSettings[pawnId] = settings;
        }

        public CustomerRuntimeSettings GetPawnSettings(int pawnId)
        {
            return pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) ? settings : null;
        }

        public int GetBudgetForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.budget > 0)
                return settings.budget;
            return totalBudget > 0 ? totalBudget : 1;
        }

        public int GetQueuePatienceForPawn(int pawnId)
        {
            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null && settings.queuePatienceTicks > 0)
                return settings.queuePatienceTicks;
            if (RuntimeCustomerKind?.shoppingBehavior != null && RuntimeCustomerKind.shoppingBehavior.queuePatience > 0)
                return RuntimeCustomerKind.shoppingBehavior.queuePatience;
            return 2500;
        }

        public float GetPreferenceMultiplier(int pawnId, ThingDef def)
        {
            float multiplier = 1f;

            if (pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) && settings != null)
                multiplier *= settings.GetPreferenceMultiplier(def);

            if (RuntimeCustomerKind != null)
                multiplier *= RuntimeCustomerKind.GetPreferenceMultiplier(def);

            return multiplier;
        }

        public int EnsureCheckoutOrder(int pawnId)
        {
            if (checkoutOrder.TryGetValue(pawnId, out int order)) return order;
            int next = nextCheckoutOrder++;
            checkoutOrder[pawnId] = next;
            return next;
        }

        public int GetCheckoutOrder(int pawnId)
        {
            return checkoutOrder.TryGetValue(pawnId, out int order) ? order : int.MaxValue;
        }

        public void QueuePostCheckoutJobs(int pawnId, IEnumerable<Job> jobs)
        {
            if (pawnId <= 0 || jobs == null) return;

            List<Job> list = jobs.Where(j => j != null).ToList();
            if (list.NullOrEmpty()) return;

            if (!postCheckoutJobs.TryGetValue(pawnId, out List<Job> existing))
            {
                existing = new List<Job>();
                postCheckoutJobs[pawnId] = existing;
            }

            // 这里先排队而不是立即分配，确保收银台结账流程能完整结束。
            existing.AddRange(list);
            postCheckoutRequired.Add(pawnId);
        }

        public bool TryTakeNextPostCheckoutJob(int pawnId, out Job job)
        {
            job = null;
            if (pawnId <= 0) return false;
            if (!postCheckoutJobs.TryGetValue(pawnId, out List<Job> list) || list.NullOrEmpty()) return false;

            job = list[0];
            list.RemoveAt(0);
            if (list.Count <= 0)
                postCheckoutJobs.Remove(pawnId);

            return job != null;
        }

        public bool NeedsPostCheckoutCompletion(int pawnId)
        {
            return pawnId > 0 && postCheckoutRequired.Contains(pawnId);
        }

        public void MarkPostCheckoutCompleted(int pawnId)
        {
            if (pawnId <= 0) return;
            postCheckoutRequired.Remove(pawnId);
            postCheckoutJobs.Remove(pawnId);
            ClearCustomerServiceOrders(pawnId);
        }

        public int GetOrInitBrowseWaitStartTick(int pawnId, int nowTick)
        {
            if (pawnId <= 0) return nowTick;
            if (!browseWaitStartTick.TryGetValue(pawnId, out int start) || start <= 0 || start > nowTick)
            {
                browseWaitStartTick[pawnId] = nowTick;
                return nowTick;
            }

            return start;
        }

        public void ClearBrowseWaitStartTick(int pawnId)
        {
            if (pawnId <= 0) return;
            browseWaitStartTick.Remove(pawnId);
        }

        private bool AreAllActivePawnsReadyForCheckout()
        {
            if (lord?.ownedPawns == null || lord.ownedPawns.Count == 0) return true;

            for (int i = 0; i < lord.ownedPawns.Count; i++)
            {
                Pawn pawn = lord.ownedPawns[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;
                if (!readyForCheckout.Contains(pawn.thingIDNumber))
                    return false;
            }

            return true;
        }

        public void CheckAllCheckoutsDone()
        {
            bool allDone = true;
            foreach (Pawn pawn in lord.ownedPawns)
            {
                if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned) continue;

                int pawnId = pawn.thingIDNumber;
                if (cartValues.TryGetValue(pawnId, out float value) && value > 0f)
                {
                    allDone = false;
                    break;
                }

                // 顾客必须消费完所有购后服务 Job 后才算完成本次访问。
                if (postCheckoutRequired.Contains(pawnId))
                {
                    allDone = false;
                    break;
                }
            }

            if (allDone)
            {
                lord.ReceiveMemo("Customer_CheckoutCompleted");
            }
        }
    }
}
