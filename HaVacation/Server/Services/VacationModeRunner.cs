using HADotNet.Core.Clients;
using HADotNet.Core.Models;
using HaVacation.Server.Models;
using HaVacation.Server.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public class VacationModeRunner : IVacationModeRunner
    {
        private readonly HistoryClient _historyClient;
        private readonly ApplicationState _appState;
        private readonly IQueueService _queueService;
        private readonly ServiceClient _serviceClient;
        private readonly IOptionsSnapshot<VacationModeConfiguration> _settings;
        private readonly ILogger<VacationModeRunner> _logger;
        List<StateObject> _states = new List<StateObject>();
        public VacationModeRunner(HistoryClient historyClient, ApplicationState appState,  IQueueService queueService,  ServiceClient serviceClient, IOptionsSnapshot<VacationModeConfiguration> settings, ILogger<VacationModeRunner> logger)
        {
            _historyClient = historyClient;
            _appState = appState;
            _queueService = queueService;
            _serviceClient = serviceClient;
            _settings = settings;
            _logger = logger;
        }

        public async Task FetchStatesAsync(DateTimeOffset startTime, DateTimeOffset endTime)
        {
            if ((endTime-startTime).TotalHours > 1)
            {
                //TODO: throthle execution
            }
            foreach (var entity in _settings.Value.Entities)
            {
                var result = await _historyClient.GetHistory(entity, startTime, endTime);
                //HA History always returns the min date (startTime) with the state at this moment. in our scenario we can skip this
                if (result.Count>0)
                {
                    result.RemoveAt(0);
                }
                _states.AddRange(result);
            }

            _states = _states.OrderBy(p => p.LastChanged).ToList();

            _logger.LogDebug("Following state changes will be done:");
            foreach (var state in _states)
            {
                var prevDate = state.LastChanged;
                state.LastChanged = new DateTimeOffset(DateTimeOffset.Now.Date).Add(state.LastChanged - state.LastChanged.Date);
                _queueService.PushItem(state);
              _logger.LogDebug($"{state.EntityId} will change to {state.State} at {state.LastChanged} (was {prevDate})");
            }
          
        }
    }
}
