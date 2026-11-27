using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using SimManagementLib.SimMapComp;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //组件货柜的批量操作、排序菜单、剪贴板复制粘贴与保存提交逻辑。
    internal sealed partial class Dialog_StorageSales
    {
        //打开清单排序下拉菜单。
        private void OpenSortMenu()
        {
            var options = new List<FloatMenuOption>();
            foreach (SortMode mode in Enum.GetValues(typeof(SortMode)))
            {
                SortMode target = mode;
                options.Add(new FloatMenuOption(SortLabel(target), () =>
                {
                    sortMode = target;
                    manageScroll = Vector2.zero;
                    RebuildManageRows();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //打开当前筛选清单的批量操作菜单，减少逐行重复输入。
        private void OpenBatchMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new(SimTranslation.T("RSMF.StorageSales.Batch.SellAll"), () => ApplyBatch(rule => rule.enabled = true)),
                new(SimTranslation.T("RSMF.StorageSales.Batch.PauseAll"), () => ApplyBatch(rule => rule.enabled = false)),
                new(SimTranslation.T("RSMF.StorageSales.Batch.PriceMarket"), () => ApplyBatch(rule => { rule.price = 0f; rule.priceBuffer = "0"; })),
                new(SimTranslation.T("RSMF.StorageSales.Batch.PriceMult", "1.2".Named("mult")), () => ApplyBatchPriceMultiplier(1.2f)),
                new(SimTranslation.T("RSMF.StorageSales.Batch.PriceMult", "1.5".Named("mult")), () => ApplyBatchPriceMultiplier(1.5f)),
                new(SimTranslation.T("RSMF.StorageSales.Batch.PriceMult", "2.0".Named("mult")), () => ApplyBatchPriceMultiplier(2.0f)),
                new(SimTranslation.T("RSMF.StorageSales.Batch.TargetFromStock"), ApplyBatchTargetFromStock),
                new(SimTranslation.T("RSMF.StorageSales.Batch.ThresholdMax"), ApplyBatchMaxThresholds),
                new(SimTranslation.T("RSMF.StorageSales.Batch.ClearEmptyRules"), ClearEmptyRules)
            };
            Find.WindowStack.Add(new FloatMenu(options));
        }

        //对当前筛选出的全部行执行统一修改。
        private void ApplyBatch(Action<GoodsItemData> update)
        {
            GUI.FocusControl(null);
            for (int i = 0; i < manageRows.Count; i++) update(draft[manageRows[i]]);
            dirty = true;
            RebuildManageRows();
        }

        //按各品种基础市价倍率批量填写固定单价。
        private void ApplyBatchPriceMultiplier(float multiplier)
        {
            GUI.FocusControl(null);
            for (int i = 0; i < manageRows.Count; i++)
            {
                ThingDef def = manageRows[i];
                GoodsItemData rule = draft[def];
                rule.price = Mathf.Max(1f, Mathf.Round(def.BaseMarketValue * multiplier));
                rule.priceBuffer = rule.price.ToString("0.##");
            }
            dirty = true;
            RebuildManageRows();
        }

        //将当前筛选商品的补货目标同步为架上现存数量。
        private void ApplyBatchTargetFromStock()
        {
            GUI.FocusControl(null);
            for (int i = 0; i < manageRows.Count; i++)
            {
                ThingDef def = manageRows[i];
                GoodsItemData rule = draft[def];
                int prev = rule.count;
                rule.count = stock.GetValueOrDefault(def);
                rule.countBuffer = rule.count.ToString();
                SyncThresholdAfterTargetChange(rule, prev);
            }
            dirty = true;
            RebuildManageRows();
        }

        //将所有已设目标的商品补货阈值同步为各自目标量。
        private void ApplyBatchMaxThresholds()
        {
            GUI.FocusControl(null);
            for (int i = 0; i < manageRows.Count; i++)
            {
                GoodsItemData rule = draft[manageRows[i]];
                if (rule.count <= 0) continue;
                rule.restockThreshold = rule.count;
                rule.restockThresholdBuffer = rule.restockThreshold.ToString();
            }
            dirty = true;
        }

        //清理当前清单中没有任何现货的品种规则。
        private void ClearEmptyRules()
        {
            GUI.FocusControl(null);
            List<ThingDef> empty = manageRows.Where(d => stock.GetValueOrDefault(d) <= 0).ToList();
            if (empty.Count == 0) return;
            for (int i = 0; i < empty.Count; i++)
            {
                draft.Remove(empty[i]);
                removedRules.Add(empty[i]);
            }
            dirty = true;
            RebuildManageRows();
        }

        //将当前草稿规则复制到内存剪贴板，供其他同类储物架快速复用。
        private void CopyDraftToClipboard()
        {
            clipboard = draft.ToDictionary(pair => pair.Key, pair => pair.Value.Copy());
            Messages.Message(SimTranslation.T("RSMF.StorageSales.CopyDone", clipboard.Count.Named("count")),
                MessageTypeDefOf.TaskCompletion, false);
        }

        //从剪贴板粘贴兼容当前货柜限制的规则，并刷新所有输入缓冲。
        private void PasteClipboardToDraft()
        {
            if (clipboard == null || clipboard.Count == 0) return;
            GUI.FocusControl(null);
            int applied = 0;
            int skipped = 0;
            foreach (var entry in clipboard)
            {
                if (!sales.SalesProps.AllowsListing(entry.Key) || (!sales.AllowsDef(entry.Key) && !stock.ContainsKey(entry.Key)))
                {
                    skipped++;
                    continue;
                }
                GoodsItemData copy = entry.Value.Copy();
                NormalizeRuleBuffers(copy);
                draft[entry.Key] = copy;
                removedRules.Remove(entry.Key);
                applied++;
            }
            if (applied == 0)
            {
                Messages.Message(SimTranslation.T("RSMF.StorageSales.PasteNone"), MessageTypeDefOf.RejectInput, false);
                return;
            }
            dirty = true;
            RebuildManageRows();
            if (picking) RebuildPickerRows();
            string msg = skipped > 0
                ? SimTranslation.T("RSMF.StorageSales.PastePartial", applied.Named("applied"), skipped.Named("skipped"))
                : SimTranslation.T("RSMF.StorageSales.PasteDone", applied.Named("applied"));
            Messages.Message(msg, MessageTypeDefOf.TaskCompletion, false);
        }

        //为已保存的补货目标立即激活补货周期。
        private void TriggerSavedRestock()
        {
            MapComponent_RestockTaskQueue queue = sales.Host.Map?.GetComponent<MapComponent_RestockTaskQueue>();
            if (queue == null) return;
            int triggered = 0;
            foreach (ThingDef def in sales.RestockDefs)
            {
                if (sales.GetTargetCount(def) <= 0) continue;
                queue.ActivateRestockCycle(sales, def);
                triggered++;
            }
            Messages.Message(SimTranslation.T("RSMF.StorageSales.RestockTriggered", triggered.Named("count")),
                MessageTypeDefOf.TaskCompletion, false);
        }

        //规范化并提交草稿规则到宿主组件后关闭窗口。
        private void SaveAndClose()
        {
            foreach (GoodsItemData rule in draft.Values)
            {
                rule.count = Math.Max(0, rule.count);
                rule.price = Math.Max(0f, rule.price);
                rule.restockThreshold = rule.count <= 0
                    ? -1
                    : GoodsItemData.NormalizeRestockThreshold(rule.restockThreshold, rule.count);
            }
            sales.ApplyRules(draft);
            Close();
        }
    }
}
