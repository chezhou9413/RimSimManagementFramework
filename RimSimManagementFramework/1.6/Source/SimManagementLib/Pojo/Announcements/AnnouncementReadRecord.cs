using SimManagementLib.Tool;
using System;
using Verse;

namespace SimManagementLib.Pojo
{
    //保存本机已读公告快照，职责是保留玩家已经收到的标题、正文和读取时间。
    public sealed class AnnouncementReadRecord : IExposable
    {
        public string readKey = "";
        public string announcementCode = "";
        public string title = "";
        public string body = "";
        public string publishedAt = "";
        public string readAt = "";
        //空构造函数供 RimWorld 存档系统创建对象。
        public AnnouncementReadRecord()
        {
        }
        //从本地公告构造已读快照，负责固定保存当时看到的标题、正文和发布时间。
        public AnnouncementReadRecord(AnnouncementItemData item, DateTime readTime)
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
        //读写公告已读快照，负责持久化本机历史公告数据。
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
        //清理存档文本，负责保证历史公告字段不会携带坏编码。
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

