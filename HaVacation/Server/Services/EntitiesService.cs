using HADotNet.Core.Clients;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public class EntitiesService : IEntitiesService
    {
        private readonly EntityClient _entityClient;

        public EntitiesService(EntityClient entityClient)
        {
            _entityClient = entityClient;
        }

        public Task<IEnumerable<string>> GetAllEntities()
        {
            return _entityClient.GetEntities();
        }
        public Task<IEnumerable<string>> GetEntities(string domain)
        {
            if (!string.IsNullOrWhiteSpace(domain))
            {
                return _entityClient.GetEntities(domain.Trim().ToLower());

            }
            else
            {
                return Task.FromResult(default(IEnumerable<string>));
            }
        }
    }
}
