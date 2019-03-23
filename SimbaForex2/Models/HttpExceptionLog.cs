using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimbaForex2.Models
{
    public class HttpExceptionLog
    {
        public int ID { get; set; }
        public DateTime DateEntered { get; set; }
        public string ExceptionMessage { get; set; }
    }
}
