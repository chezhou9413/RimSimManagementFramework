namespace SimManagementLib.Alerts
{
    //单独提示没有货柜或服务设施的商店。
    public sealed class Alert_ShopMissingSalesFacility : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "MissingSalesFacility";
    }
}
