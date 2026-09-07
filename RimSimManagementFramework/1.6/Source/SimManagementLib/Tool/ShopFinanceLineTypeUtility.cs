using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 提供财务明细类型工具，负责把内置类型和外部 Def Worker 统一到合并与统计流程。
    /// </summary>
    public static class ShopFinanceLineTypeUtility
    {
        /// <summary>
        /// 规范化财务明细，负责补齐类型、数量和外部 Worker 需要的默认值。
        /// </summary>
        public static void NormalizeLine(FinanceLineItem line)
        {
            if (line == null) return;
            if (string.IsNullOrEmpty(line.lineType))
                line.lineType = line.isCombo ? FinanceLineTypes.Combo : FinanceLineTypes.Product;
            ShopFinanceLineTypeWorker worker = GetWorker(line.EffectiveLineType);
            if (worker != null)
                worker.NormalizeLine(line);
            if (line.count <= 0) line.count = 1;
        }

        /// <summary>
        /// 判断两条财务明细是否可合并，负责优先委托外部类型 Worker。
        /// </summary>
        public static bool CanMerge(FinanceLineItem existing, FinanceLineItem incoming)
        {
            if (existing == null || incoming == null) return false;
            if (existing.EffectiveLineType != incoming.EffectiveLineType) return false;

            ShopFinanceLineTypeWorker worker = GetWorker(incoming.EffectiveLineType);
            if (worker != null)
                return worker.CanMerge(existing, incoming);

            string type = incoming.EffectiveLineType;
            if (type == FinanceLineTypes.Combo || type == FinanceLineTypes.Service)
                return existing.label == incoming.label;
            return existing.defName == incoming.defName;
        }

        /// <summary>
        /// 返回财务统计键，负责让自定义类型决定自己的分组方式。
        /// </summary>
        public static string GetStatKey(FinanceLineItem line)
        {
            if (line == null) return "";
            ShopFinanceLineTypeWorker worker = GetWorker(line.EffectiveLineType);
            if (worker != null)
                return worker.GetStatKey(line);
            if (!string.IsNullOrEmpty(line.defName)) return line.defName;
            return line.label ?? "";
        }

        /// <summary>
        /// 判断明细类型是否属于框架内置统计类型。
        /// </summary>
        public static bool IsBuiltInLineType(string lineType)
        {
            return lineType == FinanceLineTypes.Product
                || lineType == FinanceLineTypes.Combo
                || lineType == FinanceLineTypes.Service;
        }

        /// <summary>
        /// 查找财务明细类型 Worker，负责同时支持 defName 和 lineType 匹配。
        /// </summary>
        private static ShopFinanceLineTypeWorker GetWorker(string lineType)
        {
            if (string.IsNullOrEmpty(lineType)) return null;
            foreach (ShopFinanceLineTypeDef def in DefDatabase<ShopFinanceLineTypeDef>.AllDefsListForReading)
            {
                if (def == null) continue;
                if (def.defName == lineType || def.EffectiveLineType == lineType)
                    return def.Worker;
            }
            return null;
        }
    }
}
