// ═══════════════════════════════════════════════════════════════════════════
// SettingsPage.xaml.cs —— "设置"页的后台代码
//
// 以后在这里写：读取/保存设置项（简单的话可以直接存成一个本地文件）。
// 目前界面是空的占位页，所以只剩一个构造函数。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent(); // 把本页 XAML 实例化（每页构造函数都固定有这句）
        }
    }
}
