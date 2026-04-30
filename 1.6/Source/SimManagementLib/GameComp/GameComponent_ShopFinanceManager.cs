using RimWorld;
using SimManagementLib.Pojo;
using SimManagementLib.SimThingClass;
using SimManagementLib.SimZone;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.GameComp
{
    public class GameComponent_ShopFinanceManager : GameComponent
    {
        private const int DefaultMaxBillRecords = 2000;

        private float totalIncome;

        private Dictionary<string, int> productSoldCounts = new Dictionary<string, int>();
        private Dictionary<string, float> productRevenues = new Dictionary<string, float>();

        private Dictionary<string, int> comboSoldCounts = new Dictionary<string, int>();
        private Dictionary<string, float> comboRevenues = new Dictionary<string, float>();

        private Dictionary<int, ShopFinanceState> shopStates = new Dictionary<int, ShopFinanceState>();
        private Dictionary<int, float> dailyRevenue = new Dictionary<int, float>();
        private Dictionary<int, float> dailyProfit = new Dictionary<int, float>();

        private List<FinanceBillRecord> billRecords = new List<FinanceBillRecord>();
        private Dictionary<int, PendingFinanceBill> pendingBills = new Dictionary<int, PendingFinanceBill>();

        private List<int> tmpPendingKeys;
        private List<PendingFinanceBill> tmpPendingValues;
        private List<int> tmpStateKeys;
        private List<ShopFinanceState> tmpStateValues;

        // Legacy save compatibility.
        private Dictionary<int, float> legacyShopRevenue;
        private Dictionary<int, float> legacyShopProfit;
        private Dictionary<int, string> legacyShopLabels;
        private List<string> tmpLegacyLabels;

        public float TotalIncome => totalIncome;
        public IReadOnlyDictionary<string, int> ProductSoldCounts => productSoldCounts;
        public IReadOnlyDictionary<string, float> ProductRevenues => productRevenues;
        public IReadOnlyDictionary<string, int> ComboSoldCounts => comboSoldCounts;
        public IReadOnlyDictionary<string, float> ComboRevenues => comboRevenues;
        public IReadOnlyDictionary<int, float> ShopRevenue => shopStates.ToDictionary(kv => kv.Key, kv => kv.Value?.revenue ?? 0f);
        public IReadOnlyDictionary<int, float> ShopProfit => shopStates.ToDictionary(kv => kv.Key, kv => kv.Value?.profit ?? 0f);
        public IReadOnlyDictionary<int, float> DailyRevenue => dailyRevenue;
        public IReadOnlyDictionary<int, float> DailyProfit => dailyProfit;
        public IReadOnlyList<FinanceBillRecord> BillRecords => billRecords;

        public GameComponent_ShopFinanceManager(Game game)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Values.Look(ref totalIncome, "totalIncome", 0f);

            Scribe_Collections.Look(ref productSoldCounts, "productSoldCounts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref productRevenues, "productRevenues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref comboSoldCounts, "comboSoldCounts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref comboRevenues, "comboRevenues", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref shopStates, "shopStates", LookMode.Value, LookMode.Deep, ref tmpStateKeys, ref tmpStateValues);
            Scribe_Collections.Look(ref dailyRevenue, "dailyRevenue", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dailyProfit, "dailyProfit", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref billRecords, "billRecords", LookMode.Deep);
            Scribe_Collections.Look(ref pendingBills, "pendingBills", LookMode.Value, LookMode.Deep, ref tmpPendingKeys, ref tmpPendingValues);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Collections.Look(ref legacyShopRevenue, "shopRevenue", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopProfit, "shopProfit", LookMode.Value, LookMode.Value);
                Scribe_Collections.Look(ref legacyShopLabels, "shopLabels", LookMode.Value, LookMode.Value, ref tmpStateKeys, ref tmpLegacyLabels);
            }

            if (productSoldCounts == null) productSoldCounts = new Dictionary<string, int>();
            if (productRevenues == null) productRevenues = new Dictionary<string, float>();
            if (comboSoldCounts == null) comboSoldCounts = new Dictionary<string, int>();
            if (comboRevenues == null) comboRevenues = new Dictionary<string, float>();
            if (shopStates == null) shopStates = new Dictionary<int, ShopFinanceState>();
            if (dailyRevenue == null) dailyRevenue = new Dictionary<int, float>();
            if (dailyProfit == null) dailyProfit = new Dictionary<int, float>();
            if (billRecords == null) billRecords = new List<FinanceBillRecord>();
            if (pendingBills == null) pendingBills = new Dictionary<int, PendingFinanceBill>();

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MigrateLegacyShopStates();
                TrimBillRecordsIfNeeded();
            }
        }

        public void QueueProductSale(Pawn customer, Zone_Shop zone, ThingDef productDef, int count, float amount, float cost = 0f)
        {
            if (customer == null || productDef == null || count <= 0 || amount <= 0f) return;

            PendingFinanceBill pending = GetOrCreatePending(customer, zone);
            pending.AddOrMergeLine(new FinanceLineItem
            {
                isCombo = false,
                label = productDef.LabelCap.RawText,
                defName = productDef.defName,
                count = count,
                amount = amount,
                cost = Mathf.Max(0f, cost)
            });
        }

        public void QueueComboSale(Pawn customer, Zone_Shop zone, string comboName, float amount, float cost = 0f)
        {
            if (customer == null || amount <= 0f) return;
            string finalName = string.IsNullOrEmpty(comboName) ? "未命名套餐" : comboName;

            PendingFinanceBill pending = GetOrCreatePending(customer, zone);
            pending.AddOrMergeLine(new FinanceLineItem
            {
                isCombo = true,
                label = finalName,
                defName = "",
                count = 1,
                amount = amount,
                cost = Mathf.Max(0f, cost)
            });
        }

        public void ClearPendingBill(Pawn customer)
        {
            if (customer == null) return;
            pendingBills.Remove(customer.thingIDNumber);
        }

        public void CommitCheckout(Pawn customer, Building_CashRegister register, int paidSilver)
        {
            if (customer == null || paidSilver <= 0) return;

            int customerId = customer.thingIDNumber;
            if (!pendingBills.TryGetValue(customerId, out PendingFinanceBill pending))
            {
                pending = new PendingFinanceBill();
            }

            if (pending.zoneId < 0 || string.IsNullOrEmpty(pending.zoneLabel))
            {
                Zone_Shop fallbackZone = FindZoneByRegister(register);
                if (fallbackZone != null)
                {
                    pending.zoneId = fallbackZone.ID;
                    pending.zoneLabel = fallbackZone.label;
                }
            }

            int gameDay = GenDate.DaysPassed;
            string zoneLabel = string.IsNullOrEmpty(pending.zoneLabel) ? "未命名商店" : pending.zoneLabel;
            int zoneId = pending.zoneId;

            FinanceBillRecord bill = new FinanceBillRecord
            {
                tickAbs = Find.TickManager.TicksAbs,
                gameDay = gameDay,
                zoneId = zoneId,
                zoneLabel = zoneLabel,
                customerName = customer.LabelShortCap,
                paidSilver = paidSilver,
                lines = CloneLines(pending.lines)
            };
            billRecords.Add(bill);
            TrimBillRecordsIfNeeded();

            totalIncome += paidSilver;
            AddFloat(dailyRevenue, gameDay, paidSilver);
            float totalCost = 0f;

            ShopFinanceState shopState = zoneId >= 0 ? GetOrCreateShopState(zoneId, zoneLabel) : null;
            if (shopState != null)
            {
                shopState.revenue += paidSilver;
            }

            for (int i = 0; i < pending.lines.Count; i++)
            {
                FinanceLineItem line = pending.lines[i];
                if (line == null || line.count <= 0 || line.amount <= 0f) continue;
                totalCost += Mathf.Max(0f, line.cost);

                if (line.isCombo)
                {
                    string comboKey = string.IsNullOrEmpty(line.label) ? "未命名套餐" : line.label;
                    AddInt(comboSoldCounts, comboKey, line.count);
                    AddFloat(comboRevenues, comboKey, line.amount);
                }
                else
                {
                    string productKey = string.IsNullOrEmpty(line.defName) ? line.label : line.defName;
                    AddInt(productSoldCounts, productKey, line.count);
                    AddFloat(productRevenues, productKey, line.amount);
                }
            }

            float profit = Mathf.Max(0f, paidSilver - totalCost);
            AddFloat(dailyProfit, gameDay, profit);
            if (shopState != null)
            {
                shopState.profit += profit;
            }

            pendingBills.Remove(customerId);
        }

        public string GetShopLabel(int zoneId)
        {
            if (zoneId < 0) return "未知商店";
            if (shopStates.TryGetValue(zoneId, out ShopFinanceState state) && state != null && !string.IsNullOrEmpty(state.label))
                return state.label;
            return "商店 #" + zoneId;
        }

        private PendingFinanceBill GetOrCreatePending(Pawn customer, Zone_Shop zone)
        {
            int customerId = customer.thingIDNumber;
            if (!pendingBills.TryGetValue(customerId, out PendingFinanceBill pending))
            {
                pending = new PendingFinanceBill();
                pendingBills[customerId] = pending;
            }

            if (zone != null)
            {
                pending.zoneId = zone.ID;
                pending.zoneLabel = zone.label;
            }

            return pending;
        }

        private ShopFinanceState GetOrCreateShopState(int zoneId, string label)
        {
            if (!shopStates.TryGetValue(zoneId, out ShopFinanceState state) || state == null)
            {
                state = new ShopFinanceState();
                shopStates[zoneId] = state;
            }

            if (!string.IsNullOrEmpty(label))
            {
                state.label = label;
            }

            if (string.IsNullOrEmpty(state.label))
            {
                state.label = "商店 #" + zoneId;
            }

            return state;
        }

        private void MigrateLegacyShopStates()
        {
            if (legacyShopRevenue == null && legacyShopProfit == null && legacyShopLabels == null)
            {
                return;
            }

            HashSet<int> keys = new HashSet<int>();
            AddKeys(keys, legacyShopRevenue);
            AddKeys(keys, legacyShopProfit);
            AddKeys(keys, legacyShopLabels);

            foreach (int zoneId in keys)
            {
                string label = TryGet(legacyShopLabels, zoneId, out string savedLabel) ? savedLabel : null;
                ShopFinanceState state = GetOrCreateShopState(zoneId, label);
                if (TryGet(legacyShopRevenue, zoneId, out float revenue)) state.revenue = revenue;
                if (TryGet(legacyShopProfit, zoneId, out float profit)) state.profit = profit;
            }
        }

        private static void AddKeys<T>(HashSet<int> keys, Dictionary<int, T> source)
        {
            if (source == null) return;
            foreach (int key in source.Keys)
            {
                keys.Add(key);
            }
        }

        private static bool TryGet<T>(Dictionary<int, T> source, int key, out T value)
        {
            if (source != null && source.TryGetValue(key, out value)) return true;
            value = default(T);
            return false;
        }

        private static List<FinanceLineItem> CloneLines(List<FinanceLineItem> source)
        {
            List<FinanceLineItem> result = new List<FinanceLineItem>();
            if (source == null) return result;

            for (int i = 0; i < source.Count; i++)
            {
                FinanceLineItem line = source[i];
                if (line == null) continue;

                result.Add(new FinanceLineItem
                {
                    label = line.label,
                    defName = line.defName,
                    isCombo = line.isCombo,
                    count = line.count,
                    amount = line.amount,
                    cost = line.cost
                });
            }

            return result;
        }

        private static Zone_Shop FindZoneByRegister(Building_CashRegister register)
        {
            if (register?.Map == null || !register.Spawned) return null;
            return register.Map.zoneManager.AllZones
                .OfType<Zone_Shop>()
                .FirstOrDefault(z => z.Cells.Contains(register.Position));
        }

        private static void AddInt(Dictionary<string, int> map, string key, int value)
        {
            if (string.IsNullOrEmpty(key) || value == 0) return;
            if (map.TryGetValue(key, out int old))
                map[key] = old + value;
            else
                map[key] = value;
        }

        private static void AddFloat(Dictionary<string, float> map, string key, float value)
        {
            if (string.IsNullOrEmpty(key) || Math.Abs(value) < 0.001f) return;
            if (map.TryGetValue(key, out float old))
                map[key] = old + value;
            else
                map[key] = value;
        }

        private static void AddFloat(Dictionary<int, float> map, int key, float value)
        {
            if (Math.Abs(value) < 0.001f) return;
            if (map.TryGetValue(key, out float old))
                map[key] = old + value;
            else
                map[key] = value;
        }

        private void TrimBillRecordsIfNeeded()
        {
            if (billRecords == null) return;

            int maxRecords = SimManagementLibMod.Settings?.maxFinanceBillRecords ?? DefaultMaxBillRecords;
            maxRecords = Mathf.Clamp(maxRecords, 200, 50000);
            while (billRecords.Count > maxRecords)
            {
                billRecords.RemoveAt(0);
            }
        }
    }
}
