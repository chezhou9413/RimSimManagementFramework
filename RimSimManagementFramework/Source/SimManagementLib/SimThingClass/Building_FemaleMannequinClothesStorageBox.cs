using RimWorld;
using UnityEngine;

namespace SimManagementLib.SimThingClass
{
    //女性体型服装假人，职责是使用原版 Female 体型校验并展示衣服。
    public sealed class Building_FemaleMannequinClothesStorageBox : Building_MannequinClothesStorageBox
    {
        protected override BodyTypeDef MannequinBodyType => BodyTypeDefOf.Female;
        protected override float BodyDisplayScale => 1.4f;
        protected override Vector2 BodyDisplayOffset => new Vector2(0f, 0.05f);
        protected override float HeadgearDisplayScale => 1.4f;
        protected override Vector2 HeadgearDisplayOffset => Vector2.zero;
    }
}
