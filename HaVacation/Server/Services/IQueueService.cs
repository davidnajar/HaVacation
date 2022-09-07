using HADotNet.Core.Models;
using System.Collections.Generic;

namespace HaVacation.Server.Services
{
    public interface IQueueService
    {
        StateObject PeekItem();
        StateObject PopItem();
        void PushItem(StateObject item);
        List<StateObject> PeekAll();
    }
}