// ═══════════════════════════════════════════════════════════════════════════
// Heartbeat.cs —— 心跳
//
// 【分区】
//   A. 主流程区   —— 计时器开关 + 每次跳一拍干什么（Tick）
//   B. 推送区     —— 把拉好的数据整理 / 推给主页面和自选股页
//   C. 分组区     —— 把一次拉回来的盘口分成"主页面那只 / 自选那几只"
//   D. 取数区     —— 盘口、分钟K、数据时间戳怎么拉，查询串怎么拼
//   E. 市场状态区 —— 每 1 分钟问一次"市场开没开"
//   F. 接收区     —— 外面把页面实例、自选代码交进来
//   G. 小工具区   —— 判断代码属于哪个市场、解析时间 / 数字 / K 线
//   H. 声明区     —— 计时器、请求器、接口地址
//   I. 数据区     —— 查询串的原料、拉回来的各种表、市场开关、时间戳
//
// 【这个文件负责】按"数据时间戳变没变"决定要不要拉数据，并把结果推给主页面和自选股页。
//   它不认识界面上的控件，只调这两个页面公开的几个入口方法。
// ═══════════════════════════════════════════════════════════════════════════
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace 炒股小助手
{
    public class Heartbeat
    {
        // ══════════════════ A. 主流程区 ══════════════════
        // 本区方法：
        //   Start   —— 启动两个计时器
        //   Pause   —— 暂停心跳（自选变动时短暂停一下）
        //   Resume  —— 恢复心跳
        //   Tick    —— 心跳本体：每 0.5 秒一跳
        //
        // 【一跳的流程】（下面写的是 A股那一路；美股那一路一模一样，只是换成英伟达和美股的表）
        //   ① A股开市？
        //   └ 拉宇树科技的时间戳
        //     └ 时间戳变了？（变了才说明市场有新数据）
        //       ├ 主页面是 A股？
        //       │  ├ 是 → 临时代码集 = 主页面 + A股自选 → 拉盘口 → 分离（第一条主页面、后面自选）→ 推主页面盘口
        //       │  │       → 跨分钟了？就再拉分钟K 推过去（这一段就在"是"这一支里面）
        //       │  └ 否 → 代码集 = 只有 A股自选 → 拉盘口（这一路只管自选股页，不动主页面）
        //   ② 美股开市？（同上）
        //   ③ 不管前面有没有更新，最后都推一次自选股

        // 接收：无　返回：无　改变：两个计时器开始跑（心跳 0.5 秒、市场状态 1 分钟）
        public void Start()
        {
            marketTimer.Interval = TimeSpan.FromMinutes(1);   // 市场状态：1 分钟一跳
            marketTimer.Tick += MarketTimer_Tick;             // 挂上处理函数
            marketTimer.Start();                              // 开跑

            timer.Interval = TimeSpan.FromSeconds(0.5);       // 心跳：0.5 秒一跳
            timer.Tick += Tick;                               // 挂上处理函数
            timer.Start();                                    // 开跑
        }

        // 接收：无　返回：无　改变：心跳计时器停下（1 分钟那个市场状态计时器不动）
        public void Pause()
        {
            timer.Stop();
        }

        // 接收：无　返回：无　改变：心跳计时器继续跑（间隔在 Start 里已经设过）
        public void Resume()
        {
            timer.Start();
        }

        // 接收：计时器事件参数　返回：无　改变：无（真正的活在下面的 TickBody 里，这里只兜一层网）
        private async void Tick(object? sender, EventArgs e)
        {
            try
            {
                await TickBody();
            }
            catch
            {
                // 兜底：这一跳出意外就吞掉，等 0.5 秒后的下一跳重来。
                // 【为什么必须有这一层】Tick 是 async void（计时器事件只能是这个签名），
                //   里面漏出去的异常会直接交给 Dispatcher —— 那是【整个程序退出】，不是"这次失败"。
                //   心跳每 0.5 秒一跳，失败一两次无所谓，程序活着更重要。
            }
        }

        // 接收：无　返回：无　改变：按上面的流程走一遍（可能拉数据并刷两个页面）
        private async Task TickBody()
        {
            if (aShareMarketOpen)                             // 基于"A股开没开"判断要不要处理 A股这一路
            {
                await FetchUnitreeStampAsync();               // 拉宇树科技的时间戳（写进新值）

                if (unitreeSecond != UnitreeSecond)           // 基于"新旧时间戳是否相同"判断有没有新数据
                {
                    unitreeSecond = UnitreeSecond;            // 记账：先搬新值，免得同一批数据重复拉

                    if (MainMarket == '1')                    // 基于"主页面是不是 A股"分开处理
                    {
                        // 临时代码集 = 主页面代码 + A股自选代码集
                        aShareQuotes = await FetchQuoteAsync(BuildQuery(MainCode, aShareWatchCodes));
                        // 分离：按代码挑出主页面的那只，其余全是自选股的
                        SplitQuoteTable(aShareQuotes, true, MainCode, aShareMainQuotes, aShareWatchQuotes);
                        // 推数据到主页面更新实时盘口 UI
                        MainOrderbookUpdate(aShareMainQuotes, unitreeSecond);   // 推主页面的盘口图 + 实时区"盘口"格

                        if (unitreeMinute != UnitreeMinute)                     // 基于"分钟位变没变"判断要不要拉分钟K
                        {
                            unitreeMinute = UnitreeMinute;                                   // 记账
                            aShareMinuteCandles = await FetchMinuteKAsync(MainCode ?? "");   // 拉分钟K
                            MainMinbarUpdate(aShareMinuteCandles, unitreeMinute);            // 推给主页面刷图
                        }
                    }
                    else                                      // 主页面不是 A股：这一路只管 A股自选
                    {
                        // 只用 A股自选代码集拉数据（不带主页面代码）
                        aShareQuotes = await FetchQuoteAsync(BuildQuery(null, aShareWatchCodes));
                        SplitQuoteTable(aShareQuotes, false, MainCode, aShareMainQuotes, aShareWatchQuotes);
                    }
                }
            }

            if (usMarketOpen)                                 // 基于"美股开没开"判断要不要处理美股这一路
            {
                await FetchNvidiaStampAsync();                // 拉英伟达的时间戳（写进新值）

                if (nvidiaSecond != NvidiaSecond)             // 基于"新旧时间戳是否相同"判断有没有新数据
                {
                    nvidiaSecond = NvidiaSecond;              // 记账

                    if (MainMarket == '2')                    // 基于"主页面是不是美股"分开处理
                    {
                        // 临时代码集 = 主页面代码 + 美股自选代码集
                        usQuotes = await FetchQuoteAsync(BuildQuery(MainCode, usWatchCodes));
                        // 分离：按代码挑出主页面的那只，其余全是自选股的
                        SplitQuoteTable(usQuotes, true, MainCode, usMainQuotes, usWatchQuotes);
                        // 推数据到主页面更新实时盘口 UI
                        MainOrderbookUpdate(usMainQuotes, nvidiaSecond);        // 推主页面的盘口图 + 实时区"盘口"格

                        if (nvidiaMinute != NvidiaMinute)                       // 基于"分钟位变没变"判断要不要拉分钟K
                        {
                            nvidiaMinute = NvidiaMinute;                                     // 记账
                            usMinuteCandles = await FetchMinuteKAsync(MainCode ?? "");       // 拉分钟K
                            MainMinbarUpdate(usMinuteCandles, nvidiaMinute);                 // 推给主页面刷图
                        }
                    }
                    else                                      // 主页面不是美股：这一路只管美股自选
                    {
                        // 只用美股自选代码集拉数据（不带主页面代码）
                        usQuotes = await FetchQuoteAsync(BuildQuery(null, usWatchCodes));
                        SplitQuoteTable(usQuotes, false, MainCode, usMainQuotes, usWatchQuotes);
                    }
                }
            }

            WatchlistOrderbookUpdate();   // 最后一步：不管前面有没有更新，都推一次自选股
        }

        // ══════════════════ B. 推送区 ══════════════════
        // 本区方法：
        //   MainOrderbookUpdate      —— 主页面盘口
        //   MainMinbarUpdate         —— 主页面分钟K
        //   WatchlistOrderbookUpdate —— 自选股列表
        //   RefreshWatchlistAsync    —— 主动拉一遍自选股再推送

        // 接收：主页面那只的盘口表 + 秒级时间戳　返回：无　改变：主页面实时区"盘口"格 + 盘口图
        private void MainOrderbookUpdate(List<Quote> mainQuotes, string stamp)
        {
            mainPage?.RefreshQuoteStamp(stamp);      // 报时间
            mainPage?.RefreshQuoteUI(mainQuotes);    // 推盘口表
        }

        // 接收：分钟K 表 + 分钟级时间戳　返回：无　改变：主页面实时区"分钟K"格 + 分钟K 图
        private void MainMinbarUpdate(List<Candle> minuteCandles, string stamp)
        {
            mainPage?.RefreshMinuteStamp(stamp);        // 报时间
            mainPage?.RefreshMinuteKUI(minuteCandles);  // 推蜡烛表
        }

        // 接收：无　返回：无　改变：自选股页的列表
        private void WatchlistOrderbookUpdate()
        {
            // 用"代码 → 数据"的字典收集候选：同一只票只留一份（后放进去的覆盖先放的）
            var byCode = new Dictionary<string, Quote>();

            foreach (Quote q in aShareQuotes)   // 基于"A股那一路拉回来的原始表"收集（它最全：主页面那只 + A股自选）
                if (!string.IsNullOrEmpty(q.Code)) byCode[q.Code] = q;
            foreach (Quote q in usQuotes)       // 基于"美股那一路拉回来的原始表"收集
                if (!string.IsNullOrEmpty(q.Code)) byCode[q.Code] = q;

            Quote? mainQuote = mainPage?.quote;   // 主页面正在看的那只，数据最新，最后放进去覆盖同代码的旧数据
            if (mainQuote != null && !string.IsNullOrEmpty(mainQuote.Code) && !string.IsNullOrEmpty(mainQuote.Name))
                byCode[mainQuote.Code] = mainQuote;

            // 按"自选代码表"的顺序输出：顺序稳定、自动去重、自动过滤掉已删除的自选
            var all = new List<Quote>();
            foreach (string code in aShareWatchCodes)   // A股自选，按表序
                if (byCode.ContainsKey(code)) all.Add(byCode[code]);
            foreach (string code in usWatchCodes)       // 美股自选，按表序
                if (byCode.ContainsKey(code)) all.Add(byCode[code]);

            watchlistPage?.RefreshWatchlistUI(all);     // 推给自选股页
        }

        // 接收：无　返回：无　改变：两张自选表对应的数据 + 自选股页列表
        // 说明：启动时调一次（让自选股页一打开就有内容），自选加了/删了之后也调（新那只马上有数据）。
        //   这里不看开市没开市 —— 闭市时拉回来的是最近一个交易日的收盘数据，照样能显示。
        public async Task RefreshWatchlistAsync()
        {
            string aQuery = BuildMarketQuery('1');   // A股这一路要查什么（主页面是 A股就带上它）
            if (!string.IsNullOrEmpty(aQuery))       // 基于"查询串空不空"判断要不要拉这一路
            {
                aShareQuotes = await FetchQuoteAsync(aQuery);                     // 拉
                SplitQuoteTable(aShareQuotes, MainMarket == '1', MainCode,        // 按代码挑主页面那只（带了才挑）
                                aShareMainQuotes, aShareWatchQuotes);
            }

            string usQuery = BuildMarketQuery('2');  // 美股这一路要查什么
            if (!string.IsNullOrEmpty(usQuery))
            {
                usQuotes = await FetchQuoteAsync(usQuery);
                SplitQuoteTable(usQuotes, MainMarket == '2', MainCode, usMainQuotes, usWatchQuotes);
            }

            WatchlistOrderbookUpdate();               // 推送
        }

        // ══════════════════ C. 分组区 ══════════════════
        // 本区方法：SplitQuoteTable

        // 接收：一次拉回来的表 + 这一路有没有拼主页面代码 + 主页面的代码 + 两张空表　返回：无
        // 改变：把这张表分到"主页面表 / 自选表"里
        // 【为什么按代码认、不按位置认】请求时主页面代码确实排在最前面，看起来"第一条就是它"。
        //   但如果那只票没查到（代码写错 / 停牌 / 接口没返回那一行），回来的第一条其实是自选股 ——
        //   按位置认就会把自选股当成主页面那只，主页面盘口图会显示成别人的数据（串票）。
        //   按代码认没这个问题：找不到就主页面表留空，页面那边对空表会保留旧数据、不会乱显示。
        private static void SplitQuoteTable(List<Quote> quotes, bool withMain, string? mainCode,
                                            List<Quote> mainQuotes, List<Quote> watchQuotes)
        {
            mainQuotes.Clear();              // 清空上次的结果
            watchQuotes.Clear();
            if (quotes.Count == 0) return;   // 一只都没拉到

            if (!withMain)                   // 这一路没拼主页面代码：整张表都是自选
            {
                watchQuotes.AddRange(quotes);
                return;
            }

            foreach (Quote q in quotes)      // 带了主页面代码：按代码把主页面那只挑出来
            {
                if (!string.IsNullOrEmpty(mainCode) && q.Code == mainCode) mainQuotes.Add(q);
                else watchQuotes.Add(q);
            }
        }

        // ══════════════════ D. 取数区 ══════════════════
        // 本区方法：
        //   FetchQuoteAsync        —— 拉实时盘口（可一次传多只）
        //   FetchMinuteKAsync      —— 拉当日每分钟K
        //   FetchUnitreeStampAsync —— 拉宇树科技的时间戳
        //   FetchNvidiaStampAsync  —— 拉英伟达的时间戳
        //   FetchStampAsync        —— 拉一行盘口，只取里面的数据时间
        //   BuildQuery             —— 拼"主页面 + 一组自选"的查询串
        //   BuildMarketQuery       —— 拼"某个市场这一路"要查的查询串（内部调 BuildQuery）

        // 接收：腾讯代码（可逗号串多只）　返回：拉到的每一只的盘口　改变：无（只读网络）
        public async Task<List<Quote>> FetchQuoteAsync(string tenCode)
        {
            var result = new List<Quote>();
            if (string.IsNullOrEmpty(tenCode)) return result;   // 基于"有没有代码"判断要不要发请求

            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + tenCode);   // 请求盘口
                string text = Encoding.GetEncoding("GBK").GetString(bytes);                       // 腾讯用 GBK 编码

                foreach (string line in text.Split(';'))                                          // 返回是一行一只，逐行解析
                {
                    string s = line.Trim();
                    if (s == "" || !s.StartsWith("v_")) continue;                                 // 空行 / 非行情行 → 跳过

                    int eq = s.IndexOf('=');
                    if (eq < 0) continue;                                                         // 没有 = 的行 → 跳过

                    string code = s.Substring(2, eq - 2);                                         // v_ 和 = 之间就是代码
                    string[] f = s.Substring(eq + 1).Trim('"').Split('~');                        // 引号里的字段按 ~ 切开
                    if (f.Length < 39) continue;                                                  // 字段不够（多半没这只票）→ 跳过

                    var quote = new Quote { Code = code };                                        // 建一条，带上代码
                    quote.Name = f[1];                                                            // [1] 名称
                    quote.Price = ToNum(f[3]);  quote.PreClose = ToNum(f[4]);  quote.Open = ToNum(f[5]);   // [3][4][5] 现价 / 昨收 / 今开
                    quote.Volume = ToNum(f[6]);                                                          // [6] 成交量
                    quote.Buy1 = ToNum(f[9]);   quote.Buy1v = ToNum(f[10]);                              // 买一 价 / 量
                    quote.Buy2 = ToNum(f[11]);  quote.Buy2v = ToNum(f[12]);                              // 买二 价 / 量
                    quote.Buy3 = ToNum(f[13]);  quote.Buy3v = ToNum(f[14]);                              // 买三 价 / 量
                    quote.Buy4 = ToNum(f[15]);  quote.Buy4v = ToNum(f[16]);                              // 买四 价 / 量
                    quote.Buy5 = ToNum(f[17]);  quote.Buy5v = ToNum(f[18]);                              // 买五 价 / 量
                    quote.Sold1 = ToNum(f[19]); quote.Sold1v = ToNum(f[20]);                             // 卖一 价 / 量
                    quote.Sold2 = ToNum(f[21]); quote.Sold2v = ToNum(f[22]);                             // 卖二 价 / 量
                    quote.Sold3 = ToNum(f[23]); quote.Sold3v = ToNum(f[24]);                             // 卖三 价 / 量
                    quote.Sold4 = ToNum(f[25]); quote.Sold4v = ToNum(f[26]);                             // 卖四 价 / 量
                    quote.Sold5 = ToNum(f[27]); quote.Sold5v = ToNum(f[28]);                             // 卖五 价 / 量
                    quote.Change = ToNum(f[31]);   quote.ChangePct = ToNum(f[32]);                       // [31][32] 涨跌额 / 涨跌幅
                    quote.High = ToNum(f[33]);     quote.Low = ToNum(f[34]);                             // [33][34] 最高 / 最低
                    quote.Amount = ToNum(f[37]);   quote.Turnover = ToNum(f[38]);                        // [37][38] 成交额 / 换手率
                    quote.Time = ParseStamp(f[30]);                                                     // [30] 数据时间

                    result.Add(quote);
                }
            }
            catch { }   // 请求失败 → 返回已经拼好的部分（可能是空表）

            return result;
        }

        // 接收：腾讯代码　返回：这只票当天的每分钟K（拉不到给空表）　改变：无（只读网络）
        public async Task<List<Candle>> FetchMinuteKAsync(string tenCode)
        {
            var candles = new List<Candle>();
            if (string.IsNullOrEmpty(tenCode)) return candles;   // 基于"有没有代码"判断要不要发请求

            try
            {
                string url = "https://ifzq.gtimg.cn/appstock/app/kline/mkline?" +
                             $"param={tenCode},m1,,300";                                    // m1 = 一分钟
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty(tenCode).GetProperty("m1");   // 取出 m1 数组

                var all = new List<Candle>();
                foreach (var row in arr.EnumerateArray())              // 一行一根
                {
                    Candle? k = ToCandle(row, "yyyyMMddHHmm");
                    if (k != null) all.Add(k);                         // 脏数据行（解析不出来）直接跳过，别让它毁了整批
                }

                string lastDay = all.Count > 0 ? all[^1].Date.ToString("yyyyMMdd") : "";   // 接口会带上前几天，取最后一天 = 当天
                foreach (Candle k in all)                                                  // 基于"这根是不是当天的"筛选
                    if (k.Date.ToString("yyyyMMdd") == lastDay) candles.Add(k);
            }
            catch { }   // 拉不到 → 返回空表

            return candles;
        }

        // 接收：无　返回：无　改变：宇树科技的新时间戳（秒级 / 分钟级两个字段）
        private async Task FetchUnitreeStampAsync()
        {
            (string second, string minute) = await FetchStampAsync(UnitreeTenCode);
            UnitreeSecond = second;
            UnitreeMinute = minute;
        }

        // 接收：无　返回：无　改变：英伟达的新时间戳（秒级 / 分钟级两个字段）
        private async Task FetchNvidiaStampAsync()
        {
            (string second, string minute) = await FetchStampAsync(NvidiaTenCode);
            NvidiaSecond = second;
            NvidiaMinute = minute;
        }

        // 接收：腾讯代码　返回：秒级 / 分钟级两个时间戳（拉不到给两个空串）　改变：无
        private async Task<(string second, string minute)> FetchStampAsync(string tenCode)
        {
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + tenCode);
                string raw = Encoding.GetEncoding("GBK").GetString(bytes).Split('~')[30];   // 下标 30 = 数据时间

                DateTime? t = ParseStamp(raw);
                if (!t.HasValue) return ("", "");                          // 解析不出来 → 空串（调用方就知道这次没拿到）

                return (t.Value.ToString("yyyyMMddHHmmss"),                // 秒级
                        t.Value.ToString("yyyyMMddHHmm"));                 // 分钟级
            }
            catch { return ("", ""); }
        }

        // 接收：要拼在最前面的主页面代码（没有就给 null）+ 一组自选代码　返回：逗号串（已去重）　改变：无
        // 说明：主页面代码排最前面 —— 拉回来之后第一条就是它的数据，分离时靠这个"第一条"来认。
        private static string BuildQuery(string? mainCode, List<string> watchCodes)
        {
            var codes = new List<string>();
            if (!string.IsNullOrEmpty(mainCode)) codes.Add(mainCode);   // 主页面那只放最前面
            codes.AddRange(watchCodes);                                 // 接上自选
            return string.Join(",", codes.Distinct());                  // 去重后连起来
        }

        // 接收：市场标记（'1' = A股，'2' = 美股）　返回：这个市场要查的代码串　改变：无
        // 说明：主页面那只正好属于这个市场才带上它，否则这一路只有这个市场的自选。
        private string BuildMarketQuery(char market)
        {
            string? main = (MainMarket == market) ? MainCode : null;   // 基于"主页面属不属于这个市场"决定带不带
            return BuildQuery(main, market == '1' ? aShareWatchCodes : usWatchCodes);
        }

        // ══════════════════ E. 市场状态区 ══════════════════
        // 本区方法：MarketTimer_Tick、FetchMarketStatusAsync

        // 接收：计时器事件参数　返回：无　改变：发起一次市场状态查询
        private async void MarketTimer_Tick(object? sender, EventArgs e)
        {
            try { await FetchMarketStatusAsync(); }
            catch { }   // 兜底：这次出意外就吞掉，等下一分钟重来（同样是因为 async void 不能让异常漏出去）
        }

        // 接收：无　返回：无　改变：两个"市场开没开"的开关
        // 说明：接口返回的是一家家交易所，同一组里只要有一家开着就算这个市场开着。
        private async Task FetchMarketStatusAsync()
        {
            try
            {
                string json = await http.GetStringAsync(StatusUrl);
                using JsonDocument doc = JsonDocument.Parse(json);

                bool aOpen = false;   // A股本次结果
                bool usOpen = false;  // 美股本次结果

                foreach (JsonElement exchange in doc.RootElement.GetProperty("exchanges").EnumerateArray())   // 逐个交易所看
                {
                    string id = exchange.GetProperty("id").GetString() ?? "";
                    bool isOpen = exchange.GetProperty("open").GetBoolean();

                    if (id is "sse" or "szse") aOpen |= isOpen;       // 上交所 / 深交所 → A股
                    if (id is "nyse" or "nasdaq") usOpen |= isOpen;   // 纽交所 / 纳斯达克 → 美股
                }

                aShareMarketOpen = aOpen;   // 整轮解析成功才覆盖旧值
                usMarketOpen = usOpen;
            }
            catch { }   // 这次没拉到：保留上一次的状态，等下一分钟
        }

        // ══════════════════ F. 接收区 ══════════════════
        // 本区方法：SetWatchCodes、SetMainPage、SetWatchlistPage
        // 说明：这些是外面（MainWindow）把"要用的东西"交进来，心跳自己不主动去找。

        // 接收：两组自选代码　返回：无　改变：aShareWatchCodes / usWatchCodes（直接共用页面那两个 List）
        public void SetWatchCodes(List<string> aShareCodes, List<string> usCodes)
        {
            aShareWatchCodes = aShareCodes;
            usWatchCodes = usCodes;
        }

        // 接收：主页面实例　返回：无　改变：mainPage
        public void SetMainPage(MainPage page)
        {
            mainPage = page;
        }

        // 接收：自选股页实例　返回：无　改变：watchlistPage
        public void SetWatchlistPage(WatchlistPage page)
        {
            watchlistPage = page;
        }

        // ══════════════════ G. 小工具区 ══════════════════
        // 本区方法：IsWatchCode、ParseStamp、ToCandle、PickKArray、ToNum

        // 接收：腾讯代码　返回：在不在自选代码集里　改变：无
        private bool IsWatchCode(string? tenCode)
        {
            if (string.IsNullOrEmpty(tenCode)) return false;
            return aShareWatchCodes.Contains(tenCode) || usWatchCodes.Contains(tenCode);
        }

        // 接收：时间字符串　返回：DateTime（都解析不出来给 null）　改变：无（挨个格式试，接口时间字段有三种写法）
        private static DateTime? ParseStamp(string s)
        {
            foreach (string format in StampFormats)   // 一个一个试
                if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out DateTime t))
                    return t;
            return null;
        }

        // 接收：一行 JSON（[时间, 开, 收, 高, 低, ...]）+ 时间格式　返回：一根 K 线（这行是脏数据就给 null）　改变：无
        // 【为什么返回可空 + 全用 TryParse】以前这里用 Parse / ParseExact：只要有一行脏数据
        //   （空串、格式对不上）就会抛异常，把整批 K 线的解析打断 —— 结果就是"只画出前面几根"。
        //   现在改成：单行解析不了就返回 null，调用方跳过它 —— 宁可少一根柱，也不要画出错的。
        private static Candle? ToCandle(JsonElement row, string timeFormat)
        {
            // 时间格式必须对得上，对不上就整行丢掉
            if (!DateTime.TryParseExact(row[0].GetString() ?? "", timeFormat, CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out DateTime date))
                return null;

            // 四个价格：任何一个解析不出来，也整行丢掉
            if (!decimal.TryParse(row[1].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal open)) return null;
            if (!decimal.TryParse(row[2].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal close)) return null;
            if (!decimal.TryParse(row[3].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal high)) return null;
            if (!decimal.TryParse(row[4].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal low)) return null;

            if (open <= 0) open = close;   // 脏数据兜底：没给"开"就用"收"顶上，免得画歪

            return new Candle
            {
                Date  = date,
                Open  = open,
                Close = close,
                High  = Math.Max(high, Math.Max(open, close)),   // 保证 高 >= max(开,收)
                Low   = Math.Min(low, Math.Min(open, close)),    // 保证 低 <= min(开,收)
            };
        }

        // 接收：接口返回的那一只票的节点　返回：K 线数组　改变：无（找不到就抛异常，由调用方的 catch 接住）
        private static JsonElement PickKArray(JsonElement node)
        {
            foreach (string key in new[] { "qfqday", "day", "hfqday" })   // 三种可能的键名
                if (node.TryGetProperty(key, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr;

            throw new Exception("接口返回里没有 K 线数组（qfqday / day / hfqday 都没找到）");
        }

        // 接收：字符串　返回：小数（转不了给 null）　改变：无
        private static decimal? ToNum(string s)
            => decimal.TryParse(s, out decimal v) ? v : (decimal?)null;

        // ══════════════════ H. 声明区 ══════════════════
        // 说明：这一区是"干活要用的零件"，不是业务数据。
        private readonly DispatcherTimer timer = new();         // 心跳计时器（0.5 秒一跳）
        private readonly DispatcherTimer marketTimer = new();   // 市场状态计时器（1 分钟一跳）
        private readonly HttpClient http = new();               // 发请求用
        private const string StatusUrl = "https://api.tradeti.me/v1/status";   // 市场状态接口地址

        // ══════════════════ I. 数据区 ══════════════════
        // ── 查询串的原料 ──
        public string? MainCode;                        // 主页面当前标的（没搜过 = null）
        public List<string> aShareWatchCodes = new();   // A股自选的完整腾讯代码
        public List<string> usWatchCodes = new();       // 美股自选的完整腾讯代码

        // ── 推数据要用的页面引用 ──
        private MainPage? mainPage;                     // 主页面实例
        private WatchlistPage? watchlistPage;           // 自选股页实例

        // ── 主页面这只属于哪个市场（搜索时由主页面传进来）──
        public char MainMarket = '0';   // '1' = A股，'2' = 美股，'0' = 还没搜过

        // ── 两路拉回来的数据 ──
        public List<Quote> aShareQuotes = new();           // A股：原始盘口表（主页面那只 + A股自选）
        public List<Quote> aShareMainQuotes = new();       // A股：分家后的主页面那只
        public List<Quote> aShareWatchQuotes = new();      // A股：分家后的自选那几只
        public List<Candle> aShareMinuteCandles = new();   // A股：主页面标的的分钟K

        public List<Quote> usQuotes = new();               // 美股：原始盘口表
        public List<Quote> usMainQuotes = new();           // 美股：分家后的主页面那只
        public List<Quote> usWatchQuotes = new();          // 美股：分家后的自选那几只
        public List<Candle> usMinuteCandles = new();       // 美股：主页面标的的分钟K

        // ── 市场开没开（Tick 的两个开关；启动时问一次，之后每 1 分钟更新）──
        public bool usMarketOpen;         // 美股开没开
        public bool aShareMarketOpen;     // A股开没开

        // ── 两个"节拍标的"：用它们的数据时间当市场节拍 ──
        private const string UnitreeTenCode = "sh688836";   // A股：宇树科技
        private const string NvidiaTenCode = "usNVDA";      // 美股：英伟达

        // ── 时间戳（小写 = 旧值，大写 = 新值；每个标的存秒级 / 分钟级两种）──
        private string unitreeSecond = "", unitreeMinute = "";   // 宇树：旧值
        private string UnitreeSecond = "", UnitreeMinute = "";   // 宇树：新值
        private string nvidiaSecond = "", nvidiaMinute = "";     // 英伟达：旧值
        private string NvidiaSecond = "", NvidiaMinute = "";     // 英伟达：新值

        // ── 时间字符串的格式表（解析接口时间字段时挨个试）──
        private static readonly string[] StampFormats =
        {
            "yyyyMMddHHmmss",      // A股 / 北交所
            "yyyy-MM-dd HH:mm:ss", // 美股
            "yyyy/MM/dd HH:mm:ss", // 港股
        };
    }
}
