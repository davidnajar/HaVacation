using HaVacation.Shared.Dto;
using HaVacation.Shared.Hubs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Client.Spokes
{
    public class ApplicationStateSpoke : IApplicationStateHubClient
    {
        ISubject 
        public Task StateChanged(ApplicationStateDto newState)
        {
            throw new NotImplementedException();
        }
    }
}
