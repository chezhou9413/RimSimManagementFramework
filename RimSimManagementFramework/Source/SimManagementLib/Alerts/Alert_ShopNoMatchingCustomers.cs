namespace SimManagementLib.Alerts
{
    //单独提示商品或服务不匹配客群。
    public sealed class Alert_ShopNoMatchingCustomers : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "NoMatchingCustomers";
    }
}
