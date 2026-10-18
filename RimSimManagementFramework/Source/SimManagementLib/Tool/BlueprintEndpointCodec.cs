using System.Text;
using System;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把网络蓝图后端地址按片段和异或方式恢复，减少明文暴露。
    /// </summary>
    public static class BlueprintEndpointCodec
    {
        private static readonly byte[] HostBytes = { 57, 50, 63, 32, 50, 53, 47, 116, 51, 57, 47 };
        private static readonly byte[] PrefixBytes = { 117, 56, 54, 47, 63, 42, 40, 51, 52, 46, 119, 59, 42, 51, 117, 59, 42, 51, 117, 56, 54, 47, 63, 42, 40, 51, 52, 46, 41 };
        private const byte XorKey = 90;
        private const string ApiPathMarker = "/api/blueprints";

        /// <summary>
        /// 返回网络蓝图基础地址。
        /// </summary>
        public static string GetBlueprintApiBaseUrl()
        {
            return "https://" + Decode(HostBytes) + Decode(PrefixBytes);
        }

        /// <summary>
        /// 返回外部可访问的网络蓝图公开根地址。
        /// </summary>
        public static string GetBlueprintPublicRootUrl()
        {
            string apiBaseUrl = GetBlueprintApiBaseUrl();
            int markerIndex = apiBaseUrl.IndexOf(ApiPathMarker);
            return markerIndex > 0 ? apiBaseUrl.Substring(0, markerIndex) : apiBaseUrl;
        }

        /// <summary>
        /// 负责把服务端返回的相对或内部地址恢复成客户端可访问的公开地址。
        /// </summary>
        public static string NormalizePublicUrl(string rawUrl)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
                return string.Empty;

            string url = StringEncodingUtility.SanitizeUtf16(rawUrl).Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri absoluteUri))
            {
                string pathAndQuery = absoluteUri.PathAndQuery + absoluteUri.Fragment;
                if (pathAndQuery.StartsWith(ApiPathMarker))
                    return GetBlueprintPublicRootUrl().TrimEnd('/') + pathAndQuery;

                return absoluteUri.ToString();
            }

            if (!url.StartsWith("/"))
                url = "/" + url;

            if (url.StartsWith(ApiPathMarker))
                return GetBlueprintPublicRootUrl().TrimEnd('/') + url;

            return GetBlueprintApiBaseUrl().TrimEnd('/') + url;
        }

        /// <summary>
        /// 负责把字节数组恢复成原始文本。
        /// </summary>
        private static string Decode(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
                builder.Append((char)(bytes[i] ^ XorKey));
            return builder.ToString();
        }
    }
}
