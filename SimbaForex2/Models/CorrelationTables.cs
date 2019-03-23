using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimbaForex2.Models
{
    public class CorrelationHour
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCode { get; set; }
    }
    public class CorrelationThreeHour
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCode { get; set; }
    }
    public class CorrelationHalfDay
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCode { get; set; }
    }
    public class CorrelationDay
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCode { get; set; }
    }
    public class CorrelationWeek
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCode { get; set; }
    }
    public class CorrelationMonth
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCode { get; set; }
    }
    public interface ICorrelationModified
    {
        int ID { get; set; }
        DateTime DateRepresented { get; set; }
        DateTime DateEntered { get; set; }
        string CorrelationCodeHour { get; set; }
        string CorrelationCodeThreeHour { get; set; }
        string CorrelationCodeHalfDay { get; set; }
        string CorrelationCodeDay { get; set; }
        string CorrelationCodeWeek { get; set; }
        string CorrelationCodeMonth { get; set; }
    }
    public class CorrelationLive : ICorrelationModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCodeHour { get; set; }
        public string CorrelationCodeThreeHour { get; set; }
        public string CorrelationCodeHalfDay { get; set; }
        public string CorrelationCodeDay { get; set; }
        public string CorrelationCodeWeek { get; set; }
        public string CorrelationCodeMonth { get; set; }
    }
    public class CorrelationIncremental : ICorrelationModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCodeHour { get; set; }
        public string CorrelationCodeThreeHour { get; set; }
        public string CorrelationCodeHalfDay { get; set; }
        public string CorrelationCodeDay { get; set; }
        public string CorrelationCodeWeek { get; set; }
        public string CorrelationCodeMonth { get; set; }
    }
    public class CorrelationDoubleIncremental : ICorrelationModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCodeHour { get; set; }
        public string CorrelationCodeThreeHour { get; set; }
        public string CorrelationCodeHalfDay { get; set; }
        public string CorrelationCodeDay { get; set; }
        public string CorrelationCodeWeek { get; set; }
        public string CorrelationCodeMonth { get; set; }
    }
    public class CorrelationTripleIncremental : ICorrelationModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public string CorrelationCodeHour { get; set; }
        public string CorrelationCodeThreeHour { get; set; }
        public string CorrelationCodeHalfDay { get; set; }
        public string CorrelationCodeDay { get; set; }
        public string CorrelationCodeWeek { get; set; }
        public string CorrelationCodeMonth { get; set; }
    }
}
