namespace SimManagementLib.Alerts
{
    //单独提示收银台无人值守。
    public sealed class Alert_ShopNoCashier : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "Cashier";
    }
}
