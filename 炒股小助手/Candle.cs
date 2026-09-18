// ═══════════════════════════════════════════════════════════════════════════
// Candle.cs —— K 线数据模型（一根蜡烛）
//
// 一根 K 线 = 1 个时间 + 4 个价格：开盘、最高、最低、收盘。
// 绘制代码只认这个模型：以后换数据源（腾讯日K / 分钟K / 别的接口），
// 只要把返回的每行解析成 Candle 丢进列表，绘制部分一行都不用改。
// ═══════════════════════════════════════════════════════════════════════════
using System;

namespace 炒股小助手
{
    public class Candle
    {
        public DateTime Date { get; set; }    // 日期（画在 X 轴刻度上）
        public decimal Open { get; set; }     // 开盘价
        public decimal High { get; set; }     // 最高价
        public decimal Low { get; set; }      // 最低价
        public decimal Close { get; set; }    // 收盘价

        // 收 >= 开 算涨（画红），否则算跌（画绿）
        public bool IsUp => Close >= Open;
    }
}
