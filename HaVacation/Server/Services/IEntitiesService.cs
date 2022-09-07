using System.Collections.Generic;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public interface IEntitiesService
    {
        Task<IEnumerable<string>> GetAllEntities();
        Task<IEnumerable<string>> GetEntities(string domain);
    }
}