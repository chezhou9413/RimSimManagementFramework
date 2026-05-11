using System.Collections.Generic;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 定义财务账单行的业务类型，用于区分普通商品、套餐和服务收入。
    /// </summary>
    public static class FinanceLineTypes
    {
        public const string Product = "Product";
        public const string Combo = "Combo";
        public const string Service = "Service";
    }

    /// <summary>
    /// 保存一条待结账或已结账的财务明细，负责记录名称、类型、数量、收入和成本。
    /// </summary>
    public class FinanceLineItem : IExposable
    {
        public string label;
        public string defName;
        public string lineType;
        public bool isCombo;
        public int count;
        public float amount;
        public float cost;

        /// <summary>
        /// 返回兼容旧存档后的财务明细类型。
        /// </summary>
        public string EffectiveLineType
        {
            get
            {
                if (!string.IsNullOrEmpty(lineType)) return lineType;
                return isCombo ? FinanceLineTypes.Combo : FinanceLineTypes.Product;
            }
        }

        /// <summary>
        /// 将财务明细读写到存档，并兼容旧版只保存 isCombo 的记录。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref label, "label", "");
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref lineType, "lineType", "");
            Scribe_Values.Look(ref isCombo, "isCombo", false);
            Scribe_Values.Look(ref count, "count", 0);
            Scribe_Values.Look(ref amount, "amount", 0f);
            Scribe_Values.Look(ref cost, "cost", 0f);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && string.IsNullOrEmpty(lineType))
                lineType = isCombo ? FinanceLineTypes.Combo : FinanceLineTypes.Product;
        }
    }

    /// <summary>
    /// 保存顾客当前尚未提交的账单，负责按明细类型合并相同商品、套餐或服务。
    /// </summary>
    public class PendingFinanceBill : IExposable
    {
        public int zoneId = -1;
        public string zoneLabel = "";
        public List<FinanceLineItem> lines = new List<FinanceLineItem>();

        public float TotalAmount
        {
            get
            {
                float total = 0f;
                for (int i = 0; i < lines.Count; i++)
                    total += lines[i].amount;
                return total;
            }
        }

        public void AddOrMergeLine(FinanceLineItem incoming)
        {
            if (incoming == null) return;
            if (string.IsNullOrEmpty(incoming.lineType))
                incoming.lineType = incoming.isCombo ? FinanceLineTypes.Combo : FinanceLineTypes.Product;

            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line == null) continue;
                if (line.EffectiveLineType != incoming.EffectiveLineType) continue;

                if (incoming.EffectiveLineType == FinanceLineTypes.Combo || incoming.EffectiveLineType == FinanceLineTypes.Service)
                {
                    if (line.label != incoming.label) continue;
                }
                else
                {
                    if (line.defName != incoming.defName) continue;
                }

                line.count += incoming.count;
                line.amount += incoming.amount;
                line.cost += incoming.cost;
                return;
            }

            lines.Add(incoming);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref zoneLabel, "zoneLabel", "");
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            if (lines == null) lines = new List<FinanceLineItem>();
            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line != null && string.IsNullOrEmpty(line.lineType))
                    line.lineType = line.isCombo ? FinanceLineTypes.Combo : FinanceLineTypes.Product;
            }
        }
    }

    /// <summary>
    /// 保存一次完成结账后的账单记录，负责用于财务历史展示和统计回放。
    /// </summary>
    public class FinanceBillRecord : IExposable
    {
        public int tickAbs;
        public int gameDay;
        public int zoneId = -1;
        public string zoneLabel = "";
        public string customerName = "";
        public int paidSilver;
        public List<FinanceLineItem> lines = new List<FinanceLineItem>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref tickAbs, "tickAbs", 0);
            Scribe_Values.Look(ref gameDay, "gameDay", 0);
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref zoneLabel, "zoneLabel", "");
            Scribe_Values.Look(ref customerName, "customerName", "");
            Scribe_Values.Look(ref paidSilver, "paidSilver", 0);
            Scribe_Collections.Look(ref lines, "lines", LookMode.Deep);
            if (lines == null) lines = new List<FinanceLineItem>();
            for (int i = 0; i < lines.Count; i++)
            {
                FinanceLineItem line = lines[i];
                if (line != null && string.IsNullOrEmpty(line.lineType))
                    line.lineType = line.isCombo ? FinanceLineTypes.Combo : FinanceLineTypes.Product;
            }
        }
    }
}
