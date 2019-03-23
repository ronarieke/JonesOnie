using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimbaForex2.Models
{
    public interface ILearning
    {
        int ID { get; set; }
        DateTime DateRepresented { get; set; }
        DateTime DateEntered { get; set; }
        int CorrelationID { get; set; }
        string FactorXML { get; set; }
    }
    public class LearningHour : ILearning
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationID { get; set; }
        public string FactorXML { get; set; }
    }
    public class LearningThreeHour : ILearning
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationID { get; set; }
        public string FactorXML { get; set; }
    }
    public class LearningHalfDay : ILearning
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationID { get; set; }
        public string FactorXML { get; set; }
    }
    public class LearningDay : ILearning
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationID { get; set; }
        public string FactorXML { get; set; }
    }
    public class LearningWeek : ILearning
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationID { get; set; }
        public string FactorXML { get; set; }
    }
    public class LearningMonth : ILearning
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationID { get; set; }
        public string FactorXML { get; set; }
    }
    public interface ILearningModified
    {
        int ID { get; set; }
        DateTime DateRepresented { get; set; }
        DateTime DateEntered { get; set; }
        int CorrelationLiveID { get; set; }
        string FactorHourXML { get; set; }
        string FactorThreeHourXML { get; set; }
        string FactorHalfDayXML { get; set; }
        string FactorDayXML { get; set; }
        string FactorWeekXML { get; set; }
        string FactorMonthXML { get; set; }
    }
    public class LearningLive : ILearningModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationLiveID { get; set; }
        public string FactorHourXML { get; set; }
        public string FactorThreeHourXML { get; set; }
        public string FactorHalfDayXML { get; set; }
        public string FactorDayXML { get; set; }
        public string FactorWeekXML { get; set; }
        public string FactorMonthXML { get; set; }
    }
    public class LearningIncremental : ILearningModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationLiveID { get; set; }
        public string FactorHourXML { get; set; }
        public string FactorThreeHourXML { get; set; }
        public string FactorHalfDayXML { get; set; }
        public string FactorDayXML { get; set; }
        public string FactorWeekXML { get; set; }
        public string FactorMonthXML { get; set; }
    }
    public class LearningDoubleIncremental : ILearningModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationLiveID { get; set; }
        public string FactorHourXML { get; set; }
        public string FactorThreeHourXML { get; set; }
        public string FactorHalfDayXML { get; set; }
        public string FactorDayXML { get; set; }
        public string FactorWeekXML { get; set; }
        public string FactorMonthXML { get; set; }
    }
    public class LearningTripleIncremental : ILearningModified
    {
        public int ID { get; set; }
        public DateTime DateRepresented { get; set; }
        public DateTime DateEntered { get; set; }
        public int CorrelationLiveID { get; set; }
        public string FactorHourXML { get; set; }
        public string FactorThreeHourXML { get; set; }
        public string FactorHalfDayXML { get; set; }
        public string FactorDayXML { get; set; }
        public string FactorWeekXML { get; set; }
        public string FactorMonthXML { get; set; }
    }
}
