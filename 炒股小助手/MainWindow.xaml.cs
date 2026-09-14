// ═══════════════════════════════════════════════════════════════════════════
// 【迁移记录 2026-09-07】v0.1 的行情引擎已解封并迁往 MainPage.xaml.cs（主界面页），
//   展示层（旧输入框 getstocknumber / 旧文字表 QuoteBoard）已删除，
//   引擎字段与解析逻辑由主界面的搜索框 + 简易表格接管。
// ═══════════════════════════════════════════════════════════════════════════

// MainWindow.xaml.cs —— 主窗口的"后台代码"（code-behind）
//
// 这一代的职责分成两段：
//   A. 启动流程指挥（打开程序 → 加载界面 → 缓存检查 → 亮出主界面）
//   B. 页面导航（点左侧按钮切页面）
//
// 各方法对应关系（按你的设计）：
//   Window_Loaded    总指挥：Loading → MainCache → 输出 true 才 UIMake
//   Loading          ① 界面正中央显示"正在加载中"（黑底白字）
//   Check            ② 把门人：传进来的东西是 true 才输出 true
//   UserCacheScan    ③ 第一个执行：检测桌面 UserCache.json，没有就新建
//   UserCacheLoading ④ 读缓存（文件现在是空的，先挂空返回 true）
//   MainCache        ⑤ 缓存总方法：把 Scan / Loading 按顺序串起来
//   UIMake           ⑥ 主界面生成：收掉加载层、亮出导航 + 页面区
//
// 六个页面各自是独立的 UserControl（见同目录 *.xaml），对应关系：
//     主界面   → MainPage       自选股   → WatchlistPage
//     历史搜索 → SearchHistoryPage   当日统计 → DailyStatsPage
//     操作教程 → GuidePage      设置     → SettingsPage
// ═══════════════════════════════════════════════════════════════════════════
using System.IO;               // 文件读写（File / Path）就住在这个命名空间
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 炒股小助手
{
    // partial = 这个类被拆成"两半"，编译器最后拼成一个完整的类：
    //   一半由 XAML 编译时自动生成（把界面元素变成能用的字段），
    //   一半就是我们手写的这段逻辑。
    public partial class MainWindow : Window
    {
        // ── 六个页面的实例 ──────────────────────────────────────────────
        // readonly = 只读字段：只能在字段初始化（或构造函数）时赋值一次。
        // 页面在窗口创建时就 new 好、来回复用，而不是每次切换都重新 new：
        // 这样以后页面里输入了东西/滚动过列表，切走再切回来，状态都还在。
        private readonly MainPage mainPage = new MainPage();
        private readonly WatchlistPage watchlistPage = new WatchlistPage();
        private readonly SearchHistoryPage searchHistoryPage = new SearchHistoryPage();
        private readonly DailyStatsPage dailyStatsPage = new DailyStatsPage();
        private readonly GuidePage guidePage = new GuidePage();
        private readonly SettingsPage settingsPage = new SettingsPage();
        private readonly UpdateLogPage updateLogPage = new UpdateLogPage();

        // ── 缓存文件 ───────────────────────────────────────────────────
        // UserCacheScan 会把"桌面上 UserCache.json 的完整路径"算出来存这里，
        // 以后 UserCacheLoading 真正读数据时，就直接用这个字段找文件。
        private string userCachePath = "";

        // ── 导航按钮的两种背景色 ────────────────────────────────────────
        // Brush = 画刷，决定"用什么颜色、怎么涂"。static：一份就够，属于类本身。
        // Color.FromRgb(红, 绿, 蓝)，0x26 = 十进制 38。
        // NormalBrush 的 #262626 必须和 App.xaml 里 BtnDark 默认背景一致，
        // 这样"取消高亮"后按钮看起来和初始状态一模一样。
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xE0));  // 当前页：蓝色
        private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)); // 平时：深灰

        public MainWindow()
        {
            InitializeComponent(); // 把 XAML 里画好的界面"实例化"，之后才能用 x:Name 找到控件
            // 注意：这里不再直接切页面了 —— 启动那套事都交给 Window_Loaded，
            // 等缓存检查通过后由 UIMake 负责把主界面亮出来。
        }

        // ═══════════════════════════════════════════════════════════════
        // ① 总指挥：程序点开之后，整个启动流程都从这里开始
        // ═══════════════════════════════════════════════════════════════
        // Loaded 事件：窗口真正显示到屏幕上之后才触发（XAML 里 Loaded="Window_Loaded" 挂的）
        // async void：事件处理器允许这么写（void 是为了事件签名，async 让我们能用 await）
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Loading();               // 1. 显示"正在加载中"（盖在主界面上）
            await Task.Delay(TimeSpan.FromSeconds(1));   // 2. 强制让"正在加载中"显示满 1 秒再继续
                                     //   （Task.Delay = 暂停执行；TimeSpan.FromSeconds(1) = 1 秒。
                                     //     没有它的话，缓存检查是毫秒级，加载字一闪而过根本看不见）
            bool ok = MainCache();   // 3. 同步执行缓存总方法（文件操作是毫秒级，先不搞后台线程）
            if (ok)                  // 4. MainCache 输出 true → 才执行 UIMake 生成主界面
                UIMake();
            // 缓存失败时：UserCacheScan / MainCache 内部已经弹窗报错，
            // 这里不执行 UIMake，画面就停在"正在加载中"那一页。
        }

        // ═══════════════════════════════════════════════════════════════
        // ② 启动画面
        // ═══════════════════════════════════════════════════════════════
        // XAML 里 LoadingLayer 默认就是盖在最上层、可见的（黑底 + 中央"正在加载中"），
        // 这个方法的意义是"显式声明进入加载状态"，以后想加进度动画就往这里写。
        private void Loading()
        {
            LoadingLayer.Visibility = Visibility.Visible;    // 盖层亮出来（保险起见再设一次）
            MainUi.Visibility = Visibility.Collapsed;       // 主界面先藏着，等 UIMake 再放
        }

        // ═══════════════════════════════════════════════════════════════
        // ③ 把门人：检测逻辑
        // ═══════════════════════════════════════════════════════════════
        // 你定的规矩：只有当"被检测的东西"是 true 时，Check 才输出 true。
        // 现在它只是把参数原样传回去 —— 别嫌它绕，它的价值在"读代码"：
        // MainCache 里每完成一步都 Check 一下，流程的"把关点"一目了然。
        // 将来要加"重试/超时/写日志"之类的检测行为，也只需要改这一个方法。
        private bool Check(bool result) => result;   // "=>" 表达式体：方法只有一句话时可以这么写

        // ═══════════════════════════════════════════════════════════════
        // ④ 文件检测程序：软件打开后第一步执行的缓存文件检查
        // ═══════════════════════════════════════════════════════════════
        // 任务：UserCache.json 不在规定位置（当前定为桌面）→ 新建一个空的 JSON 文件。
        // 这个文件将来用来缓存自选股、收藏股等数据，用 JSON 格式。
        private bool UserCacheScan()
        {
            try
            {
                // SpecialFolder.Desktop = 当前用户的"桌面"目录（系统帮你找到真实路径）
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                // Path.Combine = 把"目录 + 文件名"拼成一条完整路径（自动处理斜杠）。
                // 文件全名定为 UserCache.json（.json 后缀表示里面装的是 JSON 数据）
                userCachePath = Path.Combine(desktop, "UserCache.json");

                if (File.Exists(userCachePath))     // 文件已经在了？
                    return true;                    // → 直接输出 true，不用新建

                // 不在 → 新建：写入 "{}"，这是最基础的合法 JSON（一个空对象）。
                // 以后要放自选股/收藏股，就往这个对象里加键：{"自选":[...],"收藏":[...]}
                File.WriteAllText(userCachePath, "{}");

                return true;                        // 新建成功 → 输出 true
            }
            catch (Exception)                       // 上面任何一句抛异常（没权限写桌面等）都会掉进来
            {
                // 报错文案按你定的写（未找到文件 + 缺少文件管理权限）
                MessageBox.Show("未找到缓存文件，并缺少文件管理权限",
                                "缓存检查失败",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return false;                       // 失败 → 输出 false，MainCache 就不会往下走
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ⑤ 读取 UserCache 的程序
        // ═══════════════════════════════════════════════════════════════
        // 现在缓存文件是空的（内容只有 {}），没有数据可读 → 先挂空，永远输出 true。
        // 将来 UserCache 里存了东西，就在这里用 System.Text.Json 把文件读进内存，
        // 读失败的报错文案是"缓存文件出错"（参照 UserCacheScan 的 try/catch 写法即可）。
        private bool UserCacheLoading()
        {
            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        // ⑥ 缓存总方法
        // ═══════════════════════════════════════════════════════════════
        // 把缓存相关的步骤按顺序串起来：只有前一个输出 true，才继续执行下一个。
        // 注意：这里你写的"把 Cache 和 UserCacheScan 丢进去" —— 我理解为
        // "Cache" 就是上面那个读取方法 UserCacheLoading 的简称，顺序是：
        //   先 UserCacheScan（检查/新建文件）→ 再 UserCacheLoading（读内容）。
        private bool MainCache()
        {
            try
            {
                // 第 1 步：缓存文件检测/新建 —— Check 是 true 才继续
                if (!Check(UserCacheScan()))
                    return false;                     // 失败：UserCacheScan 已自己弹窗，这里直接停

                // 第 2 步：读取缓存（目前挂空）—— 同样只有 true 才继续
                if (!Check(UserCacheLoading()))
                    return false;

                return true;   // 所有步骤都通过 → MainCache 输出 true，调用方才会去 UIMake
            }
            catch (Exception ex)   // 兜底：上面没单独处理的意外错误，报错文案"加载失败"
            {
                MessageBox.Show("加载失败：" + ex.Message,
                                "错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // ⑦ 主 UI 生成
        // ═══════════════════════════════════════════════════════════════
        // 关于"把被注释的和前面写的 UI 程序全丢进来"：
        //   文件最顶部那整段 v0.1 旧 UI 是被 // 注释的状态，注释 = 不参与编译，
        //   所以它没法被"丢进"任何方法里执行 —— 只能留在原处当技术存档。
        //   这里 UIMake 承担的是"当前这一代主界面的组装入口"：
        //   以后主界面要扩展（比如往 MainPage 塞行情区），初始化代码往这里堆。
        private void UIMake()
        {
            SwitchTo(MainBtn, mainPage);          // 默认停"主界面"页 + 点亮"主界面"按钮

            // ⚠ 顺序很重要（主人特别要求）：必须先关掉"正在加载中"盖层，再亮主界面。
            //   如果反过来，主界面会先透出来一瞬间、再被盖层遮住，造成"闪一下"。
            LoadingLayer.Visibility = Visibility.Collapsed;   // ① 先关：收掉"正在加载中"
            MainUi.Visibility = Visibility.Visible;           // ② 后开：亮出主界面（导航栏 + 页面区）
        }

        // ═══════════════════════════════════════════════════════════════
        // B 段：页面导航（v1 骨架留下的活，UIMake 之后所有切页都走这里）
        // ═══════════════════════════════════════════════════════════════
        // "=>" 表达式体：等价于 { SwitchTo(...); }，方法只有一句话时可省花括号
        // sender = 触发事件的那个控件（被点到的按钮）；RoutedEventArgs = 事件参数，这里用不上
        private void MainBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(MainBtn, mainPage);
        private void WatchlistBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(WatchlistBtn, watchlistPage);
        private void HistoryBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(HistoryBtn, searchHistoryPage);
        private void StatsBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(StatsBtn, dailyStatsPage);
        private void GuideBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(GuideBtn, guidePage);
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(SettingsBtn, settingsPage);
        private void UpdateLogBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(UpdateLogBtn, updateLogPage);

        // 切页核心：btn = 刚点下的按钮；page = 要显示的页面
        private void SwitchTo(Button btn, UserControl page)
        {
            // ContentControl 一次只能装一个孩子：给它换 Content 就等于"换页面"
            PageHost.Content = page;

            // 互斥高亮：遍历导航栏里所有子控件，点中的变蓝、其余恢复深灰。
            // NavPanel.Children = 这个面板里装着的全部子控件（一个集合）
            foreach (UIElement child in NavPanel.Children)
            {
                // is 模式匹配：如果 child 确实是 Button 类型，就把它当作变量 b 用。
                // 这样以后 NavPanel 里就算加了别的控件（提示文字等）也不会崩 —— 不是按钮就跳过。
                if (child is Button b)
                {
                    // 三元运算符 (条件) ? 真时的值 : 假时的值
                    b.Background = (b == btn) ? ActiveBrush : NormalBrush;
                }
            }
        }
    }
}
