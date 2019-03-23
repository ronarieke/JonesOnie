using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.Entity;
using SimbaForex2.Models.OandaModel;


namespace SimbaForex2.Models.Concrete
{
    public class SimbaForex2Context : DbContext
    {
        public DbSet<User> Users { get; set; }
        public DbSet<AccountSummary> AccountSummaries { get; set; }
        public DbSet<AccountHolding> AccountHoldings { get; set; }

        public DbSet<CorrelationHour> CorrelationHours { get; set; }
        public DbSet<CorrelationThreeHour> CorrelationThreeHours { get; set; }
        public DbSet<CorrelationHalfDay> CorrelationHalfDays { get; set; }
        public DbSet<CorrelationDay> CorrelationDays { get; set; }
        public DbSet<CorrelationWeek> CorrelationWeeks { get; set; }
        public DbSet<CorrelationMonth> CorrelationMonths { get; set; }

        public DbSet<CorrelationLive> CorrelationLives { get; set; }
        public DbSet<CorrelationIncremental> CorrelationIncrementals { get; set; }
        public DbSet<CorrelationDoubleIncremental> CorrelationDoubleIncrementals { get; set; }
        public DbSet<CorrelationTripleIncremental> CorrelationTripleIncrementals { get; set; }

        public DbSet<LearningHour> LearningHours { get; set; }
        public DbSet<LearningThreeHour> LearningThreeHours { get; set; }
        public DbSet<LearningHalfDay> LearningHalfDays { get; set; }
        public DbSet<LearningDay> LearningDays { get; set; }
        public DbSet<LearningWeek> LearningWeeks { get; set; }
        public DbSet<LearningMonth> LearningMonths { get; set; }

        public DbSet<LearningLive> LearningLives { get; set; }
        public DbSet<LearningIncremental> LearningIncrementals { get; set; }
        public DbSet<LearningDoubleIncremental> LearningDoubleIncrementals { get; set; }
        public DbSet<LearningTripleIncremental> LearningTripleIncrementals { get; set; }

        public DbSet<Instrument> Instruments { get; set; }

        public DbSet<HttpExceptionLog> HttpExceptionLogs { get; set; }

    }
}
