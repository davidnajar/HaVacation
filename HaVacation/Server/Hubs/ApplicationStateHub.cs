using HaVacation.Server.Hubs;
using HaVacation.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Hubs
{
    public class ApplicationStateHub : Hub<IApplicationStateHubClient>
    {
    }
}
