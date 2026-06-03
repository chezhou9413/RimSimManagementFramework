using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.GameComp;
using SimManagementLib.Pojo;
using SimManagementLib.SimAI;
using SimManagementLib.SimService;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责在主线程采集顾客离店点评快照，并把快照提交给点评管理组件。
    /// </summary>
    public static class CustomerReviewSnapshotBuilder
    {
        /// <summary>
        /// 按设置判断是否需要生成点评，并在命中抽样时提交顾客点评快照。
        /// </summary>
        public static void TryEnqueueReview(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone, List<FinanceLineItem> billLines, int paidSilver, string checkoutResult)
        {
            SimManagementLibSettings settings = SimManagementLibMod.Settings;
            if (pawn == null || lordJob == null || settings == null || !settings.HasValidReviewAiConfig()) return;
            if (Rand.Value > settings.reviewSampleRate) return;

            GameComponent_CustomerReviewManager manager = Current.Game?.GetComponent<GameComponent_CustomerReviewManager>();
            if (manager == null) return;

            CustomerReviewSnapshot snapshot = BuildSnapshot(pawn, lordJob, shopZone, billLines, paidSilver, checkoutResult);
            if (snapshot == null) return;
            SimShopReviewApi.NotifyBeforeEnqueue(new CustomerReviewSnapshotContext
            {
                customer = pawn,
                internalVisit = lordJob,
                shop = shopZone,
                billLines = billLines,
                paidSilver = paidSilver,
                checkoutResult = checkoutResult ?? "",
                snapshot = snapshot
            });
            manager.EnqueueSnapshot(snapshot);
        }

        /// <summary>
        /// 根据顾客运行状态构造纯数据点评快照。
        /// </summary>
        private static CustomerReviewSnapshot BuildSnapshot(Pawn pawn, LordJob_CustomerVisit lordJob, Zone_Shop shopZone, List<FinanceLineItem> billLines, int paidSilver, string checkoutResult)
        {
            int pawnId = pawn.thingIDNumber;
            string reviewId = Find.TickManager.TicksAbs + "_" + pawnId + "_" + Rand.RangeInclusive(1000, 9999);
            List<CustomerServiceOrder> serviceOrders = lordJob.GetServiceOrders(pawnId);
            int budget = lordJob.GetBudgetForPawn(pawnId);
            float cartValue = lordJob.GetCartValue(pawnId) > 0f ? lordJob.GetCartValue(pawnId) : paidSilver;
            bool hasCompletedService = HasCompletedService(serviceOrders);
            bool hasServiceFailure = HasFailedService(serviceOrders);
            bool hasFreeCompletedService = paidSilver <= 0 && hasCompletedService;
            bool paid = paidSilver > 0 || hasFreeCompletedService;
            float spent = paid ? paidSilver : 0f;
            string priceRejectionReason = lordJob.GetPriceRejectionReason(pawnId);
            string publicCheckoutResult = BuildPublicCheckoutResult(checkoutResult, paidSilver > 0, cartValue, hasFreeCompletedService);
            List<ReviewFeaturedItem> featuredItems = paid ? BuildFeaturedItems(billLines) : new List<ReviewFeaturedItem>();
            string purchasedSummary = BuildPurchasedSummary(billLines, spent, paid, cartValue, hasFreeCompletedService);
            string serviceSummary = BuildServiceSummary(serviceOrders, publicCheckoutResult);
            string avatarId = CustomerReviewAvatarCache.SaveAvatar(pawn, reviewId);

            return new CustomerReviewSnapshot
            {
                reviewId = reviewId,
                tickAbs = Find.TickManager.TicksAbs,
                gameDay = GenDate.DaysPassed,
                zoneId = shopZone?.ID ?? lordJob.targetShopZoneId,
                zoneLabel = shopZone?.label ?? SimTranslation.T("RSMF.CustomerReview.Snapshot.ShopNumber", lordJob.targetShopZoneId.Named("id")),
                customerDisplayName = pawn.LabelShortCap,
                spentSilver = spent,
                kindId = lordJob.customerKindId ?? "",
                kindLabel = lordJob.RuntimeCustomerKind?.label ?? lordJob.customerKind?.label ?? lordJob.customerKindId ?? SimTranslation.T("RSMF.CustomerReview.Snapshot.UnknownCustomer"),
                kindDescription = BuildKindDescription(lordJob),
                raceLabel = pawn.def?.label ?? SimTranslation.T("RSMF.CustomerReview.Snapshot.UnknownRace"),
                raceDescription = BuildRaceDescription(pawn),
                ageSummary = BuildAgeSummary(pawn),
                backstorySummary = BuildBackstorySummary(pawn),
                backstoryDetailSummary = BuildBackstoryDetailSummary(pawn),
                traitSummary = BuildTraitSummary(pawn),
                xenotypeSummary = BuildXenotypeSummary(pawn),
                geneSummary = BuildGeneSummary(pawn),
                personalityBiasSummary = BuildPersonalityBiasSummary(pawn, publicCheckoutResult, spent, serviceSummary, hasCompletedService, hasServiceFailure),
                moodSummary = BuildMoodSummary(pawn),
                healthSummary = BuildHealthSummary(pawn),
                budgetSummary = BuildBudgetSummary(budget, spent, cartValue, publicCheckoutResult, paid, hasFreeCompletedService, priceRejectionReason),
                purchasedSummary = purchasedSummary,
                serviceSummary = serviceSummary,
                shopEnvironmentSummary = BuildEnvironmentSummary(shopZone),
                cashierSummary = BuildCashierSummary(pawn),
                checkoutJobSummary = BuildCheckoutJobSummary(pawn, lordJob, publicCheckoutResult),
                postPurchaseSummary = paid ? BuildPostPurchaseSummary(lordJob, pawnId) : SimTranslation.T("RSMF.CustomerReview.Snapshot.NoPostPurchaseUnpaid"),
                roomSummary = BuildRoomSummary(pawn, shopZone),
                relationSummary = BuildRelationSummary(pawn),
                weatherSummary = BuildWeatherSummary(pawn.Map),
                gameConditionSummary = BuildGameConditionSummary(pawn.Map),
                colonyWealthSummary = BuildColonyWealthSummary(pawn.Map),
                colonyShopSummary = BuildColonyShopSummary(pawn.Map),
                colonyLeaderSummary = BuildColonyLeaderSummary(pawn.Map),
                colonyCultureSummary = BuildColonyCultureSummary(),
                featuredItems = featuredItems,
                avatarImageId = avatarId
            };
        }

        private static List<ReviewFeaturedItem> BuildFeaturedItems(List<FinanceLineItem> billLines)
        {
            List<ReviewFeaturedItem> result = new List<ReviewFeaturedItem>();
            if (billLines.NullOrEmpty()) return result;

            for (int i = 0; i < billLines.Count && result.Count < 4; i++)
            {
                FinanceLineItem line = billLines[i];
                if (line == null) continue;
                ReviewFeaturedItem item = new ReviewFeaturedItem
                {
                    label = line.label,
                    defName = line.defName,
                    lineType = line.EffectiveLineType,
                    count = line.count,
                    amount = line.amount
                };
                result.Add(item);
            }
            return result;
        }

        private static string BuildPurchasedSummary(List<FinanceLineItem> billLines, float spent, bool paid, float cartValue, bool hasFreeCompletedService)
        {
            if (hasFreeCompletedService && (billLines == null || billLines.Count == 0))
                return SimTranslation.T("RSMF.CustomerReview.Snapshot.FreeServiceOnly");

            if (!paid)
            {
                return cartValue > 0f
                    ? SimTranslation.T("RSMF.CustomerReview.Snapshot.CartReturnedUnpaid", cartValue.ToString("F0").Named("cartValue"))
                    : SimTranslation.T("RSMF.CustomerReview.Snapshot.NoPurchaseNoPayment");
            }

            if (billLines.NullOrEmpty())
                return spent > 0f
                    ? SimTranslation.T("RSMF.CustomerReview.Snapshot.PaidNoLineItems", spent.ToString("F0").Named("spent"))
                    : SimTranslation.T("RSMF.CustomerReview.Snapshot.NoPurchase");

            List<string> parts = new List<string>();
            for (int i = 0; i < billLines.Count && i < 6; i++)
            {
                FinanceLineItem line = billLines[i];
                if (line == null) continue;
                string name = string.IsNullOrEmpty(line.label) ? line.defName : line.label;
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.PurchaseLine", name.Named("name"), line.count.Named("count"), line.amount.ToString("F0").Named("amount"), BuildLineDescription(line).Named("description")));
            }
            return string.Join(SimTranslation.T("RSMF.Common.SemicolonSeparator"), parts);
        }

        private static string BuildServiceSummary(List<CustomerServiceOrder> orders, string checkoutResult)
        {
            if (orders.NullOrEmpty()) return SimTranslation.T("RSMF.CustomerReview.Snapshot.NoServiceWithCheckout", checkoutResult.Named("checkoutResult"));
            List<string> parts = new List<string>();
            for (int i = 0; i < orders.Count && i < 6; i++)
            {
                CustomerServiceOrder order = orders[i];
                if (order == null) continue;
                string label = string.IsNullOrEmpty(order.providerLabel) ? order.serviceDefName : order.providerLabel;
                int wait = order.startedTick > 0 && order.reservedTick > 0 ? Math.Max(0, order.startedTick - order.reservedTick) : 0;
                parts.Add(SimTranslation.T(
                    "RSMF.CustomerReview.Snapshot.ServiceLine",
                    label.Named("label"),
                    BuildServiceStateText(order.state).Named("state"),
                    FormatTicks(wait).Named("wait"),
                    order.totalPrice.ToString("F0").Named("price"),
                    BuildServiceDescription(order).Named("description")));
            }
            return string.Join(SimTranslation.T("RSMF.Common.SemicolonSeparator"), parts);
        }

        /// <summary>
        /// 判断是否存在成功完成的服务订单，负责区分免费服务成功和未消费离店。
        /// </summary>
        private static bool HasCompletedService(List<CustomerServiceOrder> orders)
        {
            if (orders.NullOrEmpty())
                return false;

            for (int i = 0; i < orders.Count; i++)
            {
                CustomerServiceOrder order = orders[i];
                if (order != null && order.state == ServiceOrderState.Completed)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 判断服务订单是否存在失败或取消，负责只在真实负面流程中引导差评倾向。
        /// </summary>
        private static bool HasFailedService(List<CustomerServiceOrder> orders)
        {
            if (orders.NullOrEmpty())
                return false;

            for (int i = 0; i < orders.Count; i++)
            {
                CustomerServiceOrder order = orders[i];
                if (order == null)
                    continue;
                if (order.state == ServiceOrderState.Canceled || order.state == ServiceOrderState.CheckoutFailed)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 构造顾客类型说明，负责把顾客 kind 的预算、耐心、偏好和目标类别喂给模型。
        /// </summary>
        private static string BuildKindDescription(LordJob_CustomerVisit lordJob)
        {
            if (lordJob == null)
                return SimTranslation.T("RSMF.CustomerReview.Snapshot.NoCustomerKindDescription");

            List<string> parts = new List<string>();
            if (lordJob.RuntimeCustomerKind != null)
            {
                var kind = lordJob.RuntimeCustomerKind;
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.CustomerKind", (kind.label ?? kind.kindId ?? SimTranslation.T("RSMF.Common.Unknown")).Named("label")));
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.BudgetRange", kind.budgetRange.min.Named("min"), kind.budgetRange.max.Named("max")));
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.QueuePatienceRange", kind.queuePatienceRange.min.Named("min"), kind.queuePatienceRange.max.Named("max")));
                if (!kind.targetGoodsCategoryIds.NullOrEmpty())
                    parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.TargetGoodsCategories", string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), kind.targetGoodsCategoryIds.Take(8)).Named("value")));
                if (!kind.targetServiceCategoryIds.NullOrEmpty())
                    parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.TargetServiceCategories", string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), kind.targetServiceCategoryIds.Take(8)).Named("value")));
                if (!kind.itemPreferences.NullOrEmpty())
                    parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.ItemPreferences", string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), kind.itemPreferences.Select(p => p?.preferredThing?.label ?? p?.preferredGoodsCategoryId).Where(s => !string.IsNullOrEmpty(s)).Take(8)).Named("value")));
            }
            else if (lordJob.customerKind != null)
            {
                var kind = lordJob.customerKind;
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.CustomerKind", kind.LabelCap.RawText.Named("label")));
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.BudgetRange", kind.budgetRange.min.Named("min"), kind.budgetRange.max.Named("max")));
                parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.QueuePatienceRange", kind.queuePatienceRange.min.Named("min"), kind.queuePatienceRange.max.Named("max")));
                if (!kind.GetTargetGoodsCategoryIds().NullOrEmpty())
                    parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.TargetGoodsCategories", string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), kind.GetTargetGoodsCategoryIds().Take(8)).Named("value")));
                if (!kind.GetTargetServiceCategoryIds().NullOrEmpty())
                    parts.Add(SimTranslation.T("RSMF.CustomerReview.Snapshot.TargetServiceCategories", string.Join(SimTranslation.T("RSMF.Common.ListSeparator"), kind.GetTargetServiceCategoryIds().Take(8)).Named("value")));
            }

            return parts.Count > 0 ? string.Join(SimTranslation.T("RSMF.Common.SemicolonSeparator"), parts) : SimTranslation.T("RSMF.CustomerReview.Snapshot.NoCustomerKindDescription");
        }

        /// <summary>
        /// 构造账单行说明，负责为普通商品、套餐和服务补充定义描述。
        /// </summary>
        private static string BuildLineDescription(FinanceLineItem line)
        {
            if (line == null)
                return SimTranslation.T("RSMF.Common.NoDescription");

            if (line.EffectiveLineType == FinanceLineTypes.Product && !string.IsNullOrEmpty(line.defName))
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(line.defName);
                if (def != null)
                    return TrimForPrompt(def.description ?? def.DescriptionDetailed ?? "", 180, SimTranslation.T("RSMF.CustomerReview.Snapshot.NoProductDescription"));
            }

            if (line.EffectiveLineType == FinanceLineTypes.Service && !string.IsNullOrEmpty(line.defName))
            {
                ShopServiceDef service = DefDatabase<ShopServiceDef>.GetNamedSilentFail(line.defName);
                if (service != null)
                    return TrimForPrompt(service.description, 180, SimTranslation.T("RSMF.CustomerReview.Snapshot.NoServiceDescription"));
            }

            return line.EffectiveLineType == FinanceLineTypes.Combo ? SimTranslation.T("RSMF.CustomerReview.Snapshot.ComboLineDescription") : SimTranslation.T("RSMF.Common.NoDescription");
        }

        /// <summary>
        /// 构造服务定义说明，负责把服务名称、描述、计费模式和时长喂给模型。
        /// </summary>
        private static string BuildServiceDescription(CustomerServiceOrder order)
        {
            if (order == null || string.IsNullOrEmpty(order.serviceDefName))
                return "无服务定义说明";

            ShopServiceDef def = DefDatabase<ShopServiceDef>.GetNamedSilentFail(order.serviceDefName);
            if (def == null)
                return "未找到服务定义";

            string description = TrimForPrompt(def.description, 160, "无服务说明");
            return $"{def.DisplayLabel}，分类 {def.serviceCategoryId}，计费 {def.billingMode}，预计时长 {def.durationTicks.min}-{def.durationTicks.max} ticks，{description}";
        }

        /// <summary>
        /// 构造自然语言服务状态，负责避免把枚举名直接暴露给模型。
        /// </summary>
        private static string BuildServiceStateText(ServiceOrderState state)
        {
            switch (state)
            {
                case ServiceOrderState.Draft: return "刚选择服务";
                case ServiceOrderState.AwaitingPayment: return "等待付款";
                case ServiceOrderState.ReadyToUse: return "已付款待使用";
                case ServiceOrderState.TicketIssued: return "已出票待使用";
                case ServiceOrderState.InUse: return "正在使用";
                case ServiceOrderState.UsedAwaitingPayment: return "已使用待付款";
                case ServiceOrderState.Completed: return "已完成";
                case ServiceOrderState.Canceled: return "已取消";
                case ServiceOrderState.CheckoutFailed: return "结账失败";
                default: return state.ToString();
            }
        }

        /// <summary>
        /// 格式化 ticks 为更容易理解的时间描述。
        /// </summary>
        private static string FormatTicks(int ticks)
        {
            if (ticks <= 0) return "未等待";
            return ticks + " ticks，约 " + (ticks / 60f).ToString("F1") + " 秒";
        }

        /// <summary>
        /// 构造面向点评模型的结账结果，负责把程序状态转成顾客能理解的自然描述。
        /// </summary>
        private static string BuildPublicCheckoutResult(string checkoutResult, bool paid, float cartValue, bool hasFreeCompletedService)
        {
            if (hasFreeCompletedService)
                return "完成了免费服务，没有产生付款。";

            if (paid)
                return string.IsNullOrWhiteSpace(checkoutResult) ? "已付款并完成结账" : checkoutResult;

            if (ContainsAny(checkoutResult, "超时", "等太久", "没轮到"))
                return cartValue > 0f
                    ? $"等太久没轮到收银，没有付款，也没有拿走商品；原本选中的约 {cartValue:F0} 银商品已放回店里。"
                    : "等太久没轮到收银，最后没有付款也没有购买。";

            if (ContainsAny(checkoutResult, "未消费", "没有消费"))
                return "没有付款，也没有购买商品。";

            return string.IsNullOrWhiteSpace(checkoutResult)
                ? "没有完成付款，也没有购买商品。"
                : checkoutResult + "；没有完成付款，也没有拿走商品。";
        }

        /// <summary>
        /// 构造预算与付款摘要，负责区分真实付款和未付款购物车金额。
        /// </summary>
        private static string BuildBudgetSummary(int budget, float spent, float cartValue, string publicCheckoutResult, bool paid, bool hasFreeCompletedService, string priceRejectionReason)
        {
            string pricePart = string.IsNullOrWhiteSpace(priceRejectionReason) ? "" : $"，价格观察：{priceRejectionReason}";
            if (hasFreeCompletedService)
                return $"预算 {budget} 银，实际付款 0 银，完成了免费服务，结账结果：{publicCheckoutResult}{pricePart}";

            if (paid)
                return $"预算 {budget} 银，实际付款 {spent:F0} 银，结账结果：{publicCheckoutResult}{pricePart}";

            return $"预算 {budget} 银，实际付款 0 银，未付款购物车约 {Math.Max(0f, cartValue):F0} 银，结账结果：{publicCheckoutResult}{pricePart}";
        }

        private static string BuildAgeSummary(Pawn pawn)
        {
            return $"生理年龄 {pawn.ageTracker?.AgeBiologicalYearsFloat:F1}，历法年龄 {pawn.ageTracker?.AgeChronologicalYearsFloat:F1}";
        }

        private static string BuildBackstorySummary(Pawn pawn)
        {
            string childhood = pawn.story?.Childhood?.TitleCapFor(pawn.gender) ?? "";
            string adulthood = pawn.story?.Adulthood?.TitleCapFor(pawn.gender) ?? "";
            if (string.IsNullOrEmpty(childhood) && string.IsNullOrEmpty(adulthood)) return "无明显背景";
            return (childhood + " / " + adulthood).Trim(' ', '/');
        }

        /// <summary>
        /// 构造顾客种族说明，负责把种族描述喂给点评模型。
        /// </summary>
        private static string BuildRaceDescription(Pawn pawn)
        {
            string description = pawn.def?.description ?? "";
            if (string.IsNullOrWhiteSpace(description))
                description = pawn.def?.DescriptionDetailed ?? "";
            return TrimForPrompt(description, 520, "无种族说明");
        }

        /// <summary>
        /// 构造顾客背景故事详情，负责提供童年和成年经历的标题、描述和背景效果。
        /// </summary>
        private static string BuildBackstoryDetailSummary(Pawn pawn)
        {
            List<string> parts = new List<string>();
            AppendBackstoryDetail(parts, "童年", pawn.story?.Childhood, pawn);
            AppendBackstoryDetail(parts, "成年", pawn.story?.Adulthood, pawn);
            return parts.Count > 0 ? string.Join("；", parts) : "无背景故事详情";
        }

        /// <summary>
        /// 添加单段背景故事详情，负责安全调用原版背景故事描述 API 并保留原始故事正文。
        /// </summary>
        private static void AppendBackstoryDetail(List<string> parts, string label, BackstoryDef backstory, Pawn pawn)
        {
            if (parts == null || backstory == null)
                return;

            string title = backstory.TitleCapFor(pawn.gender);
            string rawDescription = BuildBackstoryRawDescription(backstory, pawn);
            string fullDescription = "";
            try
            {
                fullDescription = backstory.FullDescriptionFor(pawn).Resolve();
            }
            catch
            {
                fullDescription = rawDescription;
            }

            List<string> detailParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(rawDescription))
                detailParts.Add("故事正文: " + TrimForPrompt(rawDescription, 520, ""));
            if (!string.IsNullOrWhiteSpace(fullDescription) && !SamePromptText(rawDescription, fullDescription))
                detailParts.Add("展开说明: " + TrimForPrompt(fullDescription, 520, ""));
            if (detailParts.Count == 0)
                detailParts.Add("无描述");

            parts.Add(label + " " + title + ": " + string.Join("；", detailParts));
        }

        /// <summary>
        /// 构造背景故事原始正文，负责优先提供不含界面附加项的经历文本。
        /// </summary>
        private static string BuildBackstoryRawDescription(BackstoryDef backstory, Pawn pawn)
        {
            if (backstory == null)
                return "";

            string description = backstory.description;
            if (string.IsNullOrWhiteSpace(description))
                description = backstory.baseDesc;
            if (string.IsNullOrWhiteSpace(description))
                return "";

            try
            {
                return description.Formatted(pawn.Named("PAWN")).AdjustedFor(pawn).Resolve();
            }
            catch
            {
                return description;
            }
        }

        /// <summary>
        /// 比较两段提示词文本，负责避免重复发送完全相同的背景描述。
        /// </summary>
        private static bool SamePromptText(string left, string right)
        {
            return string.Equals(NormalizePromptText(left), NormalizePromptText(right), StringComparison.Ordinal);
        }

        /// <summary>
        /// 规范化提示词文本，负责让重复判断忽略换行和多余空白。
        /// </summary>
        private static string NormalizePromptText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            return Regex.Replace(value, "\\s+", " ").Trim();
        }

        /// <summary>
        /// 构造特性详情摘要，负责把特性名称、描述和关键效果喂给点评模型。
        /// </summary>
        private static string BuildTraitSummary(Pawn pawn)
        {
            if (pawn.story?.traits?.allTraits == null || pawn.story.traits.allTraits.Count == 0) return "无明显特性";
            return string.Join("；", pawn.story.traits.allTraits.Select(t => BuildTraitDetail(t, pawn)).Where(s => !string.IsNullOrEmpty(s)).Take(6));
        }

        /// <summary>
        /// 构造单个特性的完整说明，负责避免只给模型一个特性名字。
        /// </summary>
        private static string BuildTraitDetail(Trait trait, Pawn pawn)
        {
            if (trait == null)
                return "";

            TraitDegreeData data = trait.CurrentData;
            string label = data != null ? data.GetLabelCapFor(pawn?.gender ?? Gender.None) : trait.LabelCap;
            string description = data?.description ?? trait.def?.description ?? "";
            string effect = BuildTraitEffectHint(data);
            List<string> parts = new List<string> { label };
            if (!string.IsNullOrWhiteSpace(description))
                parts.Add("说明: " + TrimForPrompt(description, 260, ""));
            if (!string.IsNullOrWhiteSpace(effect))
                parts.Add("效果: " + effect);
            return string.Join("，", parts);
        }

        /// <summary>
        /// 构造特性效果摘要，负责把会影响口吻和行为倾向的数值字段转成文本。
        /// </summary>
        private static string BuildTraitEffectHint(TraitDegreeData data)
        {
            if (data == null)
                return "";

            List<string> effects = new List<string>();
            if (Math.Abs(data.socialFightChanceFactor - 1f) > 0.01f)
                effects.Add("社交冲突倾向 x" + data.socialFightChanceFactor.ToString("F2"));
            if (Math.Abs(data.hungerRateFactor - 1f) > 0.01f)
                effects.Add("饥饿速度 x" + data.hungerRateFactor.ToString("F2"));
            if (Math.Abs(data.painOffset) > 0.001f)
                effects.Add("疼痛偏移 " + data.painOffset.ToString("F2"));
            if (Math.Abs(data.painFactor - 1f) > 0.01f)
                effects.Add("疼痛倍率 x" + data.painFactor.ToString("F2"));
            if (data.randomMentalState != null)
                effects.Add("可能随机进入 " + data.randomMentalState.label);
            if (data.forcedMentalState != null)
                effects.Add("可能强制进入 " + data.forcedMentalState.label);
            if (!data.skillGains.NullOrEmpty())
                effects.Add("技能倾向 " + string.Join("、", data.skillGains.Select(s => s.skill?.label + "+" + s.amount).Take(4)));
            if (!data.aptitudes.NullOrEmpty())
                effects.Add("技能资质 " + string.Join("、", data.aptitudes.Select(a => a.skill?.label + "+" + a.level).Take(4)));
            return effects.Count > 0 ? string.Join("；", effects) : "";
        }

        /// <summary>
        /// 构造异种摘要，负责传入异种名称和简短说明。
        /// </summary>
        private static string BuildXenotypeSummary(Pawn pawn)
        {
            if (pawn.genes == null)
                return "无异种数据";

            string label = pawn.genes.XenotypeLabelCap;
            string desc = pawn.genes.XenotypeDescShort;
            return string.IsNullOrWhiteSpace(desc) ? label : label + ": " + TrimForPrompt(desc, 260, "");
        }

        /// <summary>
        /// 构造基因摘要，负责把激活基因、异源/内源类型和效果说明喂给模型。
        /// </summary>
        private static string BuildGeneSummary(Pawn pawn)
        {
            if (pawn.genes?.GenesListForReading == null || pawn.genes.GenesListForReading.Count == 0)
                return "无显著基因";

            List<string> parts = new List<string>();
            for (int i = 0; i < pawn.genes.GenesListForReading.Count && parts.Count < 10; i++)
            {
                Gene gene = pawn.genes.GenesListForReading[i];
                if (gene?.def == null || !gene.Active)
                    continue;

                string type = pawn.genes.IsXenogene(gene) ? "异源" : "内源";
                string desc = gene.def.DescriptionFull;
                if (string.IsNullOrWhiteSpace(desc))
                    desc = gene.def.description;
                parts.Add($"{gene.LabelCap}({type}): {TrimForPrompt(desc, 180, "无说明")}");
            }

            return parts.Count > 0 ? string.Join("；", parts) : "无激活基因";
        }

        /// <summary>
        /// 构造顾客评价倾向，负责让坏特性、坏心情或糟糕流程更可能产生激进差评。
        /// </summary>
        private static string BuildPersonalityBiasSummary(Pawn pawn, string checkoutResult, float spent, string serviceSummary, bool hasCompletedService, bool hasServiceFailure)
        {
            List<string> parts = new List<string>();
            float mood = pawn.needs?.mood?.CurLevelPercentage ?? 0.5f;
            if (mood < 0.25f) parts.Add("当前心情很差，容易迁怒商店，可能故意给低分");
            else if (mood < 0.45f) parts.Add("当前心情偏低，评价会更挑剔");
            else if (mood > 0.75f) parts.Add("当前心情很好，更容易宽容或夸赞");

            string traits = BuildTraitSummary(pawn);
            if (ContainsAny(traits, "贪婪", "嫉妒", "神经质", "易怒", "悲观", "苦行", "化学兴趣", "化学痴迷"))
                parts.Add("特性偏难伺候或情绪化，差评时可以更激进");
            if (ContainsAny(traits, "乐观", "善良", "勤劳", "禁欲", "铁人意志"))
                parts.Add("特性偏克制或坚韧，即使不满也可能讲具体原因");

            if ((!hasCompletedService && spent <= 0f) || hasServiceFailure || ContainsAny(checkoutResult, "失败", "取消", "超时") || ContainsAny(serviceSummary, "取消", "失败", "等待过久"))
                parts.Add("购物或服务流程有问题，可以出现故意差评、迁怒或尖锐吐槽");

            return parts.Count > 0 ? string.Join("；", parts) : "按实际体验自然评价";
        }

        private static string BuildMoodSummary(Pawn pawn)
        {
            if (pawn.needs?.mood == null) return "无心情数据";
            return $"心情 {(pawn.needs.mood.CurLevelPercentage * 100f):F0}%";
        }

        private static string BuildHealthSummary(Pawn pawn)
        {
            if (pawn.health?.hediffSet == null) return "无健康数据";
            List<string> hediffs = pawn.health.hediffSet.hediffs
                .Where(h => h != null && h.Visible)
                .Select(BuildHediffDetail)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .Take(5)
                .ToList();
            return hediffs.Count > 0 ? string.Join("、", hediffs) : "健康状况稳定";
        }

        /// <summary>
        /// 构造健康状态详情，负责把病症、伤口和状态描述转成模型可理解的文本。
        /// </summary>
        private static string BuildHediffDetail(Hediff hediff)
        {
            if (hediff == null)
                return "";

            string label = hediff.LabelBase;
            string part = hediff.Part != null ? "，部位 " + hediff.Part.Label : "";
            string severity = hediff.Severity > 0f ? "，严重度 " + hediff.Severity.ToString("F2") : "";
            string desc = hediff.def != null ? TrimForPrompt(hediff.def.description, 120, "") : "";
            return string.IsNullOrWhiteSpace(desc)
                ? label + part + severity
                : label + part + severity + "，说明: " + desc;
        }

        private static string BuildEnvironmentSummary(Zone_Shop shopZone)
        {
            if (shopZone == null) return "未知商店环境";
            GameComponent_ShopAnalyticsManager analytics = Current.Game?.GetComponent<GameComponent_ShopAnalyticsManager>();
            ShopMetricsSnapshot metrics = analytics?.GetOrEvaluateShopMetrics(shopZone);
            if (metrics == null) return shopZone.GetValidationMessage();
            return $"环境参考数值: 综合 {metrics.score:F1}，口碑 {metrics.reputation:F1}，满意度 {metrics.satisfaction:F1}，美观 {metrics.beautyAverage:F1}。这些只是背景数值，不要求每条评价都提环境。";
        }

        /// <summary>
        /// 构造原版天气摘要，负责提供当前天气、雨雪风和移动/命中影响。
        /// </summary>
        private static string BuildWeatherSummary(Map map)
        {
            WeatherManager weather = map?.weatherManager;
            if (weather == null)
                return "没有可用天气数据。";

            WeatherDef perceived = weather.CurWeatherPerceived ?? weather.curWeather;
            string label = perceived?.LabelCap.Resolve() ?? perceived?.label ?? "未知天气";
            List<string> parts = new List<string>
            {
                "当前天气 " + label,
                "持续 " + FormatTicks(weather.curWeatherAge),
                "雨量 " + weather.RainRate.ToString("F2"),
                "雪量 " + weather.SnowRate.ToString("F2"),
                "风速系数 " + weather.CurWindSpeedFactor.ToString("F2"),
                "移动倍率 " + weather.CurMoveSpeedMultiplier.ToString("F2"),
                "命中倍率 " + weather.CurWeatherAccuracyMultiplier.ToString("F2")
            };
            if (!string.IsNullOrWhiteSpace(perceived?.description))
                parts.Add("说明: " + TrimForPrompt(perceived.description, 160, ""));
            return string.Join("；", parts);
        }

        /// <summary>
        /// 构造当前原版事件摘要，负责提供正在影响地图的 GameCondition。
        /// </summary>
        private static string BuildGameConditionSummary(Map map)
        {
            List<GameCondition> conditions = map?.gameConditionManager?.ActiveConditions;
            if (conditions.NullOrEmpty())
                return "当前没有显著持续事件。";

            List<string> parts = new List<string>();
            for (int i = 0; i < conditions.Count && parts.Count < 6; i++)
            {
                GameCondition condition = conditions[i];
                if (condition?.def == null)
                    continue;

                string duration = condition.Permanent ? "永久" : "剩余 " + FormatTicks(Math.Max(0, condition.TicksLeft));
                string desc = TrimForPrompt(condition.Description, 120, "");
                parts.Add(string.IsNullOrWhiteSpace(desc)
                    ? condition.LabelCap + "，" + duration
                    : condition.LabelCap + "，" + duration + "，说明: " + desc);
            }
            return parts.Count > 0 ? string.Join("；", parts) : "当前没有显著持续事件。";
        }

        /// <summary>
        /// 构造殖民地财富摘要，负责提供讲述者财富拆分和人口规模背景。
        /// </summary>
        private static string BuildColonyWealthSummary(Map map)
        {
            WealthWatcher wealth = map?.wealthWatcher;
            if (wealth == null)
                return "没有可用财富数据。";

            int freeColonists = map.mapPawns?.FreeColonistsSpawnedCount ?? 0;
            return $"总财富 {wealth.WealthTotal:F0}，物品 {wealth.WealthItems:F0}，建筑 {wealth.WealthBuildings:F0}，角色 {wealth.WealthPawns:F0}，殖民者 {freeColonists} 人。财富只是背景，不要求评价主动提。";
        }

        /// <summary>
        /// 构造殖民地商店数量摘要，负责让模型知道当前商业规模。
        /// </summary>
        private static string BuildColonyShopSummary(Map map)
        {
            if (map?.zoneManager?.AllZones == null)
                return "没有可用商店数量数据。";

            List<Zone_Shop> shops = map.zoneManager.AllZones.OfType<Zone_Shop>().ToList();
            int valid = shops.Count(z => z != null && z.IsValidShop());
            int open = shops.Count(z => z != null && z.IsValidShop() && z.IsOpenNow());
            string labels = string.Join("、", shops.Where(z => z != null).Select(z => z.label).Where(s => !string.IsNullOrWhiteSpace(s)).Take(6));
            return $"商店区域 {shops.Count} 个，有效商店 {valid} 个，当前开业 {open} 个" + (string.IsNullOrWhiteSpace(labels) ? "。" : "，名称: " + labels + "。");
        }

        /// <summary>
        /// 构造殖民地领袖摘要，负责提供派系领袖或实际社交代表。
        /// </summary>
        private static string BuildColonyLeaderSummary(Map map)
        {
            Pawn leader = Faction.OfPlayerSilentFail?.leader;
            string source = "派系领袖";
            if (leader == null || leader.Destroyed)
            {
                leader = map?.mapPawns?.FreeColonistsSpawned
                    .Where(p => p != null)
                    .OrderByDescending(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0)
                    .FirstOrDefault();
                source = "本地图社交代表";
            }

            if (leader == null)
                return "没有可用殖民地领袖数据。";

            int social = leader.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0;
            string title = Faction.OfPlayerSilentFail?.LeaderTitle ?? "无头衔";
            return $"{source}: {leader.LabelShortCap}，头衔 {title}，社交 {social}，背景 {BuildBackstorySummary(leader)}，特性 {BuildTraitSummary(leader)}。";
        }

        /// <summary>
        /// 构造殖民地文化摘要，负责提供 Ideology 文化、信仰和主要模因。
        /// </summary>
        private static string BuildColonyCultureSummary()
        {
            if (!ModsConfig.IdeologyActive)
                return "未启用文化/信仰资料。";

            Ideo ideo = Faction.OfPlayerSilentFail?.ideos?.PrimaryIdeo;
            if (ideo == null)
                return "没有可用殖民地文化数据。";

            string culture = ideo.culture?.LabelCap.Resolve() ?? ideo.culture?.label ?? "未知文化";
            string memes = ideo.memes.NullOrEmpty()
                ? "无显著模因"
                : string.Join("、", ideo.memes.Where(m => m != null).Select(m => m.LabelCap.Resolve()).Take(6));
            return $"主要信仰 {ideo.name}，文化 {culture}，成员称呼 {ideo.memberName}，模因 {memes}。这些只作为殖民地氛围背景。";
        }

        /// <summary>
        /// 构造原版房间摘要，负责把顾客所在房间或商店区域的类型、室内外和房间属性喂给模型。
        /// </summary>
        private static string BuildRoomSummary(Pawn pawn, Zone_Shop shopZone)
        {
            Room room = pawn?.GetRoom();
            if (room == null && shopZone != null && shopZone.Cells.Count > 0)
                room = shopZone.Cells.First().GetRoom(shopZone.Map);
            if (room == null)
                return "没有可用房间数据。";

            List<string> parts = new List<string>();
            parts.Add("房间类型 " + SafeRoomRoleLabel(room));
            parts.Add("大小 " + room.CellCount + " 格");
            parts.Add(room.PsychologicallyOutdoors ? "心理上属于室外" : "心理上属于室内");
            parts.Add(room.TouchesMapEdge ? "连接地图边缘" : "不连接地图边缘");
            parts.Add("温度 " + room.Temperature.ToString("F1") + "℃");
            parts.Add("美观 " + SafeRoomStat(room, RoomStatDefOf.Beauty).ToString("F1"));
            parts.Add("财富 " + SafeRoomStat(room, RoomStatDefOf.Wealth).ToString("F0"));
            parts.Add("空间 " + SafeRoomStat(room, RoomStatDefOf.Space).ToString("F1"));
            parts.Add("清洁 " + SafeRoomStat(room, RoomStatDefOf.Cleanliness).ToString("F2"));
            parts.Add("印象 " + SafeRoomStat(room, RoomStatDefOf.Impressiveness).ToString("F1"));
            return string.Join("；", parts);
        }

        /// <summary>
        /// 构造房间类型文本，负责避免房间角色 API 异常影响快照生成。
        /// </summary>
        private static string SafeRoomRoleLabel(Room room)
        {
            try
            {
                return room?.GetRoomRoleLabel() ?? "未知";
            }
            catch
            {
                return room?.Role?.label ?? "未知";
            }
        }

        /// <summary>
        /// 安全读取房间属性，负责避免部分房间或属性缺失导致快照生成失败。
        /// </summary>
        private static float SafeRoomStat(Room room, RoomStatDef stat)
        {
            if (room == null || stat == null)
                return 0f;
            try
            {
                return room.GetStat(stat);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// 构造原版关系摘要，负责把顾客和地图上其他角色的关系与好感作为可选判断背景。
        /// </summary>
        private static string BuildRelationSummary(Pawn pawn)
        {
            if (pawn?.relations == null)
                return "没有可用关系数据。";

            List<string> parts = new List<string>();
            if (!pawn.relations.DirectRelations.NullOrEmpty())
            {
                for (int i = 0; i < pawn.relations.DirectRelations.Count && parts.Count < 6; i++)
                {
                    DirectPawnRelation relation = pawn.relations.DirectRelations[i];
                    if (relation?.def == null || relation.otherPawn == null)
                        continue;

                    string other = relation.otherPawn.LabelShortCap;
                    int opinion = 0;
                    try
                    {
                        opinion = pawn.relations.OpinionOf(relation.otherPawn);
                    }
                    catch
                    {
                        opinion = 0;
                    }
                    parts.Add($"{relation.def.GetGenderSpecificLabelCap(relation.otherPawn)} {other}，好感 {opinion}");
                }
            }

            if (parts.Count == 0 && pawn.Map != null)
            {
                List<Pawn> nearby = pawn.Map.mapPawns.AllPawnsSpawned
                    .Where(p => p != null && p != pawn && p.RaceProps?.Humanlike == true)
                    .OrderBy(p => p.Position.DistanceToSquared(pawn.Position))
                    .Take(5)
                    .ToList();
                for (int i = 0; i < nearby.Count; i++)
                {
                    int opinion = 0;
                    try
                    {
                        opinion = pawn.relations.OpinionOf(nearby[i]);
                    }
                    catch
                    {
                        opinion = 0;
                    }
                    if (Math.Abs(opinion) >= 20)
                        parts.Add($"对 {nearby[i].LabelShortCap} 好感 {opinion}");
                }
            }

            return parts.Count > 0 ? string.Join("；", parts) : "没有显著关系或强烈好感记录。";
        }

        /// <summary>
        /// 构造收银员摘要，负责把结账相关的收银员参数作为可选背景喂给评价模型。
        /// </summary>
        private static string BuildCashierSummary(Pawn customer)
        {
            Building_CashRegister register = customer?.CurJob?.targetA.Thing as Building_CashRegister;
            Pawn cashier = register?.CurrentCashier;
            if (cashier == null)
                return "没有记录到收银员，可能是无人值守或收银员已离开。";

            float socialImpact = CashierSocialUtility.GetServiceSocialImpact(cashier);
            float tradePriceImprovement = SafeStat(cashier, StatDefOf.TradePriceImprovement);
            float workSpeed = SafeStat(cashier, StatDefOf.WorkSpeedGlobal);
            int socialSkill = CashierSocialUtility.GetEffectiveSocialLevel(cashier);
            string name = cashier.LabelShortCap;
            string cashierTraits = BuildTraitSummary(cashier);
            return $"{name}，社交 {socialSkill}，魅力/社交影响 {socialImpact:F2}，谈判价格能力 {tradePriceImprovement:F2}，全局工作速度 {workSpeed:F2}，收银台 {(register?.LabelShortCap ?? "未知")}，收银员特性说明: {cashierTraits}";
        }

        /// <summary>
        /// 构造结账 Job 参数摘要，负责记录排队、服务耗时和结果。
        /// </summary>
        private static string BuildCheckoutJobSummary(Pawn pawn, LordJob_CustomerVisit lordJob, string checkoutResult)
        {
            int pawnId = pawn?.thingIDNumber ?? -1;
            int order = lordJob?.GetCheckoutOrder(pawnId) ?? int.MaxValue;
            string orderText = order == int.MaxValue ? "未记录排队序号" : "排队序号 " + order;
            string jobName = pawn?.CurJobDef != null ? (!string.IsNullOrEmpty(pawn.CurJobDef.label) ? pawn.CurJobDef.label : pawn.CurJobDef.defName) : "无当前 Job";
            return $"{jobName}，{orderText}，结果 {checkoutResult}。";
        }

        /// <summary>
        /// 构造付款后行为摘要，负责让评价理解堂食、用药、服务使用等售后流程。
        /// </summary>
        private static string BuildPostPurchaseSummary(LordJob_CustomerVisit lordJob, int pawnId)
        {
            return lordJob != null ? lordJob.DescribePostCheckoutJobs(pawnId) : "无付款后行为。";
        }

        /// <summary>
        /// 安全读取角色属性，负责避免个别 Stat 缺失影响快照生成。
        /// </summary>
        private static float SafeStat(Pawn pawn, StatDef stat)
        {
            if (pawn == null || stat == null) return 0f;
            try
            {
                return pawn.GetStatValue(stat);
            }
            catch
            {
                return 0f;
            }
        }

        /// <summary>
        /// 判断文本是否包含任意关键词，负责识别影响评价倾向的特性和流程状态。
        /// </summary>
        private static bool ContainsAny(string text, params string[] words)
        {
            if (string.IsNullOrEmpty(text) || words == null)
                return false;

            for (int i = 0; i < words.Length; i++)
            {
                if (!string.IsNullOrEmpty(words[i]) && text.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 裁剪喂给模型的长文本，负责保留画像细节同时避免单字段无限增长。
        /// </summary>
        private static string TrimForPrompt(string text, int maxLength, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
                return fallback;

            string cleaned = string.Join(" ", text.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
            if (cleaned.Length > maxLength)
                cleaned = cleaned.Substring(0, maxLength);
            return cleaned;
        }
    }
}
