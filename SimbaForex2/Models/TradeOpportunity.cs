using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimbaForex2.Models
{
    public class TradeOpportunity
    {
        public long ID { get; set; }
        public string Identifier { get; set; }
        public int ListIndex { get; set; }
        public string Instrument { get; set; }
        public string BuySell { get; set; }
        public int Rank { get; set; }
        public int StRank { get; set; }
        public int MidRank { get; set; }
        public int LtRank { get; set; }
        public double mu { get; set; }
        public double logMu { get; set; }
        public TradeOpportunity() { }
        public TradeOpportunity(Tuple<string, bool, int> tup, int _index, Tuple<double, double> averages)
        {
            ListIndex = _index;
            Instrument = tup.Item1;
            BuySell = (tup.Item2 ? "buy" : "sell");
            Rank = tup.Item3;
            Identifier = tup.Item1 + DateTime.Now.ToFileTimeUtc().ToString();
            System.Threading.Thread.Sleep(10);
            mu = Math.Round(10000 * averages.Item1, 3);
            logMu = Math.Round(10000 * averages.Item2, 3);
        }
        public TradeOpportunity(Tuple<bool, Tuple<string, int, int, int>> tuptup, int _index, Tuple<double, double> averages)
        {
            ListIndex = _index;
            Instrument = tuptup.Item2.Item1;
            BuySell = tuptup.Item1 ? "buy" : "sell";
            StRank = tuptup.Item2.Item2;
            MidRank = tuptup.Item2.Item3;
            LtRank = tuptup.Item2.Item4;
            Identifier = tuptup.Item2.Item1 + DateTime.Now.ToFileTimeUtc().ToString();
            mu = Math.Round(10000 * averages.Item1, 3);
            logMu = Math.Round(10000 * averages.Item2, 3);
        }
    }
}
