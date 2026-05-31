using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimZone
{
    /// <summary>
    /// 提供框选物品快捷注册为可售商品的指令，负责打开商品分类选择弹窗。
    /// </summary>
    public class Designator_RegisterGoodsFromSelection : Designator
    {
        /// <summary>
        /// 初始化快捷注册指令的显示文本、图标和拖框样式。
        /// </summary>
        public Designator_RegisterGoodsFromSelection()
        {
            defaultLabel = SimTranslation.T("RSMF.QuickGoodsRegister.DesignatorLabel");
            defaultDesc = SimTranslation.T("RSMF.QuickGoodsRegister.DesignatorDesc");
            icon = ContentFinder<Texture2D>.Get("Things/Icon/Mod_Icon", true);
            soundSucceeded = SoundDefOf.Designate_Claim;
            useMouseIcon = true;
        }

        /// <summary>
        /// 返回填充矩形样式，负责让玩家看清本次框选范围。
        /// </summary>
        public override DrawStyleCategoryDef DrawStyleCategory => DrawStyleCategoryDefOf.FilledRectangle;

        /// <summary>
        /// 判断格子内是否存在可注册商品。
        /// </summary>
        public override AcceptanceReport CanDesignateCell(IntVec3 loc)
        {
            if (!loc.InBounds(Map)) return false;
            if (loc.Fogged(Map)) return false;
            return loc.GetThingList(Map).Any(thing => CanDesignateThing(thing).Accepted);
        }

        /// <summary>
        /// 判断单个物品是否能注册为商品。
        /// </summary>
        public override AcceptanceReport CanDesignateThing(Thing t)
        {
            if (CustomGoodsDatabase.IsValidCandidateThing(t?.def))
                return true;
            return AcceptanceReport.WasRejected;
        }

        /// <summary>
        /// 单格指定时不立即写入，统一走多格收集流程。
        /// </summary>
        public override void DesignateSingleCell(IntVec3 c)
        {
        }

        /// <summary>
        /// 收集框选范围内的有效商品并打开分类选择窗口。
        /// </summary>
        public override void DesignateMultiCell(IEnumerable<IntVec3> cells)
        {
            List<ThingDef> thingDefs = QuickGoodsRegistrationUtility.CollectCandidateThingDefs(Map, cells);
            if (thingDefs.NullOrEmpty())
            {
                Finalize(false);
                Messages.Message(SimTranslation.T("RSMF.QuickGoodsRegister.NoValidItems"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Dialog_RegisterSelectedGoodsCategory(thingDefs));
            Finalize(true);
        }
    }
}
