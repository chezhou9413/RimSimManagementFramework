using Verse;

namespace SimManagementLib.BusinessAdvisor
{
    //保存聊天和可展开的查询记录；执行授权只存在于当前运行会话。
    public sealed class AdvisorEntry : IExposable
    {
        public string role = "";
        public string text = "";
        public string detail = "";
        public int tick;

        //写入当前存档的聊天和审计记录。
        public void ExposeData()
        {
            Scribe_Values.Look(ref role, "role", "");
            Scribe_Values.Look(ref text, "text", "");
            Scribe_Values.Look(ref detail, "detail", "");
            Scribe_Values.Look(ref tick, "tick");
        }
    }
}
