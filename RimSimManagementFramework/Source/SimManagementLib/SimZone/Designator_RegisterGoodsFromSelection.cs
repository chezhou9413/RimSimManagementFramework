using SimManagementLib.SimDialog;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimZone
{
    //从建筑菜单打开商品列表，再将选中的商品注册到全局分类。
    public class Designator_RegisterGoodsFromSelection : Designator
    {
        //设置建筑菜单中的快捷注册入口。
        public Designator_RegisterGoodsFromSelection()
        {
            defaultLabel = SimTranslation.T("RSMF.QuickGoodsRegister.DesignatorLabel");
            defaultDesc = SimTranslation.T("RSMF.QuickGoodsRegister.DesignatorDesc");
            icon = ContentFinder<Texture2D>.Get("Things/Icon/Mod_Icon", true);
        }

        //选中指令后直接打开窗口，由原版窗口结束地图指定状态。
        public override void Selected()
        {
            Find.WindowStack.Add(new Dialog_QuickGoodsRegistration());
        }

        //商品从已加载定义中选择，无需在地图上指定格子。
        public override AcceptanceReport CanDesignateCell(IntVec3 loc) => false;
    }
}
