// MainPage.xaml.cs —— 主界面：市场选择 + 搜索 + 行情拉取（腾讯实时 / 东财历史与分时）
// 布局：A 字段 → B 搜索链(主) → C 市场区 → D 展示 → E 工具
using System.Net.Http;
using System.Text;                 // Encoding（GBK 解码）
using System.Text.Json;            // JsonDocument（东财 JSON 解析）
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
        private readonly DispatcherTimer westTimeTimer = new();   // 东财时间戳轮询（1 秒，程序一开就转）
        private readonly HttpClient http = new HttpClient();      // 共用请求器（腾讯+东财）

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

        // 东财缓存（供画图/统计）
        public List<decimal> HistoryCloses = new();  // 历史每日收盘价
        public List<decimal> MinutePrices = new();   // 当日每分钟最新价

        // 数据更新时间戳（两接口格式不同）
        public string TenTime = "";   // 腾讯：yyyyMMddHHmmss 紧凑串，如 20260910153421
        public long WestTime;         // 东财：Unix 秒，如 1789025661
        public string? tentime;       // 上一次的腾讯时间戳（比对"数据有没有更新"用；初始为空）
        public long? westtime;        // 上一次的东财时间戳（同上；初始为空）

        // 涨跌色（A股：涨红跌绿）
        private static readonly Brush UpBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));
        private static readonly Brush DownBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));
        private static readonly Brush FlatBrush = Brushes.White;

        public MainPage()
        {
            InitializeComponent();

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // GBK 支持
            http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");   // 东财偶发 403，带 UA/Referer 防
            http.DefaultRequestHeaders.Add("Referer", "https://quote.eastmoney.com/");

            cooldownTimer.Interval = TimeSpan.FromSeconds(1);
            cooldownTimer.Tick += CooldownTimer_Tick;

            // 两条时间戳心跳：程序一打开（还在 loading 时）就开始跑
            // 注意：没搜索过时 Ten/WestNumber 都是 null，方法内部直接 return 不发请求
            timeTimer.Interval = TimeSpan.FromSeconds(0.5);        // 腾讯：0.5 秒一次
            timeTimer.Tick += async (s, e) => await LoadTenTimeAsync();
            timeTimer.Start();

            westTimeTimer.Interval = TimeSpan.FromSeconds(1);      // 东财：1 秒一次
            westTimeTimer.Tick += async (s, e) => await LoadWestTimeAsync();
            westTimeTimer.Start();
        }

        // ══════════════ B. 主事件链：搜索按钮 ══════════════
        // 点击后顺序：① MarketMode=='0' 拦下 → ② 拼前缀(Ten/West) → ③ 冷却检查
        //             → ④ 三路拉取：腾讯(TenGet) + 东财历史日收盘(仅此一次) + 东财分时(WestGet)
        //（时间戳不在这里拉 —— 腾讯 0.5 秒 / 东财 1 秒两条心跳自动轮询）
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
            _ = TenGet();                        // ① 腾讯实时（TenGet：拉取 + 刷展示框）
            _ = LoadDailyClosesAsync();          // ② 东财历史日收盘（只在点搜索时拉，不进心跳）
            _ = WestGet();                       // ③ 东财当日分时（WestGet：拉取 + 刷列表）
            // 时间戳不在这里拉：腾讯 0.5 秒 / 东财 1 秒两条心跳自动轮询（程序打开就在跑）
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

        // 拉取①：腾讯实时盘口（TenGet 总方法 + 两个子方法）
        // TenGet 只做"串流程"：先拉数据进缓存，再把缓存填进展示框
        private async Task TenGet()
        {
            await TenFetchAsync();   // 子①：拉取腾讯接口实时盘口 → 存入缓存字段
            RefreshQuoteUI();        // 子②：把缓存数据填入展示框（UI）
        }

        // 子①：拉取腾讯实时盘口 → 缓存字段（GBK → ~ 切分 → 赋值）
        private async Task TenFetchAsync()
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
            catch { ClearQuote(); }   // 失败 → 缓存清空（展示框由 TenGet 统一刷）
        }

        // 拉取②：东财 历史每日收盘价 → HistoryCloses
        // klines 每行：日期,开,收,高,低,... → 取下标 2 收盘
        private async Task LoadDailyClosesAsync()
        {
            if (WestNumber == null) return;
            try
            {
                string url = $"https://push2his.eastmoney.com/api/qt/stock/kline/get?" +
                    $"secid={WestNumber}&fields1=f1,f2,f3,f4,f5,f6" +
                    "&fields2=f51,f52,f53,f54,f55,f56,f57,f58,f59,f60,f61" +
                    "&klt=101&fqt=1&beg=0&end=20500101";   // klt=101 日线，全历史
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty("klines").EnumerateArray();

                HistoryCloses.Clear();
                foreach (var item in arr)
                {
                    string[] c = item.GetString()!.Split(',');
                    if (decimal.TryParse(c[2], out decimal v)) HistoryCloses.Add(v);
                }

                RefreshEastUI();   // 刷到就更新下方缓存区
            }
            catch { /* 失败就算了，下次搜索再拉 */ }
        }

        // 拉取③：东财当日每分钟价格（WestGet 总方法 + 两个子方法）
        // WestGet 只串流程：先拉数据进缓存，再把缓存填进下方列表
        private async Task WestGet()
        {
            await WestFetchAsync();   // 子①：拉东财当日分时 → MinutePrices 缓存
            RefreshEastUI();          // 子②：缓存 → 下方列表（UI）
        }

        // 子①：拉东财当日分时 → MinutePrices
        // trends 每行：时间,开?,最新价,高,低,量,额,均价 → 取下标 2 最新价
        private async Task WestFetchAsync()
        {
            if (WestNumber == null) return;
            try
            {
                string url = $"https://push2his.eastmoney.com/api/qt/stock/trends2/get?" +
                    $"secid={WestNumber}&fields1=f1,f2,f3,f4,f5,f6,f7,f8,f9,f10,f11,f12,f13" +
                    "&fields2=f51,f52,f53,f54,f55,f56,f57,f58&ndays=1&iscr=0";
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var arr = doc.RootElement.GetProperty("data").GetProperty("trends").EnumerateArray();

                MinutePrices.Clear();
                foreach (var item in arr)
                {
                    string[] c = item.GetString()!.Split(',');
                    if (decimal.TryParse(c[2], out decimal v)) MinutePrices.Add(v);
                }
            }
            catch { /* 失败就算了，下次心跳再拉 */ }
        }

        // 拉取④：腾讯时间戳 → TenTime
        // 腾讯 f[30] 是 14 位紧凑时间，如 20260910153421（= 2026-09-10 15:34:21）
        private async Task LoadTenTimeAsync()
        {
            if (TenNumber == null) return;
            try
            {
                byte[] bytes = await http.GetByteArrayAsync("https://qt.gtimg.cn/q=" + TenNumber);
                string[] f = Encoding.GetEncoding("GBK").GetString(bytes).Split('~');
                TenTime = f[30];

                // 时间戳变了 = 盘口数据更新了 → 触发一次 TenGet（拉盘口 + 刷界面）
                if (tentime != TenTime)
                {
                    tentime = TenTime;   // 记下这次的值，下次拿它比对
                    _ = TenGet();
                }
            }
            catch { }
        }

        // 拉取⑤：东财数据更新时间 → WestTime
        // 东财 push2 实时接口的 f86 是 Unix 秒，如 1789025661（= 2026-09-10 15:34:21）
        private async Task LoadWestTimeAsync()
        {
            if (WestNumber == null) return;
            try
            {
                string url = $"https://push2.eastmoney.com/api/qt/stock/get?secid={WestNumber}&fields=f86";
                string json = await http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                WestTime = doc.RootElement.GetProperty("data").GetProperty("f86").GetInt64();

                // 时间戳变了 = 东财数据更新了 → 触发一次 WestGet（拉当日分时 + 刷列表）
                if (westtime != WestTime)
                {
                    westtime = WestTime;
                    _ = WestGet();
                }
            }
            catch { }
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

        // 东财缓存区：条数 + 两个列表重绑（Clear+Add 后强制 ListBox 重画）
        private void RefreshEastUI()
        {
            eastDaily.Text = $"历史收盘：{HistoryCloses.Count} 条";
            eastMinute.Text = $"当日分时：{MinutePrices.Count} 条";
            listHistory.ItemsSource = null;   // 先断绑
            listHistory.ItemsSource = HistoryCloses;
            listMinute.ItemsSource = null;
            listMinute.ItemsSource = MinutePrices;
        }

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
    }
}
