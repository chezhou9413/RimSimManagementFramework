using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责调用网络蓝图后端接口，并返回客户端可直接使用的数据模型。
    /// </summary>
    public static class BlueprintNetworkApiClient
    {
        private static readonly bool EnableDebugLog = false;
        private const int TimeoutSeconds = 30;
        private const int RequestPollDelayMs = 100;
        private static readonly DataContractJsonSerializerSettings JsonSettings = new DataContractJsonSerializerSettings
        {
            UseSimpleDictionaryFormat = true
        };

        /// <summary>
        /// 请求服务状态。
        /// </summary>
        public static Task<BlueprintNetworkStatusData> GetStatusAsync(CancellationToken token)
        {
            return GetJsonAsync<BlueprintNetworkStatusData>(BuildUrl("/status"), token);
        }

        /// <summary>
        /// 请求分页蓝图列表。
        /// </summary>
        public static Task<BlueprintNetworkPagedListData> GetPagedListAsync(BlueprintNetworkSortMode sortMode, int page, int pageSize, string steamId, IEnumerable<string> activePackageIds, CancellationToken token)
        {
            StringBuilder url = new StringBuilder(BuildUrl("/list"));
            url.Append("?sortMode=").Append(GetSortModeValue(sortMode));
            url.Append("&page=").Append(Math.Max(1, page));
            url.Append("&pageSize=").Append(Math.Max(1, pageSize));

            steamId = StringEncodingUtility.SanitizeUtf16(steamId);
            if (sortMode == BlueprintNetworkSortMode.Mine && !string.IsNullOrWhiteSpace(steamId))
                url.Append("&steamId=").Append(StringEncodingUtility.EscapeDataStringSafe(steamId));

            if (sortMode == BlueprintNetworkSortMode.Compatible && activePackageIds != null)
            {
                string joined = string.Join(",", CleanUtf16Items(activePackageIds));
                url.Append("&activePackageIds=").Append(StringEncodingUtility.EscapeDataStringSafe(joined));
            }

            return GetJsonAsync<BlueprintNetworkPagedListData>(url.ToString(), token);
        }

        /// <summary>
        /// 请求蓝图详情。
        /// </summary>
        public static Task<BlueprintNetworkDetailData> GetDetailAsync(string blueprintCode, CancellationToken token)
        {
            return GetJsonAsync<BlueprintNetworkDetailData>(BuildUrl("/" + StringEncodingUtility.EscapeDataStringSafe(blueprintCode)), token);
        }

        /// <summary>
        /// 执行蓝图点赞。
        /// </summary>
        public static async Task<bool> LikeAsync(string blueprintCode, string steamId, CancellationToken token)
        {
            string url = BuildUrl("/" + StringEncodingUtility.EscapeDataStringSafe(blueprintCode) + "/like?steamId=" + StringEncodingUtility.EscapeDataStringSafe(steamId));
            BlueprintNetworkHttpResult response = await SendJsonAsync("POST", url, "", token);
            return response.success;
        }

        /// <summary>
        /// 上传本地蓝图到网络平台。
        /// </summary>
        public static async Task<BlueprintNetworkDetailData> UploadAsync(ShopBlueprintLocalRecord record, string steamId, CancellationToken token)
        {
            if (record?.Data == null)
                return null;

            ShopBlueprintLibrary.EnsureDataDefaults(record.Data);
            ShopBlueprintSignPayloadUtility.EmbedImages(record.Data);
            BlueprintDependencyCollector.PopulateRequiredMods(record.Data);
            if (record.Data.requiredMods == null)
                record.Data.requiredMods = new List<ShopBlueprintRequiredModData>();

            if (!ShopBlueprintLibrary.TryUpdateRecord(record, record.Data, out string updateError))
                throw new InvalidOperationException("上传前同步本地蓝图失败：" + (updateError ?? "未知错误"));

            if (string.IsNullOrWhiteSpace(record.BlueprintPath))
                throw new InvalidOperationException("蓝图文件路径为空，无法上传。");
            if (!File.Exists(record.BlueprintPath))
                throw new FileNotFoundException("未找到要上传的蓝图文件。", record.BlueprintPath);

            WWWForm form = new WWWForm();
            form.AddField("SteamId", StringEncodingUtility.SanitizeUtf16(steamId));
            form.AddField("Name", StringEncodingUtility.SanitizeUtf16(record.Data.label));
            form.AddField("Description", StringEncodingUtility.SanitizeUtf16(record.Data.description));
            form.AddField("RequiredModsJson", SerializeJson(SanitizeRequiredMods(record.Data.requiredMods)));
            form.AddField("ClientBlueprintSourceKind", StringEncodingUtility.SanitizeUtf16(record.Data.remoteBlueprintSourceKind));
            if (BlueprintOwnershipUtility.CanUpdateExisting(record.Data, steamId))
                form.AddField("ExistingBlueprintCode", StringEncodingUtility.SanitizeUtf16(record.Data.remoteBlueprintCode));

            byte[] blueprintBytes = File.ReadAllBytes(record.BlueprintPath);
            form.AddBinaryData(
                "File",
                blueprintBytes,
                StringEncodingUtility.SanitizeUtf16(Path.GetFileName(record.BlueprintPath)),
                "application/json");

            if (!string.IsNullOrWhiteSpace(record.PreviewPath) && File.Exists(record.PreviewPath))
            {
                byte[] previewBytes = File.ReadAllBytes(record.PreviewPath);
                form.AddBinaryData(
                    "PreviewFile",
                    previewBytes,
                    StringEncodingUtility.SanitizeUtf16(Path.GetFileName(record.PreviewPath)),
                    "image/png");
            }

            string url = BuildUrl("");
            BlueprintNetworkHttpResult response = await SendFormAsync("POST", url, form, token);
            EnsureSuccessStatusCode(response, "POST", url);
            string body = response.text;
            LogDebug("上传响应", null, response.StatusCodeText, TrimForLog(body));
            BlueprintUploadResponse upload = DeserializeJson<BlueprintUploadResponse>(body);
            if (upload == null || string.IsNullOrWhiteSpace(upload.blueprintCode))
                throw new InvalidOperationException("服务端未返回蓝图码，无法继续读取详情。");

            try
            {
                BlueprintNetworkDetailData detail = await GetDetailAsync(upload.blueprintCode, token);
                if (detail != null)
                    return detail;
            }
            catch (Exception ex)
            {
                LogDebug("上传后读取详情失败", null, ex.GetType().Name, TrimForLog(SanitizeMessage(ex.Message)));
            }

            return BuildFallbackDetail(record, steamId, upload.blueprintCode);
        }

        /// <summary>
        /// 下载网络蓝图 JSON。
        /// </summary>
        public static async Task<byte[]> DownloadBlueprintAsync(string blueprintCode, CancellationToken token)
        {
            string url = BuildUrl("/" + StringEncodingUtility.EscapeDataStringSafe(blueprintCode) + "/download");
            BlueprintNetworkHttpResult response = await SendAsync("GET", url, token);
            EnsureSuccessStatusCode(response, "GET", url);
            byte[] bytes = response.bytes ?? new byte[0];
            LogDebug("下载蓝图成功", null, response.StatusCodeText, "字节数=" + bytes.Length);
            return bytes;
        }

        /// <summary>
        /// 下载网络蓝图预览图。
        /// </summary>
        public static async Task<byte[]> DownloadPreviewAsync(string blueprintCode, CancellationToken token)
        {
            string url = BuildUrl("/" + StringEncodingUtility.EscapeDataStringSafe(blueprintCode) + "/preview");
            BlueprintNetworkHttpResult response = await SendAsync("GET", url, token);
            EnsureSuccessStatusCode(response, "GET", url);
            byte[] bytes = response.bytes ?? new byte[0];
            LogDebug("下载预览图成功", null, response.StatusCodeText, "字节数=" + bytes.Length);
            return bytes;
        }

        /// <summary>
        /// 删除自己上传的网络蓝图。
        /// </summary>
        public static async Task<bool> DeleteOwnBlueprintAsync(string blueprintCode, string steamId, CancellationToken token)
        {
            string url = BuildUrl("/" + StringEncodingUtility.EscapeDataStringSafe(blueprintCode) + "?steamId=" + StringEncodingUtility.EscapeDataStringSafe(steamId));
            BlueprintNetworkHttpResult response = await SendAsync("DELETE", url, token);
            return response.success;
        }

        /// <summary>
        /// 发送 JSON 请求，负责使用 UnityWebRequest 的 UTF-8 字节上传路径。
        /// </summary>
        private static Task<BlueprintNetworkHttpResult> SendJsonAsync(string method, string url, string body, CancellationToken token)
        {
            url = StringEncodingUtility.SanitizeUtf16(url);
            body = StringEncodingUtility.SanitizeUtf16(body);
            byte[] payload = Encoding.UTF8.GetBytes(body ?? string.Empty);
            UnityWebRequest request = new UnityWebRequest(url, method)
            {
                uploadHandler = new UploadHandlerRaw(payload),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            return SendUnityRequestAsync(request, method, url, token);
        }

        /// <summary>
        /// 发送表单请求，负责上传蓝图 JSON 和预览图的 multipart/form-data。
        /// </summary>
        private static Task<BlueprintNetworkHttpResult> SendFormAsync(string method, string url, WWWForm form, CancellationToken token)
        {
            url = StringEncodingUtility.SanitizeUtf16(url);
            UnityWebRequest request = UnityWebRequest.Post(url, form);
            if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                request.method = method;

            return SendUnityRequestAsync(request, method, url, token);
        }

        /// <summary>
        /// 发送普通请求，负责 GET、DELETE 和二进制下载。
        /// </summary>
        private static Task<BlueprintNetworkHttpResult> SendAsync(string method, string url, CancellationToken token)
        {
            url = StringEncodingUtility.SanitizeUtf16(url);
            UnityWebRequest request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            return SendUnityRequestAsync(request, method, url, token);
        }

        /// <summary>
        /// 驱动 UnityWebRequest 执行，负责统一超时、取消、响应读取和调试日志。
        /// </summary>
        private static async Task<BlueprintNetworkHttpResult> SendUnityRequestAsync(UnityWebRequest request, string method, string url, CancellationToken token)
        {
            LogDebug("发送请求", null, method, null);
            using (request)
            {
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

                UnityWebRequest.Result result = request.result;
                bool networkError = result == UnityWebRequest.Result.ConnectionError;
                bool httpError = result == UnityWebRequest.Result.ProtocolError;
                BlueprintNetworkHttpResult response = new BlueprintNetworkHttpResult
                {
                    statusCode = (int)request.responseCode,
                    text = StringEncodingUtility.SanitizeUtf16(request.downloadHandler?.text ?? string.Empty),
                    bytes = request.downloadHandler?.data,
                    success = request.responseCode > 0 && !networkError && !httpError,
                    error = StringEncodingUtility.SanitizeUtf16(request.error)
                };
                LogDebug("收到响应", null, method, response.StatusCodeText);
                return response;
            }
        }

        private static async Task<T> GetJsonAsync<T>(string url, CancellationToken token) where T : class
        {
            BlueprintNetworkHttpResult response = await SendAsync("GET", url, token);
            EnsureSuccessStatusCode(response, "GET", url);
            string body = response.text;
            LogDebug("读取 JSON 成功", null, response.StatusCodeText, TrimForLog(body));
            return NormalizeUrls(DeserializeJson<T>(body));
        }

        private static string BuildUrl(string path)
        {
            string baseUrl = BlueprintEndpointCodec.GetBlueprintApiBaseUrl().TrimEnd('/');
            return StringEncodingUtility.SanitizeUtf16(baseUrl + StringEncodingUtility.SanitizeUtf16(path));
        }

        /// <summary>
        /// 负责在失败响应时记录返回体，并抛出可直接显示的错误。
        /// </summary>
        private static void EnsureSuccessStatusCode(BlueprintNetworkHttpResult response, string method, string url)
        {
            if (response != null && response.success)
                return;

            string body = ReadBodySafe(response);
            string detail = response != null && response.statusCode > 0
                ? $"HTTP {response.statusCode}"
                : "网络连接失败";
            if (!string.IsNullOrWhiteSpace(body))
                detail += " | " + TrimForLog(body);
            else if (!string.IsNullOrWhiteSpace(response?.error))
                detail += " | " + response.error;
            string safeDetail = SanitizeMessage(detail);
            LogDebug("请求失败", null, method, safeDetail);
            throw new InvalidOperationException($"{method} 请求失败：{safeDetail}");
        }

        /// <summary>
        /// 负责安全读取失败响应内容，避免二次异常覆盖原始错误。
        /// </summary>
        private static string ReadBodySafe(BlueprintNetworkHttpResult response)
        {
            try
            {
                return StringEncodingUtility.SanitizeUtf16(response?.text ?? string.Empty);
            }
            catch (Exception ex)
            {
                return "读取失败响应内容时出错: " + StringEncodingUtility.SanitizeUtf16(ex.Message);
            }
        }

        /// <summary>
        /// 负责裁剪日志中的长文本，避免日志被整段 HTML 或 JSON 淹没。
        /// </summary>
        private static string TrimForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = StringEncodingUtility.SanitizeUtf16(text).Replace("\r", " ").Replace("\n", " ").Trim();
            const int maxLength = 320;
            return normalized.Length <= maxLength ? normalized : normalized.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// 负责脱敏异常和返回体中的链接文本，避免把服务地址直接暴露到日志或界面。
        /// </summary>
        private static string SanitizeMessage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string sanitized = StringEncodingUtility.SanitizeUtf16(text);
            string baseUrl = BlueprintEndpointCodec.GetBlueprintApiBaseUrl();
            if (!string.IsNullOrWhiteSpace(baseUrl))
                sanitized = sanitized.Replace(baseUrl, "网络蓝图服务");

            sanitized = sanitized.Replace("https://", string.Empty).Replace("http://", string.Empty);
            sanitized = sanitized.Replace("chezhou.icu", "网络蓝图服务");
            sanitized = sanitized.Replace("blueprint-api", "服务接口");
            return sanitized;
        }

        /// <summary>
        /// 负责输出网络蓝图接口调试日志。
        /// </summary>
        private static void LogDebug(string title, string url, string extraA, string extraB)
        {
            if (!EnableDebugLog)
                return;

            StringBuilder builder = new StringBuilder();
            builder.Append("[RSMF 网络蓝图] ").Append(title);
            if (!string.IsNullOrWhiteSpace(extraA))
                builder.Append(" | 信息1=").Append(extraA);
            if (!string.IsNullOrWhiteSpace(extraB))
                builder.Append(" | 信息2=").Append(extraB);
            Log.Message(builder.ToString());
        }

        /// <summary>
        /// 负责把服务端返回的相对地址恢复成客户端可直接访问的公开地址。
        /// </summary>
        private static T NormalizeUrls<T>(T data) where T : class
        {
            if (data is BlueprintNetworkPagedListData paged && paged.items != null)
            {
                for (int i = 0; i < paged.items.Count; i++)
                    NormalizeListItemUrls(paged.items[i]);
                return data;
            }

            if (data is BlueprintNetworkDetailData detail)
            {
                detail.blueprintCode = StringEncodingUtility.SanitizeUtf16(detail.blueprintCode);
                detail.steamId = StringEncodingUtility.SanitizeUtf16(detail.steamId);
                detail.name = StringEncodingUtility.SanitizeUtf16(detail.name);
                detail.description = StringEncodingUtility.SanitizeUtf16(detail.description);
                detail.originalFileName = StringEncodingUtility.SanitizeUtf16(detail.originalFileName);
                detail.contentType = StringEncodingUtility.SanitizeUtf16(detail.contentType);
                detail.createdAt = StringEncodingUtility.SanitizeUtf16(detail.createdAt);
                detail.requiredMods = SanitizeRequiredMods(detail.requiredMods);
                detail.previewUrl = BlueprintEndpointCodec.NormalizePublicUrl(detail.previewUrl);
                detail.detailUrl = BlueprintEndpointCodec.NormalizePublicUrl(detail.detailUrl);
                detail.downloadUrl = BlueprintEndpointCodec.NormalizePublicUrl(detail.downloadUrl);
                return data;
            }

            return data;
        }

        /// <summary>
        /// 负责规范化列表项中的远端地址字段。
        /// </summary>
        private static void NormalizeListItemUrls(BlueprintNetworkListItemData item)
        {
            if (item == null)
                return;

            item.blueprintCode = StringEncodingUtility.SanitizeUtf16(item.blueprintCode);
            item.steamId = StringEncodingUtility.SanitizeUtf16(item.steamId);
            item.name = StringEncodingUtility.SanitizeUtf16(item.name);
            item.description = StringEncodingUtility.SanitizeUtf16(item.description);
            item.createdAt = StringEncodingUtility.SanitizeUtf16(item.createdAt);
            item.previewUrl = BlueprintEndpointCodec.NormalizePublicUrl(item.previewUrl);
            item.detailUrl = BlueprintEndpointCodec.NormalizePublicUrl(item.detailUrl);
            item.downloadUrl = BlueprintEndpointCodec.NormalizePublicUrl(item.downloadUrl);
        }

        /// <summary>
        /// 在上传成功但详情读取失败时，回退构造最小详情对象，避免客户端把成功上传误判成失败。
        /// </summary>
        private static BlueprintNetworkDetailData BuildFallbackDetail(ShopBlueprintLocalRecord record, string steamId, string blueprintCode)
        {
            ShopBlueprintData data = record?.Data ?? new ShopBlueprintData();
            string escapedCode = StringEncodingUtility.EscapeDataStringSafe(blueprintCode);
            return NormalizeUrls(new BlueprintNetworkDetailData
            {
                blueprintCode = StringEncodingUtility.SanitizeUtf16(blueprintCode),
                steamId = StringEncodingUtility.SanitizeUtf16(steamId),
                name = StringEncodingUtility.SanitizeUtf16(data.label),
                description = StringEncodingUtility.SanitizeUtf16(data.description),
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
                detailUrl = BuildUrl("/" + escapedCode),
                previewUrl = BuildUrl("/" + escapedCode + "/preview"),
                downloadUrl = BuildUrl("/" + escapedCode + "/download"),
                requiredMods = SanitizeRequiredMods(data.requiredMods)
            });
        }

        private static string GetSortModeValue(BlueprintNetworkSortMode sortMode)
        {
            switch (sortMode)
            {
                case BlueprintNetworkSortMode.Hot: return "hot";
                case BlueprintNetworkSortMode.Downloads: return "downloads";
                case BlueprintNetworkSortMode.Mine: return "mine";
                case BlueprintNetworkSortMode.Compatible: return "compatible";
                default: return "latest";
            }
        }

        /// <summary>
        /// 清理字符串集合中的非法文本，负责让兼容性查询参数不会因为单个包名损坏而失败。
        /// </summary>
        private static IEnumerable<string> CleanUtf16Items(IEnumerable<string> values)
        {
            foreach (string value in values)
                yield return StringEncodingUtility.SanitizeUtf16(value);
        }

        /// <summary>
        /// 清理蓝图依赖模组文本字段，负责避免上传和展示远端数据时携带非法 UTF-16。
        /// </summary>
        private static List<ShopBlueprintRequiredModData> SanitizeRequiredMods(List<ShopBlueprintRequiredModData> source)
        {
            List<ShopBlueprintRequiredModData> result = source ?? new List<ShopBlueprintRequiredModData>();
            for (int i = 0; i < result.Count; i++)
            {
                ShopBlueprintRequiredModData mod = result[i];
                if (mod == null)
                    continue;

                mod.packageId = StringEncodingUtility.SanitizeUtf16(mod.packageId);
                mod.displayName = StringEncodingUtility.SanitizeUtf16(mod.displayName);
                mod.steamWorkshopUrl = StringEncodingUtility.SanitizeUtf16(mod.steamWorkshopUrl);
            }

            return result;
        }

        private static string SerializeJson<T>(T data)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T), JsonSettings);
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, data);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static T DeserializeJson<T>(string json) where T : class
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T), JsonSettings);
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(StringEncodingUtility.SanitizeUtf16(json))))
            {
                return serializer.ReadObject(stream) as T;
            }
        }

        /// <summary>
        /// 负责承接上传接口最小返回结构。
        /// </summary>
        [DataContract]
        private sealed class BlueprintUploadResponse
        {
            [DataMember] public string blueprintCode = "";
        }

        /// <summary>
        /// 保存一次蓝图网络请求结果，负责在请求对象释放后继续读取状态、文本和字节。
        /// </summary>
        private sealed class BlueprintNetworkHttpResult
        {
            public int statusCode;
            public string text = "";
            public byte[] bytes;
            public bool success;
            public string error = "";

            public string StatusCodeText => statusCode > 0 ? statusCode.ToString() : "无响应";
        }
    }
}
