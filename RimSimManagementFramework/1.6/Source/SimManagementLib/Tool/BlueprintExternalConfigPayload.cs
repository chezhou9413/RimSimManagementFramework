using System;
using System.IO;
using System.Runtime.Serialization.Json;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把桥接器私有配置对象转换为蓝图可保存的 Base64 JSON payload。
    /// </summary>
    public static class BlueprintExternalConfigPayload
    {
        /// <summary>
        /// 将桥接器私有配置对象序列化为 Base64 JSON。
        /// </summary>
        public static string Serialize<T>(T data)
        {
            if (data == null)
                return "";

            try
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                using (MemoryStream stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, data);
                    return Convert.ToBase64String(stream.ToArray());
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 蓝图外部配置序列化失败：" + ex.Message);
                return "";
            }
        }

        /// <summary>
        /// 将 Base64 JSON 还原为桥接器私有配置对象。
        /// </summary>
        public static T Deserialize<T>(string payload) where T : class
        {
            if (string.IsNullOrEmpty(payload))
                return null;

            try
            {
                byte[] bytes = Convert.FromBase64String(payload);
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                using (MemoryStream stream = new MemoryStream(bytes))
                {
                    return serializer.ReadObject(stream) as T;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SimManagementLib] 蓝图外部配置反序列化失败：" + ex.Message);
                return null;
            }
        }
    }
}
