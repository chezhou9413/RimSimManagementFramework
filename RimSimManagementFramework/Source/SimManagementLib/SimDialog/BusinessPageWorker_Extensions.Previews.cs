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
    //管理推荐封面资源，职责是异步下载图片并在主线程创建、释放纹理。
    public partial class BusinessPageWorker_Extensions
    {
        private const int MaxRemotePreviewTasks = 3;
        private static readonly Dictionary<string, Texture2D> RemotePreviewCache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Task<byte[]>> RemotePreviewTasks = new Dictionary<string, Task<byte[]>>();
        //清理远端封面缓存，负责在窗口关闭时释放运行期纹理。
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
        //绘制推荐扩展封面，负责优先使用本地贴图并按需异步拉取远端图。
        private static void DrawPreview(Rect rect, IBusinessExtensionRecommendation recommendation)
        {
            Widgets.DrawBoxSolid(rect, new Color(0f, 0f, 0f, 0.35f));
            SimUiStyle.DrawBorder(rect, new Color(1f, 1f, 1f, 0.14f));

            Texture2D texture = null;
            if (!string.IsNullOrWhiteSpace(recommendation.PreviewTexturePath))
                texture = ContentFinder<Texture2D>.Get(recommendation.PreviewTexturePath, false);
            if (texture == null)
                texture = TryGetRemotePreview(recommendation.PreviewImageUrl);

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
        //尝试获取远端封面缓存，并在未缓存时启动下载。
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
        //轮询封面下载任务，负责在主线程创建可绘制纹理。
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
        //异步下载远端封面数据。
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
        //把下载到的图片数据转换为纹理。
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
    }
}
