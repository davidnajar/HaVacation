using Autofac.Features.OwnedInstances;
using HaVacation.Server.Extensions;
using HaVacation.Server.Models;
using HaVacation.Server.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public class JobRunner : CronJobService
    {
        private readonly Func<Owned<IVacationModeRunner>> _vacationModeRunnerFactory;
        private readonly ILogger<JobRunner> _logger;
        private readonly IOptionsMonitor<VacationModeConfiguration> _settings;
        private readonly ApplicationState _appState;

        public JobRunner(IScheduleConfig<JobRunner> config, Func<Owned<IVacationModeRunner>> vacationModeRunnerFactory,
            ILogger<JobRunner> logger, IOptionsMonitor<VacationModeConfiguration> settings, ApplicationState appState)
            : base(config.CronExpression, config.TimeZoneInfo, config.CronWithSeconds)
        {
            _vacationModeRunnerFactory = vacationModeRunnerFactory;
            _logger = logger;
            _settings = settings;
            _appState = appState;
        }
        public override void Dispose()
        {
            base.Dispose();
        }

        public override async Task DoWork(CancellationToken cancellationToken)
        {

            using (var runner =  _vacationModeRunnerFactory())

            {
                var startDate = DateTimeOffset.Now.TrimSeconds().AddMinutes(1).AddDays(-_settings.CurrentValue.Days);
                var endDate = startDate.AddMinutes (60);
                if (startDate > _appState.LastTimeChecked)
                {
                    await runner.Value.FetchStatesAsync(startDate, endDate);
                    _appState.LastTimeChecked = endDate;
                }
               else if (endDate>_appState.LastTimeChecked)
                {
                    startDate = _appState.LastTimeChecked; 
                    await runner.Value.FetchStatesAsync(startDate,endDate);
                    _appState.LastTimeChecked = endDate;
                }
                else
                {
                    //skip as lastTimeChecked is already covered
                }
                
               
                    

            }

                await base.DoWork(cancellationToken);
        }

        public override Task StartAsync(CancellationToken cancellationToken)
        {
            return base.StartAsync(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            return base.StopAsync(cancellationToken);
        }
    }
}
