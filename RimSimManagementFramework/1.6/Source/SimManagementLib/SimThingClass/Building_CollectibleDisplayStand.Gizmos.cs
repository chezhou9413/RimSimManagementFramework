using System.Collections.Generic;
using RimWorld;
using SimManagementLib.SimDialog;
using SimManagementLib.Tool;
using Verse;

namespace SimManagementLib.SimThingClass
{
    /// <summary>
    /// 收藏品展台操作按钮部分，职责是提供普通管理入口和上帝模式调试入口。
    /// </summary>
    public partial class Building_CollectibleDisplayStand
    {
        /// <summary>
        /// 提供打开收藏品展台管理面板和调试面板的 Gizmo。
        /// </summary>
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            yield return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.CollectibleDisplayStand.Gizmo.Label"),
                defaultDesc = SimTranslation.T("RSMF.CollectibleDisplayStand.Gizmo.Desc"),
                icon = TexButton.Info,
                action = delegate
                {
                    Find.WindowStack.Add(new Dialog_CollectibleDisplayStand(this));
                }
            };

            if (Prefs.DevMode && DebugSettings.godMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = SimTranslation.T("RSMF.CollectibleDisplayStand.DebugGizmo.Label"),
                    defaultDesc = SimTranslation.T("RSMF.CollectibleDisplayStand.DebugGizmo.Desc"),
                    icon = TexButton.Copy,
                    action = delegate
                    {
                        Find.WindowStack.Add(new Dialog_CollectibleDisplayStandDebug(this));
                    }
                };
            }
        }
    }
}
