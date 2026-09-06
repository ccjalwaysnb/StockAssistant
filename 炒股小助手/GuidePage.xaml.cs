// ═══════════════════════════════════════════════════════════════════════════
// GuidePage.xaml.cs —— "操作教程"页的后台代码
//
// 教程内容基本是静态文字/图片，代码量会很少；需要交互时再在这里加。
// 目前界面是空的占位页，所以只剩一个构造函数。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class GuidePage : UserControl
    {
        public GuidePage()
        {
            InitializeComponent(); // 把本页 XAML 实例化（每页构造函数都固定有这句）
        }
    }
}
