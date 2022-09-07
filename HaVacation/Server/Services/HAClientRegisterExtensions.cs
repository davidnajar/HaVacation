using Autofac;
using HADotNet.Core;
using HADotNet.Core.Clients;
using HaVacation.Server.Extensions;
using HaVacation.Server.Models;
using HaVacation.Server.State;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public static class HAClientRegisterExtensions
    {
        static IDisposable _changeHandler;
        public static IServiceCollection AddHAClient(this IServiceCollection services)
        {
            services.AddCronJob<JobRunner>(c =>
            {
                c.TimeZoneInfo = TimeZoneInfo.Local;
                c.CronExpression = @"59 * * * *";
            });
            services.AddCronJob<ServiceExecutor>(c =>
            {
                c.TimeZoneInfo = TimeZoneInfo.Local;
                c.CronExpression = @"* * * * * *";
                c.CronWithSeconds = true;
            });
            return services;
        }


        public static ContainerBuilder RegisterCustomServices(this ContainerBuilder builder )
        {
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<ConfigClient>();
            });
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<EntityClient>();
            });
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<StatesClient>();
            });
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<ServiceClient>();
            });
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<CalendarClient>();
            });
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<HistoryClient>();
            });
            builder.Register(svc =>
            {

                return ClientFactory.GetClient<TemplateClient>();
            });

           
            builder.RegisterType<EntitiesService>().As<IEntitiesService>();
            builder.RegisterType<VacationModeRunner>().As<IVacationModeRunner>();
            builder.RegisterType<QueueService>().As<IQueueService>().SingleInstance();
            builder.RegisterType<ApplicationState>().SingleInstance();
            return builder;

        }
        public static IApplicationBuilder UseHAClient(this IApplicationBuilder builder)
        {
            var config = builder.ApplicationServices.GetService<IOptionsMonitor<MainConfiguration>>();
            ClientFactory.Initialize(config.CurrentValue.HomeAssistantUrl, config.CurrentValue.HomeAssistantToken);

            _changeHandler = config.OnChange(cfg =>
             {
                 ClientFactory.Reset();
                 ClientFactory.Initialize(cfg.HomeAssistantUrl, cfg.HomeAssistantToken);

             });
            return builder;



        }
        public static IApplicationBuilder WarmUp(this IApplicationBuilder builder)
        {
            using (var scope = builder.ApplicationServices.CreateScope()) {

                var runner = scope.ServiceProvider.GetService<IVacationModeRunner>();
                var config =scope.ServiceProvider.GetService<IOptionsSnapshot<VacationModeConfiguration>>();
                var appState = scope.ServiceProvider.GetService<ApplicationState>();
                //from now until next minute 59

                var from = DateTimeOffset.Now.AddDays((-1)*config.Value.Days).TrimSeconds();
                var to = from.TrimMinutes().AddHours(24);
                if (DateTimeOffset.Compare(from, to) < 0)
                {
                    Task.Factory.StartNew(async () => { await runner.FetchStatesAsync(from, to);
                        appState.LastTimeChecked = to;
                    });
                }
           }
            return builder;
        }

        public static IServiceCollection AddCronJob<T>(this IServiceCollection services, Action<IScheduleConfig<T>> options) where T : CronJobService
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options), @"Please provide Schedule Configurations.");
            }
            var config = new ScheduleConfig<T>();
            options.Invoke(config);
            if (string.IsNullOrWhiteSpace(config.CronExpression))
            {
                throw new ArgumentNullException(nameof(ScheduleConfig<T>.CronExpression), @"Empty Cron Expression is not allowed.");
            }

            services.AddSingleton<IScheduleConfig<T>>(config);
            services.AddHostedService<T>();
           
            return services;
        }
    }
}
