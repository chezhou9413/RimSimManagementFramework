using RimWorld;
using SimManagementLib.Tool;
using Verse;
using Verse.AI.Group;

namespace SimManagementLib.SimAI
{
    //类职责：为普通商店和自动售货机顾客提供统一、安全的玩家手动驱离流程。
    internal static class CustomerDismissalUtility
    {
        //创建顾客驱离命令，职责是让 Pawn Gizmo 复用统一确认和收尾逻辑。
        public static Command_Action CreateDismissCommand(Pawn pawn)
        {
            return new Command_Action
            {
                defaultLabel = SimTranslation.T("RSMF.CustomerDismiss.Label"),
                defaultDesc = SimTranslation.T("RSMF.CustomerDismiss.Desc"),
                icon = TexCommand.ClearPrioritizedWork,
                action = () => RequestDismiss(pawn)
            };
        }

        //请求驱离指定顾客，职责是在执行前显示会取消未完成交易的确认提示。
        public static void RequestDismiss(Pawn pawn)
        {
            if (!IsActiveCustomer(pawn))
            {
                Messages.Message(SimTranslation.T("RSMF.CustomerDismiss.Unavailable"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            string confirmation = SimTranslation.T(
                "RSMF.CustomerDismiss.Confirm",
                pawn.LabelShortCap.Named("customer"));
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(confirmation, () => DismissNow(pawn)));
        }

        //执行顾客驱离，职责是先退回未付款商品并清理经营状态，再交给原版离图 Lord。
        private static void DismissNow(Pawn pawn)
        {
            if (!IsActiveCustomer(pawn))
            {
                Messages.Message(SimTranslation.T("RSMF.CustomerDismiss.Unavailable"), MessageTypeDefOf.RejectInput, false);
                return;
            }

            string customerLabel = pawn.LabelShortCap;
            LordJob lordJob = pawn.Map.lordManager.LordOf(pawn)?.LordJob;
            if (lordJob is LordJob_CustomerVisit visit)
                visit.CleanupUnpaidCustomerStateForSession(pawn, "玩家手动驱离顾客");

            CustomerExitUtility.BeginEmergencyExit(pawn, "玩家手动驱离顾客");
            Messages.Message(
                SimTranslation.T("RSMF.CustomerDismiss.Success", customerLabel.Named("customer")),
                MessageTypeDefOf.TaskCompletion,
                false);
        }

        //判断 Pawn 是否仍是可驱离顾客，职责是阻止确认窗口延迟期间操作已经离图的对象。
        private static bool IsActiveCustomer(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned || pawn.Map == null)
                return false;
            LordJob lordJob = pawn.Map.lordManager?.LordOf(pawn)?.LordJob;
            return lordJob is LordJob_CustomerVisit || lordJob is LordJob_VendingMachineVisit;
        }
    }
}
