using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SimManagementLib.Pojo
{
    [DataContract]
    public sealed class CustomGoodsDatabaseData
    {
        [DataMember]
        public int version = 1;

        [DataMember]
        public List<CustomGoodsCategoryRecord> categories = new List<CustomGoodsCategoryRecord>();
    }

    [DataContract]
    public sealed class CustomGoodsCategoryRecord
    {
        [DataMember]
        public string categoryId = "";

        [DataMember]
        public string label = "";

        [DataMember]
        public bool builtInCategory;

        [DataMember]
        public List<string> itemDefNames = new List<string>();
    }
}
