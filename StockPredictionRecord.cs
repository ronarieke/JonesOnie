using System;
using System.Collections.Generic;
using System.Text;

namespace SimbaForceLibrary.Models
{
    public class StockPredictionRecord
    {
        public string id { get; set; }
        public int recordId { get; set; }
        public DateTime entryTime { get; set; }
        public string recordType { get; set; }
        public string record { get; set; }
    }
}
