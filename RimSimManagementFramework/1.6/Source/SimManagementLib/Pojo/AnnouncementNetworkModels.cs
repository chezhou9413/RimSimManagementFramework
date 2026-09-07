using SimManagementLib.Tool;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Verse;

namespace SimManagementLib.Pojo
{
    /// <summary>
    /// 保存后端公告接口返回的一条公告数据，负责承接联网读取结果。
    /// </summary>
    [DataContract]
    public sealed class AnnouncementNetworkItemData
    {
        [DataMember] public string announcementCode = "";
        [DataMember] public string title = "";
        [DataMember] public string body = "";
        [DataMember] public int revision;
        [DataMember] public string readKey = "";
        [DataMember] public string publishedAt = "";
        [DataMember] public string updatedAt = "";

        /// <summary>
        /// 清理公告字段文本，负责避免外部 JSON 中的非法 UTF-16 进入 UI 和存档。
        /// </summary>
        public void Sanitize()
        {
            announcementCode = StringEncodingUtility.SanitizeUtf16(announcementCode);
            title = StringEncodingUtility.SanitizeUtf16(title);
            body = StringEncodingUtility.SanitizeUtf16(body);
            readKey = StringEncodingUtility.SanitizeUtf16(readKey);
            publishedAt = StringEncodingUtility.SanitizeUtf16(publishedAt);
            updatedAt = StringEncodingUtility.SanitizeUtf16(updatedAt);
            if (string.IsNullOrWhiteSpace(readKey) && !string.IsNullOrWhiteSpace(announcementCode))
                readKey = announcementCode + ":" + Math.Max(1, revision);
        }
    }

    /// <summary>
    /// 保存本机已读公告快照，负责让玩家离线查看历史公告。
    /// </summary>
    public sealed class AnnouncementReadRecord : IExposable
    {
        public string readKey = "";
        public string announcementCode = "";
        public string title = "";
        public string body = "";
        public string publishedAt = "";
        public string readAt = "";

        /// <summary>
        /// 空构造函数供 RimWorld 存档系统创建对象。
        /// </summary>
        public AnnouncementReadRecord()
        {
        }

        /// <summary>
        /// 从联网公告构造已读快照，负责固定保存当时看到的标题、正文和发布时间。
        /// </summary>
        public AnnouncementReadRecord(AnnouncementNetworkItemData item, DateTime readTime)
        {
            if (item != null)
            {
                readKey = item.readKey;
                announcementCode = item.announcementCode;
                title = item.title;
                body = item.body;
                publishedAt = item.publishedAt;
            }

            readAt = readTime.ToString("O");
            Sanitize();
        }

        /// <summary>
        /// 读写公告已读快照，负责持久化本机历史公告数据。
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref readKey, "readKey", "");
            Scribe_Values.Look(ref announcementCode, "announcementCode", "");
            Scribe_Values.Look(ref title, "title", "");
            Scribe_Values.Look(ref body, "body", "");
            Scribe_Values.Look(ref publishedAt, "publishedAt", "");
            Scribe_Values.Look(ref readAt, "readAt", "");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                Sanitize();
        }

        /// <summary>
        /// 清理存档文本，负责保证历史公告字段不会携带坏编码。
        /// </summary>
        public void Sanitize()
        {
            readKey = StringEncodingUtility.SanitizeUtf16(readKey);
            announcementCode = StringEncodingUtility.SanitizeUtf16(announcementCode);
            title = StringEncodingUtility.SanitizeUtf16(title);
            body = StringEncodingUtility.SanitizeUtf16(body);
            publishedAt = StringEncodingUtility.SanitizeUtf16(publishedAt);
            readAt = StringEncodingUtility.SanitizeUtf16(readAt);
        }
    }
}
