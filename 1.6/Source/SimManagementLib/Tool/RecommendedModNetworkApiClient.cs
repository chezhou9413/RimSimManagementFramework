using SimManagementLib.Pojo;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责调用服务端推荐 Mod 接口，并返回扩展推荐页可展示的数据。
    /// </summary>
    public static class RecommendedModNetworkApiClient
    {
        private const int TimeoutSeconds = 10;
        private const int RequestPollDelayMs = 100;
        private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings
        {
            UseSimpleDictionaryFormat = true
        };

        /// <summary>
        /// 请求服务端推荐 Mod 列表。
        /// </summary>
        public static Task<List<RecommendedModNetworkItemData>> GetRecommendedModsAsync(CancellationToken token)
        {
            return GetJsonAsync<List<RecommendedModNetworkItemData>>(BuildUrl(), token);
        }

        /// <summary>
        /// 执行 GET 请求并反序列化 JSON，负责清理返回字段。
        /// </summary>
        private static async Task<T> GetJsonAsync<T>(string url, CancellationToken token) where T : class
        {
            string body = await GetTextAsync(url, token);
            T result = DeserializeJson<T>(body);
            if (result is List<RecommendedModNetworkItemData> items)
            {
                for (int i = 0; i < items.Count; i++)
                    items[i]?.Sanitize();
            }

            return result;
        }

        /// <summary>
        /// 执行 UnityWebRequest，负责短超时和无日志失败。
        /// </summary>
        private static async Task<string> GetTextAsync(string url, CancellationToken token)
        {
            url = StringEncodingUtility.SanitizeUtf16(url);
            using (UnityWebRequest request = new UnityWebRequest(url, "GET"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                float elapsedSeconds = 0f;
                while (!operation.isDone)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(RequestPollDelayMs, token);
                    elapsedSeconds += RequestPollDelayMs / 1000f;
                    if (elapsedSeconds > TimeoutSeconds)
                    {
                        request.Abort();
                        throw new TaskCanceledException();
                    }
                }

                bool networkError = request.result == UnityWebRequest.Result.ConnectionError;
                bool httpError = request.result == UnityWebRequest.Result.ProtocolError;
                if (request.responseCode <= 0 || networkError || httpError)
                    throw new IOException("推荐 Mod 服务不可用");

                return StringEncodingUtility.SanitizeUtf16(request.downloadHandler?.text ?? string.Empty);
            }
        }

        /// <summary>
        /// 拼接推荐 Mod 公开 API 地址，负责复用网络蓝图公开根地址。
        /// </summary>
        private static string BuildUrl()
        {
            string rootUrl = BlueprintEndpointCodec.GetBlueprintPublicRootUrl().TrimEnd('/');
            return StringEncodingUtility.SanitizeUtf16(rootUrl + "/api/recommended-mods");
        }

        /// <summary>
        /// 反序列化 JSON，负责使用 UTF-8 字节流读取后端返回。
        /// </summary>
        private static T DeserializeJson<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json))
                return default(T);

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T), JsonSettings);
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(StringEncodingUtility.SanitizeUtf16(json))))
            {
                return serializer.ReadObject(stream) as T;
            }
        }
    }
}
