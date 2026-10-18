using System.Collections.Generic;
using SimManagementLib.BusinessAdvisor;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //缓存消息高度并用累计高度定位可见行，长对话不逐帧排版整段历史。
    internal sealed class AdvisorTranscriptView
    {
        private readonly Dictionary<AdvisorEntry, float> heights = new Dictionary<AdvisorEntry, float>();
        private readonly Dictionary<AdvisorEntry, float> textHeights = new Dictionary<AdvisorEntry, float>();
        private readonly Dictionary<AdvisorEntry, float> detailHeights = new Dictionary<AdvisorEntry, float>();
        private readonly HashSet<AdvisorEntry> expanded = new HashSet<AdvisorEntry>();
        private readonly List<float> bottoms = new List<float>();
        private AdvisorEntry first, last;
        private float width, lineHeight;
        private Vector2 scroll;

        //只为新消息或宽度变化计算高度，只绘制当前视口内的行。
        internal void Draw(Rect rect, IReadOnlyList<AdvisorEntry> entries, bool tools)
        {
            if (rect.height < 30f || rect.width < 100f) return;
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; Text.WordWrap = true; GUI.color = Color.white;
            float viewWidth = rect.width - 18f;
            if (width != viewWidth || lineHeight != Text.LineHeight)
            {
                width = viewWidth; lineHeight = Text.LineHeight;
                heights.Clear(); textHeights.Clear(); detailHeights.Clear(); first = null; last = null;
            }
            AdvisorEntry newFirst = entries.Count > 0 ? entries[0] : null;
            AdvisorEntry newLast = entries.Count > 0 ? entries[entries.Count - 1] : null;
            if (first != newFirst || last != newLast || bottoms.Count != entries.Count)
            {
                float oldHeight = bottoms.Count > 0 ? bottoms[bottoms.Count - 1] : 0f;
                bool atBottom = scroll.y + rect.height >= oldHeight - 32f;
                bottoms.Clear();
                float total = 0f;
                var keep = new HashSet<AdvisorEntry>();
                foreach (AdvisorEntry entry in entries)
                {
                    keep.Add(entry);
                    if (!heights.TryGetValue(entry, out float height)) heights[entry] = height = Measure(entry, tools);
                    total += height + 8f; bottoms.Add(total);
                }
                foreach (AdvisorEntry entry in new List<AdvisorEntry>(heights.Keys))
                    if (!keep.Contains(entry)) { heights.Remove(entry); textHeights.Remove(entry); detailHeights.Remove(entry); expanded.Remove(entry); }
                first = newFirst; last = newLast;
                if (atBottom) scroll.y = Mathf.Max(0f, total - rect.height);
            }
            float contentHeight = bottoms.Count > 0 ? bottoms[bottoms.Count - 1] : 0f;
            Rect view = new Rect(0f, 0f, viewWidth, Mathf.Max(rect.height, contentHeight));
            Widgets.BeginScrollView(rect, ref scroll, view);
            int index = bottoms.BinarySearch(scroll.y);
            index = index < 0 ? ~index : index;
            for (int i = index; i < entries.Count; i++)
            {
                float y = i == 0 ? 0f : bottoms[i - 1];
                if (y > scroll.y + rect.height) break;
                AdvisorEntry entry = entries[i];
                bool isExpanded = expanded.Contains(entry);
                Rect row = new Rect(0f, y, viewWidth, heights[entry]);
                Widgets.DrawBoxSolid(row, entry.role == "user" ? new Color(0.10f, 0.22f, 0.27f, 0.8f) : new Color(0.10f, 0.11f, 0.13f, 0.85f));
                Rect inner = row.ContractedBy(10f);
                if (tools)
                {
                    float titleHeight = textHeights[entry];
                    Widgets.Label(new Rect(inner.x + 22f, inner.y, inner.width - 22f, titleHeight), entry.text);
                    Widgets.Label(new Rect(inner.x, inner.y, 20f, lineHeight), isExpanded ? "−" : "+");
                    if (Widgets.ButtonInvisible(new Rect(inner.x, inner.y, inner.width, titleHeight)))
                    {
                        if (!expanded.Remove(entry)) expanded.Add(entry);
                        heights.Remove(entry); last = null;
                    }
                    if (isExpanded) Widgets.Label(new Rect(inner.x, inner.y + titleHeight + 6f, inner.width, detailHeights[entry]), entry.detail);
                }
                else
                {
                    GUI.color = new Color(0.65f, 0.77f, 0.84f);
                    Widgets.Label(new Rect(inner.x, inner.y, inner.width, lineHeight), SimTranslation.T("RSMF.Business.Advisor.Role." + entry.role));
                    GUI.color = Color.white;
                    float textHeight = textHeights[entry];
                    Widgets.Label(new Rect(inner.x, inner.y + lineHeight + 4f, inner.width, textHeight), entry.text);
                    if (!string.IsNullOrEmpty(entry.detail))
                    {
                        Rect detailButton = new Rect(inner.x, inner.y + lineHeight + textHeight + 8f, inner.width, lineHeight + 8f);
                        string label = SimTranslation.T("RSMF.Business.Advisor.ErrorDetails");
                        if (SimUiStyle.DrawSecondaryButton(detailButton, (isExpanded ? "− " : "+ ") + label))
                        {
                            if (!expanded.Remove(entry)) expanded.Add(entry);
                            heights.Remove(entry); last = null;
                        }
                        if (isExpanded) Widgets.Label(new Rect(inner.x, detailButton.yMax + 6f, inner.width, detailHeights[entry]), entry.detail);
                    }
                }
            }
            Widgets.EndScrollView();
        }

        //仅在消息新增、展开状态或排版宽度变化时调用。
        private float Measure(AdvisorEntry entry, bool tools)
        {
            float inner = Mathf.Max(40f, width - 20f);
            textHeights[entry] = Text.CalcHeight(entry.text, tools ? inner - 24f : inner);
            detailHeights[entry] = expanded.Contains(entry) ? Text.CalcHeight(entry.detail, inner) : 0f;
            if (tools) return 20f + textHeights[entry] + (expanded.Contains(entry) ? 6f + detailHeights[entry] : 0f);
            return 24f + lineHeight + textHeights[entry]
                + (string.IsNullOrEmpty(entry.detail) ? 0f : lineHeight + 12f
                    + (expanded.Contains(entry) ? 6f + detailHeights[entry] : 0f));
        }
    }
}
