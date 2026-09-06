// ═══════════════════════════════════════════════════════════════════════════
// WatchlistPage.xaml.cs —— "自选股"页的后台代码
//
// 以后在这里写：自选列表的加载/增删、双击行跳转行情等逻辑。
// 目前界面是空的占位页，所以只剩一个构造函数。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class WatchlistPage : UserControl
    {
        public WatchlistPage()
        {
            InitializeComponent(); // 把本页 XAML 实例化（每页构造函数都固定有这句）
        }
    }
}
