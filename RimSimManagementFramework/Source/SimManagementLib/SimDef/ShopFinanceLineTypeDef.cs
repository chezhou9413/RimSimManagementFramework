using SimManagementLib.Pojo;
using System;
using Verse;

namespace SimManagementLib.SimDef
{
    /// <summary>
    /// 声明财务明细类型，负责让外部模组定义筹码、会员、佣金等自定义收入分类。
    /// </summary>
    public class ShopFinanceLineTypeDef : Def
    {
        public string lineType = "";
        public Type workerClass = typeof(ShopFinanceLineTypeWorker);

        [Unsaved] private ShopFinanceLineTypeWorker workerInt;

        /// <summary>
        /// 返回财务明细类型标识，负责在 XML 未填写 lineType 时回退到 defName。
        /// </summary>
        public string EffectiveLineType => string.IsNullOrEmpty(lineType) ? defName : lineType;

        /// <summary>
        /// 返回财务明细类型 Worker，负责在类型缺失时回退到默认统计逻辑。
        /// </summary>
        public ShopFinanceLineTypeWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    try
                    {
                        Type type = workerClass ?? typeof(ShopFinanceLineTypeWorker);
                        workerInt = (ShopFinanceLineTypeWorker)Activator.CreateInstance(type);
                        workerInt.def = this;
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"[SimShop.Finance] 财务类型 {defName} 初始化 Worker 失败: {ex}");
                        workerInt = new ShopFinanceLineTypeWorker { def = this };
                    }
                }

                return workerInt;
            }
        }
    }

    /// <summary>
    /// 提供财务明细类型的可继承策略，负责控制明细规范化、合并键和统计键。
    /// </summary>
    public class ShopFinanceLineTypeWorker
    {
        public ShopFinanceLineTypeDef def;

        /// <summary>
        /// 规范化入账明细，默认补齐 lineType 和安全数量。
        /// </summary>
        public virtual void NormalizeLine(FinanceLineItem line)
        {
            if (line == null) return;
            if (string.IsNullOrEmpty(line.lineType))
                line.lineType = def?.EffectiveLineType ?? FinanceLineTypes.Product;
            if (line.count <= 0) line.count = 1;
        }

        /// <summary>
        /// 判断两条明细是否可合并，默认要求类型一致且合并键一致。
        /// </summary>
        public virtual bool CanMerge(FinanceLineItem existing, FinanceLineItem incoming)
        {
            if (existing == null || incoming == null) return false;
            if (existing.EffectiveLineType != incoming.EffectiveLineType) return false;
            return GetMergeKey(existing) == GetMergeKey(incoming);
        }

        /// <summary>
        /// 返回待结账账单内的合并键，默认优先使用 defName，其次使用 label。
        /// </summary>
        public virtual string GetMergeKey(FinanceLineItem line)
        {
            if (line == null) return "";
            if (!string.IsNullOrEmpty(line.defName)) return line.defName;
            return line.label ?? "";
        }

        /// <summary>
        /// 返回财务统计键，默认沿用合并键。
        /// </summary>
        public virtual string GetStatKey(FinanceLineItem line)
        {
            return GetMergeKey(line);
        }
    }
}
