// ═══════════════════════════════════════════════════════════════════════════
// App.xaml.cs —— 程序入口：启动总流程（main）
//
// WPF 的 Main() 不用我们写：编译器会按 App.xaml 自动生成一段
//     static void Main() { App app = new(); app.InitializeComponent(); app.Run(); }
// 我们能写"入口内容"的地方就是下面的 OnStartup —— 它是 Run() 起来后的第一个回调，
// 效果等同于别的程序的 main。
//
// 所以这个文件只干一件事：把启动要做的四个动作，按顺序摆成一个方法（MainFlow）。
// 具体的实现都在 MainWindow.xaml.cs / MainPage.xaml.cs 里，这里只负责"排顺序"。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows;

namespace 炒股小助手
{
    // partial：另一半由 XAML 生成（App.xaml 里的全局资源等）
    public partial class App : Application
    {
        // ── 入口 ──
        // WPF 启动后的第一站：这里保持极简，所有事情交给 MainFlow。
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _ = MainFlow();     // 开跑启动总流程（async，不阻塞 UI 线程）
        }

        // ══════════════════ 启动总流程（main）══════════════════
        // 四个动作按顺序组合，读完这一个方法就知道"程序启动以后干了什么"：
        //   ① 打开主窗口             → 屏幕上先出现"正在加载中"那一层
        //   ② 缓存检查               → 桌面 UserCache.json 在不在、能不能读
        //   ③ 收掉加载层、亮出主界面 → 也就是"删掉旧页面、展示新页面"，默认停在主界面页
        //   ④ 启动心跳               → 0.5 秒一轮，开始盯盘口数据时间
        //
        // 顺序不能乱：② 没过就直接 return —— 不切页面、也不启动心跳，画面停在加载层。
        private static async Task MainFlow()
        {
            MainWindow win = new MainWindow();   // 建窗口：构造函数只做 XAML 实例化
            win.Show();                          // ① 打开窗口 → 看到 LoadingLayer（MainUi 这时还是隐藏的）

            // 让"正在加载中"至少显示 1 秒：缓存检查本身是毫秒级，不延迟的话加载字一闪而过看不见。
            await Task.Delay(TimeSpan.FromSeconds(1));

            if (!win.CheckCache()) return;        // ② 缓存检查：失败时它自己弹过窗了，这里直接停

            win.ShowMainUi();                     // ③ 收掉加载层 → 亮出导航 + 页面区

            win.StartHeartbeat();                 // ④ 启动心跳（转发给 MainPage）
        }
    }
}
