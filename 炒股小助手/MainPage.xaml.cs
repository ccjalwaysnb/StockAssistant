// ═══════════════════════════════════════════════════════════════════════════
// MainPage.xaml.cs —— "主界面"页的后台代码
//
// 这一页以后负责：接收用户输入的股票代码 → 查行情 → 把结果展示在主区域。
// 现在只做了界面骨架（标题 + 搜索框），搜索按钮的事件先留个空壳在这，
// 等行情引擎接回来时，把逻辑填进 Search_Click 里就行。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;

namespace 炒股小助手
{
    public partial class MainPage : UserControl
    {
        public MainPage()
        {
            InitializeComponent(); // 每一页的构造函数都要这句：把本页 XAML 实例化
        }

        // 搜索按钮的点击事件 —— 目前是空壳，故意不写逻辑。
        // 将来要做的第一步大概是这样（示意，别当真）：
        //     string code = SearchBox.Text.Trim();   // 从输入框取文字、去掉首尾空格
        //     // 然后把 code 交给行情查询去处理……
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            // 留空：等搜索逻辑做出来再填
        }
    }
}
