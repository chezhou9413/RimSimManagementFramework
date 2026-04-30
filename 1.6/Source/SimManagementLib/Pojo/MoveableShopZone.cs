using System.Collections.Generic;
using Verse;
using UnityEngine;

namespace SimManagementLib.Pojo
{
    public class MoveableShopZone : IExposable
    {
        public int zoneId = -1;
        public string label;
        public Color color = Color.white;
        public List<IntVec3> cells = new List<IntVec3>();
        public List<ShopRoleAssignment> roleAssignments = new List<ShopRoleAssignment>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref zoneId, "zoneId", -1);
            Scribe_Values.Look(ref label, "label");
            Scribe_Values.Look(ref color, "color", Color.white);
            Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
            Scribe_Collections.Look(ref roleAssignments, "roleAssignments", LookMode.Deep);

            if (cells == null) cells = new List<IntVec3>();
            if (roleAssignments == null) roleAssignments = new List<ShopRoleAssignment>();
        }
    }
}
