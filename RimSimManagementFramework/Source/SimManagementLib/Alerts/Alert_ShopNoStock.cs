namespace SimManagementLib.Alerts
{
    //单独提示可售商品缺货。
    public sealed class Alert_ShopNoStock : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "NoStock";
    }
}
