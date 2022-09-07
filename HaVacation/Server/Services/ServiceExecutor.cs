using Autofac.Features.OwnedInstances;
using HADotNet.Core.Clients;
using HaVacation.Server.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public class ServiceExecutor : CronJobService
    {
        private readonly Func<Owned<ServiceClient>> _serviceClient;
        private readonly IQueueService _queue;

        public ServiceExecutor(IScheduleConfig<ServiceExecutor> config,
            ILogger<ServiceExecutor> logger, Func<Owned<ServiceClient>> serviceClient, IQueueService queue)
            : base(config.CronExpression, config.TimeZoneInfo, config.CronWithSeconds)
        {
            _serviceClient = serviceClient;
            _queue = queue;
        }
        public override void Dispose()
        {
            base.Dispose();
        }

        public override  async Task DoWork(CancellationToken cancellationToken)
        {
            var currentDate = DateTimeOffset.Now.TrimMilliseconds();

            var nextItem = _queue.PeekItem();
            while (nextItem !=null && (nextItem.LastChanged.TrimMilliseconds() <= currentDate.AddSeconds(1)&& nextItem.LastChanged.TrimMilliseconds() >= currentDate.AddSeconds(-1)))

            {
                nextItem = _queue.PopItem();
                using (var svc = _serviceClient())
                {
                   var result = await svc.Value.CallService("notify.telegramer", new { message = $"{ nextItem.EntityId } will change to { nextItem.State } at {nextItem.LastChanged} " });
                }

                nextItem = _queue.PeekItem();
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
