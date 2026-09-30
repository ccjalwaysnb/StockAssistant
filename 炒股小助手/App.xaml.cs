// ═══════════════════════════════════════════════════════════════════════════
// App.xaml.cs —— 程序入口
//
// 【分区】
//   A. 主流程区 —— 启动总流程（MainFlow 和它用到的小检查）
//   B. 声明区   —— 单实例互斥体
//
// 【这个文件负责】把"程序启动要做的事"按顺序串起来；每一步的具体实现都在别的文件里，
//   这里只负责排顺序。
// ═══════════════════════════════════════════════════════════════════════════
using System.Threading;
using System.Windows;

namespace 炒股小助手
{
    public partial class App : Application
    {
        // ══════════════════ A. 主流程区 ══════════════════
        // 本区方法：
        //   OnStartup           —— 程序第一站，转手交给 MainFlow
        //   MainFlow            —— 启动总流程（五步）
        //   CheckSingleInstance —— 单实例检查
        //
        // 【启动总流程】
        //   ① CheckSingleInstance   已经有实例在跑？→ 直接退出
        //   └ ② new MainWindow + Show    亮出"正在加载中"那一层
        //     └ ③ Prepare               前期准备：缓存检查 → 拉一遍自选股 → 问一次市场开闭
        //        └ 缓存检查不过 → 停在这儿（它自己弹过窗了）
        //     └ ④ ShowMainUi            收掉加载层、亮出主界面
        //     └ ⑤ StartHeartbeat        启动心跳（0.5 秒一跳）

        // 接收：启动事件参数　返回：无　改变：发起 MainFlow（不等它跑完，所以不阻塞界面）
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _ = MainFlow();   // 开跑启动总流程
        }

        // 接收：无　返回：无　改变：建窗口 + 按顺序跑完五步（哪一步没过就停在哪儿）
        private static async Task MainFlow()
        {
            if (!CheckSingleInstance()) return;   // 基于"有没有抢到单实例锁"判断要不要继续

            MainWindow win = new MainWindow();   // 建窗口（构造函数只做 XAML 实例化）
            win.Show();                          // 先亮出"正在加载中"

            await Task.Delay(TimeSpan.FromSeconds(1));   // 让加载层至少显示 1 秒，不然一闪而过看不见

            if (!await win.Prepare()) return;    // 前期准备：不过就停（弹窗已经在里面做过了）

            win.ShowMainUi();                    // 收掉加载层 → 亮出主界面
            win.StartHeartbeat();                // 启动心跳
        }

        // 接收：无　返回：能不能继续启动　改变：抢到锁就持有它；没抢到就让本进程退出
        private static bool CheckSingleInstance()
        {
            singleInstanceMutex = new Mutex(true, "炒股小助手", out bool createdNew);   // 向系统要一个具名互斥体

            if (createdNew) return true;         // 基于"这个锁是不是我刚创建的"判断：是 → 放行

            Application.Current.Shutdown();      // 已经有实例在跑 → 结束本进程（此刻还没建窗口，不会留个空白窗）
            return false;
        }

        // ══════════════════ B. 声明区 ══════════════════
        // 说明：凡是在别处用到的"具名零件"，都放这一区（不是业务数据）
        private static Mutex? singleInstanceMutex;   // 单实例互斥体（必须是字段：放局部变量会被 GC 回收，防重复打开就失效了）
    }
}
