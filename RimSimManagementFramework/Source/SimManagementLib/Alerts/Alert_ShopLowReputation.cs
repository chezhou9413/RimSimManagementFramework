namespace SimManagementLib.Alerts
{
    //单独提示商店声望未达到客群要求。
    public sealed class Alert_ShopLowReputation : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "Reputation";
    }
}
