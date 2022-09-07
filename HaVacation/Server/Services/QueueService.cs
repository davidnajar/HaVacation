using HADotNet.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HaVacation.Server.Services
{
    public class QueueService : IQueueService
    {
        ConcurrentQueue<StateObject> _states = new ConcurrentQueue<StateObject>();
        ReaderWriterLockSlim _lockObj = new ReaderWriterLockSlim();

        public QueueService()
        {

        }

        /*    public bool PushItem(StateObject item)
            {
             if (   _lockObj.TryEnterReadLock(500))
                {
                    try
                    {
                        _states.Add(item);
                    }
                    finally
                    {

                    }
                }
                else{
                    return false;
                }

            }
        */
        public void PushItem(StateObject item)
        {
            _states.Enqueue(item);
        }
        public StateObject PeekItem()
        {

            if (_states.TryPeek(out var item))
            {
                return item;
            }
            return null;

        }
        public StateObject PopItem()
        {
            if (_states.TryDequeue(out var item))
            {
                return item;
            }
            return null;

        }

        public List<StateObject> PeekAll()
        {
            return _states.ToList();
        }
    }
}
