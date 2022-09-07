using HADotNet.Core.Clients;
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
    public class ServiceController : ControllerBase
    {
        private readonly ServiceClient _serviceClient;

        public ServiceController(ServiceClient serviceClient)
        {
            _serviceClient = serviceClient;
        }
        [HttpGet]
        public async Task<IActionResult> Get()
        {

            var result = await _serviceClient.CallService("notify.telegramer", new { message = $"testTest" });

            return Ok();

        }
    }
}
