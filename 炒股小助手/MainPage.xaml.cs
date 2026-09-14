// MainPage.xaml.cs —— 主界面：市场选择 + 搜索 + 行情拉取（数据源全部腾讯）
// 布局：A 字段 → B 搜索链(主) → C 市场区 → D 展示 → E 工具
//       → F 数据拉取区（四个拉取方法都在这一块，方便自检）→ G K 线区(日K / 分钟K)
using System.Globalization;        // CultureInfo：解析接口返回的小数/日期
using System.Net.Http;
using System.Text;                 // Encoding（GBK 解码）
using System.Text.Json;            // JsonDocument（K 线 JSON 解析）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace 炒股小助手
{
    public partial class MainPage : UserControl
    {
        // ══════════════ A. 字段 ══════════════

        // 引擎物件
        private readonly DispatcherTimer cooldownTimer = new();   // 搜索冷却（1 秒）
        private readonly DispatcherTimer timeTimer = new();       // 腾讯时间戳轮询（0.5 秒，程序一开就转）
        private readonly HttpClient http = new HttpClient();      // 共用请求器（全部走腾讯）

        // 市场档位：'0'未选 '1'沪 '2'深 '3'美；West/TenNumber 是拼好的完整代码（可为空）
        public char MarketMode = '0';
        public string? TenNumber;     // 腾讯代码 如 sz159248
        public string? WestNumber;    // 东财代码 如 0.159248

        // 搜索冷却：CanSearch=false 期间连点会被拦
        public int Cooldown = 5;
        public bool CanSearch = true;

        // 行情数据（decimal 避免浮点误差；null = 没拉到）
        public string stockNumber = "";     // 当前拿去查的腾讯代码
        public string stockName = "";
        public decimal? price, preClose, open, high, low, change, changePct;  // 现价/昨收/今开/最高/最低/涨跌额/涨跌幅
        public decimal? volume, amount, turnover;                            // 成交量/成交额(万)/换手率
        public decimal? buy1, buy2, buy3, buy4, buy5;        // 买一~买五 价
        public decimal? buy1v, buy2v, buy3v, buy4v, buy5v;   // 买一~买五 量
        public decimal? sold1, sold2, sold3, sold4, sold5;        // 卖一~卖五 价
        public decimal? sold1v, sold2v, sold3v, sold4v, sold5v;   // 卖一~卖五 量

        // ── 四个拉取方法的"收货区"（拉取方法只往这里存，不碰界面）──
        public string TenTime = "";                    // ① 时间戳：yyyyMMddHHmmss，如 20260911150000
        public string? tentime;                        //    上一次的时间戳（用来比对"有没有更新"）
        // ② 实时盘口 → 就是上面那堆 price / volume / buy1... 字段
        public List<Candle> DailyCandles = new();      // ③ 日K（历史每日）
        public List<Candle> MinuteCandles = new();     // ④ 当日每分钟K

        // K 线图当前的标的（腾讯代码）。现在固定 159248；
        // 以后想让"K 线跟着搜索走"，在 Search_Click 里加一行 KCode = TenNumber; 就行
        public string KCode = "sz159248";

        // K 线区切换按钮的两种底色（点中的亮蓝、没点的深灰，跟左侧导航一个风格）
        private readonly Brush KBtnActive = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xE0));
        private readonly Brush KBtnIdle = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));

        // 涨跌色（A股：涨红跌绿）
        private static readonly Brush UpBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush DownBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
        private static readonly Brush FlatBrush = Brushes.White;

        public MainPage()
        {
            InitializeComponent();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // GBK 支持
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");   // 带个 UA，稳一点
            http.DefaultRequestHeaders.Add("Referer", "https://gu.qq.com/");

            cooldownTimer.Interval = TimeSpan.FromSeconds(1);
            cooldownTimer.Tick += CooldownTimer_Tick;

            // 心跳：程序一打开（还在 loading 时）就开始转，0.5 秒一轮
            //   先拉时间戳（①）→ 发现它变了 = 盘口更新了 → 再拉盘口并刷界面（②）
            // 注意：没搜索过时 TenNumber 是 null，① 内部直接 return 不发请求
            timeTimer.Interval = TimeSpan.FromSeconds(0.5);
            timeTimer.Tick += async (s, e) =>
            {
                await FetchTimeAsync();          // ① 只拉时间戳
                if (tentime != TenTime)
                {
                    tentime = TenTime;
                    await TenGet();              // ② 拉实时盘口 + 刷新界面
                }
            };
            timeTimer.Start();

            ShowKChart(true);           // K 线区默认显示"日K"
            Loaded += MainPage_Loaded;  // 主界面一露脸就自动拉 K 线（不绑搜索键）
        }

        // ══════════════ B. 主事件链：搜索按钮 ══════════════
        // 点击后顺序：① MarketMode=='0' 拦下 → ② 拼前缀(Ten/West) → ③ 冷却检查
        //             → ④ 拉一次实时盘口(TenGet)，界面立刻有数据
        //（时间戳由 0.5 秒心跳自动轮询；K 线在程序打开时自动拉，都不在这里）
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            string input = SearchBox.Text.Trim();   // 只填纯数字，如 159248

            switch (MarketMode)
            {
                case '0':
                    MessageBox.Show("请先选择市场", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                case '1': TenNumber = "sh" + input; WestNumber = "1." + input; break;
                case '2': TenNumber = "sz" + input; WestNumber = "0." + input; break;
                default:  TenNumber = "us" + input; WestNumber = "105." + input; break;  // 美股
            }

            if (!CanSearch)
            {
                MessageBox.Show("搜索有 5 秒间隔，请稍候再试。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            CanSearch = false;            // 锁 5 秒
            Cooldown = 5;
            SearchButton.Content = "5";
            cooldownTimer.Start();

            stockNumber = TenNumber ?? "";
            _ = TenGet();                        // 拉一次实时盘口 + 刷展示框（K 线不在这里拉）
        }

        // 冷却 tick：Cooldown 倒数显示在按钮上，归 0 解锁
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

        // 实时行情总方法：负责"拉 + 刷"（用的是 F 区第 ② 个拉取方法）
        private async Task TenGet()
        {
            await FetchQuoteAsync();   // ② 拉实时盘口 → 缓存字段
            RefreshQuoteUI();          // 把缓存数据填进展示框
        }

        // ══════════════ C. 市场按钮区 ══════════════

        private void MarketBtn_Click(object sender, RoutedEventArgs e)
            => MarketPopup.IsOpen = !MarketPopup.IsOpen;   // 点市场按钮：开/关下拉

        // 下拉三个选项：档位 1沪 2深 3美
        private void MktShanghai_Click(object sender, RoutedEventArgs e) => SetMarket('1');
        private void MktShenzhen_Click(object sender, RoutedEventArgs e) => SetMarket('2');
        private void MktUS_Click(object sender, RoutedEventArgs e) => SetMarket('3');

        // 记档位 → 按钮改市场名 → 收起下拉
        private void SetMarket(char mode)
        {
            MarketMode = mode;
            MarketBtn.Content = mode switch
            {
                '1' => "沪市",
                '2' => "深市",
                _   => "美股",
            };
            MarketPopup.IsOpen = false;
        }

        // ══════════════ D. 界面展示 ══════════════

        // （原 RefreshEastUI：东财缓存 → 主页面下方两个列表，已随展示栏删除 · 2026-09-11）

        // 字段 → MainPage.xaml 的 TextBlock
        private void RefreshQuoteUI()
        {
            qName.Text = string.IsNullOrEmpty(stockName) ? "—" : stockName;
            qCode.Text = stockNumber;
            qPrice.Text = Fmt(price, "F3");
            qChange.Text = SignText(change, "F3");
            qChangePct.Text = SignText(changePct, "F2") + (changePct.HasValue ? "%" : "");

            Brush color = FlatBrush;                       // 涨红跌绿
            if (changePct.HasValue)
                color = changePct > 0 ? UpBrush : (changePct < 0 ? DownBrush : FlatBrush);
            qPrice.Foreground = qChange.Foreground = qChangePct.Foreground = color;

            qOpen.Text = Fmt(open, "F3");
            qPreClose.Text = Fmt(preClose, "F3");
            qHigh.Text = Fmt(high, "F3");
            qLow.Text = Fmt(low, "F3");
            qVol.Text = Fmt(volume, "F0") + " 手";
            qAmt.Text = Fmt(amount, "F0") + " 万";
            qTurnover.Text = Fmt(turnover, "F2") + " %";

            s5.Text = LevelText(sold5, sold5v);  s4.Text = LevelText(sold4, sold4v);
            s3.Text = LevelText(sold3, sold3v);  s2.Text = LevelText(sold2, sold2v);
            s1.Text = LevelText(sold1, sold1v);
            b1.Text = LevelText(buy1, buy1v);    b2.Text = LevelText(buy2, buy2v);
            b3.Text = LevelText(buy3, buy3v);    b4.Text = LevelText(buy4, buy4v);
            b5.Text = LevelText(buy5, buy5v);
        }

        // 盘口一档文字：价右对齐8位 + 量右对齐6位
        private static string LevelText(decimal? p, decimal? v)
            => Fmt(p, "F3").PadLeft(8) + "  " + Fmt(v, "F0").PadLeft(6) + " 手";

        // 带正负号（涨 +0.05 / 跌 -0.05）
        private static string SignText(decimal? v, string format)
            => v.HasValue ? (v >= 0 ? "+" : "") + v.Value.ToString(format) : "--";

        // ══════════════ E. 工具 ══════════════

        // 安全转 decimal：失败返回 null
        private static decimal? ToNum(string s)
            => decimal.TryParse(s, out decimal v) ? v : (decimal?)null;

        // 格式化：null → "--"
        private static string Fmt(decimal? v, string format) => v?.ToString(format) ?? "--";

        // 清空全部行情字段（拉取失败时）
        private void ClearQuote()
        {
            stockName = "";
            price = preClose = open = high = low = change = changePct = null;
            volume = amount = turnover = null;
            buy1 = buy2 = buy3 = buy4 = buy5 = null;
            buy1v = buy2v = buy3v = buy4v = buy5v = null;
            sold1 = sold2 = sold3 = sold4 = sold5 = null;
            sold1v = sold2v = sold3v = sold4v = sold5v = null;
        }

        // ══════════════ F. 数据拉取区（四个方法都在这，方便自检）═════════════
        // 四个方法各管一件事，都【只拉数据存进缓存】，不碰界面：
        //   ① FetchTimeAsync     只拉时间戳（心跳用）
        //   ② FetchQuoteAsync    只拉实时盘口
        //   ③ FetchDailyKAsync   只拉日K（历史每日）
        //   ④ FetchMinuteKAsync  只拉当日每分钟K
        // 数据源全部腾讯：实时用 qt.gtimg.cn，K 线用 web.ifzq.gtimg.cn / ifzq.gtimg.cn

        // ── ① 时间戳：只取 f[30]（14 位紧凑格式 yyyyMMddHHmmss）→ TenTime ──
        private async Task FetchTimeAsync()
        {
            if (TenNumber == null) return;        // 还没选过市场 → 不发请求
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + TenNumber);
                TenTime = Encoding.GetEncoding("GBK").GetString(bytes).Split('~')[30];
            }
            catch { }
        }

        // ── ② 实时盘口：GBK 解码 → 按 ~ 切分 → 按下标填进缓存字段 ──
        private async Task FetchQuoteAsync()
        {
            if (string.IsNullOrEmpty(stockNumber)) return;
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + stockNumber);
                string[] f = Encoding.GetEncoding("GBK").GetString(bytes).Split('~');

                stockName = f[1];
                price = ToNum(f[3]);  preClose = ToNum(f[4]);  open = ToNum(f[5]);
                volume = ToNum(f[6]);  buy1 = ToNum(f[9]);  buy1v = ToNum(f[10]);
                buy2 = ToNum(f[11]);  buy2v = ToNum(f[12]);  buy3 = ToNum(f[13]);  buy3v = ToNum(f[14]);
                buy4 = ToNum(f[15]);  buy4v = ToNum(f[16]);  buy5 = ToNum(f[17]);  buy5v = ToNum(f[18]);
                sold1 = ToNum(f[19]);  sold1v = ToNum(f[20]);  sold2 = ToNum(f[21]);  sold2v = ToNum(f[22]);
                sold3 = ToNum(f[23]);  sold3v = ToNum(f[24]);  sold4 = ToNum(f[25]);  sold4v = ToNum(f[26]);
                sold5 = ToNum(f[27]);  sold5v = ToNum(f[28]);
                change = ToNum(f[31]);  changePct = ToNum(f[32]);
                high = ToNum(f[33]);  low = ToNum(f[34]);
                amount = ToNum(f[37]);  turnover = ToNum(f[38]);
            }
            catch { ClearQuote(); }   // 失败 → 缓存清空（界面由 TenGet 统一刷）
        }

        // ── ③ 日K：拉历史每日（qfq 前复权）→ DailyCandles ──
        // 每行格式：[日期, 开, 收, 高, 低, 量]
        private async Task FetchDailyKAsync()
        {
            try
            {
                string url = "https://web.ifzq.gtimg.cn/appstock/app/fqkline/get?" +
                             $"param={KCode},day,,,320,qfq";     // 320 根足够覆盖全历史（该 ETF 共 278 根）
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty(KCode).GetProperty("day");

                DailyCandles.Clear();
                foreach (var row in arr.EnumerateArray())
                    DailyCandles.Add(ToCandle(row, "yyyy-MM-dd"));
            }
            catch { }
        }

        // ── ④ 当日每分钟K：拉 m1 → MinuteCandles ──
        // 每行格式：[时间 yyyyMMddHHmm, 开, 收, 高, 低, 量, {}, 额]
        // 注意：m1 会跨天返回（实测 400 根里含前两天），所以只保留最后一天 = "当日"
        private async Task FetchMinuteKAsync()
        {
            try
            {
                string url = "https://ifzq.gtimg.cn/appstock/app/kline/mkline?" +
                             $"param={KCode},m1,,300";            // m1 一分钟；想换周期就改这里的 m1
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty(KCode).GetProperty("m1");

                var all = new List<Candle>();
                foreach (var row in arr.EnumerateArray())
                    all.Add(ToCandle(row, "yyyyMMddHHmm"));

                string lastDay = all.Count > 0 ? all[^1].Date.ToString("yyyyMMdd") : "";
                MinuteCandles.Clear();
                foreach (Candle k in all)
                    if (k.Date.ToString("yyyyMMdd") == lastDay) MinuteCandles.Add(k);
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

        // ══════════════ G. K 线区：日K / 分钟K ══════════════
        // 这一区只管"什么时候去拉、拉完怎么用"，拉取本身在 F 区

        private bool kLoaded;   // 只在程序打开时拉一次（切页面回来不重拉）

        // 主界面出来后自动拉一次（日K + 分钟K），跟搜索键无关
        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (kLoaded) return;
            kLoaded = true;
            _ = LoadKChartsAsync();
        }

        private async Task LoadKChartsAsync()
        {
            KChartDay.SetEmptyText("日K 加载中…");
            await FetchDailyKAsync();                                    // ③ 拉日K
            if (DailyCandles.Count == 0) KChartDay.SetEmptyText("日K 没拉到数据");
            KChartDay.SetData(DailyCandles);                             // 灌进日K 图

            KChartMin.SetEmptyText("分钟K 加载中…");
            await FetchMinuteKAsync();                                   // ④ 拉当日每分钟K
            if (MinuteCandles.Count == 0) KChartMin.SetEmptyText("分钟K 没拉到数据");
            KChartMin.SetData(MinuteCandles);                            // 灌进分钟K 图
        }

        // 切换日K / 分钟K：两张图叠在同一格里，靠显隐切换；同时给按钮换底色
        private void DayKBtn_Click(object sender, RoutedEventArgs e) => ShowKChart(true);
        private void MinKBtn_Click(object sender, RoutedEventArgs e) => ShowKChart(false);

        private void ShowKChart(bool day)
        {
            KChartDay.Visibility = day ? Visibility.Visible : Visibility.Collapsed;
            KChartMin.Visibility = day ? Visibility.Collapsed : Visibility.Visible;
            DayKBtn.Background = day ? KBtnActive : KBtnIdle;
            MinKBtn.Background = day ? KBtnIdle : KBtnActive;
        }

    }
}
