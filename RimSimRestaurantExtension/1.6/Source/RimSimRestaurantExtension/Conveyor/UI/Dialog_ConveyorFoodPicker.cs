using System;
using System.Collections.Generic;
using System.Linq;
using RimSimRestaurantExtension.Tool;
using RimSimRestaurantExtension.UI;
using SimManagementLib.Api;
using UnityEngine;
using Verse;

namespace RimSimRestaurantExtension.Conveyor.UI
{
    //提供现成食品搜索窗口，职责是从给定货源支持的食物中选择上架对象。
    public sealed class Dialog_ConveyorFoodPicker : Window
    {
        private readonly List<ThingDef> foods;
        private readonly Action<ThingDef> selected;
        private string search = "";
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(Mathf.Min(720f, Verse.UI.screenWidth - 48f), Mathf.Min(680f, Verse.UI.screenHeight - 48f));

        //限制候选为人类可直接食用的营养食品，职责是排除药物、尸体和不可食用原料。
        public Dialog_ConveyorFoodPicker(IEnumerable<ThingDef> source, Action<ThingDef> selected)
        {
            foods = source.Where(f => f.ingestible?.HumanEdible == true && f.IsNutritionGivingIngestible
                && !f.IsCorpse && !f.IsDrug).OrderBy(f => f.label).ToList();
            this.selected = selected;
            doCloseX = true; closeOnAccept = false; absorbInputAroundWindow = true;
        }

        //绘制搜索列表并提交所选定义，职责是保持长中文标签可读。
        public override void DoWindowContents(Rect rect)
        {
            using (new RestaurantGuiScope())
            {
                float h = RestaurantUiStyle.ControlHeight();
                float y = ShopUiVisualUtility.DrawPageHeading(rect, "选择上架食品", "选择现有货源支持的食品，厨师将搬运实物补餐。", true);
                string next = ShopUiVisualUtility.DrawSearchField(new Rect(0, y, rect.width, h), search, "搜索食品名称或来源模组");
                if (next != search) { search = next; scroll = Vector2.zero; }
                y += h + 8f;
                var shown = foods.Where(f => RestaurantFoodUtility.MatchesSearch(f, search)).ToList();
                float line = RestaurantUiStyle.LineHeight(GameFont.Small);
                ShopUiVisualUtility.DrawCellLabel(new Rect(0, y, rect.width, line), "可选食品 " + shown.Count + " 项", RestaurantUiStyle.MutedText);
                y += line + 8f;
                var outer = new Rect(0, y, rect.width, Mathf.Max(0f, rect.height - y - h - 12f));
                ShopUiVisualUtility.DrawSection(outer);
                if (shown.Count == 0) RestaurantBusinessUiUtility.DrawEmpty(outer, "没有匹配食品，请调整搜索或选择其他货源。");
                else
                {
                    float row = RestaurantFoodRow.Height;
                    var view = new Rect(0, 0, rect.width - 16f, Mathf.Max(outer.height, shown.Count * row));
                    Widgets.BeginScrollView(outer, ref scroll, view);
                    try
                    {
                        for (int i = 0; i < shown.Count; i++)
                            if (RestaurantFoodRow.Draw(new Rect(0, i * row, view.width, row), shown[i], i))
                            { selected(shown[i]); Close(); }
                    }
                    finally { Widgets.EndScrollView(); }
                }
                if (RestaurantUiStyle.DrawSecondaryButton(new Rect(rect.width - 110f, rect.height - h, 110f, h), "取消")) Close();
            }
        }
    }
}
