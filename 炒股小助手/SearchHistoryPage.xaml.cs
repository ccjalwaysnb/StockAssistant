// ═══════════════════════════════════════════════════════════════════════════
// SearchHistoryPage.xaml.cs —— "历史搜索"页的后台代码
//
// 以后在这里写：搜索记录的存取（可能用文件或数据库）、列表展示、点击重搜。
// 目前界面是空的占位页，所以只剩一个构造函数。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class SearchHistoryPage : UserControl
    {
        public SearchHistoryPage()
        {
            InitializeComponent(); // 把本页 XAML 实例化（每页构造函数都固定有这句）
        }
    }
}
