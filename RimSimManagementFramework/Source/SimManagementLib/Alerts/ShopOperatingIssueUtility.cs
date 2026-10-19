using RimWorld.Planet;
using SimManagementLib.GameComp;
using SimManagementLib.SimDef;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.Alerts
{
    //按实际营业规则收集需要玩家处理的问题，不触发区划修复或顾客生成。
    internal static class ShopOperatingIssueUtility
    {
        //同时列出独立的缺失条件，主动关店时保持安静。
        public static void Collect(Zone_Shop shop, List<ShopOperatingIssue> issues)
        {
            if (shop.Cells.Count == 0 || !shop.GetSchedule().IsOpenNow(shop.Map)) return;
            shop.ReadShopFacilityConditions(out bool hasSalesFacility, out bool hasCashRegister, out IntVec3 outdoorCell);
            GlobalTargetInfo shopTarget = new GlobalTargetInfo(shop.Cells[0], shop.Map);
            if (outdoorCell.IsValid)
            {
                issues.Add(new ShopOperatingIssue(shop, "Outdoor", SimTranslation.T("RSMF.Zone.Validation.OutdoorCells"),
                    new GlobalTargetInfo(outdoorCell, shop.Map), delay: false));
            }
            if (!hasCashRegister)
                issues.Add(new ShopOperatingIssue(shop, "MissingRegister", SimTranslation.T("RSMF.Zone.Validation.MissingCashRegister"), shopTarget, delay: false));
            if (!hasSalesFacility)
                issues.Add(new ShopOperatingIssue(shop, "MissingSalesFacility", SimTranslation.T("RSMF.Zone.Validation.MissingStorageOrService"), shopTarget, delay: false));

            //未满足营业条件时，员工不会到岗；只报告独立设施问题，避免追加派生缺员提示。
            if (outdoorCell.IsValid || !hasCashRegister || !hasSalesFacility) return;

            var registers = ShopDataUtility.GetCashRegisterSnapshotInZone(shop);
            if (!registers.Any(register => register.IsManned))
            {
                issues.Add(new ShopOperatingIssue(shop, "Cashier", SimTranslation.T("RSMF.Alert.Shop.NoCashier"),
                    registers.Count > 0 ? new GlobalTargetInfo(registers[0]) : new GlobalTargetInfo(shop.Cells[0], shop.Map)));
            }

            bool hasActionProvider = false;
            foreach (CustomerActionDef action in DefDatabase<CustomerActionDef>.AllDefsListForReading.OrderBy(action => action.order))
            {
                if (!action.Worker.IsAttractionAvailable(shop)) continue;
                hasActionProvider = true;
                if (action.Worker.TryGetOperatingAlert(shop, out string reason, out Thing provider))
                {
                    GlobalTargetInfo target = provider != null ? new GlobalTargetInfo(provider)
                        : new GlobalTargetInfo(shop.Cells[0], shop.Map);
                    issues.Add(new ShopOperatingIssue(shop, action.defName,
                        SimTranslation.T("RSMF.Alert.Shop.ServiceIssue", action.LabelCap.Named("service"), reason.Named("reason")), target, category: "Service"));
                }
            }

            //扩展自行判断业务是否受阻，避免把服务店的空货柜或满座误报成全店停业。
            if (hasActionProvider) return;
            CollectOfferIssue(shop, issues);
        }

        //复用到客匹配规则，区分未配置、缺货、客群不匹配与声望门槛。
        private static void CollectOfferIssue(Zone_Shop shop, List<ShopOperatingIssue> issues)
        {
            HashSet<ThingDef> stock = CustomerArrivalManager.CollectStockedDefs(shop);
            HashSet<string> services = new HashSet<string>();
            var providers = ShopServiceUtility.GetServiceProvidersInZone(shop).OrderBy(thing => thing.thingIDNumber).ToList();
            foreach (Thing provider in providers)
            {
                var comp = ShopServiceUtility.GetProviderComp(provider);
                if (!comp.enabled) continue;
                foreach (var slot in comp.EnabledSlots)
                {
                    string serviceCategory = slot.ServiceDef?.serviceCategoryId;
                    //容量占满是正常经营状态，不作为缺少服务处理。
                    if (!serviceCategory.NullOrEmpty()) services.Add(serviceCategory);
                }
            }

            var storages = ShopDataUtility.GetStorageSnapshotInZone(shop).Where(storage => storage.AllowsCustomerSelfPurchase).ToList();
            Thing targetThing = storages.FirstOrDefault();
            if (targetThing == null) targetThing = providers.FirstOrDefault();
            GlobalTargetInfo target = targetThing != null ? new GlobalTargetInfo(targetThing)
                : new GlobalTargetInfo(shop.Cells[0], shop.Map);
            string reason;
            string category;
            if (stock.Count == 0 && services.Count == 0)
            {
                bool configured = storages.Any(storage => storage.CountConfiguredTargets() > 0 || storage is Building_UniqueGoodsContainer);
                category = configured ? "NoStock" : "NoOfferConfigured";
                reason = SimTranslation.T("RSMF.Alert.Shop." + category);
            }
            else
            {
                CustomerCatalog.EnsureInitialized();
                var matching = CustomerCatalog.Kinds.Where(kind => !kind.pawnKindDefs.NullOrEmpty()
                    && CustomerArrivalManager.SnapshotMatchesKind(stock, services, kind)).ToList();
                if (matching.Count == 0)
                {
                    category = "NoMatchingCustomers";
                    reason = SimTranslation.T("RSMF.Alert.Shop.NoMatchingCustomers");
                }
                else
                {
                    float reputation = Current.Game.GetComponent<GameComponent_ShopAnalyticsManager>().GetReputation(shop.ID);
                    float required = matching.Min(kind => kind.minShopReputation);
                    if (reputation >= required) return;
                    category = "Reputation";
                    reason = SimTranslation.T("RSMF.CustomerArrival.BlockReputation",
                        reputation.ToString("0.##").Named("current"), required.ToString("0.##").Named("required"));
                }
            }
            issues.Add(new ShopOperatingIssue(shop, category, reason, target));
        }
    }
}
