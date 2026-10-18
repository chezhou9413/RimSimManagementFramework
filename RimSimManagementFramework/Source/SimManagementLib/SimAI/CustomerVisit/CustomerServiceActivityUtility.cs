using Verse;

namespace SimManagementLib.SimAI.CustomerVisit
{
    //读取实际服务工作的运行状态，职责是区分移动、服务读条与已经结束的订单。
    internal static class CustomerServiceActivityUtility
    {
        //判断顾客是否持有正在执行的内置服务订单，防止普通浏览逻辑提前结账。
        internal static bool HasActiveService(Pawn pawn)
        {
            if (pawn?.jobs?.curDriver is JobDriver_SelectPaidService selecting) return selecting.HasActiveService;
            if (pawn?.jobs?.curDriver is JobDriver_UsePaidService usingService) return usingService.HasActiveService;
            return false;
        }

        //读取正在推进的服务剩余工时，移动阶段返回负值并沿用位置停滞检测。
        internal static int RemainingServiceTicks(Pawn pawn)
        {
            if (pawn?.jobs?.curDriver is JobDriver_SelectPaidService selecting && selecting.IsUsingService)
                return selecting.ticksLeftThisToil;
            if (pawn?.jobs?.curDriver is JobDriver_UsePaidService usingService && usingService.IsUsingService)
                return usingService.ticksLeftThisToil;
            return -1;
        }
    }
}
