using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
namespace SimManagementLib.SimAI
{
    public class JobGiver_Customer_BrowseAndPick : ThinkNode_JobGiver
    {
        // 货柜全部挤满时，最多等待的 tick 数（按真实游戏 Tick 计算），超过后强制去结账
        private const int MaxWaitTicks = 1200; // 约 20 秒
        private const int MaxShelfReservations = 24;

        protected override Job TryGiveJob(Pawn pawn)
        {
            var lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob as LordJob_CustomerVisit;
            if (lordJob == null) return null;

            // 计算该 pawn 的剩余预算
            int pId = pawn.thingIDNumber;
            float alreadySpent = lordJob.cartValues.TryGetValue(pId, out float v) ? v : 0f;
            float remainingBudget = lordJob.GetBudgetForPawn(pId) - alreadySpent;

            // 预算耗尽，直接去结账
            if (remainingBudget <= 0f)
            {
                lordJob.MarkPawnReadyForCheckout(pId);
                return null;
            }

            // 找商店区域
            Zone_Shop shopZone = ShopDataUtility.FindAssignedShopZone(
                pawn.Map,
                lordJob.targetShopZoneId,
                lordJob.targetShopCell);
            if (shopZone == null)
            {
                lordJob.MarkPawnReadyForCheckout(pId);
                return null;
            }

            bool hasAffordableCombo = ShopDataUtility
                .GetAffordableInStockCombos(shopZone, remainingBudget)
                .Any();

            // 找有库存且顾客买得起至少一件的货柜
            var allStockedStorages = ShopDataUtility.GetStoragesInZone(shopZone)
                .Where(s => s.ActiveDefs.Any(def =>
                {
                    if (s.CountStored(def) <= 0) return false;
                    if (hasAffordableCombo) return true;
                    // 检查该物品单价是否在预算内
                    Thing storedThing = s.GetDirectlyHeldThings()
                        .FirstOrDefault(t => t.def == def);
                    float unitPrice = storedThing != null
                        ? Mathf.CeilToInt(storedThing.MarketValue)
                        : 1f;
                    return unitPrice <= remainingBudget;
                }))
                .ToList();

            // 没有任何买得起的货，直接去结账
            if (allStockedStorages.NullOrEmpty())
            {
                // 确保 cartValues 有记录，防止 CheckAllCheckoutsDone 误判
                if (!lordJob.cartValues.ContainsKey(pId))
                    lordJob.cartValues[pId] = 0f;
                lordJob.MarkPawnReadyForCheckout(pId);
                return null;
            }

            // 过滤出当前可以预约的货柜（上限放宽，减少大量顾客时的拥堵）
            var availableStorages = allStockedStorages
                .Where(s => pawn.CanReserve(s, MaxShelfReservations))
                .ToList();

            if (availableStorages.NullOrEmpty())
            {
                // 货柜全挤满时，记录首次等待 Tick，改为真实时间判定，避免“半天都在游荡”
                int now = Find.TickManager.TicksGame;
                int waitStartTick = lordJob.GetOrInitBrowseWaitStartTick(pId, now);
                CustomerExpressionUtility.TryShowExpression(pawn, CustomerExpressionEvents.BrowseWait);

                if (now - waitStartTick >= MaxWaitTicks)
                {
                    // 等太久了，放弃购物直接去结账
                    lordJob.ClearBrowseWaitStartTick(pId);
                    if (!lordJob.cartValues.ContainsKey(pId))
                        lordJob.cartValues[pId] = 0f;
                    lordJob.MarkPawnReadyForCheckout(pId);
                }
                // 返回 null，触发 XML 兜底游荡，稍后重试
                return null;
            }

            // 找到可用货柜，清除等待计时
            lordJob.ClearBrowseWaitStartTick(pId);

            // 随机挑一个可用货柜
            Building_SimContainer targetShelf = availableStorages.RandomElement();
            Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("Customer_BrowseAndPick"), targetShelf);
            return job;
        }
    }
}
