using System;

namespace HaVacation.Server.Services
{
    public interface IScheduleConfig<T> where T :CronJobService
    {
        string CronExpression { get; set; }
        TimeZoneInfo TimeZoneInfo { get; set; }
        bool CronWithSeconds { get; set; }
    }

    public class ScheduleConfig<T> : IScheduleConfig<T> where T : CronJobService
    {
        public string CronExpression { get; set; }
        public TimeZoneInfo TimeZoneInfo { get; set; }
        public bool CronWithSeconds { get; set; }
    }
}