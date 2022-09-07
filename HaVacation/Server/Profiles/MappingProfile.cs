using AutoMapper;
using HADotNet.Core.Models;
using HaVacation.Server.State;
using HaVacation.Shared.Dto;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HaVacation.Server.Profiles
{
	public class MappingProfile : Profile
	{
		public MappingProfile()
		{
			CreateMap<StateObject, StateObjectDto>();
			CreateMap<ApplicationState, ApplicationStateDto>();

		}
	}
	
}
