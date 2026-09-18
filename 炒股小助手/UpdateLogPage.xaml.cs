// ═══════════════════════════════════════════════════════════════════════════
// UpdateLogPage.xaml.cs —— "更新日志"页
//
// 这个页面只有一件事要做：点版本号那一行 → 展开 / 收起它下面的更新内容。
// 名字的约定（照这个约定写，一个方法就能管所有版本）：
//     标题按钮 Header1（Tag 里写 "Body1"）→ 内容块 Body1 → 箭头 Arrow1
//     以后加 v1.1 就是 Header2 / Body2 / Arrow2，这段代码一行都不用改。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class UpdateLogPage : UserControl
    {
        public UpdateLogPage()
        {
            InitializeComponent(); // 实例化本页 XAML
        }

        // 点版本号那一行：把对应的内容块在"显示 / 隐藏"之间切换，顺便把箭头翻个方向。
        private void VersionHeader_Click(object sender, RoutedEventArgs e)
        {
            // 谁被点的？（sender 就是那个标题按钮）它 Tag 里写着要控制哪一块内容
            if (sender is not Button btn || btn.Tag is not string bodyName) return;

            // FindName：在本页的 XAML 里按名字找控件（比如 Body1）
            if (FindName(bodyName) is not FrameworkElement body) return;

            bool show = body.Visibility != Visibility.Visible;
            body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            // 箭头也跟着换：▼ = 已展开，▶ = 已收起
            // 名字就是把 "Body1" 里的 Body 换成 Arrow → "Arrow1"
            if (FindName(bodyName.Replace("Body", "Arrow")) is TextBlock arrow)
                arrow.Text = show ? "▼" : "▶";
        }
    }
}
