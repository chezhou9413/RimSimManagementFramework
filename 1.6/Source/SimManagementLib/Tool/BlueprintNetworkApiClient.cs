using SimManagementLib.Pojo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责调用网络蓝图后端接口，并返回客户端可直接使用的数据模型。
    /// </summary>
    public static class BlueprintNetworkApiClient
    {
        private static readonly bool EnableDebugLog = false;
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

            if (sortMode == BlueprintNetworkSortMode.Mine && !string.IsNullOrWhiteSpace(steamId))
                url.Append("&steamId=").Append(Uri.EscapeDataString(steamId));

            if (sortMode == BlueprintNetworkSortMode.Compatible && activePackageIds != null)
            {
                string joined = string.Join(",", activePackageIds);
                url.Append("&activePackageIds=").Append(Uri.EscapeDataString(joined));
            }

            return GetJsonAsync<BlueprintNetworkPagedListData>(url.ToString(), token);
        }

        /// <summary>
        /// 请求蓝图详情。
        /// </summary>
        public static Task<BlueprintNetworkDetailData> GetDetailAsync(string blueprintCode, CancellationToken token)
        {
            return GetJsonAsync<BlueprintNetworkDetailData>(BuildUrl("/" + Uri.EscapeDataString(blueprintCode)), token);
        }

        /// <summary>
        /// 执行蓝图点赞。
        /// </summary>
        public static async Task<bool> LikeAsync(string blueprintCode, string steamId, CancellationToken token)
        {
            string url = BuildUrl("/" + Uri.EscapeDataString(blueprintCode) + "/like?steamId=" + Uri.EscapeDataString(steamId));
            using (HttpClient client = CreateClient())
            using (StringContent content = new StringContent("", Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, url, token, content))
            {
                return response.IsSuccessStatusCode;
            }
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

            using (HttpClient client = CreateClient())
            {
                MultipartFormDataContent form = new MultipartFormDataContent();
                form.Add(new StringContent(steamId, Encoding.UTF8), "SteamId");
                form.Add(new StringContent(record.Data.label ?? "", Encoding.UTF8), "Name");
                form.Add(new StringContent(record.Data.description ?? "", Encoding.UTF8), "Description");
                form.Add(new StringContent(SerializeJson(record.Data.requiredMods), Encoding.UTF8), "RequiredModsJson");
                form.Add(new StringContent(record.Data.remoteBlueprintSourceKind ?? "", Encoding.UTF8), "ClientBlueprintSourceKind");
                if (BlueprintOwnershipUtility.CanUpdateExisting(record.Data, steamId))
                    form.Add(new StringContent(record.Data.remoteBlueprintCode, Encoding.UTF8), "ExistingBlueprintCode");

                byte[] blueprintBytes = File.ReadAllBytes(record.BlueprintPath);
                ByteArrayContent blueprintContent = new ByteArrayContent(blueprintBytes);
                blueprintContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                form.Add(blueprintContent, "File", Path.GetFileName(record.BlueprintPath));

                if (!string.IsNullOrWhiteSpace(record.PreviewPath) && File.Exists(record.PreviewPath))
                {
                    byte[] previewBytes = File.ReadAllBytes(record.PreviewPath);
                    ByteArrayContent previewContent = new ByteArrayContent(previewBytes);
                    previewContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
                    form.Add(previewContent, "PreviewFile", Path.GetFileName(record.PreviewPath));
                }

                string url = BuildUrl("");
                using (HttpResponseMessage response = await SendAsync(client, HttpMethod.Post, url, token, form))
                {
                    await EnsureSuccessStatusCodeAsync(response, HttpMethod.Post, url);
                    string body = await response.Content.ReadAsStringAsync();
                    LogDebug("上传响应", null, response.StatusCode.ToString(), TrimForLog(body));
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
            }
        }

        /// <summary>
        /// 下载网络蓝图 JSON。
        /// </summary>
        public static async Task<byte[]> DownloadBlueprintAsync(string blueprintCode, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
            {
                string url = BuildUrl("/" + Uri.EscapeDataString(blueprintCode) + "/download");
                using (HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, url, token))
                {
                    await EnsureSuccessStatusCodeAsync(response, HttpMethod.Get, url);
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    LogDebug("下载蓝图成功", null, response.StatusCode.ToString(), "字节数=" + bytes.Length);
                    return bytes;
                }
            }
        }

        /// <summary>
        /// 下载网络蓝图预览图。
        /// </summary>
        public static async Task<byte[]> DownloadPreviewAsync(string blueprintCode, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
            {
                string url = BuildUrl("/" + Uri.EscapeDataString(blueprintCode) + "/preview");
                using (HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, url, token))
                {
                    await EnsureSuccessStatusCodeAsync(response, HttpMethod.Get, url);
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    LogDebug("下载预览图成功", null, response.StatusCode.ToString(), "字节数=" + bytes.Length);
                    return bytes;
                }
            }
        }

        /// <summary>
        /// 删除自己上传的网络蓝图。
        /// </summary>
        public static async Task<bool> DeleteOwnBlueprintAsync(string blueprintCode, string steamId, CancellationToken token)
        {
            using (HttpClient client = CreateClient())
            {
                string url = BuildUrl("/" + Uri.EscapeDataString(blueprintCode) + "?steamId=" + Uri.EscapeDataString(steamId));
                using (HttpResponseMessage response = await SendAsync(client, HttpMethod.Delete, url, token))
                {
                    return response.IsSuccessStatusCode;
                }
            }
        }

        /// <summary>
        /// 负责发送请求并输出临时调试日志，帮助定位线上接口路径问题。
        /// </summary>
        private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, CancellationToken token, HttpContent content = null)
        {
            LogDebug("发送请求", null, method.Method, null);
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                request.Content = content;
                HttpResponseMessage response = await client.SendAsync(request, token);
                LogDebug("收到响应", null, method.Method, ((int)response.StatusCode) + " " + response.StatusCode);
                return response;
            }
        }

        private static async Task<T> GetJsonAsync<T>(string url, CancellationToken token) where T : class
        {
            using (HttpClient client = CreateClient())
            using (HttpResponseMessage response = await SendAsync(client, HttpMethod.Get, url, token))
            {
                await EnsureSuccessStatusCodeAsync(response, HttpMethod.Get, url);
                string body = await response.Content.ReadAsStringAsync();
                LogDebug("读取 JSON 成功", null, response.StatusCode.ToString(), TrimForLog(body));
                return NormalizeUrls(DeserializeJson<T>(body));
            }
        }

        private static HttpClient CreateClient()
        {
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private static string BuildUrl(string path)
        {
            string baseUrl = BlueprintEndpointCodec.GetBlueprintApiBaseUrl().TrimEnd('/');
            return baseUrl + path;
        }

        /// <summary>
        /// 负责在失败响应时记录返回体，并抛出可直接显示的错误。
        /// </summary>
        private static async Task EnsureSuccessStatusCodeAsync(HttpResponseMessage response, HttpMethod method, string url)
        {
            if (response.IsSuccessStatusCode)
                return;

            string body = await ReadBodySafeAsync(response);
            string detail = $"HTTP {(int)response.StatusCode} {response.StatusCode}";
            if (!string.IsNullOrWhiteSpace(body))
                detail += " | " + TrimForLog(body);
            string safeDetail = SanitizeMessage(detail);
            LogDebug("请求失败", null, method.Method, safeDetail);
            throw new HttpRequestException($"{method.Method} 请求失败：{safeDetail}");
        }

        /// <summary>
        /// 负责安全读取失败响应内容，避免二次异常覆盖原始错误。
        /// </summary>
        private static async Task<string> ReadBodySafeAsync(HttpResponseMessage response)
        {
            try
            {
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                return "读取失败响应内容时出错: " + ex.Message;
            }
        }

        /// <summary>
        /// 负责裁剪日志中的长文本，避免日志被整段 HTML 或 JSON 淹没。
        /// </summary>
        private static string TrimForLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
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

            string sanitized = text;
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
                detail.blueprintCode = detail.blueprintCode ?? "";
                detail.steamId = detail.steamId ?? "";
                detail.name = detail.name ?? "";
                detail.description = detail.description ?? "";
                detail.originalFileName = detail.originalFileName ?? "";
                detail.contentType = detail.contentType ?? "";
                detail.createdAt = detail.createdAt ?? "";
                detail.requiredMods = detail.requiredMods ?? new List<ShopBlueprintRequiredModData>();
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

            item.blueprintCode = item.blueprintCode ?? "";
            item.steamId = item.steamId ?? "";
            item.name = item.name ?? "";
            item.description = item.description ?? "";
            item.createdAt = item.createdAt ?? "";
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
            string escapedCode = Uri.EscapeDataString(blueprintCode ?? "");
            return NormalizeUrls(new BlueprintNetworkDetailData
            {
                blueprintCode = blueprintCode ?? "",
                steamId = steamId ?? "",
                name = data.label ?? "",
                description = data.description ?? "",
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
                detailUrl = BuildUrl("/" + escapedCode),
                previewUrl = BuildUrl("/" + escapedCode + "/preview"),
                downloadUrl = BuildUrl("/" + escapedCode + "/download"),
                requiredMods = data.requiredMods ?? new List<ShopBlueprintRequiredModData>()
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
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
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
    }
}
