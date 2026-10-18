using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SimManagementLib.BusinessAdvisor
{
    //只读取本模组和餐厅程序集内的源码资源，不允许模型访问任意磁盘路径。
    internal static class AdvisorSourceQuery
    {
        private static readonly Dictionary<string, string[]> Files = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private static bool loaded;

        //按需加载随当前 DLL 一起编译打包的源码，运行期间保持稳定。
        private static void Load()
        {
            if (loaded) return;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "SimManagementLib" || a.GetName().Name == "RimSimRestaurantExtension"))
            {
                foreach (string resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith("AdvisorSource/", StringComparison.Ordinal)))
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resource))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                        Files[assembly.GetName().Name + "/" + resource.Substring(14).Replace('\\', '/')] = reader.ReadToEnd().Replace("\r", "").Split('\n');
                }
            }
            loaded = true;
        }

        //查询文件、精确子串命中或指定代码片段，行号用于玩家核对依据。
        public static string Query(AdvisorQuery query)
        {
            Load();
            if (query.topic == "source_read")
            {
                if (query.file == null || !Files.TryGetValue(query.file, out string[] lines))
                    throw new InvalidOperationException("源码文件未打包或路径不匹配，请先查询 source_files。");
                return AdvisorQueries.Page(lines.Select((line, index) => query.file + ":" + (index + 1) + " " + line), query.offset, 60);
            }
            IEnumerable<KeyValuePair<string, string[]>> files = Files.OrderBy(p => p.Key, StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(query.file)) files = files.Where(p => AdvisorQueries.Match(p.Key, query.file));
            if (query.topic == "source_files")
                return AdvisorQueries.Page(files.Where(p => AdvisorQueries.Match(p.Key, query.filter))
                    .Select(p => p.Key + " (" + p.Value.Length + " lines)"), query.offset);
            if (string.IsNullOrWhiteSpace(query.filter)) throw new InvalidOperationException("source_search 需要 filter 精确子串。");
            return AdvisorQueries.Page(files.SelectMany(p => p.Value.Select((line, index) => new { line, index, file = p.Key }))
                .Where(p => AdvisorQueries.Match(p.line, query.filter)).Select(p => p.file + ":" + (p.index + 1) + " " + p.line.Trim()), query.offset, 30);
        }
    }
}
