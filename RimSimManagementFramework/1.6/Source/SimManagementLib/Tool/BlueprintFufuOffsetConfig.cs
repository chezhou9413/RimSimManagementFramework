using System.Runtime.Serialization;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责保存 Fufu 建筑实例上的缩放、偏移和绘制层级。
    /// </summary>
    [DataContract]
    internal sealed class FufuOffsetConfig
    {
        [DataMember] public float sizeFixed = 1f;
        [DataMember] public int sizeFixedFake = 10;
        [DataMember] public int offsetFixed;
        [DataMember] public int offsetXFixed;
        [DataMember] public string layer = AltitudeLayer.BuildingOnTop.ToString();
    }
}
