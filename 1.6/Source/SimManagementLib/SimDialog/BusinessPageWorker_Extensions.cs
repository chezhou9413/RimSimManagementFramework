using RimWorld;
using SimManagementLib.Api;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace SimManagementLib.SimDialog
{
    /// <summary>
    /// 绘制经商管理扩展推荐页，负责展示 Def 配置的推荐扩展和 Steam 入口。
    /// </summary>
    public class BusinessPageWorker_Extensions : BusinessManagerPageWorker
    {
        private const float HeaderHeight = 58f;
        private const float RowHeight = 132f;
        private const float PreviewSize = 96f;
        private const int MaxRemotePreviewTasks = 3;
        private static readonly Dictionary<string, Texture2D> RemotePreviewCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Task<byte[]>> RemotePreviewTasks = new Dictionary<string, Task<byte[]>>();

        /// <summary>
        /// 判断推荐页是否应显示，负责读取玩家设置中的显隐开关。
        /// </summary>
        public override bool CanShow(ShopUiContext context)
        {
            return SimManagementLibMod.Settings?.showExtensionRecommendationPage != false;
        }

        /// <summary>
        /// 绘制扩展推荐页主体，负责虚拟化长列表并避免阻塞 UI。
        /// </summary>
        public override void DrawBusinessPage(Rect rect, BusinessManagerUiContext context)
        {
            PollRemotePreviewTasks();
            List<BusinessExtensionRecommendationDef> rows = DefDatabase<BusinessExtensionRecommendationDef>.AllDefsListForReading
                .OrderBy(def => def.order)
                .ThenBy(def => def.defName)
                .ToList();

            Rect headerRect = new Rect(rect.x, rect.y, rect.width, HeaderHeight);
            DrawHeader(headerRect, rows);

            Rect listRect = new Rect(rect.x, headerRect.yMax + 8f, rect.width, rect.height - HeaderHeight - 8f);
            if (rows.Count == 0)
            {
                Widgets.NoneLabel(listRect.center.y, listRect.width, SimTranslation.T("RSMF.Business.Extensions.Empty"));
                return;
            }

            float viewWidth = Mathf.Max(1f, listRect.width - 18f);
            Rect viewRect = new Rect(0f, 0f, viewWidth, Mathf.Max(listRect.height + 1f, rows.Count * RowHeight));
            Widgets.BeginScrollView(listRect, ref context.ScrollPosition, viewRect);

            int first = Mathf.Max(0, Mathf.FloorToInt(context.ScrollPosition.y / RowHeight) - 1);
            int last = Mathf.Min(rows.Count - 1, Mathf.CeilToInt((context.ScrollPosition.y + listRect.height) / RowHeight) + 1);
            for (int i = first; i <= last; i++)
            {
                Rect rowRect = new Rect(0f, i * RowHeight, viewWidth, RowHeight - 6f);
                DrawRecommendationRow(rowRect, rows[i], i);
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// 清理远端封面缓存，负责在窗口关闭时释放运行期纹理。
        /// </summary>
        public static void ClearPreviewCache()
        {
            foreach (Texture2D texture in RemotePreviewCache.Values)
            {
                if (texture != null)
                    UnityEngine.Object.Destroy(texture);
            }

            RemotePreviewCache.Clear();
            RemotePreviewTasks.Clear();
        }

        /// <summary>
        /// 绘制推荐页顶部摘要。
        /// </summary>
        private static void DrawHeader(Rect rect, List<BusinessExtensionRecommendationDef> rows)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.22f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.12f));

            int checkedCount = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                if (BusinessExtensionRecommendationUtility.GetStatus(rows[i]).IsChecked)
                    checkedCount++;
            }

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, 26f), SimTranslation.T("RSMF.Business.Extensions.Title"));

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 30f, rect.width - 20f, 22f),
                SimTranslation.T("RSMF.Business.Extensions.Summary", checkedCount.Named("checked"), rows.Count.Named("total")));
            ResetTextState();
        }

        /// <summary>
        /// 绘制一条推荐扩展。
        /// </summary>
        private static void DrawRecommendationRow(Rect row, BusinessExtensionRecommendationDef def, int index)
        {
            Widgets.DrawBoxSolid(row, index % 2 == 0 ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.08f));
            SimUiStyle.DrawBorder(row, new Color(1f, 1f, 1f, 0.10f));

            Rect previewRect = new Rect(row.x + 10f, row.y + 14f, PreviewSize, PreviewSize);
            DrawPreview(previewRect, def);

            BusinessExtensionRecommendationStatus status = BusinessExtensionRecommendationUtility.GetStatus(def);
            float actionWidth = 134f;
            float textX = previewRect.xMax + 12f;
            float textWidth = Mathf.Max(160f, row.width - PreviewSize - actionWidth - 44f);
            Rect titleRect = new Rect(textX, row.y + 10f, textWidth, Text.LineHeightOf(GameFont.Small) + 4f);
            Rect descRect = new Rect(textX, titleRect.yMax + 6f, textWidth, 44f);
            Rect statusRect = new Rect(textX, descRect.yMax + 8f, textWidth, 24f);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = Color.white;
            Widgets.Label(titleRect, def.DisplayLabel);

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            Widgets.Label(descRect, def.DisplayDescription.Truncate(descRect.width * 2.15f));
            DrawStatusBadges(statusRect, status);

            float buttonY = row.y + 24f;
            Rect openRect = new Rect(row.xMax - actionWidth - 10f, buttonY, actionWidth, 30f);
            bool canOpen = !string.IsNullOrWhiteSpace(def.workshopUrl);
            if (SimUiStyle.DrawPrimaryButton(openRect, SimTranslation.T("RSMF.Business.Extensions.OpenSteam"), canOpen, GameFont.Tiny))
                BusinessExtensionRecommendationUtility.OpenWorkshopUrl(def.workshopUrl);

            ResetTextState();
        }

        /// <summary>
        /// 绘制推荐扩展封面，负责优先使用本地贴图并按需异步拉取远端图。
        /// </summary>
        private static void DrawPreview(Rect rect, BusinessExtensionRecommendationDef def)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.35f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.14f));

            Texture2D texture = null;
            if (!string.IsNullOrWhiteSpace(def.previewTexturePath))
                texture = ContentFinder<Texture2D>.Get(def.previewTexturePath, false);
            if (texture == null)
                texture = TryGetRemotePreview(def.previewImageUrl);

            if (texture != null)
            {
                GUI.DrawTexture(rect.ContractedBy(4f), texture, ScaleMode.ScaleToFit);
                return;
            }

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.72f, 0.72f, 0.72f, 1f);
            Widgets.Label(rect.ContractedBy(6f), SimTranslation.T("RSMF.Business.Extensions.NoPreview"));
            ResetTextState();
        }

        /// <summary>
        /// 绘制订阅、安装和启用状态标签。
        /// </summary>
        private static void DrawStatusBadges(Rect rect, BusinessExtensionRecommendationStatus status)
        {
            float x = rect.x;
            DrawBadge(new Rect(x, rect.y, 86f, rect.height), status.IsChecked ? "✓ " + SimTranslation.T("RSMF.Business.Extensions.Checked") : SimTranslation.T("RSMF.Business.Extensions.NotInstalled"), status.IsChecked ? new Color(0.35f, 0.80f, 0.45f, 1f) : new Color(0.95f, 0.72f, 0.25f, 1f));
            x += 94f;

            if (status.IsActive)
            {
                DrawBadge(new Rect(x, rect.y, 78f, rect.height), SimTranslation.T("RSMF.Business.Extensions.Active"), new Color(0.25f, 0.65f, 0.85f, 1f));
                x += 86f;
            }

            if (status.IsSubscribed && !status.IsInstalled)
                DrawBadge(new Rect(x, rect.y, 78f, rect.height), SimTranslation.T("RSMF.Business.Extensions.Subscribed"), new Color(0.35f, 0.80f, 0.45f, 1f));
        }

        /// <summary>
        /// 绘制单个状态标签。
        /// </summary>
        private static void DrawBadge(Rect rect, string label, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.18f));
            SimUiStyle.DrawBorder(rect, new Color(color.r, color.g, color.b, 0.55f));
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = color;
            Widgets.Label(rect.ContractedBy(3f), label);
            ResetTextState();
        }

        /// <summary>
        /// 尝试获取远端封面缓存，并在未缓存时启动下载。
        /// </summary>
        private static Texture2D TryGetRemotePreview(string previewImageUrl)
        {
            if (string.IsNullOrWhiteSpace(previewImageUrl))
                return null;

            if (RemotePreviewCache.TryGetValue(previewImageUrl, out Texture2D texture))
                return texture;

            if (!RemotePreviewTasks.ContainsKey(previewImageUrl) && RemotePreviewTasks.Count < MaxRemotePreviewTasks)
                RemotePreviewTasks[previewImageUrl] = DownloadRemotePreviewAsync(previewImageUrl);

            return null;
        }

        /// <summary>
        /// 轮询封面下载任务，负责在主线程创建可绘制纹理。
        /// </summary>
        private static void PollRemotePreviewTasks()
        {
            if (RemotePreviewTasks.Count == 0)
                return;

            List<string> keys = RemotePreviewTasks.Keys.ToList();
            for (int i = 0; i < keys.Count; i++)
            {
                string url = keys[i];
                Task<byte[]> task = RemotePreviewTasks[url];
                if (task == null || !task.IsCompleted)
                    continue;

                RemotePreviewCache[url] = !task.IsFaulted && !task.IsCanceled ? CreatePreviewTexture(task.Result) : null;
                RemotePreviewTasks.Remove(url);
            }
        }

        /// <summary>
        /// 异步下载远端封面数据。
        /// </summary>
        private static async Task<byte[]> DownloadRemotePreviewAsync(string previewImageUrl)
        {
            previewImageUrl = StringEncodingUtility.SanitizeUtf16(previewImageUrl);
            using (UnityWebRequest request = new UnityWebRequest(previewImageUrl, "GET"))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                float elapsedSeconds = 0f;
                while (!operation.isDone)
                {
                    await Task.Delay(100);
                    elapsedSeconds += 0.1f;
                    if (elapsedSeconds > 10f)
                    {
                        request.Abort();
                        throw new TaskCanceledException();
                    }
                }

                UnityWebRequest.Result result = request.result;
                if (result == UnityWebRequest.Result.ConnectionError || result == UnityWebRequest.Result.ProtocolError)
                    throw new InvalidOperationException(StringEncodingUtility.SanitizeUtf16(request.error));

                return request.downloadHandler?.data;
            }
        }

        /// <summary>
        /// 把下载到的图片数据转换为纹理。
        /// </summary>
        private static Texture2D CreatePreviewTexture(byte[] bytes)
        {
            if (bytes == null || bytes.Length <= 0)
                return null;

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes))
                return texture;

            UnityEngine.Object.Destroy(texture);
            return null;
        }

        /// <summary>
        /// 恢复 IMGUI 全局文本状态。
        /// </summary>
        private static void ResetTextState()
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }
}
