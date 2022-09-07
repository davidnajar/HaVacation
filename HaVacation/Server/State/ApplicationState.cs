using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.State
{
    public class ApplicationState
    {
        public DateTimeOffset  LastTimeChecked { get; set; } 
        public bool Fetching { get; set; }
    }
}
