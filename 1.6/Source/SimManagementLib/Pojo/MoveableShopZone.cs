using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存随重力舰移动的商店区域快照，负责跨地图恢复区划格子和商店配置。
    /// </summary>
    public class MoveableShopZone : IExposable
    {
        public int zoneId = -1;
        public string label;
        public Color color = Color.white;
        public List<IntVec3> cells = new List<IntVec3>();
        public List<ShopRoleAssignment> roleAssignments = new List<ShopRoleAssignment>();
        public ShopScheduleData schedule = new ShopScheduleData();

        /// <summary>
        /// 读写商店区域搬迁快照，负责兼容旧存档中缺失的列表和日程数据。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref color, "color", Color.white);
            Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
            Scribe_Collections.Look(ref roleAssignments, "roleAssignments", LookMode.Deep);
            Scribe_Deep.Look(ref schedule, "schedule");

            if (cells == null) cells = new List<IntVec3>();
            if (roleAssignments == null) roleAssignments = new List<ShopRoleAssignment>();
            if (schedule == null) schedule = new ShopScheduleData();
        }
    }
}
