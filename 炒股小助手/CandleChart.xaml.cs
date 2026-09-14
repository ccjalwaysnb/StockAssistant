// ═══════════════════════════════════════════════════════════════════════════
// CandleChart.xaml.cs —— K 线图控件的逻辑（可复用的一个小零件）
//
// 画法：一根 K 线 = 1 条影线（最高→最低）+ 1 个实体矩形（开盘→收盘），涨红跌绿。
//
// 视图模型（看懂这段就懂了整个交互）：
//   视图 = 一个"观察窗口"，只用 4 个数描述：
//     X 方向 → viewStart（窗口左边缘在第几根）、viewCount（窗口里显示几根）
//     Y 方向 → yTop（窗口顶端价格）、ySpan（窗口价格跨度，由框内数据自动算）
//
// 三项交互：
//   ① 悬停：鼠标停在某根 K 柱的命中矩形里满 1 秒 → 气泡（右上角；放不下就退到左下角）
//   ② 缩放：鼠标在图内 + Ctrl + 滚轮，每次只多/少显示 1 根（整数步进，柱子永远整根对齐）
//   ③ 滑动：鼠标在图内按 ← → ，沿 X 轴平移
// ═══════════════════════════════════════════════════════════════════════════
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace 炒股小助手
{
    public partial class CandleChart : UserControl
    {
        // ══════════════════ A. 数据 ══════════════════

        private List<Candle> candles = new();      // 图上的 K 线（由外面用 SetData 灌进来）
        private string emptyText = "暂无数据";      // 一根都没有时，中间显示这行字

        // 灌数据：主界面拉到 K 线后调这个（日K 一张图、分钟K 另一张图，各灌各的）
        public void SetData(List<Candle> data)
        {
            candles = data ?? new List<Candle>();
            InitView();        // 重置视图：默认贴右、显示全部
            RedrawAll();
        }

        // 设置"没数据时"的提示文字（例如"正在加载…" / "获取失败"）
        public void SetEmptyText(string text)
        {
            emptyText = text;
            if (candles.Count == 0) RedrawAll();
        }

        // ══════════════════ B. 视图状态 ══════════════════
        private double viewStart;      // 窗口左边缘在第几根（整数：缩放/平移都是整根步进）
        private double viewCount;      // 窗口里显示多少根（整数）
        private decimal yTop;          // 窗口顶端价格（由框内数据自动算）
        private decimal ySpan;         // 窗口价格跨度（由框内数据自动算）

        // 缩放范围限制
        private double MinCount => Math.Min(10, candles.Count);    // 最多放大到框内 10 根
        private double MaxCount => Math.Min(370, candles.Count);   // 最多缩小到框内 370 根（数据不够就全展示）

        // ══════════════════ C. 布局常量（画布四周留白）══════════════════
        private const double PadLeft = 58;    // 左边留给价格刻度
        private const double PadRight = 16;
        private const double PadTop = 12;
        private const double PadBottom = 26;  // 下边留给日期刻度

        // ══════════════════ D. 交互状态 ══════════════════
        private readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private int pendingIndex = -1;   // 鼠标此刻停在哪根（正在计时）
        private int hoverIndex = -1;     // 气泡正在展示哪根
        private Point lastPoint;         // 最近一次鼠标坐标

        // ══════════════════ E. 画笔 ══════════════════
        private static readonly Brush UpBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x39, 0x35));   // 涨：红
        private static readonly Brush DownBrush = new SolidColorBrush(Color.FromRgb(0x66, 0xBB, 0x6A)); // 跌：绿
        private static readonly Brush GridBrush = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)); // 横网格
        private static readonly Brush VGridBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));// 竖网格
        private static readonly Brush AxisBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)); // 绘图区边框
        private static readonly Brush CrossBrush = new SolidColorBrush(Color.FromRgb(0x78, 0x78, 0x78)); // 十字线
        private static readonly Brush BandBrush = new SolidColorBrush(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF)); // 列高亮带
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)); // 刻度字

        public CandleChart()
        {
            InitializeComponent();

            hoverTimer.Tick += HoverTimer_Tick;   // 悬停满 1 秒 → 弹气泡
            Loaded += CandleChart_Loaded;         // 控件进树时，把方向键挂到窗口上
            InitView();
        }

        // ══════════════════ F. 视图初始化与坐标换算 ══════════════════

        // 默认视图：显示全部 K 柱，并且贴右边（最新那根在最右）；Y 轴按框内数据自适应
        private void InitView()
        {
            viewCount = MaxCount;
            viewStart = Math.Max(0, candles.Count - viewCount);   // 数据比 370 多时，默认只看最近的一段
            UpdateYRange();
        }

        // 当前框内（含只露出一部分的首尾两根）的索引范围
        private int VisibleFirst => Math.Max(0, (int)Math.Floor(viewStart));
        private int VisibleLast => Math.Min(candles.Count - 1, (int)Math.Ceiling(viewStart + viewCount) - 1);

        // Y 轴自适应：上下界 = 框内 K 柱涉及到的最高价 / 最低价，再各留 8% 余量
        // （缩放只动 X，Y 每次跟着框内数据重新算 —— 所以柱子长短会变，这是预期行为）
        private void UpdateYRange()
        {
            if (candles.Count == 0) return;

            decimal lo = decimal.MaxValue, hi = decimal.MinValue;
            for (int i = VisibleFirst; i <= VisibleLast; i++)
            {
                if (candles[i].Low < lo) lo = candles[i].Low;
                if (candles[i].High > hi) hi = candles[i].High;
            }
            if (hi <= lo) hi = lo + 1m;                       // 兜底：只有一根平盘时别除零

            decimal pad = Math.Max((hi - lo) * 0.08m, 0.02m);
            yTop = hi + pad;
            ySpan = (hi - lo) + pad * 2;
        }

        private double PlotW => Math.Max(1, KLineCanvas.ActualWidth - PadLeft - PadRight);
        private double PlotH => Math.Max(1, KLineCanvas.ActualHeight - PadTop - PadBottom);
        private double SlotW => viewCount <= 0 ? 1 : PlotW / viewCount;              // 每根占的槽宽

        private double Cx(int i) => PadLeft + (i + 0.5 - viewStart) * SlotW;         // 第 i 根的中心 X
        private double Y(decimal price) => PadTop + (double)((yTop - price) / ySpan) * PlotH;

        // ══════════════════ G. 静态层：坐标轴 / 网格 / 蜡烛 ══════════════════

        private void KLineCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawAll();   // 窗口拉大拉小 → 按新尺寸重画
        }

        private void RedrawAll()
        {
            DrawChart();
            OverlayCanvas.Children.Clear();
            HideBubble();
            pendingIndex = -1;
            hoverTimer.Stop();
        }

        private void DrawChart()
        {
            var cv = KLineCanvas;
            cv.Children.Clear();

            if (cv.ActualWidth < 80 || cv.ActualHeight < 80) return;

            // 一根数据都没有：中间写一行提示，别让框子空着让人以为程序坏了
            if (candles.Count == 0)
            {
                PutTextCenter(cv, emptyText, cv.ActualWidth / 2, cv.ActualHeight / 2 - 8, TextBrush, 12);
                return;
            }

            // 绘图区边框
            cv.Children.Add(MakeRect(PadLeft, PadTop, PlotW, PlotH, null, AxisBrush, 1));

            // ── Y 轴：5 条横向网格线 + 左侧价格刻度 ──
            const int yTicks = 5;
            for (int i = 0; i <= yTicks; i++)
            {
                decimal price = yTop - ySpan * i / yTicks;
                double y = Y(price);
                cv.Children.Add(MakeLine(PadLeft, y, PadLeft + PlotW, y, GridBrush, 1));
                PutTextRight(cv, price.ToString("F2"), PadLeft - 8, y, TextBrush, 11);
            }

            // ── X 轴：日期刻度（可见根数多时每隔几根标一个，最多约 8 个）──
            int first = VisibleFirst, last = VisibleLast;
            int step = Math.Max(1, (int)Math.Ceiling((last - first + 1) / 8.0));
            for (int i = first; i <= last; i++)
            {
                if ((i - first) % step != 0) continue;
                double x = Cx(i);
                cv.Children.Add(MakeLine(x, PadTop, x, PadTop + PlotH, VGridBrush, 1));
                PutTextCenter(cv, candles[i].Date.ToString("MM-dd"), x, PadTop + PlotH + 6, TextBrush, 10);
            }

            // ── 蜡烛：影线 + 实体。柱体占槽宽 70%（挨得紧），上限 24px 免得放大后糊成一片 ──
            double bodyW = Math.Clamp(SlotW * 0.7, 1, 24);
            for (int i = first; i <= last; i++)
            {
                Candle k = candles[i];
                double cx = Cx(i);
                Brush brush = k.IsUp ? UpBrush : DownBrush;

                cv.Children.Add(MakeLine(cx, Y(k.High), cx, Y(k.Low), brush, 1));   // 影线

                double yOpen = Y(k.Open), yClose = Y(k.Close);
                double top = Math.Min(yOpen, yClose);
                double height = Math.Max(Math.Abs(yClose - yOpen), 1);              // 平盘也留 1px，不然看不见
                cv.Children.Add(MakeRect(cx - bodyW / 2, top, bodyW, height, brush, null, 0));
            }
        }

        // ══════════════════ H. 交互层：十字线 + 高亮带 ══════════════════

        private void DrawOverlay(Point p, int idx)
        {
            var cv = OverlayCanvas;
            cv.Children.Clear();

            bool inside = p.X >= PadLeft && p.X <= PadLeft + PlotW
                       && p.Y >= PadTop && p.Y <= PadTop + PlotH;
            if (!inside) return;

            // 高亮带：把鼠标所在的那一列淡淡刷白，一眼看出气泡说的是哪根
            if (idx >= 0)
            {
                double x = PadLeft + (idx - viewStart) * SlotW;
                cv.Children.Add(MakeRect(x, PadTop, SlotW, PlotH, BandBrush, null, 0));
            }

            Line h = MakeLine(PadLeft, p.Y, PadLeft + PlotW, p.Y, CrossBrush, 1);
            h.StrokeDashArray = new DoubleCollection { 4, 3 };
            cv.Children.Add(h);

            Line v = MakeLine(p.X, PadTop, p.X, PadTop + PlotH, CrossBrush, 1);
            v.StrokeDashArray = new DoubleCollection { 4, 3 };
            cv.Children.Add(v);
        }

        // 命中判定：返回鼠标压在第几根 K 柱上（-1 = 没压中）
        // 横向取"该柱所在的整列"（槽宽），纵向取该柱的最高～最低 —— 即一个恰巧包住 K 柱的矩形
        private int HitTest(Point p)
        {
            if (candles.Count == 0) return -1;
            if (p.X < PadLeft || p.X > PadLeft + PlotW) return -1;
            if (p.Y < PadTop || p.Y > PadTop + PlotH) return -1;

            int idx = (int)Math.Floor(viewStart + (p.X - PadLeft) / SlotW);
            if (idx < 0 || idx >= candles.Count) return -1;

            double a = Y(candles[idx].High), b = Y(candles[idx].Low);
            double top = Math.Min(a, b), bottom = Math.Max(a, b);
            const double tol = 4;   // 上下各给 4px 容错，不然太难点中
            if (p.Y < top - tol || p.Y > bottom + tol) return -1;

            return idx;
        }

        private void KLineCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            lastPoint = e.GetPosition(KLineCanvas);
            int idx = HitTest(lastPoint);

            DrawOverlay(lastPoint, idx);

            if (idx != pendingIndex)
            {
                // 换了一根（或移出 K 柱）→ 重新开始计时，并收掉旧气泡
                pendingIndex = idx;
                hoverTimer.Stop();
                HideBubble();
                if (idx >= 0) hoverTimer.Start();
            }
            else if (idx >= 0 && hoverIndex == idx)
            {
                PositionBubble(lastPoint);   // 气泡已弹出 → 跟着鼠标走
            }
        }

        private void KLineCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            hoverTimer.Stop();
            pendingIndex = -1;
            HideBubble();
            OverlayCanvas.Children.Clear();
        }

        // 悬停满 1 秒：正式弹出气泡
        private void HoverTimer_Tick(object? sender, EventArgs e)
        {
            hoverTimer.Stop();
            if (pendingIndex < 0) return;

            hoverIndex = pendingIndex;
            Candle k = candles[hoverIndex];

            TipOpen.Text = k.Open.ToString("F2");
            TipClose.Text = k.Close.ToString("F2");
            TipHigh.Text = k.High.ToString("F2");
            TipLow.Text = k.Low.ToString("F2");

            TipBubble.Visibility = Visibility.Visible;
            TipBubble.UpdateLayout();    // 先让气泡按内容排一次版 → 后面取到的 ActualWidth/Height 才是真尺寸
            PositionBubble(lastPoint);
        }

        private void HideBubble()
        {
            hoverIndex = -1;
            TipBubble.Visibility = Visibility.Collapsed;
        }

        // 气泡位置：只认两种摆法 —— 优先"鼠标右上方"；放不下就退到"鼠标左下方"
        private void PositionBubble(Point p)
        {
            // ⚠ 必须用 ActualWidth/ActualHeight（真正排版后的尺寸）。
            //   实测 DesiredSize 在元素被反复测量后会失真（同一个气泡能报出 325x404、508x154 这种假尺寸），
            //   拿它算位置就会把气泡摆到莫名其妙的地方 —— 之前那个"位置有错"的 bug 就是它。
            double bw = TipBubble.ActualWidth, bh = TipBubble.ActualHeight;
            if (bw <= 0 || bh <= 0)   // 极端情况没排过版 → 量一次兜底
            {
                TipBubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bw = TipBubble.DesiredSize.Width;
                bh = TipBubble.DesiredSize.Height;
            }

            double w = KLineCanvas.ActualWidth, h = KLineCanvas.ActualHeight;
            const double gap = 16, edge = 4;

            double x = p.X + gap;                  // 首选：鼠标右上方
            double y = p.Y - bh - gap;
            if (x + bw > w - edge || y < edge)
            {
                x = p.X - bw - gap;                // 右上放不下 → 鼠标左下方
                y = p.Y + gap;
            }

            // 兜底：万一两种摆法都超出图框，夹回框内（保证一定看得见）
            x = Math.Clamp(x, edge, Math.Max(edge, w - bw - edge));
            y = Math.Clamp(y, edge, Math.Max(edge, h - bh - edge));
            TipBubble.Margin = new Thickness(x, y, 0, 0);
        }

        // ══════════════════ I. Ctrl + 滚轮：缩放（只动 X 轴，每次 1 根）══════════════════

        private void KLineCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;   // 没按 Ctrl 就不管
            e.Handled = true;
            ZoomStep(e.Delta > 0 ? -1 : 1);                                  // 上滚=放大=少显示一根
        }

        // 缩放只改"框内显示多少根"，每次正好 1 根（整数步进 → 柱子永远整根对齐，不会露出半截）：
        //   ③ 正常：锁右边 —— 右边缘索引不变，新 viewStart = 原右边缘索引 - 新根数
        //   ④ 例外：最老的那根已经露出来了（viewStart 贴着 0）→ 锁左边 —— 左边缘钉死 0，
        //      只改根数。此时放大，右边缘会跟着左移。
        //   Y 轴不参与缩放，交给 UpdateYRange() 按框内数据自适应
        private void ZoomStep(double delta)
        {
            double newCount = Math.Clamp(viewCount + delta, MinCount, MaxCount);
            if (Math.Abs(newCount - viewCount) < 1e-9) return;              // 已经到头了

            double newStart = viewStart + viewCount - newCount;             // 先按"锁右边"算左边缘
            if (viewStart <= 0 || newStart < 0) newStart = 0;               // 露出最老的柱 → 改锁左边

            viewStart = newStart;
            viewCount = newCount;
            UpdateYRange();                                                 // Y 轴跟着框内数据重算

            // 缩放过 → 命中矩形全变了，旧气泡作废，重画
            hoverTimer.Stop();
            pendingIndex = -1;
            HideBubble();
            DrawChart();
            DrawOverlay(lastPoint, HitTest(lastPoint));
        }

        // ══════════════════ J. ← → 方向键：沿 X 轴滑动 ══════════════════

        // 把按键挂到窗口上（而不是依赖控件焦点）：只要鼠标在图框内，按左右键就滑动
        private void CandleChart_Loaded(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is Window win)
            {
                win.PreviewKeyDown -= Window_PreviewKeyDown;   // 先摘再挂，避免页面来回切换时重复订阅
                win.PreviewKeyDown += Window_PreviewKeyDown;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!KLineCanvas.IsMouseOver) return;                       // 鼠标不在图里 → 不管
            if (e.Key != Key.Left && e.Key != Key.Right) return;

            e.Handled = true;                                           // 拦掉，别让它在按钮间跳焦点
            Pan(e.Key == Key.Left ? -1 : 1);
        }

        // 平移视图：每次挪 1 根，到头就停
        private void Pan(double delta)
        {
            double maxStart = Math.Max(0, candles.Count - viewCount);
            double newStart = Math.Clamp(viewStart + delta, 0, maxStart);
            if (Math.Abs(newStart - viewStart) < 1e-9) return;           // 已经在头上了，不用重画

            viewStart = newStart;
            UpdateYRange();                                             // 框内换了一批数据 → Y 轴重算

            hoverTimer.Stop();
            pendingIndex = -1;
            HideBubble();
            DrawChart();
            DrawOverlay(lastPoint, HitTest(lastPoint));
        }

        // ══════════════════ K. 画图小工具 ══════════════════

        private static Rectangle MakeRect(double x, double y, double w, double h, Brush? fill, Brush? stroke, double thickness)
        {
            var r = new Rectangle
            {
                Width = Math.Max(w, 0.5),
                Height = Math.Max(h, 0.5),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
            };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            return r;
        }

        private static Line MakeLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness)
            => new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness };

        private static TextBlock MakeText(string s, Brush fg, double size)
        {
            var t = new TextBlock
            {
                Text = s,
                Foreground = fg,
                FontSize = size,
                FontFamily = new FontFamily("Consolas"),
            };
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));   // 先量出占多大
            return t;
        }

        // 文字右对齐（Y 轴价格刻度：右边贴着绘图区左边界）
        private static void PutTextRight(Canvas cv, string s, double xRight, double yCenter, Brush fg, double size)
        {
            TextBlock t = MakeText(s, fg, size);
            Canvas.SetLeft(t, xRight - t.DesiredSize.Width);
            Canvas.SetTop(t, yCenter - t.DesiredSize.Height / 2);
            cv.Children.Add(t);
        }

        // 文字水平居中（X 轴日期刻度：正对着那条竖网格线）
        private static void PutTextCenter(Canvas cv, string s, double xCenter, double yTop, Brush fg, double size)
        {
            TextBlock t = MakeText(s, fg, size);
            Canvas.SetLeft(t, xCenter - t.DesiredSize.Width / 2);
            Canvas.SetTop(t, yTop);
            cv.Children.Add(t);
        }
    }
}
