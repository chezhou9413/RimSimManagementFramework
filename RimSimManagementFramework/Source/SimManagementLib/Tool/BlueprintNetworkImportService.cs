using SimManagementLib.Pojo;
using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using Verse;

namespace SimManagementLib.Tool
{
    /// <summary>
    /// 负责把网络蓝图导入本地蓝图库，并写入远端来源信息。
    /// </summary>
    public static class BlueprintNetworkImportService
    {
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ShopBlueprintData));

        /// <summary>
        /// 将网络蓝图详情和二进制内容导入本地蓝图库。
        /// </summary>
        public static bool TryImport(BlueprintNetworkDetailData detail, byte[] blueprintBytes, byte[] previewBytes, out ShopBlueprintLocalRecord record, out string error)
        {
            record = null;
            error = null;
            if (detail == null || blueprintBytes == null || blueprintBytes.Length <= 0)
            {
                error = SimTranslation.T("RSMF.Blueprint.Network.Error.ImportFailed");
                return false;
            }

            try
            {
                ShopBlueprintData data;
                using (MemoryStream stream = new MemoryStream(blueprintBytes))
                    data = Serializer.ReadObject(stream) as ShopBlueprintData;
                if (data == null)
                {
                    error = SimTranslation.T("RSMF.Blueprint.Network.Error.ImportFailed");
                    return false;
                }

                data.remoteBlueprintCode = detail.blueprintCode ?? "";
                BlueprintOwnershipUtility.MarkAsImported(data, detail.steamId ?? "", DateTime.UtcNow.Ticks);
                data.requiredMods = detail.requiredMods ?? new System.Collections.Generic.List<ShopBlueprintRequiredModData>();
                data.blueprintId = DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_remote_" + (detail.blueprintCode ?? "blueprint").ToLowerInvariant();
                ShopBlueprintLibrary.EnsureDataDefaults(data);
                ShopBlueprintSignPayloadUtility.ImportImages(data);

                string directory = Path.Combine(ShopBlueprintLibrary.LibraryDirectory, data.blueprintId);
                Directory.CreateDirectory(directory);
                string blueprintPath = Path.Combine(directory, "blueprint.json");
                string previewPath = Path.Combine(directory, "preview.png");

                using (FileStream stream = File.Create(blueprintPath))
                    Serializer.WriteObject(stream, data);

                if (previewBytes != null && previewBytes.Length > 0)
                    File.WriteAllBytes(previewPath, previewBytes);
                else
                    ShopBlueprintLibrary.TryUpdateImportedPreview(data, previewPath);

                record = new ShopBlueprintLocalRecord
                {
                    DirectoryPath = directory,
                    BlueprintPath = blueprintPath,
                    PreviewPath = previewPath,
                    Data = data
                };
                return true;
            }
            catch (Exception ex)
            {
                error = SimTranslation.T("RSMF.Blueprint.Network.Error.ImportFailedWithMessage", ex.Message.Named("message"));
                return false;
            }
        }
    }
}
