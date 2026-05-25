using SimManagementLib.Pojo;
using System.Collections.Generic;
using Verse;

namespace SimManagementLib.SimAI
{
    /// <summary>
    /// 保存每个顾客的运行时设置，负责把预算、耐心和偏好配置从 LordJob 中拆出。
    /// </summary>
    public class CustomerPawnSettingsState
    {
        public Dictionary<int, CustomerRuntimeSettings> pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();

        private List<int> tmpSettingKeys;
        private List<CustomerRuntimeSettings> tmpSettingValues;

        /// <summary>
        /// 读写顾客运行设置存档数据，并在读档后补齐集合实例。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Collections.Look(ref pawnSettings, "pawnSettings", LookMode.Value, LookMode.Deep, ref tmpSettingKeys, ref tmpSettingValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && pawnSettings == null)
                pawnSettings = new Dictionary<int, CustomerRuntimeSettings>();
        }

        /// <summary>
        /// 设置指定顾客的运行时配置。
        /// </summary>
        public void SetPawnSettings(int pawnId, CustomerRuntimeSettings settings)
        {
            if (pawnId <= 0 || settings == null) return;
            pawnSettings[pawnId] = settings;
        }

        /// <summary>
        /// 获取指定顾客的运行时配置。
        /// </summary>
        public CustomerRuntimeSettings GetPawnSettings(int pawnId)
        {
            return pawnSettings.TryGetValue(pawnId, out CustomerRuntimeSettings settings) ? settings : null;
        }
    }
}
