using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HaVacation.Shared.Dto
{
    public class StateObjectDto
    {
        public string EntityId { get; set; }
       
        public string State { get; set; }
       
        public Dictionary<string, object> Attributes { get; set; }

        public DateTimeOffset LastChanged { get; set; }
        
      
    }
}
