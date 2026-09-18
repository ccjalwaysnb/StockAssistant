// ═══════════════════════════════════════════════════════════════════════════
// CandleChart.xaml.cs —— K 线图控件的逻辑（可复用的小控件）
//
// 画法：一根 K 线 = 1 条影线（最高→最低）+ 1 个实体矩形（开盘→收盘），涨红跌绿。
//
// 视图状态只有 4 个数，整个交互都建立在这上面：
//     X 方向 → viewStart（左边缘在第几根）、viewCount（窗口里显示几根）
//     Y 方向 → yTop（窗口顶端价格）、ySpan（价格跨度，按框内数据自动算）
//
// 三项交互：
//   ① 悬停：鼠标停在某根 K 柱上满 1 秒 → 弹气泡（右上角；放不下就退到左下角）
//   ② 缩放：鼠标在图内 + Ctrl + 滚轮，每次按 1.25 倍变动显示根数，再吸附到 4 的倍数
//          （步进比原来大得多；默认视角是一屏 12 根、贴着最右边，见 DefaultCount）
//   ③ 平移：鼠标在图内按 ← → ，沿 X 轴滑动
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

        // ── 这张图是"日K"还是"分钟K" ──
        // 只影响 X 轴刻度怎么写：日K → 最左写"2026年"、刻度写"09-15"（月-日）
        //                        分钟K → 刻度写"10:03"（时:分），最左那格不写
        // 由外面在 XAML 里指定（MainPage.xaml 中 KChartMin 写了 MinuteMode="True"）。
        // 这里特意写成"后备字段 + 属性"而不是自动属性 { get; set; }，是因为要在赋值后重画一次：
        // 以后万一先 SetData 再改 MinuteMode，刻度样式也会立刻跟着变，不会停在旧写法上。
        private bool minuteMode;
        public bool MinuteMode
        {
            get => minuteMode;
            set
            {
                if (minuteMode == value) return;
                minuteMode = value;
                // 解析 XAML 时也可能走到这里，那时画布字段还是 null，所以要挡一下
                if (KLineCanvas != null) RedrawAll();
            }
        }

        // 灌数据：主界面拉到 K 线后调这个（日K 一张图、分钟K 另一张图，各灌各的）。
        // 不带第二个参数 = 重置视图成"最近 12 根"：搜索换标的时想要的就是这个视角。
        public void SetData(List<Candle> data) => SetData(data, resetView: true);

        // 灌数据（可保留当前视图）—— 给"分钟K 自动刷新"这类场景用：
        //   resetView = true ：换标的了，重置成默认视角（一屏 12 根、贴右）
        //   resetView = false：只是末尾接上了新柱子，别动用户当前的缩放和位置；
        //                      但如果他本来就贴着最右边在看最新那几根，
        //                      视图要跟着新数据往前挪，不然新柱子会跑到屏幕外看不见。
        public void SetData(List<Candle> data, bool resetView)
        {
            int oldCount = candles.Count;   // 换数据前先记住旧根数：下面要判断"原来是不是贴着最右边"
            candles = data ?? new List<Candle>();

            if (resetView)
            {
                InitView();                 // 重置成默认：一屏 12 根、贴右边
            }
            else if (viewCount <= 0 || candles.Count == 0)
            {
                InitView();                 // 还没有过视图（或数据空了）→ 还是得初始化一次
            }
            else
            {
                // 原来贴着最右边（右边缘正好是最后一根）→ 视图跟着新数据往前挪，继续看最新
                bool wasAtRightEdge = Math.Abs(viewStart + viewCount - oldCount) < 1e-9;

                if (wasAtRightEdge)
                    viewStart = Math.Max(0, candles.Count - viewCount);
                else
                    viewStart = Math.Clamp(viewStart, 0, Math.Max(0, candles.Count - viewCount));

                UpdateYRange();             // 框内数据变了 → Y 轴重算
            }

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

        // ── 缩放范围限制 ──
        private double MinCount => Math.Min(4, MaxCount);                      // 最多放大到框内 4 根
        // 364 = 一年交易日（365 天去掉零头后向下取整到 4 的倍数），
        // 所以"拉到最远"差不多正好是一年的日K；数据不够 364 根就全显示。
        private double MaxCount => SnapDown4(Math.Min(364, candles.Count));

        // ── 视角不变式：viewCount（一屏显示几根）永远是 4 的倍数 ──
        // 这是定下来的规矩，作用有两个：
        //   ① 缩放档位整齐：4、8、12、16…，每滚一格至少动 4 根，
        //      不会碎成 12、13、14 这种一步一根的小碎步（原来就是那样，缩起来太慢）；
        //   ② 一屏正好装整数根，柱子永远整根对齐，不会出现"半根柱子"。
        // 特例：数据总根数不足 4 根时（新股、刚开盘几分钟），不套这条规则，有多少显示多少。
        private static double SnapDown4(double v) => v < 4 ? v : Math.Floor(v / 4) * 4;

        // ══════════════════ C. 布局常量（画布四周留白）══════════════════
        // 留白宽度是跟着字号定的：字放大之后，左边装得下"1500.00"这种价格、下边装得下"09-15"这种刻度，
        // 所以 PadLeft / PadBottom 都比字体变大的幅度一起调大了。
        private const double PadLeft = 66;    // 左边留给价格刻度
        private const double PadRight = 16;
        private const double PadTop = 12;
        private const double PadBottom = 34;  // 下边留给日期刻度（要比最低那条价格刻度再低一点，两行字才不会挨在一起）

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

        // ── 刻度文字的字号与字体 ──
        // 字号：原来价格刻度 11 号、日期刻度 10 号，黑底上偏小偏细；这里统一放大到 13 号并加粗半档。
        // 字体：跟盘口图里的数字统一用 Consolas（等宽）。
        //   盘口图那边的一堆读数本来就是 Consolas，这里如果单独用微软雅黑，两块地方的字形风格会打架；
        //   反过来让盘口图迁就雅黑就要动十几处、还会破坏五档用 PadLeft 做的等宽对齐，所以统一到 Consolas。
        //   唯一的小瑕疵：日K 最左那格"2026年"里的"年"字 Consolas 没有，由系统自动兜底成中文字体。
        private const double AxisFontSize = 13;
        private static readonly FontFamily AxisFont = new FontFamily("Consolas");

        public CandleChart()
        {
            InitializeComponent();

            hoverTimer.Tick += HoverTimer_Tick;   // 悬停满 1 秒 → 弹气泡
            Loaded += CandleChart_Loaded;         // 控件进树时，把方向键挂到窗口上
            InitView();
        }

        // ══════════════════ F. 视图初始化与坐标换算 ══════════════════

        // 默认缩放：一屏 12 根（日K 就是最近 12 个交易日，分钟K 就是最近 12 分钟），贴最右边 = 最新的 12 根
        // 之前默认是"全显示"（最多 370 根），一眼看过去柱子糊成一片，还得自己往回缩；
        // 现在默认就站在"最近几天 / 最近十分钟"这个最常用的位置，想看更早的用 Ctrl+滚轮或 ← 键。
        private const int DefaultCount = 12;

        // 每滚一格，显示根数乘 / 除这个倍率（放大 ÷1.25、缩小 ×1.25）。
        // 之所以不做"+ 固定根数"，是因为可见根数跨度很大（4 根 ~ 364 根）：
        //   固定加 4 根时，从小图缩到全图要滚 90 次；按倍率滚 15 次左右就能从最近 12 根拉到全图。
        private const double ZoomRatio = 1.25;

        // 默认视图：一屏 12 根、贴右边（最新的 12 根在最右端）；Y 轴按框内数据自适应
        private void InitView()
        {
            // Math.Clamp：把 12 夹到 [最少, 最多] 区间里。
            // 数据不够 12 根时（比如只有 5 根），这里会取到 MaxCount，直接全显示。
            viewCount = Math.Clamp(DefaultCount, MinCount, MaxCount);
            viewStart = Math.Max(0, candles.Count - viewCount);   // 贴右边：从"总数 - 一屏根数"开始
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
                PutTextRight(cv, price.ToString("F2"), PadLeft - 8, y, TextBrush, AxisFontSize);
            }

            // ── X 轴：4 个刻度（最老 / 两个中间点 / 最新），位置尽量均匀 ──
            //    摆法：首、尾，以及把"首尾之间"3 等分得到的两个点：
            //      最老那根 —— 1/3 处 —— 2/3 处 —— 最新那根
            //    为什么是 3 等分而不是 4 等分：4 个标签要同时压住最老和最新两端，中间就只能切 3 段。
            //    切 4 段的话第 4 个点会落在 3/4 处，最新那根反而没有日期（最新日期恰恰是常看的）。
            //    写什么字由 MinuteMode 决定：日K 写"月-日"，分钟K 写"时:分"（见 XLabel）。
            int first = VisibleFirst, last = VisibleLast;
            int span = last - first;        // 首尾相差几根
            for (int n = 0; n < 4; n++)
            {
                // 极端情况：一屏只有一根（span = 0）时 4 个刻度会重叠在一起，那就只画一个
                if (span == 0 && n > 0) break;

                // n = 0,1,2,3 → 位置 = 首 + span × (0、1/3、2/3、1)，四舍五入到某一根上
                int idx = first + (int)Math.Round(span * n / 3.0);
                double x = Cx(idx);
                cv.Children.Add(MakeLine(x, PadTop, x, PadTop + PlotH, VGridBrush, 1));   // 竖网格跟着刻度走
                PutTextCenter(cv, XLabel(candles[idx].Date), x, PadTop + PlotH + 10, TextBrush, AxisFontSize);
            }

            // ── 最左边那格：只有日K 写年份，分钟K 不写 ──
            // 放在画布最左端（x = 4），字号和上面 4 个刻度一样，视觉上跟刻度是同一行。
            // 年份取的是【最右侧那根】的年份（也就是最新一根）：看行情时最关心的是"最新这段属于哪一年"，
            // 往左滚回老数据时这格会跟着变成那一年的年份。
            // （分钟K 原来在这里写了"10时"，看着多余又突兀，已按用户要求去掉：
            //   分钟K 的 4 个刻度本身都带小时，如 10:03，不需要再单独交代小时。）
            if (!minuteMode)
                PutTextLeft(cv, HeadLabel(candles[last].Date), 4, PadTop + PlotH + 10, TextBrush, AxisFontSize);

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
            // 第五行：这根 K 柱的时间。日K 给"年-月-日"，分钟K 给"月-日 时:分"（分钟才有意义）
            TipTime.Text = minuteMode ? k.Date.ToString("MM-dd HH:mm") : k.Date.ToString("yyyy-MM-dd");

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
            // 必须用 ActualWidth/ActualHeight（排版后的真实尺寸）。
            // DesiredSize 在元素被反复测量后会失真（同一个气泡能报出 325x404 这种假尺寸），
            // 用它算位置会把气泡摆到错误的地方。
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

        // ══════════════════ I. Ctrl + 滚轮：缩放（只动 X 轴，每次 1.25 倍、吸附到 4 的倍数）══════════════════

        private void KLineCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;   // 没按 Ctrl 就不管
            e.Handled = true;
            ZoomStep(e.Delta > 0 ? +1 : -1);                                 // 上滚 = 放大（少显示几根）
        }

        // 缩放只改"框内显示多少根"。
        // 新的算法分两步：① 按倍率算出目标根数 → ② 把它吸附到 4 的倍数上（保住上面那条不变式）。
        //   正常：锁右边 —— 右边缘索引不变，新 viewStart = 原右边缘索引 - 新根数
        //   例外：最老的那根已经露出来了（viewStart 贴着 0）→ 改成锁左边，只改根数，
        //        放大时右边缘会跟着左移
        //   Y 轴不参与缩放，交给 UpdateYRange() 按框内数据自适应
        private void ZoomStep(int dir)
        {
            // dir = +1 放大（少显示几根）/ -1 缩小（多显示几根）
            // 目标根数 = 当前根数 ÷1.25（放大）或 ×1.25（缩小）
            double target = dir > 0 ? viewCount / ZoomRatio : viewCount * ZoomRatio;

            // 吸附到最近的 4 的倍数。Round(…, AwayFromZero) 就是"四舍五入"，写在参数里是为了明确
            // 1.5、2.5 这种正好在中间的数往远离 0 的方向取整（默认规则是"取偶数"，对学习者更绕）。
            double newCount = Math.Round(target / 4, MidpointRounding.AwayFromZero) * 4;

            // 上面那步有可能把结果吸回原值（例如 4 → 目标 3.2 → 吸附回 4），那就等于"滚了没反应"。
            // 所以再强制"每滚一格至少动 4 根"，保证手感一致。
            const double minStep = 4;
            newCount = dir > 0 ? Math.Min(newCount, viewCount - minStep)
                               : Math.Max(newCount, viewCount + minStep);

            // 最后夹到允许范围里：如果已经在最小值 / 最大值上，这一步会让它等于当前值 → 后面直接 return
            newCount = Math.Clamp(newCount, MinCount, MaxCount);
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
                FontFamily = AxisFont,                 // 跟主界面同一套字体（微软雅黑）
                FontWeight = FontWeights.SemiBold,     // 比默认粗半档：黑底上更饱满、更醒目
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

        // 文字左对齐（X 轴最左边那格：年份 / 小时，贴着画布左边缘放）
        private static void PutTextLeft(Canvas cv, string s, double xLeft, double yTop, Brush fg, double size)
        {
            TextBlock t = MakeText(s, fg, size);
            Canvas.SetLeft(t, xLeft);
            Canvas.SetTop(t, yTop);
            cv.Children.Add(t);
        }

        // ── X 轴刻度文字 ──
        // 日K：写"月-日"，例如 09-15
        // 分钟K：写"时:分"，例如 10:03
        // 这里之所以给分钟K 也带上小时，是因为单写"03"没人知道是上午 10:03 还是下午 2:03，
        // 带上小时后每个刻度都能独立读懂。
        private string XLabel(DateTime t) => minuteMode ? t.ToString("HH:mm") : t.ToString("MM-dd");

        // X 轴最左端那格的文字：日K 写年份（2026年）。分钟K 不写这格
        private string HeadLabel(DateTime t) => t.Year + "年";
    }
}
