// ═══════════════════════════════════════════════════════════════════════════
// DeadCode.cs —— 暂时用不上的代码存档
//
// 【这个文件是干什么的】把项目里"确认没人调用、但看着像是当初特意写的"方法搬到这里，
//   整段注释起来存放。既不参与编译（注释掉的东西编译器看不见），也不占运行开销。
//
// 【搬进来的规矩】
//   ① 只搬"确认没有任何调用者"的方法（搬之前全项目搜过方法名）；
//   ② 整段用行注释 // 注释掉，不删原文，方便以后捞回来；
//   ③ 每条写清楚：原来在哪个文件、什么时候搬的、为什么用不上了、以后什么时候可能用得上。
//
// 【什么时候可以删】确定以后也不会用了，整个文件删掉即可（它不参与编译，删了不影响功能）。
//
// 注：这个文件里没有任何可执行的 C# 代码，全部是注释，所以不需要 using，也不会编译出东西。
// ═══════════════════════════════════════════════════════════════════════════

namespace 炒股小助手
{
    // 这个类也是空的，纯粹给下面那些注释一个"落脚的地方"，让文件看起来有条理。
    internal static class DeadCodeArchive
    {
        // ═══════════════════════════════════════════════════════════════════
        // 【1】MainPage.KGet()　—— 原来在 MainPage.xaml.cs
        //   搬进来：2026-09-29
        //   做的事：拉一次分钟K，然后刷新分钟K 图和实时区那格。
        //   为什么用不上了：它是老版心跳用的（当时心跳跨分钟就调它）。
        //     新心跳拉完分钟K 之后，直接调 MainPage.RefreshMinuteKUI(表) 把数据推过来，
        //     不用页面自己去拉；搜索那条路走的是 LoadKChartsAsync（先清空再画），也不经过它。
        //   以后可能用得上：如果想让"主页面自己定时刷新分钟K"（不靠心跳推），它是现成的。
        //
        // private async Task KGet()   // 分钟K 总入口（KGet = 分钟K Get）：拉分钟K → 刷图
        // {
        //     // 拉到就刷图；没拉到就什么都不做（图上留着同一只标的的旧数据，不动它）
        //     if (await FetchMinuteKAsync())     // 拉分钟K（G 区）
        //     {
        //         RefreshTimeBar();     // 实时区"分钟K"那格跟着更新（E 区）
        //
        //         // 灌新数据但不重置视图：自动刷新时不能把用户当前的缩放和位置顶掉
        //         KChartMin.SetData(MinuteCandles, resetView: false);
        //     }
        // }
        // ═══════════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════════
        // 【2】Heartbeat.FetchDailyKAsync(string tenCode)　—— 原来在 Heartbeat.cs
        //   搬进来：2026-09-29
        //   做的事：传一个腾讯代码进去，拉这只票的历史日K（前复权），返回蜡烛列表。
        //   为什么用不上了：心跳的活是"盯实时数据变没变"，而日K 按设计只在搜索时更新
        //     （一天才一根，跟着心跳刷没意义）。它是当初把主页面的三个取数方法
        //     一起复制到心跳时顺带搬过来的，复制完就没被调用过。
        //   以后可能用得上：如果想让日K 也自动更新（比如收盘后补一次），它是现成的 ——
        //     它已经是"代码从参数进来、结果 return 出去"的形态，比主页面里那份更好接。
        //
        // public async Task<List<Candle>> FetchDailyKAsync(string tenCode)
        // {
        //     var candles = new List<Candle>();                             // 结果表
        //     if (string.IsNullOrEmpty(tenCode)) return candles;            // 没代码就不发请求
        //
        //     try
        //     {
        //         string url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?" +
        //                      $"param={tenCode},day,,,320,qfq";             // 320 根够用
        //         string json = await http.GetStringAsync(url);              // 请求
        //         using var doc = JsonDocument.Parse(json);                  // 解析 JSON
        //
        //         JsonElement node = doc.RootElement.GetProperty("data").GetProperty(tenCode);
        //         JsonElement arr = PickKArray(node);                        // 挑出 K 线数组
        //
        //         foreach (var row in arr.EnumerateArray())                  // 一行一根
        //             candles.Add(ToCandle(row, "yyyy-MM-dd"));              // 转成 Candle 收下
        //     }
        //     catch { }   // 拉不到 → 返回空表
        //
        //     return candles;
        // }
        //
        // 【要捞回来的话，还需要的配件】下面两样没被搬走（别处在用）：
        //   · 解析工具 ToCandle(row, timeFormat) / PickKArray(node) —— 心跳里还在（拉分钟K 用）
        //   · 请求器 http —— 心跳的字段
        // ═══════════════════════════════════════════════════════════════════
    }
}
