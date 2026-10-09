using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimSimRestaurantExtension.Models
{
    //保存可跨餐厅分享的设置，不包含地图、员工、建筑或订单引用。
    [DataContract]
    internal sealed class RestaurantSettingsTransferPackage
    {
        [DataMember(IsRequired = true)] public string format = "RimSimRestaurantSettings";
        [DataMember(IsRequired = true)] public int version = 1;
        [DataMember(IsRequired = true)] public bool enabled;
        [DataMember(IsRequired = true)] public float priceMultiplier;
        [DataMember(IsRequired = true)] public int maxWaitTicks;
        [DataMember(IsRequired = true)] public int maxServiceWaitTicks;
        [DataMember(IsRequired = true)] public int maxOrderRounds;
        [DataMember(IsRequired = true)] public int reorderIntervalTicks;
        [DataMember(IsRequired = true)] public bool useCustomerPreferences;
        [DataMember(IsRequired = true)] public float preferenceStrength;
        [DataMember(IsRequired = true)] public List<Dish> dishes = new List<Dish>();

        //使用物品定义名称表示一道菜及其每份原料。
        [DataContract]
        internal sealed class Dish
        {
            [DataMember] public string label;
            [DataMember(IsRequired = true)] public string meal;
            [DataMember(IsRequired = true)] public bool enabled;
            [DataMember(IsRequired = true)] public float price;
            [DataMember(IsRequired = true)] public int minimum;
            [DataMember(IsRequired = true)] public int maximum;
            [DataMember(IsRequired = true)] public Dictionary<string, int> ingredients;
        }
    }
}
