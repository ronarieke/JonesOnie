using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimbaForex2.Models.OandaModel
{
    public class InstrumentsResponse
    {
        public List<Instrument> instruments { get; set; }

    }
    public class Instrument

    {
        public int ID { get; set; }
        public string name { get; set; }

        public string type { get; set; }

        public string displayName { get; set; }

        public int pipLocation { get; set; }

        public int displayPrecision { get; set; }

        public int tradeUnitsPrecision { get; set; }

        public long minimumTradeSize { get; set; }

        public decimal maximumTrailingStopDistance { get; set; }

        public decimal minimumTrailingStopDistance { get; set; }

        public decimal maximumPositionSize { get; set; }

        public long maximumOrderUnits { get; set; }

        public decimal marginRate { get; set; }

    }

    public class CandlesResponse// : Response

    {

        public string instrument;

        public string granularity;

        public List<Candlestick> candles;

    }
    public class Candlestick

    {

        public string time { get; set; }

        public CandleStickData bid { get; set; }

        public CandleStickData ask { get; set; }

        public CandleStickData mid { get; set; }

        public int volume { get; set; }

        public bool complete { get; set; }

    }

    public class CandleStickData

    {

        public decimal o { get; set; }

        public decimal h { get; set; }

        public decimal l { get; set; }

        public decimal c { get; set; }

    }


    public class CandlestickPlus : Candlestick

    {

        public CandlestickPlus() { }

        public CandlestickPlus(Candlestick candlestick)

        {

            time = candlestick.time;

            bid = candlestick.bid;

            ask = candlestick.ask;

            mid = candlestick.mid;

            volume = candlestick.volume;

            complete = candlestick.complete;

        }

        public string instrument { get; set; }

        public string granularity { get; set; }

    }
}
