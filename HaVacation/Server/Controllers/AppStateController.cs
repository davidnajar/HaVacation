using AutoMapper;
using HaVacation.Server.State;
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
    public class AppStateController : ControllerBase
    {
        private readonly ApplicationState _state;
        private readonly IMapper _mapper;

        public AppStateController(ApplicationState state, IMapper mapper)
        {
            _state = state;
            _mapper = mapper;
        }
[HttpGet]
        public IActionResult Get()
        {
            return Ok(_mapper.Map<ApplicationStateDto>(_state));
        }
    }
}
