using SimManagementLib.Tool;

namespace SimManagementLib.SimAI
{
    public partial class LordJob_CustomerVisit
    {
        public Pojo.RuntimeCustomerKind RuntimeCustomerKind => CustomerCatalog.GetKind(customerKindId);
    }
}
