using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class EngineRoomServiceDB : DB
    {
        public enum LogEventType
        {
            ON,
            OFF,
            ALERT,
            ALERT_OFF,
            WARNING,
            ONLINE,
            OFFLINE,
            ADD,
            REMOVE,
            ERROR,
            INFO,
            START,
            STOP,
            CONNECT,
            DISCONNECT,
            RESET
        }
        static public EngineRoomServiceDB Create(System.Configuration.ApplicationSettingsBase settings, String dbnameKey = null)
        {
            EngineRoomServiceDB db = dbnameKey != null ? DB.Create<EngineRoomServiceDB>(settings, dbnameKey) : DB.Create<EngineRoomServiceDB>(settings);
            return db;
        }

        override public void Initialize()
        {
            //SELECTS
            // - Events
            String fields = "e.*";
            String from = "event_log e";
            String filter = null;
            String sort = "created {0}";
            this.AddSelectStatement("events", fields, from, filter, sort, null);
            
            // - Event
            fields = "e.*";
            from = "event_log e";
            filter = "event_type='{0}' AND event_source='{1}' AND IF({2} >= 0, e.id < {2}, true)";
            sort = "created DESC";
            this.AddSelectStatement("latest-event", fields, from, filter, sort, null);

            // - Event
            fields = "e.*";
            from = "event_log e";
            filter = "event_type='{0}' AND event_source='{1}' AND IF({2} >= 0, e.id > {2}, true)";
            sort = "created ASC";
            this.AddSelectStatement("first-event", fields, from, filter, sort, null);

            //Init base
            base.Initialize();
        }

        public long LogEvent(LogEventType logEvent, String source, String description = null)
        {
            var newRow = new DBRow();
            newRow["event_type"] = logEvent.ToString();
            newRow["event_source"] = source;
            if (description != null) newRow["event_description"] = description;

            return Insert("event_log", newRow);
        }

        public DBRow GetLatestEvent(LogEventType logEvent, String source, long limitId = -1)
        {
            return SelectRow("latest-event", "*", logEvent.ToString(), source, limitId.ToString());
        }

        public DBRow GetFirstEvent(LogEventType logEvent, String source, long limitId = -1)
        {
            return SelectRow("first-event", "*", logEvent.ToString(), source, limitId.ToString());
        }

        public DBRow GetLatestEvent(LogEventType currentEvent, LogEventType preceedingEvent, String source)
        {
            DBRow pe = GetLatestEvent(preceedingEvent, source);
            long limitId = pe == null ? -1 : pe.ID;
            DBRow ce = GetFirstEvent(currentEvent, source, limitId);
            return ce;
        }

        public DBRow GetFirstOnAfterLastOff(String source)
        {
            return GetLatestEvent(LogEventType.ON, LogEventType.OFF, source);
        }

        public long LogState(String stateSource, String stateName, Object state, String description = null)
        {
            var newRow = new DBRow();
            newRow["state_source"] = stateSource;
            newRow["state_name"] = stateName;
            if(description != null)newRow["state_description"] = description;
            newRow["state"] = state;
            return Insert("state_log", newRow);
        }
    }
}
