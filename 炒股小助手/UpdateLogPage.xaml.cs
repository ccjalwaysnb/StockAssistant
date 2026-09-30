// ═══════════════════════════════════════════════════════════════════════════
// UpdateLogPage.xaml.cs —— "更新日志"页
//
// 【分区】
//   A. 初始化区   —— 构造函数
//   B. 展开收起区 —— 点版本号那一行把内容展开 / 收起
//
// 【这个文件负责】版本号那一行的点击：把对应的内容块在"显示 / 隐藏"之间切换，箭头跟着翻方向。
//   名字约定：标题按钮 HeaderN（Tag 里写 "BodyN"）→ 内容块 BodyN → 箭头 ArrowN。
//   以后加版本只改 XAML，这个文件一行都不用动。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class UpdateLogPage : UserControl
    {
        // ══════════════════ A. 初始化区 ══════════════════
        // 本区方法：UpdateLogPage（构造函数）

        // 接收：无　返回：无　改变：实例化本页 XAML
        public UpdateLogPage()
        {
            InitializeComponent();
        }

        // ══════════════════ B. 展开收起区 ══════════════════
        // 本区方法：VersionHeader_Click

        // 接收：被点的那个标题按钮（subject）　返回：无　改变：对应内容块的显示 / 隐藏 + 箭头方向
        // 说明：按钮的 Tag 里写着要控制哪一块内容（比如 "Body1"）；
        //   箭头名就是把 "Body1" 里的 Body 换成 Arrow（"Arrow1"）。
        private void VersionHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string bodyName) return;   // 基于"Tag 里有没有名字"判断能不能处理
            if (FindName(bodyName) is not FrameworkElement body) return;              // 按名字把内容块找出来

            bool show = body.Visibility != Visibility.Visible;                        // 基于"它现在是显示还是隐藏"决定这次要开还是要关
            body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (FindName(bodyName.Replace("Body", "Arrow")) is TextBlock arrow)
                arrow.Text = show ? "▼" : "▶";                                        // 箭头跟着换：▼ 已展开 / ▶ 已收起
        }
    }
}
