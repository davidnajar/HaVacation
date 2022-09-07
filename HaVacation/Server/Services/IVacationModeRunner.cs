using System;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public interface IVacationModeRunner
    {
        Task FetchStatesAsync(DateTimeOffset startTime, DateTimeOffset endTime);
    }
}