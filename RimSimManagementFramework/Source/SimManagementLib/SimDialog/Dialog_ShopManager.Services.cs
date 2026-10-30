using SimManagementLib.Api;
using SimManagementLib.SimService;
using SimManagementLib.SimThingComp;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    public partial class Dialog_ShopManager
    {
        private bool serviceSortByProvider;

        //服务首页只显示经营状态，单项参数通过编辑按钮打开。
        private void DrawServicesPanel(Rect rect)
        {
            float h = ShopUiVisualUtility.ControlHeight();
            string sort = SimTranslation.T(serviceSortByProvider ? "RSMF.ServiceEditor.SortProvider" : "RSMF.ServiceEditor.SortService");
            if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(rect.x, rect.y, Mathf.Min(230f, rect.width), h), sort))
            {
                serviceSortByProvider = !serviceSortByProvider;
                listScroll = Vector2.zero;
            }
            List<ServiceProviderRow> rows = BuildServiceRows()
                .Where(r => MatchSearch(r.ProviderLabel) || MatchSearch(r.ServiceLabel))
                .OrderBy(r => serviceSortByProvider ? r.ProviderLabel : r.ServiceLabel)
                .ThenBy(r => r.Provider.thingIDNumber).ToList();
            float y = rect.y + h + 8f;
            float width = Mathf.Max(1f, rect.width - 18f);
            float editW = Mathf.Max(64f, Text.CalcSize(SimTranslation.T("RSMF.ServiceEditor.Edit")).x + 24f);
            float remaining = Mathf.Max(1f, width - editW - 28f);
            var header = new Rect(rect.x, y, width, h);
            ShopUiVisualUtility.DrawTableHeaderBackground(header);
            DrawServiceCells(header, remaining, SimTranslation.T("RSMF.ShopManager.ServiceName"),
                SimTranslation.T("RSMF.ShopManager.OverridePrice"), SimTranslation.T("RSMF.ShopManager.Usage"));
            y += h + 2f;
            Rect outer = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            float rowHeight = h * 2f + 4f;
            float contentHeight = rows.Count * rowHeight;
            listScroll.y = Mathf.Clamp(listScroll.y, 0, Mathf.Max(0f, contentHeight - outer.height));
            Widgets.BeginScrollView(outer, ref listScroll, new Rect(0, 0, width, Mathf.Max(outer.height, contentHeight)));
            try
            {
                if (rows.Count == 0)
                    ShopUiVisualUtility.DrawCellLabel(new Rect(8f, 8f, width - 16f, h), SimTranslation.T("RSMF.ServiceEditor.Empty"), CTextMid);
                int first = Mathf.Max(0, Mathf.FloorToInt(listScroll.y / rowHeight));
                int last = Mathf.Min(rows.Count, Mathf.CeilToInt((listScroll.y + outer.height) / rowHeight) + 1);
                for (int i = first; i < last; i++)
                {
                    var data = rows[i];
                    var slot = data.Slot;
                    Rect row = new Rect(0, i * rowHeight, width, rowHeight);
                    ShopUiVisualUtility.DrawTableRowBackground(row, i, false);
                    int active = ShopServiceUtility.CountActiveUsers(data.Provider.Map, data.Provider.thingIDNumber, slot.serviceDefName);
                    DrawServiceCells(new Rect(0, row.y, width, h), remaining, data.ServiceLabel,
                        slot.priceOverrideEnabled ? slot.priceOverride.ToString("0.##") : SimTranslation.T("RSMF.ServiceEditor.DefaultPrice"),
                        $"{active}/{Mathf.Max(1, slot.maxSimultaneousUsers)}");
                    ShopUiVisualUtility.DrawCellLabel(new Rect(8f, row.y + h, width - 16f, h),
                        data.ProviderLabel + " · " + SimTranslation.T(slot.enabled ? "RSMF.ServiceEditor.Enabled" : "RSMF.ServiceEditor.Disabled"), CTextMid);
                    if (ShopUiVisualUtility.DrawSecondaryButton(new Rect(width - editW - 6f, row.y + 2f, editW, h), SimTranslation.T("RSMF.ServiceEditor.Edit")))
                        Find.WindowStack.Add(new Dialog_ShopServiceEditor(data.ServiceLabel, slot));
                }
            }
            finally { Widgets.EndScrollView(); }
        }

        //表头与数据行共同计算列宽，避免固定列宽侵占服务名称。
        private static void DrawServiceCells(Rect rect, float width, string name, string price, string usage)
        {
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 8f, rect.y, width * 0.54f - 8f, rect.height), name);
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 8f + width * 0.54f, rect.y, width * 0.24f - 8f, rect.height), price);
            ShopUiVisualUtility.DrawCellLabel(new Rect(rect.x + 8f + width * 0.78f, rect.y, width * 0.22f - 8f, rect.height), usage);
        }

        //从服务建筑草稿数据构建可绘制的服务行。
        private List<ServiceProviderRow> BuildServiceRows()
        {
            List<ServiceProviderRow> rows = new List<ServiceProviderRow>();
            for (int i = 0; i < serviceProviders.Count; i++)
            {
                Thing provider = serviceProviders[i];
                if (provider == null || provider.Destroyed) continue;
                if (!draftServiceData.TryGetValue(provider.thingIDNumber, out List<ServiceSlotData> slots) || slots.NullOrEmpty())
                    continue;

                for (int j = 0; j < slots.Count; j++)
                {
                    ServiceSlotData slot = slots[j];
                    if (slot == null) continue;
                    ShopServiceDef serviceDef = slot.ServiceDef;
                    rows.Add(new ServiceProviderRow
                    {
                        Provider = provider,
                        ProviderLabel = provider.LabelCap,
                        Slot = slot,
                        ServiceDef = serviceDef,
                        ServiceLabel = serviceDef?.DisplayLabel ?? slot.serviceDefName
                    });
                }
            }

            return rows;
        }

        //保存服务管理表格中一行需要展示的建筑、服务和草稿槽位。
        private sealed class ServiceProviderRow
        {
            public Thing Provider;
            public string ProviderLabel;
            public ServiceSlotData Slot;
            public ShopServiceDef ServiceDef;
            public string ServiceLabel;
        }
    }
}
