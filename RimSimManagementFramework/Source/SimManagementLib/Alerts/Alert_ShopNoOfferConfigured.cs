namespace SimManagementLib.Alerts
{
    //单独提示尚未设置营业内容。
    public sealed class Alert_ShopNoOfferConfigured : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "NoOfferConfigured";
    }
}
