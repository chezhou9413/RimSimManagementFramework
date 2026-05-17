using System.Collections.Generic;
using RimWorld;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 显示建筑现金库存和自动取现阈值的滑条控件，负责让玩家像设置燃料目标一样调整取现阈值。
    /// </summary>
    public class Gizmo_CashWithdrawThreshold : Gizmo_Slider
    {
        private readonly ThingComp_CashStorage cashStorage;
        private static bool draggingBar;

        /// <summary>
        /// 返回或设置滑条中的取现阈值比例。
        /// </summary>
        protected override float Target
        {
            get => cashStorage.WithdrawThreshold / (float)cashStorage.MaxWithdrawThreshold;
            set => cashStorage.SetWithdrawThreshold(Mathf.RoundToInt(value * cashStorage.MaxWithdrawThreshold));
        }

        /// <summary>
        /// 返回当前白银库存相对最大阈值的显示比例。
        /// </summary>
        protected override float ValuePercent => Mathf.Clamp01(cashStorage.StoredSilver / (float)cashStorage.MaxWithdrawThreshold);

        /// <summary>
        /// 返回滑条标题。
        /// </summary>
        protected override string Title => SimTranslation.T("RSMF.CashStorage.WithdrawThreshold");

        /// <summary>
        /// 返回当前资金和阈值的条内文字。
        /// </summary>
        protected override string BarLabel => SimTranslation.T(
            "RSMF.CashStorage.BarLabel",
            cashStorage.StoredSilver.Named("stored"),
            cashStorage.MaxWithdrawThreshold.Named("max"),
            cashStorage.WithdrawThreshold.Named("threshold"));

        /// <summary>
        /// 返回滑条是否可拖动。
        /// </summary>
        protected override bool IsDraggable => true;

        /// <summary>
        /// 返回拖动状态，供原版滑条处理鼠标拖动。
        /// </summary>
        protected override bool DraggingBar
        {
            get => draggingBar;
            set => draggingBar = value;
        }

        /// <summary>
        /// 返回滑条宽度。
        /// </summary>
        protected override float Width => 180f;

        /// <summary>
        /// 返回可拖动刻度数量。
        /// </summary>
        protected override int Increments => 20;

        /// <summary>
        /// 负责初始化现金阈值滑条。
        /// </summary>
        public Gizmo_CashWithdrawThreshold(ThingComp_CashStorage cashStorage)
        {
            this.cashStorage = cashStorage;
        }

        /// <summary>
        /// 返回滑条上的阈值标记。
        /// </summary>
        protected override IEnumerable<float> GetBarThresholds()
        {
            yield return Mathf.Clamp01(cashStorage.WithdrawThreshold / (float)cashStorage.MaxWithdrawThreshold);
        }

        /// <summary>
        /// 返回鼠标悬停提示文本。
        /// </summary>
        protected override string GetTooltip()
        {
            return SimTranslation.T("RSMF.CashStorage.ThresholdTooltip");
        }
    }
}
