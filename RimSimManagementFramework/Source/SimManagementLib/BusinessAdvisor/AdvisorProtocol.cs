using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SimManagementLib.BusinessAdvisor
{
    //只接收已声明的查询和操作参数，模型输出不能执行任意代码。
    [DataContract]
    internal sealed class AdvisorReply
    {
        [DataMember] public string reply { get; set; }
        [DataMember] public string taskState { get; set; }
        //省略未使用的工具数组只表示没有该类工具，不推断任何操作参数。
        [DataMember] public List<AdvisorQuery> queries { get; set; } = new List<AdvisorQuery>();
        [DataMember] public List<AdvisorAction> actions { get; set; } = new List<AdvisorAction>();
    }

    //保存查询范围；页码使用记录偏移，源码读取使用从零开始的行偏移。
    [DataContract]
    internal sealed class AdvisorQuery
    {
        [DataMember] public string topic { get; set; }
        [DataMember] public int? mapId { get; set; }
        [DataMember] public int? shopId { get; set; }
        [DataMember] public int? targetId { get; set; }
        [DataMember] public string filter { get; set; }
        [DataMember] public string file { get; set; }
        [DataMember] public int offset { get; set; }
        [DataMember] public int? x { get; set; }
        [DataMember] public int? z { get; set; }
        [DataMember] public int? width { get; set; }
        [DataMember] public int? height { get; set; }
    }

    //描述单个待批准变更，缺失的必要参数会被拒绝而不是填默认值。
    [DataContract]
    internal sealed class AdvisorAction
    {
        [DataMember] public string kind { get; set; }
        [DataMember] public string command { get; set; }
        [DataMember] public string color { get; set; }
        [DataMember] public int? mapId { get; set; }
        [DataMember] public int? shopId { get; set; }
        [DataMember] public int? targetId { get; set; }
        [DataMember] public int? pawnId { get; set; }
        [DataMember] public string role { get; set; }
        [DataMember] public string definition { get; set; }
        [DataMember] public string stuff { get; set; }
        [DataMember] public string category { get; set; }
        [DataMember] public string name { get; set; }
        [DataMember] public string hours { get; set; }
        [DataMember] public int? x { get; set; }
        [DataMember] public int? z { get; set; }
        [DataMember] public int? width { get; set; }
        [DataMember] public int? height { get; set; }
        [DataMember] public int? rotation { get; set; }
        [DataMember] public int? count { get; set; }
        [DataMember] public int? threshold { get; set; }
        [DataMember] public int? priority { get; set; }
        [DataMember] public int? slot { get; set; }
        [DataMember] public float? price { get; set; }
        [DataMember] public bool? enabled { get; set; }
    }

    //为模型协议和人格导入提供明确的 JSON 读写入口。
    internal static class AdvisorJson
    {
        //读取完整 JSON；格式错误由调用者显示，不自动发起修复请求。
        public static T Read<T>(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        //输出稳定字段顺序，不在历史消息中重写时间或查询结果。
        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
