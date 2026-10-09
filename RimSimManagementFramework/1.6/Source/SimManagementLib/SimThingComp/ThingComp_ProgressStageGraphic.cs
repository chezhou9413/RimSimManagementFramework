using SimManagementLib.SimThingClass;
using SimManagementLib.Tool;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimThingComp
{
    //定义货柜按补货进度切换贴图所需的阶段配置。
    public class CompProperties_ProgressStageGraphic : CompProperties
    {
        //保存多个进度阶段与对应贴图路径。
        public List<ProgressStageGraphicEntry> stages = new List<ProgressStageGraphicEntry>();

        //初始化组件类型，供 XML 创建实例。
        public CompProperties_ProgressStageGraphic()
        {
            compClass = typeof(ThingComp_ProgressStageGraphic);
        }
    }

    //根据库存比例或玩家固定选择显示货柜贴图。
    public class ThingComp_ProgressStageGraphic : ThingComp
    {
        private readonly Dictionary<string, Graphic> cachedGraphics = new Dictionary<string, Graphic>();
        private string fixedStagePath = "";

        private CompProperties_ProgressStageGraphic StageProps => props as CompProperties_ProgressStageGraphic;

        //返回当前货柜贴图，未配置阶段时保留建筑默认贴图。
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

        //在贴图参数热重载后清空缓存。
        public override void Notify_DefsHotReloaded()
        {
            base.Notify_DefsHotReloaded();
            cachedGraphics.Clear();
        }

        //保存每个货柜独立的外观选择。
        public override void PostExposeData()
        {
            Scribe_Values.Look(ref fixedStagePath, "fixedStagePath", "");
        }

        //提供自动外观和现有阶段贴图的选择入口。
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (parent.Faction != Faction.OfPlayer || StageProps.stages.Count < 2) yield break;
            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.StageGraphic.Choose"),
                defaultDesc = SimTranslation.T("RSMF.StageGraphic.Hint"),
                icon = parent.def.uiIcon,
                action = OpenStageMenu
            };
        }

        //只列出此建筑已经配置的阶段，不引入额外资源路径。
        private void OpenStageMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(SimTranslation.T("RSMF.StageGraphic.Auto"), () => SelectStage(""))
            };
            foreach (var stage in StageProps.stages.Where(s => !s.texPath.NullOrEmpty()).OrderBy(s => s.minFillPercent))
            {
                string path = stage.texPath;
                string label = SimTranslation.T("RSMF.StageGraphic.Fixed", stage.minFillPercent.ToString("P0").Named("fill"));
                if (fixedStagePath == path) label += " ✓";
                options.Add(new FloatMenuOption(label, () => SelectStage(path)));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //切换显示方式后立即更新地图建筑网格。
        private void SelectStage(string path)
        {
            fixedStagePath = path;
            ((Building_SimContainer)parent).RefreshProgressStageGraphic();
        }

        //固定模式优先使用指定贴图，自动模式按库存比例匹配阈值。
        private ProgressStageGraphicEntry ResolveCurrentStage(Building_SimContainer container)
        {
            List<ProgressStageGraphicEntry> entries = StageProps?.stages;
            if (entries == null || entries.Count == 0)
                return null;
            if (!fixedStagePath.NullOrEmpty())
            {
                var selected = entries.FirstOrDefault(entry => entry.texPath == fixedStagePath);
                if (selected != null) return selected;
                Log.Warning("[RSMF] 货柜固定贴图已从定义中移除：" + fixedStagePath);
                fixedStagePath = "";
            }

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

    //保存单个进度阶段的最小库存比例和贴图路径。
    public class ProgressStageGraphicEntry
    {
        //命中当前贴图所需达到的最小库存比例。
        public float minFillPercent = 0f;

        //当前阶段的贴图路径。
        public string texPath = string.Empty;
    }
}
