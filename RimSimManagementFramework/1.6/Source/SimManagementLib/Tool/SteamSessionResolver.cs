using System;
using System.Reflection;
using SimManagementLib.Pojo;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责解析当前本地 Steam 会话，供网络蓝图入口做前置校验。
    /// </summary>
    public static class SteamSessionResolver
    {
        private static Type cachedSteamUserType;
        private static MethodInfo cachedGetSteamIdMethod;

        /// <summary>
        /// 尝试读取当前 Steam 登录状态、昵称和 SteamId。
        /// </summary>
        public static SteamSessionInfo TryGetCurrentSession()
        {
            SteamSessionInfo result = new SteamSessionInfo();

            try
            {
                if (!Verse.Steam.SteamManager.Initialized)
                {
                    result.ErrorMessage = SimTranslation.T("RSMF.Blueprint.Network.Error.SteamNotLoggedIn");
                    return result;
                }

                string personaName = Verse.SteamUtility.SteamPersonaName;
                string steamId = ResolveSteamId();
                if (string.IsNullOrWhiteSpace(steamId))
                {
                    result.ErrorMessage = SimTranslation.T("RSMF.Blueprint.Network.Error.SteamIdUnavailable");
                    return result;
                }

                result.IsAvailable = true;
                result.PersonaName = string.IsNullOrWhiteSpace(personaName) ? steamId : personaName;
                result.SteamId = steamId;
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = SimTranslation.T("RSMF.Blueprint.Network.Error.SteamSessionFailed", ex.Message.Named("message"));
                return result;
            }
        }

        /// <summary>
        /// 通过反射读取 Steamworks 当前账号 ID，避免新增显式程序集引用。
        /// </summary>
        private static string ResolveSteamId()
        {
            if (cachedSteamUserType == null)
            {
                cachedSteamUserType = HarmonyLib.AccessTools.TypeByName("Steamworks.SteamUser");
                cachedGetSteamIdMethod = cachedSteamUserType?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static);
            }

            object value = cachedGetSteamIdMethod?.Invoke(null, null);
            return value?.ToString() ?? "";
        }
    }
}
