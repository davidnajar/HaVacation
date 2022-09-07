using HaVacation.Shared.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Shared.Hubs
{
    public interface IApplicationStateHubClient
    {
        public Task StateChanged(ApplicationStateDto newState);
    }
}
