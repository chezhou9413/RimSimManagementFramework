using System.Collections.Generic;
using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.SimThingComp
{
    /// <summary>
    /// 定义收藏品展台单个槽位的默认展示参数，职责是从 XML 控制初始位置和显示变换。
    /// </summary>
    public class CollectibleDisplaySlotDefault
    {
        public int index = -1;
        public float offsetX;
        public float offsetZ;
        public float height = 0.08f;
        public float scale = 1f;
        public float rotation;
    }

    /// <summary>
    /// 定义收藏品展台的槽位网格参数，职责是让 XML 控制展台行列数量。
    /// </summary>
    public class CompProperties_CollectibleDisplayStand : CompProperties
    {
        public int rows = 2;
        public int columns = 3;
        public float defaultHeight = 0.08f;
        public float defaultScale = 1f;
        public float defaultRotation;
        public List<CollectibleDisplaySlotDefault> slotDefaults = new List<CollectibleDisplaySlotDefault>();

        /// <summary>
        /// 初始化收藏品展台配置组件类型，供 Def 加载时绑定运行时组件。
        /// </summary>
        public CompProperties_CollectibleDisplayStand()
        {
            compClass = typeof(ThingComp_CollectibleDisplayStand);
        }
    }

    /// <summary>
    /// 挂在收藏品展台上的轻量配置组件，职责是暴露经过清理后的行列数量。
    /// </summary>
    public class ThingComp_CollectibleDisplayStand : ThingComp
    {
        private CompProperties_CollectibleDisplayStand StandProps => props as CompProperties_CollectibleDisplayStand;

        /// <summary>
        /// 返回展台槽位行数，并限制到安全范围。
        /// </summary>
        public int Rows => System.Math.Max(1, StandProps?.rows ?? 2);

        /// <summary>
        /// 返回展台槽位列数，并限制到安全范围。
        /// </summary>
        public int Columns => System.Math.Max(1, StandProps?.columns ?? 3);

        /// <summary>
        /// 把 XML 配置的默认展示参数应用到槽位，缺省时回退到均匀网格位置。
        /// </summary>
        public void ApplyDefaultTransform(CollectibleDisplaySlotData slot)
        {
            if (slot == null)
                return;

            CollectibleDisplaySlotDefault configured = FindSlotDefault(slot.index);
            if (configured != null)
            {
                slot.SetDisplayTransform(configured.offsetX, configured.offsetZ, configured.height, configured.scale, configured.rotation);
                return;
            }

            int row = slot.index / Columns;
            int column = slot.index % Columns;
            float offsetX = column - (Columns - 1) * 0.5f;
            float offsetZ = row - (Rows - 1) * 0.5f;
            slot.SetDisplayTransform(offsetX, offsetZ, StandProps?.defaultHeight ?? 0.08f, StandProps?.defaultScale ?? 1f, StandProps?.defaultRotation ?? 0f);
        }

        /// <summary>
        /// 查找指定槽位的 XML 默认参数。
        /// </summary>
        private CollectibleDisplaySlotDefault FindSlotDefault(int index)
        {
            List<CollectibleDisplaySlotDefault> defaults = StandProps?.slotDefaults;
            if (defaults == null)
                return null;

            for (int i = 0; i < defaults.Count; i++)
            {
                if (defaults[i] != null && defaults[i].index == index)
                    return defaults[i];
            }
            return null;
        }
    }
}
