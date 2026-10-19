namespace SimManagementLib.Alerts
{
    //单独提示扩展服务受阻，避免与商店基础设施问题合并。
    public sealed class Alert_ShopServiceBlocked : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "Service";
    }
}
