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
        /// <summary>
        /// 绘制服务管理面板，负责展示商店区域内服务建筑及其服务槽位配置。
        /// </summary>
        private void DrawServicesPanel(Rect rect)
        {
            List<ServiceProviderRow> rows = BuildServiceRows()
                .Where(r => MatchSearch(r.ProviderLabel) || MatchSearch(r.ServiceLabel))
                .ToList();

            const float providerW = 150f;
            const float enabledW = 52f;
            const float capacityW = 80f;
            const float usersW = 86f;

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderH);
            DrawTableHeader(headerRect, delegate
            {
                float cx = headerRect.xMax - RowPad;
                cx -= usersW;
                DrawHdrLabel(new Rect(cx, headerRect.y, usersW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Usage"), TextAnchor.MiddleCenter);
                cx -= ColGap;
                cx -= capacityW;
                DrawHdrLabel(new Rect(cx, headerRect.y, capacityW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Capacity"));
                cx -= ColGap;
                cx -= FieldW;
                DrawHdrLabel(new Rect(cx, headerRect.y, FieldW, headerRect.height), SimTranslation.T("RSMF.ShopManager.OverridePrice"));
                cx -= ColGap;
                cx -= enabledW;
                DrawHdrLabel(new Rect(cx, headerRect.y, enabledW, headerRect.height), SimTranslation.T("RSMF.ShopManager.Enabled"), TextAnchor.MiddleCenter);
                cx -= ColGap;
                cx -= providerW;
                DrawHdrLabel(new Rect(cx, headerRect.y, providerW, headerRect.height), SimTranslation.T("RSMF.ShopManager.ServiceBuilding"));
                cx -= ColGap;
                DrawHdrLabel(new Rect(headerRect.x + RowPad, headerRect.y, cx - headerRect.x - RowPad, headerRect.height), SimTranslation.T("RSMF.ShopManager.ServiceName"));
            });

            Rect outRect = new Rect(rect.x, headerRect.yMax, rect.width, rect.height - HeaderH);
            float viewWidth = outRect.width - ScrW;
            Widgets.BeginScrollView(outRect, ref listScroll, new Rect(0f, 0f, viewWidth, rows.Count * RowH));

            for (int i = 0; i < rows.Count; i++)
            {
                DrawServiceRow(new Rect(0f, i * RowH, viewWidth, RowH), rows[i], i);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 绘制单条服务配置行，包含启用、价格覆盖、并发和当前占用数量。
        /// </summary>
        private void DrawServiceRow(Rect row, ServiceProviderRow data, int index)
        {
            const float providerW = 150f;
            const float enabledW = 52f;
            const float capacityW = 80f;
            const float usersW = 86f;

            ServiceSlotData slot = data.Slot;
            bool enabled = slot.enabled;
            DrawRowBg(row, index, enabled);
            Widgets.DrawHighlightIfMouseover(row);

            float ctrlY = row.y + (RowH - 24f) / 2f;
            float rx = row.xMax - RowPad;

            rx -= usersW;
            int activeUsers = ShopServiceUtility.CountActiveUsers(data.Provider.Map, data.Provider.thingIDNumber, slot.serviceDefName);
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = activeUsers >= Mathf.Max(1, slot.maxSimultaneousUsers) ? CStockLow : CTextMid;
            Widgets.Label(new Rect(rx, row.y, usersW, RowH), $"{activeUsers}/{Mathf.Max(1, slot.maxSimultaneousUsers)}");

            rx -= ColGap;
            rx -= capacityW;
            if (slot.capacityBuffer == null) slot.capacityBuffer = Mathf.Max(1, slot.maxSimultaneousUsers).ToString();
            Widgets.TextFieldNumeric(new Rect(rx, ctrlY, capacityW, 24f), ref slot.maxSimultaneousUsers, ref slot.capacityBuffer, 1, 99);

            rx -= ColGap;
            rx -= FieldW;
            if (slot.priceBuffer == null) slot.priceBuffer = slot.priceOverride.ToString("F0");
            Widgets.TextFieldNumeric(new Rect(rx, ctrlY, FieldW, 24f), ref slot.priceOverride, ref slot.priceBuffer, 0f, 99999f);

            rx -= ColGap;
            rx -= enabledW;
            Widgets.Checkbox(rx + (enabledW - CheckSz) / 2f, row.y + (RowH - CheckSz) / 2f, ref enabled, CheckSz, paintable: true);
            slot.enabled = enabled;

            rx -= ColGap;
            rx -= providerW;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = CTextMid;
            Widgets.Label(new Rect(rx, row.y, providerW, RowH), data.ProviderLabel.Truncate(providerW));

            rx -= ColGap;
            GUI.color = enabled ? Color.white : CTextDim;
            Widgets.Label(new Rect(row.x + RowPad, row.y, rx - row.x - RowPad, RowH), data.ServiceLabel.Truncate(rx - row.x - RowPad));

            if (data.ServiceDef != null)
                TooltipHandler.TipRegion(row, data.ServiceDef.description);
            ResetText();
        }

        /// <summary>
        /// 从服务建筑草稿数据构建可绘制的服务行。
        /// </summary>
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

        /// <summary>
        /// 保存服务管理表格中一行需要展示的建筑、服务和草稿槽位。
        /// </summary>
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
