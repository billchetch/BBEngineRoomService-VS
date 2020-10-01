using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Database;

namespace BBEngineRoomService
{
    class EngineRoomServiceDB : DB
    {
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
            String sort = "created DESC";
            this.AddSelectStatement("events", fields, from, filter, sort, null);

            // - Event
            fields = "e.*";
            from = "event_log e";
            filter = "event_type='{0}' AND event_source='{1}' ";
            sort = "created DESC";
            this.AddSelectStatement("event", fields, from, filter, sort, null);

            //Init base
            base.Initialize();
        }

        public long LogEvent(String eventType, String source, String description = null)
        {
            var newRow = new DBRow();
            newRow["event_type"] = eventType;
            newRow["event_source"] = source;
            if (description != null) newRow["event_description"] = description;

            return Insert("event_log", newRow);
        }

        public DBRow GetLatestEvent(String eventType, String source)
        {
            return SelectRow("event", "*", eventType, source);
        }
    }
}
