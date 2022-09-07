using AutoMapper;
using HaVacation.Server.Services;
using HaVacation.Shared.Dto;
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
    public class QueueController : ControllerBase
    {
        private readonly IQueueService _queueService;
        private readonly IMapper _mapper;

        public QueueController(IQueueService queueService, IMapper mapper)
        {
            _queueService = queueService;
            _mapper = mapper;
        }

        [HttpGet]
        public  IActionResult GetPending()
        {

            var items = _queueService.PeekAll();
            var result = new List<StateObjectDto>();
            foreach (var item in items)
            {
                result.Add(_mapper.Map<StateObjectDto>(item));


            }
            return Ok(result);
        }
    }
}
