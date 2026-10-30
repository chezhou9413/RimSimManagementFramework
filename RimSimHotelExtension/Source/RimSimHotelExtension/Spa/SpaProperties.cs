using Verse;

namespace RimSimHotelExtension.Spa
{
    //指定温泉档次与三层贴图目录，经营数值统一读取旅馆调参 Def。
    public sealed class SpaProperties : DefModExtension
    {
        public bool stone;
        public string textureRoot;
    }
}
