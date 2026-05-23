using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存收藏品展台单个槽位的持久化数据，职责是记录待搬运目标、展示参数和真实收藏品实例。
    /// </summary>
    public class CollectibleDisplaySlotData : IExposable, IThingHolder
    {
        public int index;
        public int pendingSourceThingId = -1;
        public string pendingSourceLabel = "";
        public float offsetX;
        public float offsetZ;
        public float height = 0.08f;
        public float scale = 1f;
        public float rotation;

        private ThingOwner<Thing> contents;
        private IThingHolder parentHolder;

        public IThingHolder ParentHolder => parentHolder;

        /// <summary>
        /// 创建空槽位数据，供存档系统反射构造。
        /// </summary>
        public CollectibleDisplaySlotData()
        {
            contents = new ThingOwner<Thing>(this, oneStackOnly: true);
        }

        /// <summary>
        /// 创建指定索引的槽位数据，并按当前网格写入默认展示位置。
        /// </summary>
        public CollectibleDisplaySlotData(int index, int rows, int columns) : this()
        {
            this.index = index;
            ResetDefaultPosition(rows, columns);
        }

        public Thing StoredThing => contents != null && contents.Count > 0 ? contents[0] : null;
        public bool HasStoredThing => StoredThing != null;
        public bool HasPendingSource => pendingSourceThingId >= 0;

        /// <summary>
        /// 绑定运行时父容器，负责让槽位内 ThingOwner 能正确回溯到展台。
        /// </summary>
        public void BindParent(IThingHolder parent)
        {
            parentHolder = parent;
            if (contents == null)
                contents = new ThingOwner<Thing>(this, oneStackOnly: true);
        }

        /// <summary>
        /// 返回槽位直接持有的收藏品容器。
        /// </summary>
        public ThingOwner GetDirectlyHeldThings()
        {
            if (contents == null)
                contents = new ThingOwner<Thing>(this, oneStackOnly: true);
            return contents;
        }

        /// <summary>
        /// 收集槽位内收藏品的子容器，负责让游戏递归保存真实 Thing 数据。
        /// </summary>
        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        /// <summary>
        /// 设置待搬运来源，职责是记录来源 Thing 的运行时 ID 和显示名称。
        /// </summary>
        public void SetPendingSource(Thing source)
        {
            pendingSourceThingId = source?.thingIDNumber ?? -1;
            pendingSourceLabel = source?.LabelCapNoCount ?? "";
        }

        /// <summary>
        /// 清空待搬运来源，职责是让工作扫描不再尝试填充该槽位。
        /// </summary>
        public void ClearPendingSource()
        {
            pendingSourceThingId = -1;
            pendingSourceLabel = "";
        }

        /// <summary>
        /// 尝试把收藏品转入槽位容器，职责是确保每格最多保存一个真实 Thing。
        /// </summary>
        public bool TryStore(Thing thing)
        {
            if (thing == null || HasStoredThing)
                return false;

            bool added = GetDirectlyHeldThings().TryAddOrTransfer(thing, canMergeWithExistingStacks: false);
            if (added)
                ClearPendingSource();
            return added;
        }

        /// <summary>
        /// 从槽位中取出收藏品，职责是把真实 Thing 交给卸载或销毁流程。
        /// </summary>
        public Thing TakeStoredThing()
        {
            Thing stored = StoredThing;
            if (stored == null)
                return null;
            return GetDirectlyHeldThings().Take(stored);
        }

        /// <summary>
        /// 按槽位行列恢复默认展示位置，职责是把收藏品铺到展台 5x5 面内。
        /// </summary>
        public void ResetDefaultPosition(int rows, int columns)
        {
            int safeRows = Mathf.Max(1, rows);
            int safeColumns = Mathf.Max(1, columns);
            int row = index / safeColumns;
            int column = index % safeColumns;
            offsetX = column - (safeColumns - 1) * 0.5f;
            offsetZ = row - (safeRows - 1) * 0.5f;
            height = 0.08f;
            scale = 1f;
            rotation = 0f;
        }

        /// <summary>
        /// 写入槽位展示参数，职责是让 XML 默认值和调试工具共用同一套边界清理。
        /// </summary>
        public void SetDisplayTransform(float offsetX, float offsetZ, float height, float scale, float rotation)
        {
            this.offsetX = Mathf.Clamp(offsetX, -2.5f, 2.5f);
            this.offsetZ = Mathf.Clamp(offsetZ, -2.5f, 2.5f);
            this.height = Mathf.Clamp(height, 0f, 1.5f);
            this.scale = Mathf.Clamp(scale, 0.2f, 3f);
            this.rotation = Mathf.Clamp(rotation, -180f, 180f);
        }

        /// <summary>
        /// 保存或读取槽位数据，负责持久化真实收藏品和展示参数。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref index, "index", 0);
            Scribe_Values.Look(ref pendingSourceThingId, "pendingSourceThingId", -1);
            Scribe_Values.Look(ref pendingSourceLabel, "pendingSourceLabel", "");
            Scribe_Values.Look(ref offsetX, "offsetX", 0f);
            Scribe_Values.Look(ref offsetZ, "offsetZ", 0f);
            Scribe_Values.Look(ref height, "height", 0.08f);
            Scribe_Values.Look(ref scale, "scale", 1f);
            Scribe_Values.Look(ref rotation, "rotation", 0f);
            Scribe_Deep.Look(ref contents, "contents", this);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && contents == null)
                contents = new ThingOwner<Thing>(this, oneStackOnly: true);
        }
    }
}
