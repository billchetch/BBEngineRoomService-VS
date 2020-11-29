using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Messaging;

namespace BBEngineRoomService
{
    public interface IMonitorable
    {
        void Initialise(EngineRoomServiceDB erdb);
        void Monitor(EngineRoomServiceDB erdb, List<Message> messages, bool returnEventsOnly);
        void LogState(EngineRoomServiceDB erdb);
    }
}
