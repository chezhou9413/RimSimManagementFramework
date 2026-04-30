using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存玩家注册的运行时顾客类型，加载时会在 CustomerKindDef 之后合并。
    /// </summary>
    [DataContract]
    public sealed class CustomCustomerDatabaseData
    {
        [DataMember]
        public int version = 1;

        [DataMember]
        public List<CustomCustomerKindRecord> kinds = new List<CustomCustomerKindRecord>();
    }

    /// <summary>
    /// 描述一个由游戏内顾客注册面板创建的运行时顾客类型。
    /// </summary>
    [DataContract]
    public sealed class CustomCustomerKindRecord
    {
        [DataMember]
        public string kindId = "";

        [DataMember]
        public string label = "";

        [DataMember]
        public List<string> pawnKindDefNames = new List<string>();

        [DataMember]
        public float baseMtbDays = 0.25f;

        [DataMember]
        public int budgetMin = 100;

        [DataMember]
        public int budgetMax = 400;

        [DataMember]
        public int queuePatienceMin = 900;

        [DataMember]
        public int queuePatienceMax = 3000;

        [DataMember]
        public float activeHourMin = 0f;

        [DataMember]
        public float activeHourMax = 24f;

        [DataMember]
        public List<string> allowedWeatherDefNames = new List<string>();

        [DataMember]
        public float minShopReputation;

        [DataMember]
        public List<string> targetGoodsCategoryIds = new List<string>();

        [DataMember]
        public List<CustomCustomerPreferenceRecord> itemPreferences = new List<CustomCustomerPreferenceRecord>();

        [DataMember]
        public List<CustomCustomerProfileRecord> spawnProfiles = new List<CustomCustomerProfileRecord>();
    }

    /// <summary>
    /// 保存一条面向 ThingDef 或商品类型的运行时顾客偏好。
    /// </summary>
    [DataContract]
    public sealed class CustomCustomerPreferenceRecord
    {
        [DataMember]
        public string preferredGoodsCategoryId = "";

        [DataMember]
        public string preferredThingDefName = "";

        [DataMember]
        public string tag = "";

        [DataMember]
        public float weight = 1f;
    }

    /// <summary>
    /// 保存一个包含预算、耐心、天气和偏好覆盖项的运行时顾客档案。
    /// </summary>
    [DataContract]
    public sealed class CustomCustomerProfileRecord
    {
        [DataMember]
        public string label = "";

        [DataMember]
        public float weight = 1f;

        [DataMember]
        public int budgetMin = 100;

        [DataMember]
        public int budgetMax = 400;

        [DataMember]
        public int queuePatienceMin = 900;

        [DataMember]
        public int queuePatienceMax = 3000;

        [DataMember]
        public float activeHourMin = 0f;

        [DataMember]
        public float activeHourMax = 24f;

        [DataMember]
        public List<string> allowedWeatherDefNames = new List<string>();

        [DataMember]
        public List<string> preferredThingDefNames = new List<string>();

        [DataMember]
        public List<string> preferredGoodsCategoryIds = new List<string>();
    }
}
