using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace SimbaForex2.Models.OandaModel
{

    public class AccountResponse //: Response

    {
        public Account account { get; set; }
    }

    public class Account : AccountSummary

    {

        public List<TradeSummary> trades { get; set; }

        public List<Position> positions { get; set; }

        public List<Order> orders { get; set; }

    }
    public class TradeSummary : TradeBase

    {

        public long takeProfitOrderID { get; set; }

        public long stopLossOrderID { get; set; }

        public long trailingStopLossOrderID { get; set; }

    }
    public abstract class TradeBase

    {

        public long id { get; set; }

        public string instrument { get; set; }

        public decimal price { get; set; }

        public string openTime { get; set; }

        public string state { get; set; }

        public long initialUnits { get; set; }

        public long currentUnits { get; set; }

        public decimal realizedPL { get; set; }

        public decimal unrealizedPL { get; set; }

        public decimal averageClosePrice { get; set; }

        public List<long> closingTransactionIDs { get; set; }

        public decimal financing { get; set; }

        public string closeTime { get; set; }

        public ClientExtensions clientExtensions { get; set; }

    }
    public class Order : IOrder

    {

        public string type { get; set; }

        public long id { get; set; }

        public string createTime { get; set; }

        public string state { get; set; }

        public ClientExtensions clientExtensions { get; set; }

    }
    public interface IOrder

    {

        string type { get; set; }

        long id { get; set; }

        string createTime { get; set; }

        string state { get; set; }

        ClientExtensions clientExtensions { get; set; }

    }
    public class Position

    {
        public int ID { get; set; }
        public string instrument { get; set; }

        public decimal pl { get; set; }

        public decimal unrealizedPL { get; set; }

        public decimal resettablePL { get; set; }

        public PositionSide @long { get; set; }

        public PositionSide @short { get; set; }

    }
    public class PositionSide

    {

        public long units { get; set; }

        public decimal averagePrice { get; set; }

        public List<long> tradeIDs { get; set; }

        public decimal pl { get; set; }

        public decimal unrealizedPL { get; set; }

        public decimal resettablePL { get; set; }

    }
    public class ClientExtensions

    {

        public string id { get; set; }

        public string tag { get; set; }

        public string comment { get; set; }

    }
    public class AccountSummary

    {
        public int AccountSummaryID { get; set; }

        public string id { get; set; }

        public string alias { get; set; }

        public string currency { get; set; }

        public decimal balance { get; set; }

        public int createdByUserID { get; set; }

        public string createdTime { get; set; }

        public decimal pl { get; set; }

        public decimal resettablePL { get; set; }

        public string resettablePLTime { get; set; }

        public decimal marginRate { get; set; }

        public string marginCallEnterTime { get; set; }

        public int marginCallExtensionCount { get; set; }

        public string lastMarginCallExtensionTime { get; set; }

        public int openTradeCount { get; set; }

        public int openPositionCount { get; set; }

        public int pendingOrderCount { get; set; }

        public bool hedgingEnabled { get; set; }

        public decimal unrealizedPL { get; set; }

        public decimal NAV { get; set; }

        public decimal marginUsed { get; set; }

        public decimal marginAvailable { get; set; }

        public decimal positionValue { get; set; }

        public decimal marginCloseoutUnrealizedPL { get; set; }

        public decimal marginCloseoutNAV { get; set; }

        public decimal marginCloseoutMarginUsed { get; set; }

        public decimal marginCloseoutPercent { get; set; }

        public decimal marginCloseoutPositionValue { get; set; }

        public decimal withdrawalLimit { get; set; }

        public decimal marginCallMarginUsed { get; set; }

        public decimal marginCallPercent { get; set; }

        public long lastTransactionID { get; set; }

    }
}