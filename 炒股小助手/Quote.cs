// ═══════════════════════════════════════════════════════════════════════════
// Quote.cs —— 盘口数据模型
//
// 【分区】
//   A. 方法区 —— ClearData（拉失败时清空数据）
//   B. 数据区 —— 一次盘口拉取的全部量
//
// 【这个文件负责】装"一只票一次盘口拉取"的所有数据：标识与时间 / 价格成交 / 买卖五档，
//   外加两个小功能：判断这个市场有没有五档、失败时清空。
//   所有数值都用 decimal?（可空小数）：decimal 避免浮点误差，null 表示"这项没拉到"，
//   显示时统一由各页面转成 "--"。
// ═══════════════════════════════════════════════════════════════════════════
using System;

namespace 炒股小助手
{
    public class Quote
    {
        // ══════════════════ A. 方法区 ══════════════════
        // 本区方法：ClearData

        // 接收：无　返回：无　改变：价格 / 成交 / 五档全部清成 null，Name 清成空串
        // 说明：Time 故意不清 —— 它表示"上一次成功拉到盘口数据的时刻"，实时区靠它显示，
        //   清掉的话那格会变成 --，看起来像"从来没拉到过数据"。
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

        // ══════════════════ B. 数据区 ══════════════════
        // ── 标识与时间（和下面的价格是同一批拉回来的）──
        public string Code { get; set; } = "";   // 完整腾讯代码（一次查多只时，靠它认这是哪一只）
        public string Name { get; set; } = "";   // 标的名称
        public DateTime? Time { get; set; }      // 接口给的数据时间（实时区"盘口"那格显示它）

        // ── 价格与成交 ──
        public decimal? Price { get; set; }      // 现价
        public decimal? PreClose { get; set; }   // 昨收
        public decimal? Open { get; set; }       // 今开
        public decimal? High { get; set; }       // 最高
        public decimal? Low { get; set; }        // 最低
        public decimal? Change { get; set; }     // 涨跌额
        public decimal? ChangePct { get; set; }  // 涨跌幅（%）
        public decimal? Volume { get; set; }     // 成交量
        public decimal? Amount { get; set; }     // 成交额
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

        // 这个市场到底有没有五档数据？—— 十档价格里只要有一个 > 0 就算有
        // （实测：A股有；美股整段都是 0；港股只有买一 / 卖一价，量也是 0）
        public bool HasDepth =>
            Buy1 is > 0 || Buy2 is > 0 || Buy3 is > 0 || Buy4 is > 0 || Buy5 is > 0 ||
            Sold1 is > 0 || Sold2 is > 0 || Sold3 is > 0 || Sold4 is > 0 || Sold5 is > 0;
    }
}
