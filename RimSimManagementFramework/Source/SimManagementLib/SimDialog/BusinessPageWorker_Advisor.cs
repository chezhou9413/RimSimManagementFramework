using SimManagementLib.Api;
using UnityEngine;

namespace SimManagementLib.SimDialog
{
    //在统一设置开启顾问后显示独立的经商管理页面。
    public sealed class BusinessPageWorker_Advisor : BusinessManagerPageWorker
    {
        //连接未配置时仍可进入页面查看配置提示。
        public override bool CanShow(ShopUiContext context) => base.CanShow(context)
            && SimManagementLibMod.Settings.llmEnabled && SimManagementLibMod.Settings.advisorEnabled;

        //复用窗口内的聊天和操作面板。
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context) => context.Window.DrawBusinessAdvisor(rect);
    }
}
