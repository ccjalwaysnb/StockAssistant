// ═══════════════════════════════════════════════════════════════════════════
// MainPage.xaml.cs —— 主界面逻辑（盘口图 + K 图 + 实时区）
//
// 【分区】这个文件按功能区切块，从上往下读相当于走一遍程序：
//     A. 字段区      —— 所有状态变量（计时器 / 市场搜索 / 盘口数据 / K 线数据 / 实时区时间 / 颜色）
//     B. 初始化区    —— MainPage() 构造函数：控件初始化、GBK 注册、HTTP 头、冷却计时器参数
//     C. 心跳区      —— StartHeartbeat()：0.5 秒拉一次盘口时间戳 a，
//                       转出 b（秒级）/ c（分钟级），做两层嵌套判断（日K 不在心跳里）
//     D. 盘口图区    —— 上面那整块行情区：拉 + 刷（PanGet / RefreshQuoteUI / 展示小工具）
//     E. 实时区      —— K 图下面那行小字：盘口 / 日K / 分钟K 三个数据时间
//     F. K 图区      —— 什么时候拉 K 线、灌给哪张图、日K / 分钟K 切换（那两个切换按钮算这一区）
//     G. 数据拉取区  —— 拉取方法 + 解析小工具（只把数据写进字段，一律不碰界面）
//                       一个拉时间戳的 + 三个拉数据的
//     H. 按键区      —— 搜索键、冷却倒计时、市场选择（K 图那两个按钮不在这，在 F 区）
//
// 【谁调谁】顺着这幅图看代码，不会迷路：
//     程序启动 → App.MainFlow 第 ④ 步 → C 区 StartHeartbeat()
//     点搜索   → H 区 Search_Click() → H 区 SearchReloadAsync()：无视前提，三个数据全拉一遍
//     心跳节拍 → G 区 FetchTimeAsync() 拿 a，转出 b / c
//                  → 两层嵌套的 if（盘口比 b → 分钟K 比 c）决定要拉哪个数据
//     数据流   → G 区只管"拉数据存字段"，界面统一由 D / E / F 区去刷
//
// 【盘口数据放哪】盘口那二十多项数值不再平铺在字段区，全在 A3 的 quote 对象里（模型见 Quote.cs）。
// ═══════════════════════════════════════════════════════════════════════════
using System.Globalization;        // CultureInfo：解析接口返回的小数/日期
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;                 // Encoding（GBK 解码）
using System.Text.Json;            // JsonDocument（K 线 JSON 解析）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;        // KeyEventArgs（输入框回车绑搜索键用）
using System.Windows.Media;
using System.Windows.Threading;

namespace 炒股小助手
{
    public partial class MainPage : UserControl
    {
        // ══════════════════ A. 字段区 ══════════════════

        // ── A1. 计时器与请求器 ──
        private readonly DispatcherTimer cooldownTimer = new();   // 搜索冷却（1 秒一次 tick）
        private readonly DispatcherTimer timeTimer = new();       // 心跳：三个数据时间戳轮询（0.5 秒）
        private readonly HttpClient http = new HttpClient();      // 共用请求器（全部走腾讯）
        private bool heartbeatStarted;                            // 心跳只允许启动一次（防止重复订阅）

        // ── A2. 标的与搜索状态 ──
        // 市场档位：'0' 未选 / '1' 沪市 / '2' 深市 / '3' 美股 / '4' 美股2（指数）
        public char MarketMode = '0';
        public string stockNumber = "";   // 搜索框里的原始输入，如 159248 / AAPL / DJI（不带前缀）
        public string? TenNumber;         // 腾讯代码 = 市场前缀 + stockNumber，如 sz159248（请求接口用它）
        public int Cooldown = 5;          // 冷却剩余秒数（会显示在搜索按钮上）
        public bool CanSearch = true;     // false 期间连点搜索会被拦下来

        // ── A3. 盘口数据 ──
        // 一次盘口拉取拉回来的东西全装在 quote 这一个对象里（模型定义见 Quote.cs）。
        // 以前是二十多个 decimal? 字段平铺在这里，检查数据要一个个看；现在只看一个 quote 就够。
        public Quote quote = new();

        // ── A4. 拉取方法的"收货区" ──
        // 心跳靠"比时间戳"决定要不要拉数据。盘口时间戳的来龙去脉（用 a / b / c 三个名字说清）：
        //   a = 接口原文（临时变量，不存字段）
        //   b = a 转成秒级 yyyyMMddHHmmss → 给第一层"盘口"的 if 比
        //   c = a 转成分钟级 yyyyMMddHHmm → 给第二层"分钟K"的 if 比
        // 心跳每轮只拉一次接口（拿到 a），转出 b / c 就能同时喂饱两层判断。
        // 每一层各有一对"新值 / 旧值"：新值每轮刷新，拉完数据后把新值转存成旧值。
        public string TenSecond = "";               // b：盘口时间戳·秒级（新值）
        public string tentime = "";                 //    盘口时间戳·旧值（初值 "" → 没搜索过时不会误触发）
        public string TenMinute = "";               // c：盘口时间戳·分钟级（新值）
        public string tentimeMin = "";              //    c 的旧值（分钟K 那层比它）
        private DateTime? dailyTime;                // 日K·上次拉到的数据时间，同时给实时区"日K"那格显示
        private DateTime? minuteTime;               // 分钟K·上次拉到的数据时间，同时给实时区"分钟K"那格显示
        public List<Candle> DailyCandles = new();   // ④ 日K（历史每日）
        public List<Candle> MinuteCandles = new();  // ⑤ 当日每分钟K

        // ── A5. 实时区（K 图下面那行小字）用的三个"数据时间" ──
        // 三个时间就是 A4 里那三对时间戳：
        //   盘口   → quote.Time
        //   日K    → dailyTime      （上次成功拉到日K 数据时，最后一根日K 的日期）
        //   分钟K  → minuteTime     （就是分钟K时间戳的"旧值"）
        // 之所以直接复用"旧值"，是因为它本来就是"上一次成功拉到这批数据的时间"，
        // 正好就是实时区要显示的东西，不用再单独存一份。
        // 注意：它们是"数据的时间"，不是"本机此刻的时间"，所以盘中午休时会停在上一个成交时刻不动。

        // ── A6. 颜色 ──
        // K 图区切换按钮的两种底色（点中的亮蓝、没点的深灰，跟左侧导航一个风格）
        private readonly Brush KBtnActive = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xE0));
        private readonly Brush KBtnIdle = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));
        // 涨跌色（A股习惯：涨红跌绿）
        private static readonly Brush UpBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush DownBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
        private static readonly Brush FlatBrush = Brushes.White;

        // ══════════════════ B. 初始化区 ══════════════════

        // 构造函数：只做"一次性设置"，不拉数据、不切页面。
        // 这里故意不启动心跳 —— 心跳由启动流程的最后一步调用 StartHeartbeat() 启动（见 C 区）。
        public MainPage()
        {
            InitializeComponent();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 注册 GBK 支持（腾讯接口是 GBK）
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");   // 带个 UA，稳一点
            http.DefaultRequestHeaders.Add("Referer", "https://gu.qq.com/");

            // 搜索冷却计时器：1 秒一次，由 CooldownTimer_Tick（H 区）倒数
            cooldownTimer.Interval = TimeSpan.FromSeconds(1);
            cooldownTimer.Tick += CooldownTimer_Tick;

            ShowKChart(true);           // K 图区默认显示"日K"

            // K 图的标的完全由搜索决定，程序刚起来时还没有标的：
            // 两张图先摆一行提示语等着（真正拉数据在 F 区，由搜索触发）。
            KChartDay.SetEmptyText("搜索后显示日K");
            KChartMin.SetEmptyText("搜索后显示分钟K");

            RefreshTimeBar();           // 实时区三个时间先摆成 "--"
        }

        // ══════════════════ C. 心跳区 ══════════════════

        // 启动心跳（由启动流程 App.MainFlow 的第 ④ 步调用）。
        //
        // 这个 0.5 秒的节拍从"主界面刚出现"（启动流程第 ④ 步）一直跳到程序结束，中途不停。
        //
        // 心跳只管两件事：实时盘口、分钟K。
        //   第一步 FetchTimeAsync()：只拉【盘口时间戳】，原文叫 a。
        //        然后把 a 转成两种比较用的形式：
        //          b = 秒级（yyyyMMddHHmmss）→ 第一层"盘口"的 if 用
        //          c = 分钟级（yyyyMMddHHmm）→ 第二层"分钟K"的 if 用
        //   第二步 两层嵌套判断，从外到内：盘口(b) → 分钟K(c)
        //
        // 为什么分钟K 那层不用自己拉时间戳：分钟K 一分钟一根，而 c 正好是"现在第几分钟"，
        // 拿它和"上次拉的时候"一比就知道有没有跨分钟，省掉一次接口请求。
        //
        // 【日K 永久不参与心跳】日K 一天才变一次，塞在 0.5 秒一轮的心跳里毫无意义，
        //   所以这一层已经整层删除（连同天级时间戳 d）。日K 现在只在两种情况更新：
        //   ① 搜索时（H 区 SearchReloadAsync → F 区 LoadKChartsAsync）；
        //   ② 以后如果想让它盘中自动更新，另开一个"低频心跳"（比如每 5 分钟一轮）单独管。
        //
        // 每次拉完数据，都要把新值转存成旧值，下一轮才有得比。
        // 还没搜索过（没标的）时：tick 进来先看一眼 TenNumber，空的就空转，一个请求都不发。
        public void StartHeartbeat()
        {
            if (heartbeatStarted) return;   // 防重复：这个方法只该被启动流程调用一次
            heartbeatStarted = true;

            timeTimer.Interval = TimeSpan.FromSeconds(0.5);
            timeTimer.Tick += async (s, e) =>
            {
                // 空转：没有标的（还没搜过）就什么都不做，只占着这个 0.5 秒的节拍
                if (string.IsNullOrEmpty(TenNumber)) return;

                // ── 第一步：拉盘口时间戳，并转出 b（秒级）和 c（分钟级）（G 区）──
                await FetchTimeAsync();

                // ── 第二步：两层嵌套判断 ──
                // 第一层：盘口 —— 用 b（秒级）
                if (tentime != TenSecond)        // 新 b != 旧 b
                {
                    tentime = TenSecond;         // 旧值更新成新值
                    await PanGet();              // 拉实时盘口数据 + 填表（D 区）

                    // 第二层：分钟K —— 用 c（分钟级）
                    if (tentimeMin != TenMinute)     // 新 c != 旧 c
                    {
                        // 刚跨分钟的时候，接口往往还没把新那一分钟的 K 线发布出来 → 先等半秒再拉
                        await Task.Delay(TimeSpan.FromMilliseconds(500));

                        await FetchMinuteKAsync();         // 拉分钟K（G 区）
                        RefreshTimeBar();                  // 实时区"分钟K"那格跟着更新（E 区）

                        // 灌新数据但【不重置视图】：自动刷新时不能把用户当前的缩放和位置顶掉
                        KChartMin.SetData(MinuteCandles, resetView: false);

                        tentimeMin = TenMinute;            // 拉完了 → c 也记成旧值
                    }
                }
            };
            timeTimer.Start();
        }

        // ══════════════════ D. 盘口图区 ══════════════════
        // 上面那整块行情区：现价 / 涨跌 / 7 个指标格 / 买卖五档。
        // 这一区负责"拉 + 刷"，真正发请求的细节在 G 区。

        // 盘口总入口（PanGet = 盘口 Get）：拉一次盘口 → 把数据填进盘口图 → 顺手刷实时区。
        // 被两处调用：搜索键（H 区）和心跳发现有更新时（C 区）。
        private async Task PanGet()
        {
            await FetchQuoteAsync();   // ② 拉实时盘口 → 写进 quote 对象（G 区）
            RefreshQuoteUI();          // 把 quote 里的数据填进界面
            RefreshTimeBar();          // 实时区的"盘口"那格跟着更新（E 区）
        }

        // 把 quote 里的盘口数据写进界面（数据 → 屏幕）。
        // 界面现在是"上半报价行 + 左边指标面板 + 右边五档表"，所以这里也按这三块写。
        // 数值一律走 Fmt：没拉到就显示 "--"；涨跌带正负号；涨红跌绿。
        private void RefreshQuoteUI()
        {
            // ── 顶部报价行（名字靠左，价格靠右）：名称 / 代码 / 现价 / 涨跌额 / 涨跌幅 ──
            qName.Text = string.IsNullOrEmpty(quote.Name) ? "—" : quote.Name;
            qCode.Text = TenNumber;        // 显示带前缀的腾讯代码，如 sz159248
            qPrice.Text = Fmt(quote.Price, "F3");
            qChange.Text = SignText(quote.Change, "F3");
            qChangePct.Text = SignText(quote.ChangePct, "F2") + (quote.ChangePct.HasValue ? "%" : "");

            // 顶部这三个数字用的是【实时涨跌】的颜色
            qPrice.Foreground = qChange.Foreground = qChangePct.Foreground = ColorOf(quote.ChangePct);

            // ── 左边：指标面板（第一行 今开+开盘涨跌 / 第二行 最高+最低 / 第三行 量·额·换手）──
            qOpen.Text = Fmt(quote.Open, "F3");

            // 注意：面板里的"涨跌"不是实时涨跌，而是【开盘价 相对 昨收】的涨跌 ——
            //   涨跌额 = 今开 - 昨收；涨跌幅 = 涨跌额 / 昨收 × 100%
            // 所以这两个值要在这里自己算，不能直接用接口给的 change / changePct（那是实时价相对昨收的）。
            decimal? openChange = null, openChangePct = null;
            if (quote.Open.HasValue && quote.PreClose.HasValue)
            {
                openChange = quote.Open.Value - quote.PreClose.Value;

                // 昨收是 0（停牌、新股等脏数据）就不算百分比，免得除出无限大
                if (quote.PreClose.Value != 0m)
                    openChangePct = openChange.Value / quote.PreClose.Value * 100m;
            }
            pChange.Text = ChangeText(openChange, openChangePct);   // 额 + 幅拼一串
            pChange.Foreground = ColorOf(openChangePct);            // 颜色跟着开盘涨跌走

            qHigh.Text = Fmt(quote.High, "F3");
            qLow.Text = Fmt(quote.Low, "F3");

            // 成交量 / 成交额的单位跟着市场走 —— 腾讯三个市场给的原始单位就不一样：
            //   A股（sh / sz / bj）：量是"手"，额是"万元"
            //   美股（us / us.）：量是"股"，额是"美元"（数字很大，换成"亿"显示，不然一长串没法看）
            bool isAShare = TenNumber != null
                            && (TenNumber.StartsWith("sh") || TenNumber.StartsWith("sz") || TenNumber.StartsWith("bj"));
            qVol.Text = VolumeText(quote.Volume, isAShare ? "手" : "股");
            qAmt.Text = quote.Amount.HasValue
                        ? (isAShare ? quote.Amount.Value.ToString("F0") + " 万"
                                    : (quote.Amount.Value / 100000000m).ToString("F2") + " 亿")
                        : "--";
            qTurnover.Text = Fmt(quote.Turnover, "F2") + " %";

            // ── 右边：五档表（每档拆成 4 格：买量 / 买价 / 卖价 / 卖量；中间那列档位号是 XAML 里写死的）──
            // 一律走 DepthCell：没有五档的市场（美股）或者某一档没挂单时，显示 "--" 而不是 0.000
            bv1.Text = DepthVolumeCell(quote.Buy1v);  bp1.Text = DepthCell(quote.Buy1, "F3");
            bv2.Text = DepthVolumeCell(quote.Buy2v);  bp2.Text = DepthCell(quote.Buy2, "F3");
            bv3.Text = DepthVolumeCell(quote.Buy3v);  bp3.Text = DepthCell(quote.Buy3, "F3");
            bv4.Text = DepthVolumeCell(quote.Buy4v);  bp4.Text = DepthCell(quote.Buy4, "F3");
            bv5.Text = DepthVolumeCell(quote.Buy5v);  bp5.Text = DepthCell(quote.Buy5, "F3");

            sp1.Text = DepthCell(quote.Sold1, "F3");  sv1.Text = DepthVolumeCell(quote.Sold1v);
            sp2.Text = DepthCell(quote.Sold2, "F3");  sv2.Text = DepthVolumeCell(quote.Sold2v);
            sp3.Text = DepthCell(quote.Sold3, "F3");  sv3.Text = DepthVolumeCell(quote.Sold3v);
            sp4.Text = DepthCell(quote.Sold4, "F3");  sv4.Text = DepthVolumeCell(quote.Sold4v);
            sp5.Text = DepthCell(quote.Sold5, "F3");  sv5.Text = DepthVolumeCell(quote.Sold5v);
        }

        // 数量文字：超过【1 万】就换成"万"作单位、保留两位小数，并且带上"万"字。
        // （1 万 = 5 位数，是我挑的门槛：4 位数以内一眼能读完，再长就不好认了）
        //   9999      + "手" → "9999手"
        //   121720    + "手" → "12.17万手"
        //   48156246  + "股" → "4815.62万股"
        private static string VolumeText(decimal? v, string unit)
        {
            if (!v.HasValue) return "--";
            return v.Value < 10000m
                 ? v.Value.ToString("F0") + unit                         // 没到一万：原样显示
                 : (v.Value / 10000m).ToString("F2") + "万" + unit;      // 到一万：换成万 + 两位小数
        }

        // 五档里的"量"格子：市场没五档（美股）、或者这一档没挂单 → "--"；否则按上面的"过万换万"规则
        private string DepthVolumeCell(decimal? v)
            => quote.HasDepth && v is > 0 ? VolumeText(v.Value, "") : "--";

        // 五档表格一格的文字。两种情况都显示 "--"：
        //   ① 这个市场根本没有五档（美股，接口那 20 个字段全是 0）；
        //   ② 这一档没有挂单（价格是 0 或没拉到，比如涨跌停、港股的量位）。
        // 用实例方法（不是 static）是因为要用 quote 里算出来的 HasDepth。
        private string DepthCell(decimal? v, string format)
            => quote.HasDepth && v is > 0 ? v.Value.ToString(format) : "--";

        // 涨跌文字：涨跌额 + 涨跌幅拼成一串，例如 "+0.058   +3.48%"
        // （指标面板第一行右边那格用；两个值都没有就返回 "--"）
        private static string ChangeText(decimal? chg, decimal? pct)
        {
            if (!chg.HasValue && !pct.HasValue) return "--";
            return SignText(chg, "F3") + "   " + SignText(pct, "F2") + (pct.HasValue ? "%" : "");
        }

        // 按涨跌幅给颜色：涨红 / 跌绿 / 平盘或没数据用白色。
        // 抽成一个方法是因为现在有两处要用同一套规则：顶部报价行（实时涨跌）、面板（开盘涨跌）。
        private static Brush ColorOf(decimal? pct)
        {
            if (!pct.HasValue) return FlatBrush;
            return pct > 0 ? UpBrush : (pct < 0 ? DownBrush : FlatBrush);
        }

        // 带正负号的数字（涨 +0.05 / 跌 -0.05）；没数据返回 --
        private static string SignText(decimal? v, string format)
            => v.HasValue ? (v >= 0 ? "+" : "") + v.Value.ToString(format) : "--";

        // ══════════════════ E. 实时区 ══════════════════
        // K 图下面那一行小字：盘口 / 日K / 分钟K 三个"数据时间"。

        // 把 A5 的三个时间写进界面（没拉到就显示 --）。
        // 字段由 G 区的拉取方法写入，这里只负责"照着字段写界面"：
        // 拉取那边一个都不用碰界面，界面这边也只有一个地方在改文字。
        // 格式：盘口精确到秒（心跳就是拿秒级时间戳判断更新的），日K 只要日期，分钟K 到分钟。
        private void RefreshTimeBar()
        {
            tQuote.Text = "盘口 " + (quote.Time?.ToString("MM-dd HH:mm:ss") ?? "--");
            tDaily.Text = "日K " + (dailyTime?.ToString("MM-dd") ?? "--");
            tMinute.Text = "分钟K " + (minuteTime?.ToString("MM-dd HH:mm") ?? "--");
        }

        // ══════════════════ F. K 图区 ══════════════════
        // 只管"什么时候去拉 K 线、拉完灌给哪张图、怎么切换"；拉取本身在 G 区。
        // 触发时机：每次点搜索（H 区）拉一次。
        // 以前这里是"程序打开时自动拉一次 + 用 kLoaded 挡重复"，因为标的写死是 159248；
        // 现在标的跟着搜索走，没有默认标的，所以那个自动触发和 kLoaded 一起去掉了。

        // 拉一次日K + 分钟K，分别灌进两张图；中途顺手刷实时区。
        // 顺序是：先 SetEmptyText（"加载中…"）→ 拉 → 空就改成"没拉到数据"→ 才 SetData。
        // 之所以能这么写，是因为拉取失败时列表不会被清空（Clear 都在解析成功之后），
        // 图会保留上一次成功的数据，不会突然变空白。
        private async Task LoadKChartsAsync()
        {
            KChartDay.SetEmptyText("日K 加载中…");
            await FetchDailyKAsync();                                    // ③ 拉日K
            RefreshTimeBar();                                            // 实时区"日K"先刷出来
            if (DailyCandles.Count == 0) KChartDay.SetEmptyText("日K 没拉到数据");
            KChartDay.SetData(DailyCandles);                             // 灌进日K 图

            KChartMin.SetEmptyText("分钟K 加载中…");
            await FetchMinuteKAsync();                                   // ④ 拉当日每分钟K
            RefreshTimeBar();                                            // 实时区"分钟K"再刷
            if (MinuteCandles.Count == 0) KChartMin.SetEmptyText("分钟K 没拉到数据");
            KChartMin.SetData(MinuteCandles);                            // 灌进分钟K 图
        }

        // 两个切换按钮（日K / 分钟K）：只是切显隐，不重新拉数据
        private void DayKBtn_Click(object sender, RoutedEventArgs e) => ShowKChart(true);
        private void MinKBtn_Click(object sender, RoutedEventArgs e) => ShowKChart(false);

        // 切换显示哪张图：两张图叠在同一格里，靠 Visibility 显隐；同时给按钮换底色
        private void ShowKChart(bool day)
        {
            KChartDay.Visibility = day ? Visibility.Visible : Visibility.Collapsed;
            KChartMin.Visibility = day ? Visibility.Collapsed : Visibility.Visible;
            DayKBtn.Background = day ? KBtnActive : KBtnIdle;
            MinKBtn.Background = day ? KBtnIdle : KBtnActive;
        }

        // ══════════════════ G. 数据拉取区 ══════════════════
        // 所有拉取方法都在这一块，方便自检。它们【只把数据存进字段】，一律不碰界面。
        //   【时间戳】心跳每 0.5 秒用，只要时间不要数据：
        //       FetchTimeAsync         拉盘口时间戳（a）→ 转出 b（秒级）/ c（分钟级）
        //   【数据】时间戳变了才用，拉完整数据：
        //       FetchQuoteAsync        实时盘口 → quote
        //       FetchDailyKAsync       日K     → DailyCandles
        //       FetchMinuteKAsync      分钟K   → MinuteCandles
        // 数据源全部腾讯：盘口用 qt.gtimg.cn，K 线用 web.ifzq.gtimg.cn / ifzq.gtimg.cn
        // （解析小工具 ToCandle / ToNum / ParseStamp / Fmt 也放在这一区，它们只服务于上面这些方法）
        //
        // 注：上一版这里还有"日K时间戳""分钟K时间戳"两个只拉时间的小方法，现在都删了
        //     —— 分钟K 直接用盘口时间戳的分钟级形式（c）比就行；日K 已永久退出心跳，不需要它。

        // ── 心跳用 · 盘口时间戳：一次拿到 a，转出 b 和 c ──
        // 只取接口的数据时间字段（a），再把同一个时间转成两种比较形式：
        //   b = 秒级   yyyyMMddHHmmss → 第一层"盘口"用（盘口几秒就更新一次，这一层要看得细）
        //   c = 分钟级 yyyyMMddHHmm   → 第二层"分钟K"用（分钟K 一分钟一根，只看分钟）
        // 三个市场的原文格式不一样，所以先用 ParseStamp 解析成 DateTime，再统一格式化：
        //   A股/北交所 20260916161409、美股 2026-09-16 16:14:09、港股 2026/09/16 16:14:09
        //   → b = 20260916161409、c = 202609161614
        // （正常调用前 tick 已经挡过空标的；这里再挡一次是兜底，万一以后别处也调它。）
        // 解析失败就保持上一次的值（当成本轮没更新）。
        private async Task FetchTimeAsync()
        {
            if (TenNumber == null) return;        // 还没选过市场 → 不发请求
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + TenNumber);
                string a = Encoding.GetEncoding("GBK").GetString(bytes).Split('~')[30];   // a：接口原文

                DateTime? t = ParseStamp(a);
                if (t.HasValue)
                {
                    TenSecond = t.Value.ToString("yyyyMMddHHmmss");   // b：秒级
                    TenMinute = t.Value.ToString("yyyyMMddHHmm");     // c：分钟级
                }
            }
            catch { }
        }

        // ── 拉数据① 实时盘口：GBK 解码 → 按 ~ 切分 → 按下标填进 quote 对象 ──
        // 一行的字段顺序是腾讯定死的，所以按下标取值（[1] 名称、[3] 现价 … [38] 换手率）。
        // 标的有空就不发请求；请求或解析出问题就 quote.ClearData()（界面由 PanGet 统一刷）。
        private async Task FetchQuoteAsync()
        {
            if (string.IsNullOrEmpty(TenNumber)) return;
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + TenNumber);
                string[] f = Encoding.GetEncoding("GBK").GetString(bytes).Split('~');

                quote.Name = f[1];
                quote.Price = ToNum(f[3]);  quote.PreClose = ToNum(f[4]);  quote.Open = ToNum(f[5]);
                quote.Volume = ToNum(f[6]);
                quote.Buy1 = ToNum(f[9]);   quote.Buy1v = ToNum(f[10]);
                quote.Buy2 = ToNum(f[11]);  quote.Buy2v = ToNum(f[12]);
                quote.Buy3 = ToNum(f[13]);  quote.Buy3v = ToNum(f[14]);
                quote.Buy4 = ToNum(f[15]);  quote.Buy4v = ToNum(f[16]);
                quote.Buy5 = ToNum(f[17]);  quote.Buy5v = ToNum(f[18]);
                quote.Sold1 = ToNum(f[19]); quote.Sold1v = ToNum(f[20]);
                quote.Sold2 = ToNum(f[21]); quote.Sold2v = ToNum(f[22]);
                quote.Sold3 = ToNum(f[23]); quote.Sold3v = ToNum(f[24]);
                quote.Sold4 = ToNum(f[25]); quote.Sold4v = ToNum(f[26]);
                quote.Sold5 = ToNum(f[27]); quote.Sold5v = ToNum(f[28]);
                quote.Change = ToNum(f[31]);   quote.ChangePct = ToNum(f[32]);
                quote.High = ToNum(f[33]);     quote.Low = ToNum(f[34]);
                quote.Amount = ToNum(f[37]);   quote.Turnover = ToNum(f[38]);
                quote.Time = ParseStamp(f[30]);   // 实时区"盘口"那格的数据时间（和心跳查的是同一个字段）
            }
            catch { quote.ClearData(); }   // 失败 → 盘口数据清空（界面由 PanGet 统一刷）
        }

        // ── 拉数据② 日K：拉历史每日（qfq 前复权）→ DailyCandles ──
        // 每行格式：[日期, 开, 收, 高, 低, 量]
        // 标的用 TenNumber（前缀 + 搜索框输入），不是写死的代码。
        private async Task FetchDailyKAsync()
        {
            if (string.IsNullOrEmpty(TenNumber)) return;   // 还没搜过 → 没标的可拉，直接不发请求
            try
            {
                string url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?" +
                             $"param={TenNumber},day,,,320,qfq";   // 320 根够覆盖全历史（这只 ETF 共 278 根）
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty(TenNumber).GetProperty("day");

                DailyCandles.Clear();
                foreach (var row in arr.EnumerateArray())
                    DailyCandles.Add(ToCandle(row, "yyyy-MM-dd"));

                // 实时区"日K"那格：取最后一根（最新那天）的日期。
                // DailyCandles 是"先 Clear 再逐根 Add"，所以拉失败时这里不会执行，
                // 界面上的旧时间和旧 K 线保持一致，不会出现"时间变了但图还是旧的"。
                if (DailyCandles.Count > 0) dailyTime = DailyCandles[^1].Date;
            }
            catch { }
        }

        // ── 拉数据③ 当日每分钟K：拉 m1 → MinuteCandles ──
        // 每行格式：[时间 yyyyMMddHHmm, 开, 收, 高, 低, 量, {}, 额]
        // 注意：m1 会跨天返回（实测 400 根里含前两天），所以只保留最后一天 = "当日"
        private async Task FetchMinuteKAsync()
        {
            if (string.IsNullOrEmpty(TenNumber)) return;   // 还没搜过 → 没标的可拉
            try
            {
                string url = "https://ifzq.gtimg.cn/appstock/app/kline/mkline?" +
                             $"param={TenNumber},m1,,300";     // m1 一分钟；想换周期就改这里的 m1
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty(TenNumber).GetProperty("m1");

                var all = new List<Candle>();
                foreach (var row in arr.EnumerateArray())
                    all.Add(ToCandle(row, "yyyyMMddHHmm"));

                string lastDay = all.Count > 0 ? all[^1].Date.ToString("yyyyMMdd") : "";
                MinuteCandles.Clear();
                foreach (Candle k in all)
                    if (k.Date.ToString("yyyyMMdd") == lastDay) MinuteCandles.Add(k);

                // 实时区"分钟K"那格：取最后一根（最新那一分钟）的时间
                if (MinuteCandles.Count > 0) minuteTime = MinuteCandles[^1].Date;
            }
            catch { }
        }

        // 一个 K 线行（JSON 数组）→ 一根 K 线：下标 0=时间/日期 1=开 2=收 3=高 4=低
        private static Candle ToCandle(JsonElement row, string timeFormat)
        {
            decimal open  = decimal.Parse(row[1].GetString()!, CultureInfo.InvariantCulture);
            decimal close = decimal.Parse(row[2].GetString()!, CultureInfo.InvariantCulture);
            decimal high  = decimal.Parse(row[3].GetString()!, CultureInfo.InvariantCulture);
            decimal low   = decimal.Parse(row[4].GetString()!, CultureInfo.InvariantCulture);

            if (open <= 0) open = close;   // 脏数据兜底：没给"开"就用"收"顶上，免得画出歪柱子

            return new Candle
            {
                Date  = DateTime.ParseExact(row[0].GetString()!, timeFormat, CultureInfo.InvariantCulture),
                Open  = open,
                Close = close,
                High  = Math.Max(high, Math.Max(open, close)),   // 保证 最高 >= max(开,收)
                Low   = Math.Min(low, Math.Min(open, close)),    // 保证 最低 <= min(开,收)
            };
        }

        // 安全转 decimal：失败返回 null（接口偶发的空串 / 异常值都走这条路）
        private static decimal? ToNum(string s)
            => decimal.TryParse(s, out decimal v) ? v : (decimal?)null;

        // 腾讯的时间字段有几种格式，挨个试；一个都解析不出来就给 null
        // （实时区显示 "--"），不让它抛异常打断后面的流程。
        // 三种格式都实测过：A股 / 北交所是 14 位紧凑格式，美股带横杠，港股带斜杠。
        private static readonly string[] StampFormats =
        {
            "yyyyMMddHHmmss",      // 20260916143012（A股、北交所）
            "yyyy-MM-dd HH:mm:ss", // 2026-09-16 11:39:20（美股）
            "yyyy/MM/dd HH:mm:ss", // 2026/09/16 16:08:47（港股）
        };

        // 把接口的时间字段转成 DateTime（依次试上面三种格式）
        private static DateTime? ParseStamp(string s)
        {
            foreach (string format in StampFormats)
                if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out DateTime t))
                    return t;
            return null;
        }

        // 格式化：null → "--"
        private static string Fmt(decimal? v, string format) => v?.ToString(format) ?? "--";

        // ══════════════════ H. 按键区 ══════════════════
        // 搜索键 + 搜索冷却 + 市场选择。（K 图那两个切换按钮属于 F 区，不在这）

        // 输入框里按回车 = 点搜索键。
        // 绑定在 XAML 的 SearchBox 上（KeyDown="SearchBox_KeyDown"）；
        // 主键盘回车和小键盘回车在 WPF 里是两个枚举值，所以两个都认一下。
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;

            e.Handled = true;                    // 拦下来，别再往上传（免得触发 TextBox 的默认行为）
            Search_Click(SearchButton, e);       // 直接借用搜索键那套逻辑（它不看 sender）
        }

        // 搜索键：先记录标的（搜索框原文 + 市场前缀），再开始"无视前提"的全量更新。
        // 顺序：① 市场没选就拦下 → ② 冷却检查（5 秒内连点会被拒）
        //       → ③ 记标的：stockNumber = 搜索框原文，TenNumber = 市场前缀 + stockNumber
        //       → ④ SearchReloadAsync()：盘口 + 日K + 分钟K 全部直接拉一遍
        //
        // 注意 ③ 放在两个检查【之后】：被拦下的点击不应该改动当前标的，
        // 否则会出现"心跳去查新代码、盘口图还显示旧数据"的错位。
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            if (MarketMode == '0')
            {
                MessageBox.Show("请先选择市场", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!CanSearch)
            {
                MessageBox.Show("搜索有 5 秒间隔，请稍候再试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 市场前缀：switch 表达式（C# 8 起支持）—— 每个档位对应一个前缀字符串
            string prefix = MarketMode switch
            {
                '1' => "sh",     // 沪市
                '2' => "sz",     // 深市
                '3' => "us",     // 美股（股票 / ETF）
                '4' => "us.",    // 美股2（指数：DJI / IXIC / INX / NDX）
                _   => "",       // 理论上走不到（'0' 上面已经拦掉了）
            };

            stockNumber = SearchBox.Text.Trim();    // 搜索框原文，如 159248 / AAPL / DJI
            TenNumber = prefix + stockNumber;       // 腾讯代码 = 市场前缀 + stockNumber

            CanSearch = false;            // 锁 5 秒
            Cooldown = 5;
            SearchButton.Content = "5";
            cooldownTimer.Start();

            _ = SearchReloadAsync();   // ④ 无视前提，三个数据全量更新（见下）
        }

        // ── 搜索用的"强制全量更新"（无视心跳的前提）──
        // 和心跳的区别：心跳必须先比时间戳、变了才拉；这里不管时间戳，三个数据直接拉一遍。
        // 先刷一次盘口时间戳（拿到新标的的 b / c），拉完再把它们记成旧值，
        // 这样紧接着到来的心跳不会因为"时间戳对不上"又把刚拉的数据重复拉一遍。
        // 注意：日K 只在这里更新（心跳里已经没有日K 了）。
        private async Task SearchReloadAsync()
        {
            await FetchTimeAsync();     // 拿 a → 转出 b / c（G 区）
            await PanGet();             // 盘口：拉数据 + 填盘口图（D 区）
            await LoadKChartsAsync();   // 日K + 分钟K：全量拉一遍（F 区）

            // 三个数据都刚拉过 → 两对时间戳全记成旧值
            tentime = TenSecond;
            tentimeMin = TenMinute;
        }

        // 冷却计时器的每秒 tick：Cooldown 倒数显示在按钮上，归 0 解锁
        private void CooldownTimer_Tick(object? sender, EventArgs e)//限制器核心逻辑
        {
            if (Cooldown > 0)
            {
                Cooldown--;
                SearchButton.Content = Cooldown.ToString();
            }
            else
            {
                CanSearch = true;
                Cooldown = 5;
                SearchButton.Content = "搜索";
                cooldownTimer.Stop();
            }
        }

        // 点"市场"按钮：开 / 关下拉（Popup）
        private void MarketBtn_Click(object sender, RoutedEventArgs e)
            => MarketPopup.IsOpen = !MarketPopup.IsOpen;

        // 下拉里的四个选项，各自对应一个市场档位
        private void MktShanghai_Click(object sender, RoutedEventArgs e) => SetMarket('1');   // 沪市
        private void MktShenzhen_Click(object sender, RoutedEventArgs e) => SetMarket('2');   // 深市
        private void MktUS_Click(object sender, RoutedEventArgs e)       => SetMarket('3');   // 美股
        private void MktUS2_Click(object sender, RoutedEventArgs e)      => SetMarket('4');   // 美股2（指数）

        // 记住档位 → 把市场按钮的文字改成市场名 → 收起下拉
        private void SetMarket(char mode)
        {
            MarketMode = mode;
            MarketBtn.Content = mode switch
            {
                '1' => "沪市",
                '2' => "深市",
                '3' => "美股",
                '4' => "美股2",
                _   => "市场",
            };
            MarketPopup.IsOpen = false;
        }
    }
}
