namespace SimManagementLib.Alerts
{
    //单独提示缺少收银台的商店。
    public sealed class Alert_ShopMissingRegister : Alert_ShopOperatingBlocked
    {
        protected override string IssueCategory => "MissingRegister";
    }
}
