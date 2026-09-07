using SimManagementLib.SimThingClass;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义货柜按补货进度切换贴图所需的阶段配置。
    /// </summary>
    public class CompProperties_ProgressStageGraphic : CompProperties
    {
        /// <summary>
        /// 保存多个进度阶段与对应贴图路径，支持按百分比无限追加。
        /// </summary>
        public List<ProgressStageGraphicEntry> stages = new List<ProgressStageGraphicEntry>();

        /// <summary>
        /// 初始化组件类型，供 XML 创建实例。
        /// </summary>
        public CompProperties_ProgressStageGraphic()
        {
            compClass = typeof(ThingComp_ProgressStageGraphic);
        }
    }

    /// <summary>
    /// 挂在货柜上的进度贴图组件，负责根据库存完成度选择当前显示贴图。
    /// </summary>
    public class ThingComp_ProgressStageGraphic : ThingComp
    {
        private readonly Dictionary<string, Graphic> cachedGraphics = new Dictionary<string, Graphic>();

        private CompProperties_ProgressStageGraphic StageProps => props as CompProperties_ProgressStageGraphic;

        /// <summary>
        /// 根据当前货柜进度返回要绘制的贴图；未命中时返回 null 让原贴图继续生效。
        /// </summary>
        public Graphic GetCurrentGraphic()
        {
            Building_SimContainer container = parent as Building_SimContainer;
            if (container == null || parent?.def?.graphicData == null)
                return null;

            ProgressStageGraphicEntry stage = ResolveCurrentStage(container);
            if (stage == null || string.IsNullOrWhiteSpace(stage.texPath))
                return null;

            string key = stage.texPath.Trim();
            if (cachedGraphics.TryGetValue(key, out Graphic cached) && cached != null)
                return cached;

            GraphicData baseGraphicData = parent.def.graphicData;
            Type graphicClass = baseGraphicData.graphicClass ?? typeof(Graphic_Multi);
            Shader shader = baseGraphicData.shaderType?.Shader ?? ShaderDatabase.Cutout;
            Graphic graphic = GraphicDatabase.Get(
                graphicClass,
                key,
                shader,
                baseGraphicData.drawSize,
                parent.DrawColor,
                parent.DrawColorTwo,
                baseGraphicData,
                baseGraphicData.shaderParameters,
                baseGraphicData.maskPath);
            cachedGraphics[key] = graphic;
            return graphic;
        }

        /// <summary>
        /// 在贴图参数热重载后清空缓存，避免继续使用旧图。
        /// </summary>
        public override void Notify_DefsHotReloaded()
        {
            base.Notify_DefsHotReloaded();
            cachedGraphics.Clear();
        }

        /// <summary>
        /// 计算当前货柜应该命中的贴图阶段，默认选取不超过当前进度的最高阈值。
        /// </summary>
        private ProgressStageGraphicEntry ResolveCurrentStage(Building_SimContainer container)
        {
            List<ProgressStageGraphicEntry> entries = StageProps?.stages;
            if (entries == null || entries.Count == 0)
                return null;

            float progress = container.GetVisualFillPercent();
            ProgressStageGraphicEntry result = null;
            float bestThreshold = float.MinValue;

            for (int i = 0; i < entries.Count; i++)
            {
                ProgressStageGraphicEntry current = entries[i];
                if (current == null || string.IsNullOrWhiteSpace(current.texPath))
                    continue;

                float threshold = Mathf.Clamp01(current.minFillPercent);
                if (threshold > progress)
                    continue;

                if (result == null || threshold > bestThreshold)
                {
                    result = current;
                    bestThreshold = threshold;
                }
            }

            if (result != null)
                return result;

            return entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.texPath))
                .OrderBy(entry => Mathf.Clamp01(entry.minFillPercent))
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// 保存单个进度阶段的最小库存比例和贴图路径。
    /// </summary>
    public class ProgressStageGraphicEntry
    {
        /// <summary>
        /// 负责定义命中当前贴图所需达到的最小补货比例，范围为 0 到 1。
        /// </summary>
        public float minFillPercent = 0f;

        /// <summary>
        /// 负责保存当前阶段对应的贴图路径。
        /// </summary>
        public string texPath = string.Empty;
    }
}
