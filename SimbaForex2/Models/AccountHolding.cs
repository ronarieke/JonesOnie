using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimbaForex2.Models
{
    public class AccountHolding
    {
        public int ID { get; set; }
        public int UserID { get; set; }
        public string Setting { get; set; }
        public string BaseUrl { get; set; }
        public string AccountID { get; set; }
        public string AccessToken { get; set; }
        public DateTime DateEntered { get; set; }
        public string alias { get; set; }
        public string currency { get; set; }
        public decimal balance { get; set; }
        public int createdByUserID { get; set; }
        public string createdTime { get; set; }
        public decimal pl { get; set; }
        public decimal resettablePL { get; set; }
        public string resettablePLTime { get; set; }
        public decimal? marginRate { get; set; }
        public int openTradeCount { get; set; }
        public int openPositionCount { get; set; }
        public int pendingOrderCount { get; set; }
        public bool hedgingEnabled { get; set; }
        public decimal unrealizedPL { get; set; }
        public decimal NAV { get; set; }
        public decimal marginUsed { get; set; }
        public decimal marginAvailable { get; set; }
        public decimal positionValue { get; set; }

    }
}
