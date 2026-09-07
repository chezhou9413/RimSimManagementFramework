using SimManagementLib.SimDef;
using SimManagementLib.Pojo;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace SimManagementLib.Api
{
    /// <summary>
    /// 提供顾客行为动作的可继承逻辑，负责控制动作可见性、目标选择、Job 创建、账单和完成回调。
    /// </summary>
    public class CustomerActionWorker
    {
        public CustomerActionDef def;

        //判断动作是否允许在当前上下文中执行，默认检查 Def 启用、顾客类型、兴趣分类和店内目标建筑。
        public virtual bool CanRun(CustomerActionContext context, out string reason)
        {
            reason = "";
            if (def == null || !def.defaultEnabled)
            {
                reason = "动作未启用";
                return false;
            }
            if (context?.customer == null || context.shop == null || !context.HasValidVisit)
            {
                reason = "顾客上下文无效";
                return false;
            }
            if (!MatchesCustomerKind(context))
            {
                reason = "顾客类型不匹配";
                return false;
            }
            if (!MatchesInterest(context))
            {
                reason = "顾客兴趣不匹配";
                return false;
            }
            if (!HasRequiredThing(context))
            {
                reason = "店铺缺少动作目标";
                return false;
            }
            return true;
        }

        //判断动作提供设施是否能让商店具备接待能力，职责是为顾客生成快照提供不依赖具体 Pawn 的扩展点。
        public virtual bool IsAttractionAvailable(Zone_Shop shop)
        {
            return def != null
                && def.defaultEnabled
                && shop?.Map != null
                && HasRequiredThing(shop);
        }

        //提供已配置动作的到客阻塞原因，职责是让扩展按需刷新经营状态并向手动刷新报告具体限制。
        public virtual string GetAttractionBlockReason(Zone_Shop shop, bool refresh = false)
        {
            return "";
        }

        //判断动作是否能吸引指定顾客类型，职责是让外部经营扩展进入框架的到客匹配流程。
        public virtual bool CanAttractCustomer(Zone_Shop shop, RuntimeCustomerKind customerKind)
        {
            return customerKind != null
                && !customerKind.pawnKindDefs.NullOrEmpty()
                && IsAttractionAvailable(shop)
                && MatchesCustomerKind(customerKind)
                && MatchesInterest(customerKind);
        }

        //返回动作本次选择权重，默认使用 Def 权重。
        public virtual float GetSelectionWeight(CustomerActionContext context)
        {
            return def?.selectionWeight ?? 1f;
        }

        //创建顾客动作 Job，外部模组在这里返回按摩、赌博、呼叫服务员等自定义 Job。
        public virtual Job MakeJob(CustomerActionContext context)
        {
            return null;
        }

        //判断本次动作是否应创建持久化订单，默认读取动作 Def 的可选配置。
        public virtual bool ShouldCreateOrder(CustomerActionContext context)
        {
            return def?.createsPersistentOrder == true;
        }

        //创建顾客动作订单，外部模组可重写以写入目标、金额和自定义数据。
        public virtual CustomerActionOrder CreateOrder(CustomerActionContext context)
        {
            Thing target = FindDefaultTargetThing(context?.shop);
            return new CustomerActionOrder
            {
                customerThingId = context?.customer?.thingIDNumber ?? -1,
                shopZoneId = context?.shop?.ID ?? -1,
                actionDefName = def?.defName ?? "",
                targetThingId = target?.thingIDNumber ?? -1,
                targetLabel = target?.LabelCap ?? "",
                billAmount = def?.defaultBillAmount ?? 0f,
                state = CustomerActionOrderState.Active,
                createdTick = Find.TickManager?.TicksGame ?? 0
            };
        }

        //判断动作订单是否可以开始执行，默认要求订单和顾客有效。
        public virtual bool CanStartOrder(CustomerActionContext context, out string reason)
        {
            reason = "";
            if (context?.order == null)
            {
                reason = "动作订单无效";
                return false;
            }
            if (context.customer == null)
            {
                reason = "顾客无效";
                return false;
            }
            return true;
        }

        //基于持久化动作订单创建 Job，默认通过临时上下文创建 Job。
        public virtual Job MakeJobForOrder(CustomerActionContext context)
        {
            Job job = MakeJob(context);
            if (job != null && context?.order != null)
                job.count = context.order.orderId;
            return job;
        }

        //在动作 Job 创建后接收通知，默认不执行额外逻辑。
        public virtual void NotifyJobCreated(CustomerActionContext context, Job job)
        {
        }

        //在动作订单开始执行时接收通知，默认不执行额外逻辑。
        public virtual void NotifyOrderStarted(CustomerActionContext context)
        {
        }

        //在动作订单完成时接收通知，默认按订单金额加账并推进结账。
        public virtual void NotifyOrderCompleted(CustomerActionContext context)
        {
            if (context?.order != null && context.order.billAmount > 0f)
                context.AddBill(context.order.billAmount);
            NotifyActionCompleted(context);
        }

        //在动作订单取消或失败时接收通知，默认不执行额外逻辑。
        public virtual void NotifyOrderCanceled(CustomerActionContext context, string reason)
        {
        }

        //在外部 Job 完成时可由外部调用，默认按需要记录消费并触发结账。
        public virtual void NotifyActionCompleted(CustomerActionContext context)
        {
            if (context != null && (context.RegisterConsumptionActionAndShouldCheckout() || context.ShouldCheckoutAfterAction("外部动作完成")))
                context.MarkReadyForCheckout();
        }

        //判断顾客类型是否匹配动作 Def。
        protected virtual bool MatchesCustomerKind(CustomerActionContext context)
        {
            if (def.targetCustomerKindIds.NullOrEmpty()) return true;
            string kindId = context?.customerKindId ?? "";
            return !string.IsNullOrEmpty(kindId) && def.targetCustomerKindIds.Contains(kindId);
        }

        //判断运行时顾客类型是否匹配动作 Def，职责是让到客快照复用动作执行阶段的类型限制。
        protected virtual bool MatchesCustomerKind(RuntimeCustomerKind customerKind)
        {
            if (def.targetCustomerKindIds.NullOrEmpty()) return true;
            string kindId = customerKind?.kindId ?? "";
            return !string.IsNullOrEmpty(kindId) && def.targetCustomerKindIds.Contains(kindId);
        }

        //判断顾客兴趣分类是否匹配动作 Def。
        protected virtual bool MatchesInterest(CustomerActionContext context)
        {
            if (!def.targetGoodsCategoryIds.NullOrEmpty())
            {
                List<string> ids = context?.customerKind?.GetTargetGoodsCategoryIds();
                if (ids.NullOrEmpty() || !def.targetGoodsCategoryIds.Any(ids.Contains))
                    return false;
            }

            if (!def.targetServiceCategoryIds.NullOrEmpty())
            {
                List<string> ids = context?.customerKind?.GetTargetServiceCategoryIds();
                if (ids.NullOrEmpty() || !def.targetServiceCategoryIds.Any(ids.Contains))
                    return false;
            }

            return true;
        }

        //判断运行时顾客兴趣是否匹配动作 Def，职责是让到客匹配与入店后的动作选择保持相同语义。
        protected virtual bool MatchesInterest(RuntimeCustomerKind customerKind)
        {
            if (!def.targetGoodsCategoryIds.NullOrEmpty())
            {
                List<string> ids = customerKind?.GetTargetGoodsCategoryIds();
                if (ids.NullOrEmpty() || !def.targetGoodsCategoryIds.Any(ids.Contains))
                    return false;
            }

            if (!def.targetServiceCategoryIds.NullOrEmpty())
            {
                List<string> ids = customerKind?.GetTargetServiceCategoryIds();
                if (ids.NullOrEmpty() || !def.targetServiceCategoryIds.Any(ids.Contains))
                    return false;
            }

            return true;
        }

        //判断商店中是否存在动作需要的建筑或类型。
        protected virtual bool HasRequiredThing(CustomerActionContext context)
        {
            return HasRequiredThing(context?.shop);
        }

        //判断商店中是否存在动作需要的建筑或类型，职责是支持没有具体顾客上下文的营业与吸引力检查。
        protected virtual bool HasRequiredThing(Zone_Shop shop)
        {
            if (def.requiredThingDefs.NullOrEmpty() && def.requiredThingClasses.NullOrEmpty()) return true;
            if (shop?.Map == null) return false;

            foreach (IntVec3 cell in shop.Cells)
            {
                List<Thing> things = shop.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed) continue;
                    if (!def.requiredThingDefs.NullOrEmpty() && def.requiredThingDefs.Contains(thing.def)) return true;
                    if (!def.requiredThingClasses.NullOrEmpty() && def.requiredThingClasses.Any(type => type != null && type.IsInstanceOfType(thing))) return true;
                }
            }

            return false;
        }

        //查找动作默认目标建筑，负责为简单动作订单填充可追踪目标。
        protected virtual Thing FindDefaultTargetThing(Zone_Shop shop)
        {
            if (shop?.Map == null) return null;
            foreach (IntVec3 cell in shop.Cells)
            {
                List<Thing> things = shop.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing thing = things[i];
                    if (thing == null || thing.Destroyed) continue;
                    if (!def.requiredThingDefs.NullOrEmpty() && def.requiredThingDefs.Contains(thing.def)) return thing;
                    if (!def.requiredThingClasses.NullOrEmpty() && def.requiredThingClasses.Any(type => type != null && type.IsInstanceOfType(thing))) return thing;
                }
            }
            return null;
        }
    }
}
