using HaVacation.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EntitiesController : ControllerBase
    {
        private readonly IEntitiesService _entities;

        public EntitiesController(IEntitiesService entities)
        {
            _entities = entities;
        } 
        [HttpGet]
        public async Task<IActionResult> Get(string domains)
        {
            if (!string.IsNullOrWhiteSpace(domains))
            {
                List<string> result = new List<string>();
                foreach(var domain in domains.Split(','))
                {
                   result.AddRange(await _entities.GetEntities(domain));
                }
                return Ok(result);
            }
            else
            {
                return Ok(await _entities.GetAllEntities());
            }
        }
    }
}
