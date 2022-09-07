using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Models
{
    public class VacationModeConfiguration
    {
        public int Days { get; set; }
        public List<string> Entities { get; set; }
    }
}
