using SimManagementLib.Pojo;
using SimManagementLib.SimDef;
using SimManagementLib.Tool;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SimManagementLib.GameComp
{
    /// <summary>
    /// 维护运行时顾客目录，负责合并 CustomerKindDef 模板和玩家注册数据。
    /// </summary>
    public class GameComponent_CustomerCatalog : GameComponent
    {
        private Dictionary<string, RuntimeCustomerKind> kindsById = new Dictionary<string, RuntimeCustomerKind>();
        private bool initialized;

        /// <summary>
        /// 为当前游戏创建顾客目录组件。
        /// </summary>
        public GameComponent_CustomerCatalog(Game game)
        {
        }

        public IReadOnlyCollection<RuntimeCustomerKind> Kinds
        {
            get
            {
                EnsureInitialized();
                return kindsById.Values;
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EnsureInitialized();
        }

        /// <summary>
        /// 在首次查询时构建顾客目录。
        /// </summary>
        public void EnsureInitialized()
        {
            if (initialized) return;
            RebuildCatalog();
        }

        /// <summary>
        /// 根据 Def 数据和玩家注册记录重建运行时顾客目录。
        /// </summary>
        public void RebuildCatalog()
        {
            kindsById.Clear();
            AddDefKinds();
            AddCustomKinds();
            initialized = true;
        }

        /// <summary>
        /// 根据源数据重建顾客目录。
        /// </summary>
        public void RebuildFromDefs()
        {
            RebuildCatalog();
        }

        /// <summary>
        /// 强制目录立即重建并让后续查询使用最新数据。
        /// </summary>
        public void NotifyCatalogChanged()
        {
            initialized = false;
            RebuildCatalog();
        }

        /// <summary>
        /// 将内置 CustomerKindDef 条目加入运行时目录。
        /// </summary>
        private void AddDefKinds()
        {
            foreach (CustomerKindDef def in DefDatabase<CustomerKindDef>.AllDefsListForReading.Where(d => d != null))
            {
                RuntimeCustomerKind kind = RuntimeCustomerKind.FromDef(def);
                if (kind != null)
                    kindsById[kind.kindId] = kind;
            }
        }

        /// <summary>
        /// 将玩家注册的顾客记录加入目录，同时禁止覆盖内置 Def ID。
        /// </summary>
        private void AddCustomKinds()
        {
            CustomCustomerDatabaseData data = CustomCustomerDatabase.Load();
            if (data?.kinds == null) return;

            for (int i = 0; i < data.kinds.Count; i++)
            {
                RuntimeCustomerKind kind = RuntimeCustomerKind.FromCustomRecord(data.kinds[i]);
                if (kind == null || string.IsNullOrEmpty(kind.kindId)) continue;
                if (kindsById.ContainsKey(kind.kindId)) continue;
                kindsById[kind.kindId] = kind;
            }
        }

        /// <summary>
        /// 按 ID 返回一个运行时顾客类型。
        /// </summary>
        public RuntimeCustomerKind GetKind(string kindId)
        {
            EnsureInitialized();
            if (string.IsNullOrEmpty(kindId)) return null;
            kindsById.TryGetValue(kindId, out RuntimeCustomerKind kind);
            return kind;
        }
    }
}
