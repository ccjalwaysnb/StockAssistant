// ═══════════════════════════════════════════════════════════════════════════
// WatchlistPage.xaml.cs —— "自选股"页
//
// 【分区】
//   A. 初始化区 —— 构造函数
//   B. 画页面区 —— 把一批盘口数据画成一行行的长方形（对外入口 RefreshWatchlistUI）
//   C. 造控件区 —— 一行 / 一个格子是怎么拼出来的
//   D. 取值区   —— 数字、涨跌、颜色转成界面上要的文字
//   E. 跳转区   —— 点某一行 → 让主窗口切到主页面并搜这只票
//   F. 分组区   —— 把自选按市场拆成两组代码（给心跳用，跟画界面无关）
//   G. 声明区   —— 画界面用的颜色
//   H. 数据区   —— 自选代码表 + 列配置表
//
// 【这个文件负责】画自选股列表、把自选按市场分组。
//   数据不是这一页自己拉的：心跳拉回来之后调 RefreshWatchlistUI(表)，把表递进来。
// ═══════════════════════════════════════════════════════════════════════════
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace 炒股小助手
{
    public partial class WatchlistPage : UserControl
    {
        // ══════════════════ A. 初始化区 ══════════════════
        // 本区方法：WatchlistPage（构造函数）

        // 接收：无　返回：无　改变：实例化 XAML，先按"空表"画一次（页面正中央显示"还未选择自选股"）
        public WatchlistPage()
        {
            InitializeComponent();                  // 实例化本页 XAML（HeaderRow / RowList / EmptyHint）
            RefreshWatchlistUI(new List<Quote>());  // 先画一次空表
        }

        // ══════════════════ B. 画页面区 ══════════════════
        // 本区方法：RefreshWatchlistUI

        // 接收：一批盘口数据（一条一只票）　返回：无
        // 改变：清空列表 → 有数据就画表头 + 一行一只票；没数据就只显示居中的"还未选择自选股"
        public void RefreshWatchlistUI(List<Quote> quotes)
        {
            RowList.Children.Clear();                    // 清掉上一次画的行
            HeaderRow.Children.Clear();                  // 表头也清掉

            if (quotes == null || quotes.Count == 0)   // 基于"表里有没有数据"决定显示哪种状态
            {
                EmptyHint.Visibility = Visibility.Visible;     // 没数据 → 亮出居中提示
                HeaderRow.Visibility = Visibility.Collapsed;   // 表头也藏起来
                return;                                        // 不用再画行了
            }

            EmptyHint.Visibility = Visibility.Collapsed;   // 有数据 → 藏提示
            HeaderRow.Visibility = Visibility.Visible;     // 露表头

            foreach (WatchColumn col in Columns)                 // 照列配置生成表头
                HeaderRow.Children.Add(MakeCell(col, col.Title, HeadFg, 13));
            foreach (Quote q in quotes)                          // 一行一只票
                RowList.Children.Add(BuildRow(q));
        }

        // ══════════════════ C. 造控件区 ══════════════════
        // 本区方法：MakeCell（造一个格子）、BuildRow（造一行）

        // 接收：一列配置 + 文字 + 颜色 + 字号　返回：一个格子　改变：无（只造控件，不挂事件）
        private static TextBlock MakeCell(WatchColumn col, string text, Brush color, double fontSize)
        {
            var cell = new TextBlock
            {
                Text = text,
                Width = col.Width,                                // 宽度取自列配置 —— 表头和每行同一个宽度才能对齐
                Foreground = color,
                FontSize = fontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,    // 文字太长变省略号，不撑破版面
            };

            if (col.RightAlign)   // 基于"这一列是不是数值列"决定对齐方式和字体
            {
                cell.TextAlignment = TextAlignment.Right;         // 数值右对齐，个位对个位
                cell.FontFamily = new FontFamily("Consolas");     // 数值用等宽字体
            }
            return cell;
        }

        // 接收：一只票的盘口数据　返回：一行（圆角长方形）　改变：给这一行挂上"鼠标左键松开 → 跳去主页面搜它"
        private Border BuildRow(Quote q)
        {
            var cells = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 0, 16, 0),   // 内容左右各留 16，不贴边框
            };
            foreach (WatchColumn col in Columns)        // 逐个格子摆进这一行
                cells.Children.Add(MakeCell(col, col.Text(q), col.Color(q), 15));

            var row = new Border
            {
                Background = RowBg,
                BorderBrush = RowLine,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Height = 42,
                Margin = new Thickness(0, 0, 0, 6),     // 行与行之间留 6 的缝
                Child = cells,
                Cursor = Cursors.Hand,                  // 鼠标移上去变小手，暗示"这一行可以点"
            };

            row.MouseLeftButtonUp += (s, e) => JumpToSearch(q.Code);   // 点这一行 → 跳去主页面搜它
            return row;
        }

        // ══════════════════ D. 取值区 ══════════════════
        // 本区方法：NameText、NumText、PctText、SignedText、TrendBrush
        // 都是 static —— 因为 G/H 区的列配置表是静态的，里面的 lambda 只能调静态方法。

        // 接收：一条盘口数据　返回：名字（没名字给 "—"）　改变：无
        private static string NameText(Quote q)
            => string.IsNullOrEmpty(q.Name) ? "—" : q.Name;

        // 接收：数字 + 格式　返回：格式化后的文字（没有值给 "--"）　改变：无
        private static string NumText(decimal? v, string format)
            => v?.ToString(format) ?? "--";

        // 接收：涨跌幅　返回：带正负号和 % 的文字（如 +0.85%）　改变：无
        private static string PctText(decimal? pct)
            => pct.HasValue ? (pct.Value >= 0 ? "+" : "") + pct.Value.ToString("F2") + "%" : "--";

        // 接收：数字 + 格式　返回：带正负号的文字（如 +14.200）　改变：无
        private static string SignedText(decimal? v, string format)
            => v.HasValue ? (v.Value >= 0 ? "+" : "") + v.Value.ToString(format) : "--";

        // 接收：涨跌幅　返回：这一格该用的颜色　改变：无（基于"涨 / 跌 / 平"判断红绿白）
        private static Brush TrendBrush(decimal? pct)
            => !pct.HasValue ? Brushes.White
             : pct.Value > 0 ? UpFg
             : pct.Value < 0 ? DownFg
             : Brushes.White;

        // ══════════════════ E. 跳转区 ══════════════════
        // 本区方法：JumpToSearch

        // 接收：完整腾讯代码　返回：无　改变：让主窗口切到主页面并搜这只票
        private void JumpToSearch(string tenCode)
        {
            if (string.IsNullOrEmpty(tenCode)) return;                                 // 没代码就不跳
            if (Window.GetWindow(this) is MainWindow win) win.JumpToSearch(tenCode);   // 跨页面的事交给窗口做
        }

        // ══════════════════ F. 分组区 ══════════════════
        // 本区方法：GroupWatchCodes

        // 接收：缓存文件里读出来的自选（market + 纯代码 + 名字）　返回：无
        // 改变：把 A股 / 美股两组完整代码分别写进 aShareWatchCodes / usWatchCodes（先清空再填）
        public void GroupWatchCodes(List<WatchItem> items)
        {
            aShareWatchCodes.Clear();   // 先清空：这个方法要能反复调，每次都是全新的结果
            usWatchCodes.Clear();

            foreach (WatchItem item in items)   // 基于"每条自选的 market 前缀"决定分到哪一组
            {
                if (item.market is "sh" or "sz") aShareWatchCodes.Add(item.tenCode);     // 沪市 / 深市
                else if (item.market is "us" or "us.") usWatchCodes.Add(item.tenCode);   // 美股 / 美股指数
            }
        }

        // ══════════════════ G. 声明区 ══════════════════
        // 说明：这一区是"画界面要用的东西"，不是业务数据。
        // 【顺序有讲究】它们必须排在 H 区的 Columns 前面 —— 静态字段按声明顺序初始化，
        //   写到后面的话，Columns 初始化时拿到的是 null。
        private static readonly Brush UpFg    = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));   // 涨：红
        private static readonly Brush DownFg  = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A));   // 跌：绿
        private static readonly Brush HeadFg  = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));   // 表头：灰
        private static readonly Brush CodeFg  = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A));   // 代码：灰
        private static readonly Brush RowBg   = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));   // 行的底色
        private static readonly Brush RowLine = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A));   // 行的边框

        // ══════════════════ H. 数据区 ══════════════════
        public List<string> aShareWatchCodes = new();   // A股自选的完整腾讯代码（心跳拿它拼查询串）
        public List<string> usWatchCodes = new();       // 美股自选的完整腾讯代码（同上）

        // 一列的配置：标题 / 宽度 / 是不是数值列 / 显示什么 / 用什么颜色
        private sealed record WatchColumn(string Title, double Width, bool RightAlign,
                                          Func<Quote, string> Text, Func<Quote, Brush> Color);

        // 列配置表：要加一列就在这儿加一行（表头、每行、对齐、颜色都会自动跟着变）
        private static readonly WatchColumn[] Columns =
        {
            new("名称",   200, false, q => NameText(q),                _ => Brushes.White),
            new("代码",   120, false, q => q.Code,                     _ => CodeFg),
            new("现价",   110, true,  q => NumText(q.Price, "F3"),     q => TrendBrush(q.ChangePct)),
            new("涨跌幅", 110, true,  q => PctText(q.ChangePct),       q => TrendBrush(q.ChangePct)),
            new("涨跌",   110, true,  q => SignedText(q.Change, "F3"), q => TrendBrush(q.ChangePct)),
        };
    }
}
