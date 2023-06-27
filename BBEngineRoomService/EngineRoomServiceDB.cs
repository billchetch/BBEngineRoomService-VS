using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class EngineRoomServiceDB : ADMServiceDB
    {
        public enum LogEventType
        {
            ON,
            OFF,
            ALERT,
            ALERT_OFF,
            WARNING,
            ENABLE,
            DISABLE,
            ADD,
            REMOVE,
            ERROR,
            INFO,
            START,
            STOP,
            CONNECT,
            DISCONNECT,
            RESET,
            INITIALISE
        }
        static public EngineRoomServiceDB Create(System.Configuration.ApplicationSettingsBase settings, String dbnameKey = null)
        {
            EngineRoomServiceDB db = dbnameKey != null ? DB.Create<EngineRoomServiceDB>(settings, dbnameKey) : DB.Create<EngineRoomServiceDB>(settings);
            return db;
        }

        
    }
}
