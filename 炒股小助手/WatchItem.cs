// ═══════════════════════════════════════════════════════════════════════════
// WatchItem.cs —— 自选股的数据模型（对应桌面 UserCache.json 的内容）
//
// 【分区】
//   A. 数据区 —— WatchItem（一条自选）+ WatchCache（整个文件）
//
// 【这个文件负责】描述缓存文件长什么样：
//   { "watchlist": [ { "market": "sh", "code": "600519", "name": "贵州茅台" } ] }
//   属性名故意和 JSON 的键名一模一样（market / code / name / watchlist），
//   这样读写两边都不用再写映射，将来改格式也只动这一个文件。
//   两个类都写成 public：序列化器要能访问到它们。
// ═══════════════════════════════════════════════════════════════════════════
using System.Text.Json.Serialization;

namespace 炒股小助手
{
    // 一条自选
    public class WatchItem
    {
        // ══════════════════ A. 数据区 ══════════════════
        public string market { get; set; } = "";   // 市场前缀：sh / sz / us / us.
        public string code { get; set; } = "";     // 纯代码（不带前缀）：600519 / 159248 / NVDA
        public string name { get; set; } = "";     // 名称：贵州茅台 / 英伟达

        // 现拼的完整腾讯代码：market + code（sz + 159248 → sz159248）
        // [JsonIgnore] = 不写进文件（它是算出来的，存进去只会多一个没用的字段）
        [JsonIgnore]
        public string tenCode => market + code;
    }

    // 整个缓存文件（里面就一个自选清单）
    public class WatchCache
    {
        // ══════════════════ A. 数据区 ══════════════════
        public List<WatchItem> watchlist { get; set; } = new();   // 文件里的自选清单
    }
}
