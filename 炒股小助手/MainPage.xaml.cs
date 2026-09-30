// ═══════════════════════════════════════════════════════════════════════════
// MainPage.xaml.cs —— 主界面（盘口图 + K 图 + 实时区）
//
// 【分区】
//   A. 初始化区 —— 构造函数
//   B. 主流程区 —— 搜索键、加自选按钮（两个"大按钮"的点击流程都在这一区）
//   C. 盘口图区 —— 上面那块行情区：拉 + 刷 + 数字转文字的小工具
//   D. 实时区   —— K 图下面那行小字：盘口 / 日K / 分钟K 三个数据时间
//   E. K 图区   —— 什么时候拉 K 线、灌给哪张图、日K / 分钟K 切换
//   F. 取数区   —— 三个拉取方法 + 解析小工具（只把数据写进字段，一律不碰界面）
//   G. 零散按键区 —— 回车键、两个冷却计时器的 tick、市场下拉
//   H. 声明区   —— 两个冷却计时器、请求器、界面颜色
//   I. 数据区   —— 搜索状态、盘口数据、K 线数据、实时区那两个时间
//
// 【这个文件负责】主界面上的一切：搜索、盘口图、K 图、实时区。
//   数据更新不是它自己定时拉的 —— 心跳（Heartbeat.cs）发现有新数据时会调本页的四个入口：
//     RefreshQuoteUI(盘口表) / RefreshQuoteStamp(时间串) / RefreshMinuteStamp(时间串) / RefreshMinuteKUI(蜡烛表)
//   本页自己只在"点搜索"的时候全量拉一遍。
// ═══════════════════════════════════════════════════════════════════════════
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace 炒股小助手
{
    public partial class MainPage : UserControl
    {
        // ══════════════════ A. 初始化区 ══════════════════
        // 本区方法：MainPage（构造函数）

        // 接收：无　返回：无　改变：实例化 XAML、注册 GBK、设两个冷却计时器的间隔、把界面摆成初始样子
        // 说明：这里不拉数据、不启动任何定时刷新 —— 那些由启动流程和心跳负责。
        public MainPage()
        {
            InitializeComponent();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);   // 注册 GBK（腾讯接口是 GBK）
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");     // 带个 UA，稳一点
            http.DefaultRequestHeaders.Add("Referer", "https://gu.qq.com/");

            cooldownTimer.Interval = TimeSpan.FromSeconds(1);       // 搜索键冷却：1 秒一跳
            cooldownTimer.Tick += CooldownTimer_Tick;
            watchCooldownTimer.Interval = TimeSpan.FromSeconds(0.5); // 加自选按钮冷却：0.5 秒一跳
            watchCooldownTimer.Tick += WatchCooldownTimer_Tick;

            ShowKChart(true);                          // K 图区默认显示"日K"
            KChartDay.SetEmptyText("搜索后显示日K");     // 还没有标的：两张图先摆提示语
            KChartMin.SetEmptyText("搜索后显示分钟K");
            RefreshTimeBar();                          // 实时区三个时间先摆成 "--"
        }

        // ══════════════════ B. 主流程区 ══════════════════
        // 本区方法：
        //   搜索键：Search_Click
        //          CheckMarketSelected / CheckSearchCooldown / StartSearchCooldown / RecordSearchTarget
        //          RefreshWatchButton（K 线那边调的是 E 区的 LoadKChartsAsync）
        //   加自选键：AddWatchBtn_Click
        //          CheckWatchBtnCooldown / ShowWatchResult / WatchCooldownTimer_Tick
        //
        // 【搜索键的流程】
        //   ① 市场选了吗？→ 没选就提示一下，打住
        //   ② 5 秒冷却过了吗？→ 没过就提示一下，打住
        //   ③ 锁冷却（按钮开始倒数 5→0）
        //   ④ 记标的：前缀 + 搜索框原文 → TenNumber，并把"代码 + 市场标记"告诉心跳
        //   ⑤ 无视前提，盘口 + 日K + 分钟K 全拉一遍
        //   ⑥ 等半秒（让盘口把名字填进 qName）
        //   ⑦ 按"有没有名字 / 是不是自选"摆好那个"加自选"按钮
        //
        // 【"加自选 / 删除自选股"按钮的流程】
        //   ① 0.5 秒冷却过了吗？→ 没过就直接不理（这个按钮不弹提示）
        //   ② 有标的吗 & 找得到所在窗口吗？
        //   ③ 点之前它在不在自选里？
        //   ④ 在 → 删；不在 → 加（读写文件都由 MainWindow 做）
        //   ⑤ 成功就把按钮上的字翻过来
        //   ⑥ 弹框反馈（列表那边要等心跳推一次才变，所以这里给个即时反馈）

        // 接收：按钮点击事件　返回：无　改变：搜索状态、盘口图、K 图、加自选按钮
        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckMarketSelected()) return;      // ① 基于"有没有选市场"判断能不能搜
            if (!CheckSearchCooldown()) return;      // ② 基于"冷却过没过"判断能不能搜

            StartSearchCooldown();                   // ③ 锁 5 秒
            RecordSearchTarget();                    // ④ 记标的 + 告诉心跳

            _ = LoadKChartsAsync();                  // ⑤ 日K + 分钟K：放后台慢慢拉，不挡下面的按钮

            await PanGet();                          // ⑥ 盘口：等它真拉完 —— 名字填好了才好决定按钮露不露脸
                                                     //    （以前这里是死等 500 毫秒；网络一慢，名字还没回来就被判成
                                                     //      "没名字"，按钮藏起来，这一轮搜索就再也见不到它了）

            RefreshWatchButton();                    // ⑦ 摆好"加自选"按钮
        }

        // 接收：无　返回：能不能搜　改变：没选市场时弹提示
        private bool CheckMarketSelected()
        {
            if (MarketMode != '0') return true;   // 基于"市场档位是不是 0"判断

            MessageBox.Show("请先选择市场", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        // 接收：无　返回：能不能搜　改变：冷却中时弹提示
        private bool CheckSearchCooldown()
        {
            if (CanSearch) return true;           // 基于"锁还在不在"判断

            MessageBox.Show("搜索有 5 秒间隔，请稍候再试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        // 接收：无　返回：无　改变：CanSearch=false、Cooldown=5、按钮显示"5"、冷却计时器开跑
        private void StartSearchCooldown()
        {
            CanSearch = false;             // 上锁：这 5 秒里再点会被 CheckSearchCooldown 拦下
            Cooldown = 5;                  // 倒计时从 5 开始
            SearchButton.Content = "5";    // 按钮先写 5，之后每秒由 CooldownTimer_Tick 减 1
            cooldownTimer.Start();
        }

        // 接收：无　返回：无　改变：stockNumber / TenNumber（本页标的）+ 心跳的 MainCode / MainMarket
        private void RecordSearchTarget()
        {
            // 市场档位 → 接口前缀（switch 表达式）
            string prefix = MarketMode switch
            {
                '1' => "sh",     // 沪市
                '2' => "sz",     // 深市
                '3' => "us",     // 美股（股票 / ETF）
                '4' => "us.",    // 美股2（指数：DJI / IXIC / INX / NDX）
                _   => "",       // 理论上走不到（'0' 在上面子方法①就被拦掉了）
            };

            stockNumber = SearchBox.Text.Trim();   // 搜索框原文，如 159248 / AAPL / DJI
            TenNumber = prefix + stockNumber;      // 腾讯代码 = 市场前缀 + 搜索框原文

            // 这只票属于哪个市场：'1' = A股（沪 / 深），'2' = 美股（含美股2）—— 心跳靠它判断要不要管主页面
            char marketKind = (MarketMode == '1' || MarketMode == '2') ? '1' : '2';

            if (Window.GetWindow(this) is MainWindow win) win.SetHeartbeatMainCode(TenNumber, marketKind);
        }

        // 接收：无　返回：无　改变："加自选"按钮的显隐和文字
        private void RefreshWatchButton()
        {
            // 基于"界面上有没有名字"判断按钮露不露脸（没名字时 RefreshQuoteUI 会写成 "—"）
            bool hasName = !string.IsNullOrWhiteSpace(qName.Text) && qName.Text != "—";
            AddWatchBtn.Visibility = hasName ? Visibility.Visible : Visibility.Collapsed;

            if (!hasName) return;   // 没名字就不折腾按钮上的字了

            // 按钮上的字 = "点下去会发生的那件事"：已经在自选里 → "删除自选股"；不在 → "加自选"
            bool isWatch = Window.GetWindow(this) is MainWindow w && w.IsWatchCode(TenNumber);
            AddWatchBtn.Content = isWatch ? "删除自选股" : "加自选";
        }

        // 接收：按钮点击事件　返回：无　改变：自选文件（通过 MainWindow 加 / 删）、按钮文字
        private void AddWatchBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckWatchBtnCooldown()) return;                         // ① 基于"0.5 秒冷却过没过"判断
            if (string.IsNullOrEmpty(TenNumber)) return;                  // ② 还没有标的
            if (Window.GetWindow(this) is not MainWindow win) return;     //    找不到窗口就算了（理论上不会）

            bool wasWatch = win.IsWatchCode(TenNumber);                    // ③ 点之前它在不在自选里

            bool ok = wasWatch ? win.RemoveWatchItem(TenNumber)            // ④ 在 → 删
                               : win.AddWatchItem(TenNumber, qName.Text);  //    不在 → 加

            if (ok) AddWatchBtn.Content = wasWatch ? "加自选" : "删除自选股";   // ⑤ 成功就把字样翻过来

            ShowWatchResult(ok, wasWatch);                                 // ⑥ 弹框反馈
        }

        // 接收：无　返回：这次点击要不要放行　改变：放行时顺手把锁合上
        private bool CheckWatchBtnCooldown()
        {
            if (!canWatchClick) return false;   // 基于"锁在不在"判断；冷却里直接不理，不弹提示

            canWatchClick = false;              // 合锁
            watchCooldownTimer.Start();         // 0.5 秒后由 WatchCooldownTimer_Tick 解锁
            return true;
        }

        // 接收：操作成没成 + 这次是加还是删　返回：无　改变：弹一个结果框
        private void ShowWatchResult(bool ok, bool wasWatch)
        {
            MessageBox.Show(
                ok ? $"{(wasWatch ? "已删除自选" : "已加入自选")}：{qName.Text}（{TenNumber}）"
                   : "操作失败，看看桌面 UserCache.json 是不是被别的程序占用了",
                wasWatch ? "删除自选" : "加自选",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // 接收：计时器事件参数　返回：无　改变：canWatchClick=true，计时器停下（它只跑一次）
        private void WatchCooldownTimer_Tick(object? sender, EventArgs e)
        {
            canWatchClick = true;
            watchCooldownTimer.Stop();
        }

        // ══════════════════ C. 盘口图区 ══════════════════
        // 本区方法：
        //   PanGet               —— 搜索那条路：拉盘口 → 填盘口图 → 刷实时区
        //   RefreshQuoteUI()     —— 把 quote 里的数据写进界面
        //   RefreshQuoteUI(表)   —— 给心跳用的入口①：换掉 quote 再刷界面
        //   RefreshQuoteStamp    —— 给心跳用的入口②：只刷实时区"盘口"那格
        //   RefreshMinuteStamp   —— 给心跳用的入口③：只刷实时区"分钟K"那格
        //   RefreshMinuteKUI     —— 给心跳用的入口④：换分钟K 数据 + 重画分钟K 图
        //   VolumeText / DepthVolumeCell / DepthCell / ChangeText / ColorOf / SignText —— 数字转文字 / 颜色

        // 接收：无　返回：无　改变：quote 对象 + 盘口图 + 实时区"盘口"格
        private async Task PanGet()
        {
            await FetchQuoteAsync();   // 拉实时盘口（F 区）
            RefreshQuoteUI();          // 数据 → 屏幕
            RefreshTimeBar();          // 实时区跟着更新
        }

        // 接收：无　返回：无　改变：报价行 + 指标面板 + 五档表
        private void RefreshQuoteUI()
        {
            // ── 顶部报价行：名称 / 代码 / 现价 / 涨跌额 / 涨跌幅 ──
            qName.Text = string.IsNullOrEmpty(quote.Name) ? "—" : quote.Name;
            qCode.Text = TenNumber;                                          // 带前缀的腾讯代码
            qPrice.Text = Fmt(quote.Price, "F3");
            qChange.Text = SignText(quote.Change, "F3");
            qChangePct.Text = SignText(quote.ChangePct, "F2") + (quote.ChangePct.HasValue ? "%" : "");
            qPrice.Foreground = qChange.Foreground = qChangePct.Foreground = ColorOf(quote.ChangePct);   // 涨跌色

            // ── 左边指标面板：第一行 今开 + 开盘涨跌 / 第二行 最高 + 最低 / 第三行 量 · 额 · 换手 ──
            qOpen.Text = Fmt(quote.Open, "F3");

            // 面板里的"涨跌"是【今开 相对 昨收】，不是实时涨跌，所以在这儿自己算
            decimal? openChange = null, openChangePct = null;
            if (quote.Open.HasValue && quote.PreClose.HasValue)      // 基于"今开和昨收都有值"判断能不能算
            {
                openChange = quote.Open.Value - quote.PreClose.Value;
                if (quote.PreClose.Value != 0m)                      // 昨收是 0（停牌 / 新股等脏数据）就不算百分比
                    openChangePct = openChange.Value / quote.PreClose.Value * 100m;
            }
            pChange.Text = ChangeText(openChange, openChangePct);
            pChange.Foreground = ColorOf(openChangePct);

            qHigh.Text = Fmt(quote.High, "F3");
            qLow.Text = Fmt(quote.Low, "F3");

            // 量 / 额的单位跟着市场走：A股是"手 / 万元"，美股是"股 / 美元（数字大，换亿显示）"
            bool isAShare = TenNumber != null
                            && (TenNumber.StartsWith("sh") || TenNumber.StartsWith("sz") || TenNumber.StartsWith("bj"));
            qVol.Text = VolumeText(quote.Volume, isAShare ? "手" : "股");
            qAmt.Text = quote.Amount.HasValue
                        ? (isAShare ? quote.Amount.Value.ToString("F0") + " 万"
                                    : (quote.Amount.Value / 100000000m).ToString("F2") + " 亿")
                        : "--";
            qTurnover.Text = Fmt(quote.Turnover, "F2") + " %";

            // ── 右边五档表：每档 4 格（买量 / 买价 / 卖价 / 卖量），中间档位号是 XAML 里写死的 ──
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

        // 接收：主页面那只的盘口表（最多一条）　返回：无　改变：quote 对象 + 盘口图 + 实时区
        // 说明：表是空的（这次没查到主页面那只）就只按现有数据重画一遍，不会把界面清空。
        public void RefreshQuoteUI(List<Quote> quotes)
        {
            if (quotes.Count > 0) quote = quotes[0];   // 拿心跳拉到的那条替换本页数据

            RefreshQuoteUI();      // 复用"数据 → 屏幕"那套
            RefreshTimeBar();
        }

        // 接收：秒级时间戳字符串　返回：无　改变：实时区"盘口"那格（解析失败就保持原样）
        public void RefreshQuoteStamp(string stamp)
        {
            DateTime? t = ParseStamp(stamp);
            if (t.HasValue) heartbeatQuoteTime = t;
            RefreshTimeBar();
        }

        // 接收：分钟级时间戳字符串　返回：无　改变：实时区"分钟K"那格（解析失败就保持原样）
        public void RefreshMinuteStamp(string stamp)
        {
            DateTime? t = ParseStamp(stamp);
            if (t.HasValue) heartbeatMinuteTime = t;
            RefreshTimeBar();
        }

        // 接收：分钟K 表　返回：无　改变：MinuteCandles、minuteTime、分钟K 图、实时区
        // 说明：表是空的（这次没拉到）就什么都不动，图上留着同一只标的的旧数据。
        public void RefreshMinuteKUI(List<Candle> candles)
        {
            if (candles.Count == 0) return;   // 基于"表里有没有数据"判断要不要刷新

            MinuteCandles = candles;               // 直接换引用（心跳每次都新建一个表，不会改旧的）
            minuteTime = MinuteCandles[^1].Date;   // [^1] = 最新那一分钟

            KChartMin.SetData(MinuteCandles, resetView: false);   // 灌图；resetView=false = 不重置用户的缩放位置
            RefreshTimeBar();
        }

        // 接收：数量 + 单位　返回：带"万"单位的文字（如 12.17万手）　改变：无
        // 说明：10000 是门槛 —— 4 位数以内一眼能读完，再长就不好认。
        private static string VolumeText(decimal? v, string unit)
        {
            if (!v.HasValue) return "--";
            return v.Value < 10000m
                 ? v.Value.ToString("F0") + unit
                 : (v.Value / 10000m).ToString("F2") + "万" + unit;
        }

        // 接收：五档里的量　返回：文字或 "--"　改变：无
        private string DepthVolumeCell(decimal? v)
            => quote.HasDepth && v is > 0 ? VolumeText(v.Value, "") : "--";   // 基于"这个市场有没有五档"判断

        // 接收：五档里的价 + 格式　返回：文字或 "--"　改变：无
        private string DepthCell(decimal? v, string format)
            => quote.HasDepth && v is > 0 ? v.Value.ToString(format) : "--";

        // 接收：涨跌额 + 涨跌幅　返回：拼成一串的文字（如 "+0.058   +3.48%"）　改变：无
        private static string ChangeText(decimal? chg, decimal? pct)
        {
            if (!chg.HasValue && !pct.HasValue) return "--";
            return SignText(chg, "F3") + "   " + SignText(pct, "F2") + (pct.HasValue ? "%" : "");
        }

        // 接收：涨跌幅　返回：该用的颜色　改变：无（基于"涨 / 跌 / 平"判断红绿白）
        private static Brush ColorOf(decimal? pct)
        {
            if (!pct.HasValue) return FlatBrush;
            return pct > 0 ? UpBrush : (pct < 0 ? DownBrush : FlatBrush);
        }

        // 接收：数字 + 格式　返回：带正负号的文字（涨 +0.05 / 跌 -0.05）　改变：无
        private static string SignText(decimal? v, string format)
            => v.HasValue ? (v >= 0 ? "+" : "") + v.Value.ToString(format) : "--";

        // ══════════════════ D. 实时区 ══════════════════
        // 本区方法：RefreshTimeBar

        // 接收：无　返回：无　改变：K 图下面那三格文字
        // 说明：盘口 / 分钟K 两格各有两个来源 —— 心跳送来的优先，没送过就退回本页自己拉到的那份。
        private void RefreshTimeBar()
        {
            DateTime? quoteStamp = heartbeatQuoteTime ?? quote.Time;     // 基于"心跳送过没有"选来源
            DateTime? minuteStamp = heartbeatMinuteTime ?? minuteTime;

            tQuote.Text = "盘口 " + (quoteStamp?.ToString("MM-dd HH:mm:ss") ?? "--");
            tDaily.Text = "日K " + (dailyTime?.ToString("MM-dd") ?? "--");
            tMinute.Text = "分钟K " + (minuteStamp?.ToString("MM-dd HH:mm") ?? "--");
        }

        // ══════════════════ E. K 图区 ══════════════════
        // 本区方法：LoadKChartsAsync / DayKBtn_Click / MinKBtn_Click / ShowKChart
        // 说明：这一区只管"什么时候拉、拉完灌给哪张图、怎么切换"，拉取本身在 F 区。

        // 接收：无　返回：无　改变：两张 K 图的数据和画面、实时区"日K / 分钟K"两格
        // 说明：先清空再画 —— 从根上避免"上一只的 K 线留在图上冒充新标的"。
        private async Task LoadKChartsAsync()
        {
            // ── ① 先清空 ──
            DailyCandles.Clear();
            MinuteCandles.Clear();
            dailyTime = null;
            minuteTime = null;
            heartbeatQuoteTime = null;      // 换标的了：心跳上次送来的时间属于上一只票，一并清掉
            heartbeatMinuteTime = null;

            KChartDay.SetEmptyText("日K 加载中…");
            KChartDay.SetData(DailyCandles);     // 灌空列表 → 图上显示"加载中…"
            KChartMin.SetEmptyText("分钟K 加载中…");
            KChartMin.SetData(MinuteCandles);
            RefreshTimeBar();

            // ── ② 日K：拉到就画，没拉到就在图上写"获取失败" ──
            if (await FetchDailyKAsync())
            {
                KChartDay.SetData(DailyCandles);
            }
            else
            {
                DailyCandles.Clear();     // 先清掉"解析到一半留下的那几条"—— 不清的话它们会被当成结果画出来
                KChartDay.SetEmptyText("日K 获取失败");
                KChartDay.SetData(DailyCandles);
            }
            RefreshTimeBar();

            // ── ③ 分钟K：同样处理 ──
            if (await FetchMinuteKAsync())
            {
                KChartMin.SetData(MinuteCandles);
            }
            else
            {
                MinuteCandles.Clear();    // 同上：失败就清空，保证图上是"获取失败"而不是半截数据
                KChartMin.SetEmptyText("分钟K 获取失败");
                KChartMin.SetData(MinuteCandles);
            }
            RefreshTimeBar();
        }

        // 接收：按钮点击事件　返回：无　改变：切到"日K"那张图
        private void DayKBtn_Click(object sender, RoutedEventArgs e) => ShowKChart(true);

        // 接收：按钮点击事件　返回：无　改变：切到"分钟K"那张图
        private void MinKBtn_Click(object sender, RoutedEventArgs e) => ShowKChart(false);

        // 接收：true = 显示日K　返回：无　改变：两张图的显隐 + 两个按钮的底色
        private void ShowKChart(bool day)
        {
            KChartDay.Visibility = day ? Visibility.Visible : Visibility.Collapsed;
            KChartMin.Visibility = day ? Visibility.Collapsed : Visibility.Visible;
            DayKBtn.Background = day ? KBtnActive : KBtnIdle;
            MinKBtn.Background = day ? KBtnIdle : KBtnActive;
        }

        // ══════════════════ F. 取数区 ══════════════════
        // 本区方法：
        //   FetchQuoteAsync    —— 实时盘口 → quote
        //   FetchDailyKAsync   —— 日K → DailyCandles
        //   FetchMinuteKAsync  —— 分钟K → MinuteCandles
        //   PickKArray / ToCandle / ToNum / ParseStamp / Fmt —— 解析小工具
        // 数据源全是腾讯：盘口 qt.gtimg.cn，K 线 web.ifzq.gtimg.cn / ifzq.gtimg.cn。

        // 接收：无（用本页的 TenNumber）　返回：无　改变：quote 对象（失败就清空它）
        private async Task FetchQuoteAsync()
        {
            if (string.IsNullOrEmpty(TenNumber)) return;   // 基于"有没有标的"判断要不要发请求
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + TenNumber);
                string[] f = Encoding.GetEncoding("GBK").GetString(bytes).Split('~');   // 单只票：按 ~ 切字段

                quote.Code = TenNumber;   // 完整腾讯代码也记上（自选股页"点行跳搜索"要用它）
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
                quote.Time = ParseStamp(f[30]);   // [30] = 数据时间
            }
            catch { quote.ClearData(); }   // 请求 / 解析失败 → 把盘口清空（界面由 PanGet 统一刷）
        }

        // 接收：无（用本页的 TenNumber）　返回：这次拉到没有　改变：DailyCandles + dailyTime
        private async Task<bool> FetchDailyKAsync()
        {
            if (string.IsNullOrEmpty(TenNumber)) return false;   // 基于"有没有标的"判断要不要发请求
            try
            {
                string url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?" +
                             $"param={TenNumber},day,,,320,qfq";     // 320 根够覆盖全历史
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);

                JsonElement node = doc.RootElement.GetProperty("data").GetProperty(TenNumber);
                JsonElement arr = PickKArray(node);                  // 数组名不一定叫 day，交给它挑

                DailyCandles.Clear();
                foreach (var row in arr.EnumerateArray())            // 一行一根
                {
                    Candle? c = ToCandle(row, "yyyy-MM-dd");
                    if (c != null) DailyCandles.Add(c);              // 脏数据行（解析不出来）直接跳过
                }

                if (DailyCandles.Count > 0) dailyTime = DailyCandles[^1].Date;   // 实时区"日K"那格 = 最新那天的日期

                return DailyCandles.Count > 0;
            }
            catch { return false; }
        }

        // 接收：接口返回的那只票的节点　返回：K 线数组　改变：无（找不到就抛异常，由调用方的 catch 接住）
        // 说明：要复权的标的是 qfqday，不要复权的（ETF）是 day，后复权是 hfqday —— 三种都认。
        private static JsonElement PickKArray(JsonElement node)
        {
            foreach (string key in new[] { "qfqday", "day", "hfqday" })
                if (node.TryGetProperty(key, out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr;

            throw new Exception("接口返回里没有 K 线数组（qfqday / day / hfqday 都没找到）");
        }

        // 接收：无（用本页的 TenNumber）　返回：这次拉到没有　改变：MinuteCandles + minuteTime
        private async Task<bool> FetchMinuteKAsync()
        {
            if (string.IsNullOrEmpty(TenNumber)) return false;   // 基于"有没有标的"判断要不要发请求
            try
            {
                string url = "https://ifzq.gtimg.cn/appstock/app/kline/mkline?" +
                             $"param={TenNumber},m1,,300";                       // m1 = 一分钟
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty(TenNumber).GetProperty("m1");

                var all = new List<Candle>();
                foreach (var row in arr.EnumerateArray())
                {
                    Candle? c = ToCandle(row, "yyyyMMddHHmm");
                    if (c != null) all.Add(c);                       // 脏数据行直接跳过
                }

                string lastDay = all.Count > 0 ? all[^1].Date.ToString("yyyyMMdd") : "";   // 接口会带前两天，只留当天
                MinuteCandles.Clear();
                foreach (Candle k in all)                                                  // 基于"这根是不是当天的"筛选
                    if (k.Date.ToString("yyyyMMdd") == lastDay) MinuteCandles.Add(k);

                if (MinuteCandles.Count > 0) minuteTime = MinuteCandles[^1].Date;   // 实时区那格 = 最新那一分钟

                return MinuteCandles.Count > 0;
            }
            catch { return false; }
        }

        // 接收：一行 JSON（[时间, 开, 收, 高, 低, ...]）+ 时间格式　返回：一根 K 线（这行是脏数据就给 null）　改变：无
        // 【为什么返回可空 + 全用 TryParse】以前用 Parse / ParseExact：一行脏数据就会抛异常，
        //   把整批解析打断，结果"只画出前面几根"（英伟达日K 只显示两根就是这个原因）。
        //   现在单行解析不了就返回 null，调用方跳过它 —— 宁可少一根柱，也不要画出错的。
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

        // 接收：字符串　返回：小数（转不了给 null）　改变：无
        private static decimal? ToNum(string s)
            => decimal.TryParse(s, out decimal v) ? v : (decimal?)null;

        // 接收：时间字符串　返回：DateTime（都解析不出来给 null）　改变：无
        private static DateTime? ParseStamp(string s)
        {
            foreach (string format in StampFormats)   // 挨个格式试（见数据区的 StampFormats）
                if (DateTime.TryParseExact(s, format, CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out DateTime t))
                    return t;
            return null;
        }

        // 接收：数字 + 格式　返回：文字（null → "--"）　改变：无
        private static string Fmt(decimal? v, string format) => v?.ToString(format) ?? "--";

        // ══════════════════ G. 零散按键区 ══════════════════
        // 本区方法：
        //   SearchBox_KeyDown       —— 回车 = 点搜索
        //   CooldownTimer_Tick      —— 搜索键冷却的每秒倒数
        //   MarketBtn_Click / MktShanghai_Click / MktShenzhen_Click / MktUS_Click / MktUS2_Click —— 市场下拉
        //   SetMarket               —— 记档位 + 改按钮文字 + 收起下拉
        //   SearchByTenCode         —— 从外面进来的搜索（自选股页点行跳过来用）

        // 接收：键盘事件　返回：无　改变：等价于点了一次搜索键
        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter && e.Key != Key.Return) return;   // 基于"按的是不是回车"判断（两个都认）

            e.Handled = true;                    // 拦下来，别再往上传
            Search_Click(SearchButton, e);       // 借用搜索键那套逻辑
        }

        // 接收：计时器事件参数　返回：无　改变：按钮上的倒计时文字 + 解不解锁
        private void CooldownTimer_Tick(object? sender, EventArgs e)
        {
            if (Cooldown > 0)                 // 基于"还剩几秒"判断是继续倒数还是解锁
            {
                Cooldown--;
                SearchButton.Content = Cooldown.ToString();
            }
            else
            {
                CanSearch = true;             // 解锁
                Cooldown = 5;                 // 下次从头数
                SearchButton.Content = "搜索";
                cooldownTimer.Stop();
            }
        }

        // 接收：按钮点击事件　返回：无　改变：市场下拉开 / 关
        private void MarketBtn_Click(object sender, RoutedEventArgs e)
            => MarketPopup.IsOpen = !MarketPopup.IsOpen;

        // 接收：按钮点击事件　返回：无　改变：市场档位 = 沪市 / 深市 / 美股 / 美股2
        private void MktShanghai_Click(object sender, RoutedEventArgs e) => SetMarket('1');
        private void MktShenzhen_Click(object sender, RoutedEventArgs e) => SetMarket('2');
        private void MktUS_Click(object sender, RoutedEventArgs e)       => SetMarket('3');
        private void MktUS2_Click(object sender, RoutedEventArgs e)      => SetMarket('4');

        // 接收：市场档位字符　返回：无　改变：MarketMode + 市场按钮的文字 + 收起下拉
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

        // 接收：完整腾讯代码　返回：无　改变：市场档位 + 搜索框 + （借搜索键）标的和数据
        // 说明：自选股页点了某一行会走这里。认前缀的顺序有讲究："us." 必须排在 "us" 前面。
        public void SearchByTenCode(string tenCode)
        {
            string code = tenCode;   // 纯代码；默认先用原样顶着

            if (tenCode.StartsWith("sh"))       { SetMarket('1'); code = tenCode[2..]; }   // 沪市
            else if (tenCode.StartsWith("sz"))  { SetMarket('2'); code = tenCode[2..]; }   // 深市
            else if (tenCode.StartsWith("us.")) { SetMarket('4'); code = tenCode[3..]; }   // 美股2（指数）
            else if (tenCode.StartsWith("us"))  { SetMarket('3'); code = tenCode[2..]; }   // 美股

            SearchBox.Clear();         // 先清空，再填纯代码（前缀由上面的档位决定）
            SearchBox.Text = code;

            Search_Click(SearchButton, new RoutedEventArgs());   // 借用搜索键那套逻辑
        }

        // ══════════════════ H. 声明区 ══════════════════
        // 说明：这一区是"要用的东西"，不是业务数据。
        private readonly DispatcherTimer cooldownTimer = new();        // 搜索键冷却（1 秒一跳）
        private readonly DispatcherTimer watchCooldownTimer = new();   // 加自选按钮冷却（0.5 秒一跳）
        private readonly HttpClient http = new();                      // 共用请求器（全部走腾讯）

        // 涨跌色（A股习惯：涨红跌绿，平盘或没数据用白）
        private static readonly Brush UpBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush DownBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
        private static readonly Brush FlatBrush = Brushes.White;

        // K 图那两个切换按钮的底色（点中的亮蓝、没点的深灰，跟左侧导航一个风格）
        private readonly Brush KBtnActive = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xE0));
        private readonly Brush KBtnIdle = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));

        // ══════════════════ I. 数据区 ══════════════════
        // ── 搜索状态 ──
        public char MarketMode = '0';        // 市场档位：'0' 未选 / '1' 沪 / '2' 深 / '3' 美股 / '4' 美股2
        public string stockNumber = "";      // 搜索框里的原始输入（不带前缀），如 159248 / AAPL
        public string? TenNumber;            // 腾讯代码 = 前缀 + stockNumber，如 sz159248（请求接口用）
        public int Cooldown = 5;             // 搜索冷却剩余秒数（显示在搜索按钮上）
        public bool CanSearch = true;        // false = 搜索冷却中，连点会被拦
        private bool canWatchClick = true;   // false = 加自选按钮在 0.5 秒冷却里，点击直接不理

        // ── 盘口数据 ──
        public Quote quote = new();          // 主页面这只票的盘口（模型见 Quote.cs）

        // ── K 线数据 ──
        public List<Candle> DailyCandles = new();    // 日K（历史每日）
        public List<Candle> MinuteCandles = new();   // 当日每分钟K
        private DateTime? dailyTime;                 // 日K 的数据时间（实时区"日K"那格）
        private DateTime? minuteTime;                // 分钟K 的数据时间（实时区"分钟K"那格）

        // ── 心跳送来的"节拍时间"（只服务实时区那两格）──
        // 存两个独立字段而不是直接覆盖 quote.Time / minuteTime —— 两路各写一份会互相踩，界面上的数字会来回跳。
        private DateTime? heartbeatQuoteTime;        // 心跳送来的盘口数据时间（秒级）
        private DateTime? heartbeatMinuteTime;       // 心跳送来的分钟K 数据时间（分钟级）

        // ── 时间字符串格式表（解析接口时间字段时挨个试）──
        private static readonly string[] StampFormats =
        {
            "yyyyMMddHHmmss",      // A股 / 北交所；心跳的秒级时间戳也是这种
            "yyyy-MM-dd HH:mm:ss", // 美股
            "yyyy/MM/dd HH:mm:ss", // 港股
            "yyyyMMddHHmm",        // 心跳的分钟级时间戳
        };
    }
}
