using System;
using System.Collections.Generic;
using System.Linq;
using SimManagementLib.BusinessAdvisor;

namespace SimManagementLib.GameComp
{
    //准备时一次反馈多个参数错误，避免每轮只发现一个问题反复调用模型。
    public sealed partial class GameComponent_BusinessAdvisor
    {
        //所有准备均为只读；任一操作不合法就整批拒绝，不执行其余成功准备的步骤。
        private static List<AdvisorPreparedStep> PrepareActions(List<AdvisorAction> actions)
        {
            var steps = new List<AdvisorPreparedStep>();
            var errors = new List<string>();
            for (int index = 0; index < actions.Count; index++)
            {
                try { steps.Add(AdvisorActions.Prepare(actions[index])); }
                catch (InvalidOperationException ex)
                {
                    errors.Add("操作 " + (index + 1) + " (" + actions[index].kind + ")：" + ex.Message);
                    if (errors.Count == 8) break;
                }
            }
            if (errors.Count > 0)
                throw new InvalidOperationException("方案准备未通过，本批全部未执行。请一并修正以下问题（最多列出8项）：\n" + string.Join("\n\n", errors));
            DeduplicateForbiddenTargets(steps);
            var duplicates = steps.Select((step, index) => new { step.resource, index })
                .GroupBy(s => s.resource).Where(g => g.Count() > 1)
                .Select(g => g.Key + "：操作 " + string.Join(",", g.Select(s => s.index + 1)));
            string conflicts = string.Join("；", duplicates);
            if (conflicts.Length > 0)
                throw new InvalidOperationException("本批重复修改同一对象，整批未执行：" + conflicts
                    + "。同一员工/货柜分批提交，不同对象的独立操作可合批。");
            return steps;
        }

        //重叠矩形只处理一次相同物品，避免解禁后被搬走又在后续矩形重复查找旧编号。
        private static void DeduplicateForbiddenTargets(List<AdvisorPreparedStep> steps)
        {
            var seen = new Dictionary<string, string>();
            foreach (var step in steps)
            {
                if (step.action.kind != "order" || (step.action.command != "allow" && step.action.command != "forbid")) continue;
                var targets = step.children ?? new List<AdvisorPreparedStep> { step };
                int removed = 0;
                for (int index = 0; index < targets.Count; index++)
                {
                    var action = targets[index].action;
                    if (!action.targetId.HasValue) continue;
                    string id = action.mapId + ":" + action.targetId;
                    if (!seen.TryGetValue(id, out string previous)) { seen.Add(id, action.command); continue; }
                    if (previous != action.command)
                        throw new InvalidOperationException("同一批对物品 " + id + " 同时禁止和解禁，请明确最终状态后再提交。");
                    if (step.children == null)
                        throw new InvalidOperationException("物品 " + id + " 已包含在本批较早的指令中，请删除重复的单体指令。");
                    targets.RemoveAt(index--);
                    removed++;
                }
                if (removed > 0) step.preview += "\n本批去重：上述目标中 " + removed + " 个由较早指令处理，本项实际剩余 " + targets.Count + " 个。";
            }
        }
    }
}
