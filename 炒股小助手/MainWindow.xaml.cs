// ═══════════════════════════════════════════════════════════════════════════
// MainWindow.xaml.cs —— 主窗口外壳：启动动作 + 页面导航
//
// 【分区】
//     A. 页面与字段区 —— 七个页面实例、缓存文件路径、导航按钮配色
//     B. 启动区       —— 启动流程要用的三个动作（缓存检查 / 亮出主界面 / 启动心跳）
//     C. 导航区       —— 七个导航按钮 + 切页方法
//
// 【启动流程在谁手上】在程序入口 App.xaml.cs 的 MainFlow（main）里，窗口自己不再主动跑：
//     ① 打开窗口 → ② CheckCache() → ③ ShowMainUi() → ④ StartHeartbeat()
// 所以这个文件里没有 Window_Loaded 事件 —— 那是上一版的写法，已经搬到 main 了。
//
// 七个页面都是独立的 UserControl（见同目录 *.xaml），各自对应一个导航按钮：
//     主界面 → MainPage        自选股 → WatchlistPage
//     历史搜索 → SearchHistoryPage   当日统计 → DailyStatsPage
//     操作教程 → GuidePage      设置 → SettingsPage     更新日志 → UpdateLogPage
// ═══════════════════════════════════════════════════════════════════════════
using System.IO;               // 文件读写（File / Path）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace 炒股小助手
{
    // partial：这个类分成两半，一半由 XAML 编译时生成（界面元素转成字段），
    //   另一半是这里手写的逻辑，编译时合成同一个类。
    public partial class MainWindow : Window
    {
        // ══════════════════ A. 页面与字段区 ══════════════════

        // ── 七个页面的实例 ──────────────────────────────────────────────
        // 页面只在窗口创建时 new 一次、之后复用，不在切换时重建：
        // 这样页面里的输入内容和列表滚动位置，切走再切回来还在。
        private readonly MainPage mainPage = new MainPage();
        private readonly WatchlistPage watchlistPage = new WatchlistPage();
        private readonly SearchHistoryPage searchHistoryPage = new SearchHistoryPage();
        private readonly DailyStatsPage dailyStatsPage = new DailyStatsPage();
        private readonly GuidePage guidePage = new GuidePage();
        private readonly SettingsPage settingsPage = new SettingsPage();
        private readonly UpdateLogPage updateLogPage = new UpdateLogPage();

        // ── 缓存文件 ───────────────────────────────────────────────────
        // UserCacheScan 把桌面 UserCache.json 的完整路径算出来存这里，
        // 后面 UserCacheLoading 读数据时直接用这个字段找文件。
        private string userCachePath = "";

        // ── 导航按钮的两种背景色 ────────────────────────────────────────
        // #262626 必须和 App.xaml 里 BtnDark 的默认背景一致，
        // 否则"取消高亮"之后按钮颜色和初始状态对不上。
        private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x66, 0xE0));  // 当前页：蓝色
        private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x26)); // 平时：深灰

        // 构造函数：只做 XAML 实例化（InitializeComponent 之后才能用 x:Name 找到控件）。
        // 启动流程不在这里跑 —— 那是程序入口 App.MainFlow 的事，它按顺序调用下面 B 区的几个方法。
        public MainWindow()
        {
            InitializeComponent();
        }

        // ══════════════════ B. 启动区 ══════════════════
        // 供启动流程调用的三个动作。谁先谁后由 App.MainFlow 决定，这里只提供"零件"。

        // ② 缓存检查（启动流程第 ② 步）：桌面 UserCache.json 在不在 → 能不能读。
        //    返回 false 就表示启动流程该停下来了（问题已经在这里弹过窗）。
        //    按顺序执行，前一步返回 true 才继续：
        //      先 UserCacheScan（检查/新建文件）→ 再 UserCacheLoading（读内容）
        public bool CheckCache()
        {
            try
            {
                // 第 1 步：检查 / 新建缓存文件
                if (!Check(UserCacheScan()))
                    return false;                     // 失败时 UserCacheScan 已弹窗，这里直接停

                // 第 2 步：读取缓存内容（当前为空实现）
                if (!Check(UserCacheLoading()))
                    return false;

                return true;   // 全部通过 → 启动流程才会继续往下走
            }
            catch (Exception ex)   // 兜底：上面没单独处理的意外错误
            {
                MessageBox.Show("加载失败：" + ex.Message,
                                "错误",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return false;
            }
        }

        // ③ 收掉加载层、亮出主界面（就是"删掉旧页面 → 展示新页面"）。
        //    窗口一打开时看到的是 XAML 里那层"正在加载中"，这个方法负责把它收掉。
        public void ShowMainUi()
        {
            SwitchTo(MainBtn, mainPage);          // 默认停在"主界面"页 + 点亮该按钮

            // 顺序不能反：先关盖层，再亮主界面。
            // 反过来的话主界面会先透出来一瞬再被盖住，看起来像闪了一下。
            LoadingLayer.Visibility = Visibility.Collapsed;   // ① 先关：收掉"正在加载中"
            MainUi.Visibility = Visibility.Visible;           // ② 后开：亮出主界面（导航栏 + 页面区）
        }

        // ④ 启动心跳（启动流程第 ④ 步）：转发给主界面，真正的计时器在 MainPage 的 C 区
        public void StartHeartbeat() => mainPage.StartHeartbeat();

        // 统一把关点：只有传入 true 才放行。
        // 现在只是原样返回，作用是让 CheckCache 里"每一步是否通过"都写在同一处，
        // 一眼能看出卡在哪一步；以后要加重试、超时、写日志，只改这一个方法。
        private bool Check(bool result) => result;

        // ── 缓存文件检查（第 ② 步的第 1 小步）────────────────────────────
        // 规定位置当前是桌面。文件不在就新建一个空 JSON，
        // 以后用这个文件存自选股、收藏股等数据。
        private bool UserCacheScan()
        {
            try
            {
                // SpecialFolder.Desktop = 当前用户的桌面目录（系统给出真实路径）
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                // Path.Combine 把"目录 + 文件名"拼成完整路径，斜杠由它处理
                userCachePath = Path.Combine(desktop, "UserCache.json");

                if (File.Exists(userCachePath))     // 文件已存在
                    return true;                    // → 不用新建

                // 不存在 → 写入 "{}"（最小的合法 JSON：一个空对象）。
                // 以后放数据就往这个对象里加键，例如 {"自选":[...],"收藏":[...]}
                File.WriteAllText(userCachePath, "{}");

                return true;                        // 新建成功
            }
            catch (Exception)                       // 没有写桌面的权限等情况都会掉进来
            {
                MessageBox.Show("未找到缓存文件，并缺少文件管理权限",
                                "缓存检查失败",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                return false;                       // 失败 → CheckCache 不会再往下走
            }
        }

        // ── 读取缓存内容（第 ② 步的第 2 小步）────────────────────────────
        // 文件里现在只有 {}，没有数据可读，先写成空实现（永远返回 true）。
        // 以后缓存里存了东西，就在这里用 System.Text.Json 读进内存，
        // 读失败时按 UserCacheScan 的写法弹"缓存文件出错"。
        private bool UserCacheLoading()
        {
            return true;
        }

        // ══════════════════ C. 导航区 ══════════════════
        // ShowMainUi（亮出主界面）之后，所有切页都走这里。
        // sender 是被点到的按钮，这里用不上，直接写死要切到哪一页。

        private void MainBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(MainBtn, mainPage);
        private void WatchlistBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(WatchlistBtn, watchlistPage);
        private void HistoryBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(HistoryBtn, searchHistoryPage);
        private void StatsBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(StatsBtn, dailyStatsPage);
        private void GuideBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(GuideBtn, guidePage);
        private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(SettingsBtn, settingsPage);
        private void UpdateLogBtn_Click(object sender, RoutedEventArgs e) => SwitchTo(UpdateLogBtn, updateLogPage);

        // 切页核心：btn = 刚点下的按钮，page = 要显示的页面
        private void SwitchTo(Button btn, UserControl page)
        {
            // ContentControl 一次只放一个内容，换 Content 就等于换页面
            PageHost.Content = page;

            // 互斥高亮：遍历导航栏所有子控件，点中的变蓝、其余恢复深灰
            foreach (UIElement child in NavPanel.Children)
            {
                // 只处理按钮：以后导航栏里混进别的控件也不会出错
                if (child is Button b)
                {
                    b.Background = (b == btn) ? ActiveBrush : NormalBrush;
                }
            }
        }
    }
}
