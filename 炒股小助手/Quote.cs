// ═══════════════════════════════════════════════════════════════════════════
// Quote.cs —— 盘口数据模型（某个标的一次盘口拉取的完整结果）
//
// 和 Candle.cs 一个思路：把"一个方法拉回来的所有数据"装进一个对象里，
// 不让它们散落在 MainPage 里排成一大排字段。
//
// 这样做的好处：
//   ① 检查数据方便：程序一跑，只看一个 quote 对象就知道这批盘口到底拉到了什么；
//   ② 换数据源方便：以后接口变了，只要把新接口解析成 Quote，下面的展示代码一行都不用改；
//   ③ 清空方便：一个 ClearData() 全清，不会漏掉某个字段。
//
// 所有数值都用 decimal?（可空小数）：decimal 避免浮点误差，null 表示"这项没拉到"，
// 显示时统一由 Fmt() 转成 "--"。
// ═══════════════════════════════════════════════════════════════════════════
using System;

namespace 炒股小助手
{
    public class Quote
    {
        // ── 标识与时间（和下面这些价格是同一批拉回来的）──
        public string Name { get; set; } = "";   // 标的名称，如"苹果"
        public DateTime? Time { get; set; }      // 接口给的数据时间（实时区"盘口"那格显示它）

        // ── 价格与成交 ──
        public decimal? Price { get; set; }      // 现价
        public decimal? PreClose { get; set; }   // 昨收
        public decimal? Open { get; set; }       // 今开
        public decimal? High { get; set; }       // 最高
        public decimal? Low { get; set; }        // 最低
        public decimal? Change { get; set; }     // 涨跌额
        public decimal? ChangePct { get; set; }  // 涨跌幅（%）
        public decimal? Volume { get; set; }     // 成交量（手）
        public decimal? Amount { get; set; }     // 成交额（万）
        public decimal? Turnover { get; set; }   // 换手率（%）

        // ── 买五档：买一~买五 的 价 / 量 ──
        public decimal? Buy1 { get; set; }  public decimal? Buy1v { get; set; }
        public decimal? Buy2 { get; set; }  public decimal? Buy2v { get; set; }
        public decimal? Buy3 { get; set; }  public decimal? Buy3v { get; set; }
        public decimal? Buy4 { get; set; }  public decimal? Buy4v { get; set; }
        public decimal? Buy5 { get; set; }  public decimal? Buy5v { get; set; }

        // ── 卖五档：卖一~卖五 的 价 / 量 ──
        public decimal? Sold1 { get; set; }  public decimal? Sold1v { get; set; }
        public decimal? Sold2 { get; set; }  public decimal? Sold2v { get; set; }
        public decimal? Sold3 { get; set; }  public decimal? Sold3v { get; set; }
        public decimal? Sold4 { get; set; }  public decimal? Sold4v { get; set; }
        public decimal? Sold5 { get; set; }  public decimal? Sold5v { get; set; }

        // 这个市场到底有没有五档数据？（实测腾讯接口：A股有；美股整段都是 0；港股只有买一/卖一价、量也是 0）
        // 判断办法：买一到买五、卖一到卖五的【价格】里，只要有一个大于 0，就算有五档。
        // 写成只读属性（只有 get）而不是普通字段：它是算出来的，不需要存，也不会跟那 20 个字段对不上。
        public bool HasDepth =>
            Buy1 is > 0 || Buy2 is > 0 || Buy3 is > 0 || Buy4 is > 0 || Buy5 is > 0 ||
            Sold1 is > 0 || Sold2 is > 0 || Sold3 is > 0 || Sold4 is > 0 || Sold5 is > 0;

        // 清空盘口数据（拉取失败时用，免得旧数字被误当成新数据）。
        // 注意：Time 不清 —— 它表示"上一次成功拉到盘口数据的时刻"，实时区靠它显示；
        //       清掉的话实时区会变成 --，看起来像"从来没拉到过数据"。
        public void ClearData()
        {
            Name = "";
            Price = PreClose = Open = High = Low = Change = ChangePct = null;
            Volume = Amount = Turnover = null;

            Buy1 = Buy2 = Buy3 = Buy4 = Buy5 = null;
            Buy1v = Buy2v = Buy3v = Buy4v = Buy5v = null;

            Sold1 = Sold2 = Sold3 = Sold4 = Sold5 = null;
            Sold1v = Sold2v = Sold3v = Sold4v = Sold5v = null;
        }
    }
}
