namespace SimManagementLib.Alerts
{
    //单独提示商店存在室外区域。
    public sealed class Alert_ShopOutdoor : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "Outdoor";
    }
}
