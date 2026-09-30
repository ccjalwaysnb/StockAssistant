// ═══════════════════════════════════════════════════════════════════════════
// MainWindow.xaml.cs —— 主窗口外壳（启动动作 + 页面导航 + 自选文件读写）
//
// 【分区】
//   A. 初始化区     —— 构造函数
//   B. 主流程区     —— 启动流程要用的零件（前期准备 / 亮主界面 / 启心跳）
//   C. 喂输入区     —— 把主页面搜索到的代码和市场告诉心跳
//   D. 自选读写区   —— 自选的增删、链表与 json 文件的读写、按市场分组
//   E. 导航区       —— 六个导航按钮 + 切页 + 从自选股跳回主页面搜索
//   F. 声明区       —— 页面实例、心跳、请求器、界面颜色、JSON 设置
//   G. 数据区       —— 缓存文件路径、自选链表、市场前缀表
//
// 【这个文件负责】当"窗口外壳"：管六个页面、管启动动作、管自选文件，
//   页面内部的事都在各自的 .xaml.cs 里，这里只做它们之间的调度。
// ═══════════════════════════════════════════════════════════════════════════
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 炒股小助手
{
    public partial class MainWindow : Window
    {
        // ══════════════════ A. 初始化区 ══════════════════
        // 本区方法：MainWindow（构造函数）

        // 接收：无　返回：无　改变：实例化 XAML、建心跳对象、把两个页面交给心跳
        // 说明：启动流程不在这里跑 —— 那是 App.MainFlow 的事，它按顺序调 B 区那几个方法。
        public MainWindow()
        {
            InitializeComponent();

            heartbeat = new Heartbeat();                  // 无参建出来就行（它不认识页面）
            heartbeat.SetMainPage(mainPage);              // 把主界面交给心跳：拉完盘口要调页面刷新
            heartbeat.SetWatchlistPage(watchlistPage);    // 把自选股页也交给它：拉完自选要调页面刷新
        }

        // ══════════════════ B. 主流程区 ══════════════════
        // 本区方法：
        //   Prepare                 —— ③ 前期准备（缓存 → 自选股预拉 → 市场状态）
        //   CheckCache              —— 子方法①：缓存检查
        //   UserCacheScan           —— 子方法①的第 1 小步：找 / 建缓存文件
        //   UserCacheLoading        —— 子方法①的第 2 小步：读缓存并分给页面和心跳
        //   CheckMarketStatusAsync  —— 子方法③：问一次市场开没开
        //   ShowMainUi              —— ④ 收掉加载层、亮出主界面
        //   StartHeartbeat          —— ⑤ 启动心跳
        //
        // 【启动流程】（App.MainFlow 按顺序调这里的零件）
        //   ③ Prepare
        //   ├ ① CheckCache            缓存文件在不在、能不能读
        //   │ ├ UserCacheScan         找 / 建 UserCache.json
        //   │ └ UserCacheLoading      读进 watchlist 链表 → 分给自选股页和心跳
        //   ├ ② RefreshWatchlistAsync （心跳那边的方法）先拉一遍自选股，让自选股页一打开就有内容
        //   └ ③ CheckMarketStatusAsync 问一次市场开没开（失败不拦启动，心跳每分钟还会再问）
        //   ④ ShowMainUi               收掉"正在加载中"、亮出主界面
        //   ⑤ StartHeartbeat           心跳开始跳

        // 接收：无　返回：能不能继续启动　改变：跑完三个子方法（缓存不过就返回 false）
        public async Task<bool> Prepare()
        {
            if (!CheckCache()) return false;           // 子方法①：缓存检查（不过就停）

            await heartbeat.RefreshWatchlistAsync();   // 子方法②：先拉一遍自选股并推给自选股页

            await CheckMarketStatusAsync();            // 子方法③：市场状态检查（成不成都不拦启动）

            return true;
        }

        // 接收：无　返回：缓存能不能用　改变：错误时弹窗提示；通过时 watchlist 链表已填好
        // 说明：报错集中在这一个方法里 —— 下面两个小方法只回答"成没成"，自己不弹窗。
        private bool CheckCache()
        {
            try
            {
                if (!UserCacheScan())     // 第 1 小步：文件不在就建一个
                {
                    MessageBox.Show("未找到缓存文件，并缺少文件管理权限",
                                    "缓存检查失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                if (!UserCacheLoading())  // 第 2 小步：把内容读进内存
                {
                    MessageBox.Show("缓存文件读取失败（内容可能不是合法 JSON）",
                                    "缓存检查失败", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                return true;
            }
            catch (Exception ex)          // 兜底：没预料到的异常
            {
                MessageBox.Show("缓存检查失败：" + ex.Message,
                                "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        // 接收：无　返回：成没成　改变：算出 userCachePath；文件不在就新建一个 "{}"
        // 说明：只回答成没成，弹窗交给 CheckCache 统一做。
        private bool UserCacheScan()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);   // 桌面目录
                userCachePath = Path.Combine(desktop, "UserCache.json");                          // 拼出完整路径

                if (File.Exists(userCachePath)) return true;    // 已有 → 不用建

                File.WriteAllText(userCachePath, "{}");         // 写一个最小的合法 JSON

                if (!File.Exists(userCachePath))                // 写完复查：万一"报成功但文件不在"
                    throw new IOException("缓存文件写入后没找到：" + userCachePath);

                return true;
            }
            catch { return false; }   // 没权限 / 磁盘问题等，一律算没成
        }

        // 接收：无　返回：成没成　改变：watchlist 链表 + 自选股页和心跳拿到的两组自选代码
        // 说明：文件读不出来（不存在 / 内容坏）也返回 true —— 自选读不出来不该拦住整个程序启动。
        private bool UserCacheLoading()
        {
            watchlist = ReadWatchCache().watchlist;                                          // 文件 → 链表
            watchlistPage.GroupWatchCodes(watchlist);                                        // 链表 → 两张自选代码表
            heartbeat.SetWatchCodes(watchlistPage.aShareWatchCodes, watchlistPage.usWatchCodes);   // 交给心跳（共用同一对 List）
            return true;
        }

        // 接收：无　返回：无　改变：心跳的两个"市场开没开"开关
        // 说明：解析方式跟 Heartbeat.FetchMarketStatusAsync 一样，区别是这里只在启动时跑一次，
        //   而且是把结果写进【心跳的】开关 —— 这样心跳一启动就不会空转。
        private async Task CheckMarketStatusAsync()
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

                heartbeat.aShareMarketOpen = aOpen;   // 整轮解析成功才覆盖
                heartbeat.usMarketOpen = usOpen;
            }
            catch { }   // 这次没问到：什么都不做，心跳每分钟一次的问询会补上
        }

        // 接收：无　返回：无　改变：默认停在主界面页；收掉加载层、亮出主界面
        public void ShowMainUi()
        {
            SwitchTo(MainBtn, mainPage);   // 默认停在"主界面"页 + 点亮该按钮

            LoadingLayer.Visibility = Visibility.Collapsed;   // 先关"正在加载中"（顺序不能反，反了会闪一下）
            MainUi.Visibility = Visibility.Visible;           // 再亮出导航 + 页面区
        }

        // 接收：无　返回：无　改变：心跳开始跳
        public void StartHeartbeat() => heartbeat.Start();

        // ══════════════════ C. 喂输入区 ══════════════════
        // 本区方法：SetHeartbeatMainCode

        // 接收：完整腾讯代码 + 市场标记（'1' = A股，'2' = 美股）　返回：无
        // 改变：心跳里的 MainCode（主页面标的）和 MainMarket（它属于哪个市场）
        // 说明：主页面搜索成功时调它 —— 心跳靠这两个量拼查询串、判断要不要动主页面。
        public void SetHeartbeatMainCode(string? tenCode, char marketKind)
        {
            heartbeat.MainCode = tenCode;
            heartbeat.MainMarket = marketKind;
        }

        // ══════════════════ D. 自选读写区 ══════════════════
        // 本区方法：
        //   AddWatchItem        —— 加自选（总入口，四步）
        //   RemoveWatchItem     —— 删自选（总入口，四步）
        //   AddWatchToList      —— 第①步：进链表
        //   RemoveWatchFromList —— 删除时的第①步：从链表拿掉
        //   ClearCacheFile      —— 第②步：清空 json 文件
        //   SaveWatchlistToFile —— 第③步：链表整体写回文件
        //   RegroupWatchCodes   —— 第④步：按市场重新分到两张自选代码表
        //   ReadWatchCache      —— 启动时：把文件读成对象
        //   IsWatchCode         —— 判断某只在不在自选里
        //   SplitTenCode        —— "市场前缀 + 纯代码"拆开
        //
        // 【整体思路】自选股的"真相"是内存链表 watchlist，json 文件只是它的副本。
        //   所以"加一只自选" = ① 改链表 → ② 清文件 → ③ 整体写回 → ④ 重新分组；
        //   删除只是把第①步换成"从链表里拿掉"。这样文件和链表永远对得上。

        // 接收：完整腾讯代码 + 名字　返回：成没成　改变：链表、json 文件、两张自选代码表
        public bool AddWatchItem(string tenCode, string name)
        {
            if (!AddWatchToList(tenCode, name)) return false;   // 第①步：进链表
            if (!ClearCacheFile()) return false;                // 第②步：清空 json
            if (!SaveWatchlistToFile()) return false;           // 第③步：整体写回
            RegroupWatchCodes();                                // 第④步：重新分组
            return true;
        }

        // 接收：完整腾讯代码　返回：成没成　改变：链表、json 文件、两张自选代码表
        public bool RemoveWatchItem(string tenCode)
        {
            if (!RemoveWatchFromList(tenCode)) return false;    // 第①步：从链表拿掉
            if (!ClearCacheFile()) return false;                // 第②步：清空 json
            if (!SaveWatchlistToFile()) return false;           // 第③步：整体写回
            RegroupWatchCodes();                                // 第④步：重新分组
            return true;
        }

        // 接收：完整腾讯代码 + 名字　返回：加成了或本来就有　改变：watchlist 链表
        public bool AddWatchToList(string tenCode, string name)
        {
            (string market, string code) = SplitTenCode(tenCode);   // 拆成"市场前缀 + 纯代码"
            if (code == "") return false;                           // 拆不出来就不加

            // 基于"链表里有没有同市场同代码"判断要不要真的加（有就直接算成功）
            if (watchlist.Any(item => item.market == market && item.code == code)) return true;

            watchlist.Add(new WatchItem { market = market, code = code, name = name });
            return true;
        }

        // 接收：完整腾讯代码　返回：删掉了没有　改变：watchlist 链表
        private bool RemoveWatchFromList(string tenCode)
        {
            int removed = watchlist.RemoveAll(item => item.tenCode == tenCode);   // 基于"哪几条代码相同"删除
            return removed > 0;                                                   // 一条都没删 = 本来就没有
        }

        // 接收：无　返回：成没成　改变：json 文件变成 "{}"
        private bool ClearCacheFile()
        {
            try
            {
                if (string.IsNullOrEmpty(userCachePath)) return false;
                File.WriteAllText(userCachePath, "{}");
                return true;
            }
            catch { return false; }   // 没权限 / 文件被占用
        }

        // 接收：无　返回：成没成　改变：json 文件 = 链表内容（先按 market 排序）
        // 说明：必须整体写一次 —— JSON 是完整结构，往中间追加会写成坏文件。
        private bool SaveWatchlistToFile()
        {
            try
            {
                if (string.IsNullOrEmpty(userCachePath)) return false;

                watchlist = watchlist.OrderBy(item => item.market).ToList();   // 按市场排序（同一个市场的挨在一起）
                var cache = new WatchCache { watchlist = watchlist };          // 包成"文件那一层"
                File.WriteAllText(userCachePath, JsonSerializer.Serialize(cache, CacheJsonOptions));
                return true;
            }
            catch { return false; }
        }

        // 接收：无　返回：链表、两张自选代码表　改变：它们都跟着新链表走
        // 说明：中间要停一下心跳 —— 重新分组时两张表会短暂为空，心跳正好那时拼查询串就白拉一次。
        private void RegroupWatchCodes()
        {
            heartbeat.Pause();                          // ① 先停心跳（0.5 秒那个）

            try
            {
                watchlistPage.GroupWatchCodes(watchlist);   // ② 按前缀重新分组（它开头会先清空两张表）
            }
            finally
            {
                heartbeat.Resume();                       // ③ 不管成没成，心跳都要恢复
            }

            _ = heartbeat.RefreshWatchlistAsync();        // ④ 立刻按新自选拉一次行情并推送（新加的那只要马上有数据）
        }

        // 接收：无　返回：文件内容对应的对象　改变：无
        // 说明：文件不存在 / 内容是 {} / 内容被改坏 —— 一律返回空对象，不让"读自选"把程序搞崩。
        private WatchCache ReadWatchCache()
        {
            if (!File.Exists(userCachePath)) return new WatchCache();

            try
            {
                WatchCache? cache = JsonSerializer.Deserialize<WatchCache>(File.ReadAllText(userCachePath));
                if (cache?.watchlist == null) return new WatchCache();
                return cache;
            }
            catch { return new WatchCache(); }
        }

        // 接收：完整腾讯代码　返回：在不在自选里　改变：无
        // 说明：查的是【链表】而不是那两张代码表 —— 链表是真相，代码表是从它派生出来的。
        public bool IsWatchCode(string? tenCode)
        {
            if (string.IsNullOrEmpty(tenCode)) return false;
            return watchlist.Any(item => item.tenCode == tenCode);
        }

        // 接收：完整腾讯代码（如 sz159248 / us.NVDA）　返回：市场前缀 + 纯代码　改变：无
        // 说明：基于"开头匹配哪个已知前缀"来拆；"us." 在表里排在 "us" 前面，否则 us.NVDA 会被拆错。
        private static (string market, string code) SplitTenCode(string tenCode)
        {
            foreach (string prefix in KnownPrefixes)
                if (tenCode.StartsWith(prefix))
                    return (prefix, tenCode[prefix.Length..]);   // [n..] = C# 8 的范围写法：从第 n 个字符取到最后

            return ("", tenCode);   // 认不出来的前缀就原样当代码（理论上走不到）
        }

        // ══════════════════ E. 导航区 ══════════════════
        // 本区方法：
        //   六个 Xxx_Click  —— 导航按钮，各自切到自己那一页
        //   SwitchTo        —— 切页核心（换内容 + 互斥高亮）
        //   JumpToSearch    —— 自选股页点了某一行 → 跳主页面并搜这只票

        private void MainBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(MainBtn, mainPage);
        private void WatchlistBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(WatchlistBtn, watchlistPage);
        private void StatsBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(StatsBtn, dailyStatsPage);
        private void GuideBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(GuideBtn, guidePage);
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(SettingsBtn, settingsPage);
        private void UpdateLogBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(UpdateLogBtn, updateLogPage);

        // 接收：被点的按钮 + 要显示的页面　返回：无　改变：页面区内容 + 导航按钮高亮
        private void SwitchTo(Button btn, UserControl page)
        {
            PageHost.Content = page;   // ContentControl 一次只放一个内容，换 Content 就等于换页面

            foreach (UIElement child in NavPanel.Children)   // 遍历导航栏做"互斥高亮"
            {
                if (child is Button b)                       // 只处理按钮（以后混进别的控件也不会出错）
                    b.Background = (b == btn) ? ActiveBrush : NormalBrush;
            }
        }

        // 接收：完整腾讯代码　返回：无　改变：切到主页面并按这只票搜一遍（冷却中则只提示、不跳）
        public void JumpToSearch(string tenCode)
        {
            if (!mainPage.CanSearch)   // 基于"搜索冷却过没过"判断能不能跳
            {
                MessageBox.Show($"搜索冷却中，还有 {mainPage.Cooldown} 秒",
                                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;                // 不跳：跳过去也搜不了，反而像点了没反应
            }

            SwitchTo(MainBtn, mainPage);        // 切到主页面
            mainPage.SearchByTenCode(tenCode);  // 填搜索框、换市场档位、点搜索 —— 都在 MainPage 那边做
        }

        // ══════════════════ F. 声明区 ══════════════════
        // 说明：这一区是"要用的东西"，不是业务数据。
        // 页面只在窗口创建时 new 一次、之后复用（不重建），所以切走再切回来，输入内容和滚动位置还在。
        private readonly MainPage mainPage = new();
        private readonly WatchlistPage watchlistPage = new();
        private readonly DailyStatsPage dailyStatsPage = new();
        private readonly GuidePage guidePage = new();
        private readonly SettingsPage settingsPage = new();
        private readonly UpdateLogPage updateLogPage = new();
        private readonly Heartbeat heartbeat;              // 心跳（构造函数里建）

        private readonly HttpClient http = new();          // 前期准备里问一次市场状态用
        private const string StatusUrl = "https://api.tradeti.me/v1/status";

        // 导航按钮的两种背景色（#262626 要和 App.xaml 里 BtnDark 的默认背景一致，否则"取消高亮"会对不上）
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xE0));   // 当前页：蓝
        private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26));   // 平时：深灰

        // 写 JSON 的两条设置：带缩进（人看着舒服）、中文不转义（不然会变成 \uXXXX）
        private static readonly JsonSerializerOptions CacheJsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        // ══════════════════ G. 数据区 ══════════════════
        private string userCachePath = "";        // 缓存文件的完整路径（UserCacheScan 算出来存这儿）
        public List<WatchItem> watchlist = new();  // 自选链表（内存里的"真相"：market + 纯代码 + 名字）

        // 认得出的市场前缀。顺序有讲究："us." 必须在 "us" 前面，否则 us.NVDA 会被拆错。
        private static readonly string[] KnownPrefixes = { "us.", "sh", "sz", "us", "bj", "hk" };
    }
}
