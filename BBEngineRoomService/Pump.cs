using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino.Devices;
using Chetch.Database;
using Chetch.Messaging;

namespace BBEngineRoomService
{
    public class Pump : SwitchSensor, IMonitorable
    {
        public enum PumpState
        {
            ON,
            OFF,
            ON_TOO_LONG,
            OFF_TOO_LONG,
            ON_TOO_FREQUENTLY
        }

        public const String SENSOR_NAME = "PUMP";

        public int MaxOnDuration = -1; //at a maximum on time to raise alarms (time in secs)
        
        public PumpState StateOfPump = PumpState.OFF;

        public Pump(int pinNumber, String id) : base(pinNumber, 250, id, SENSOR_NAME) { }

        public void Initialise(EngineRoomServiceDB erdb)
        {
            //get latest data
            DBRow row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, ID);
            if (row != null)
            {
                LastOn = row.GetDateTime("created");
            }
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);
            if (row != null)
            {
                LastOff = row.GetDateTime("created");
            }

            DBRow enabled = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ENABLE, ID);
            DBRow disabled = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.DISABLE, ID);

            bool enable = true;
            if (disabled != null)
            {
                enable = enabled == null ? false : enabled.GetDateTime("created").Ticks > enabled.GetDateTime("created").Ticks;
            }
            else if (enabled != null)
            {
                enable = true;
            }
            Enable(enable);

            String desc = String.Format("Initialised pump {0} ... pump is {1}", ID, Enabled ? "enabled" : "disabled");
            erdb.LogEvent(EngineRoomServiceDB.LogEventType.INITIALISE, ID, desc);
        }

        public void LogState(EngineRoomServiceDB erdb)
        {
            if (!Enabled) return;

            String desc = IsOn ? String.Format("On @ {0} for {1} secs", LastOn, DateTime.Now.Subtract(LastOn).TotalSeconds) : String.Empty;
            erdb.LogState(ID, "Pump On", State, desc);
        }

        public void Monitor(EngineRoomServiceDB erdb, List<Message> messages, bool returnEventsOnly)
        {
            if (!Enabled) return;

            EngineRoomServiceDB.LogEventType let = EngineRoomServiceDB.LogEventType.INFO;
            String desc = null;
            Message msg = null;

            PumpState pumpState = StateOfPump;
            if(IsOn && DateTime.Now.Subtract(LastOn).TotalSeconds > MaxOnDuration)
            {
                StateOfPump = PumpState.ON_TOO_LONG;
            }
            else
            {
                StateOfPump = IsOn ? PumpState.ON : PumpState.OFF;
            }

            switch (StateOfPump)
            {
                case PumpState.ON_TOO_LONG:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Pump is: {0}", StateOfPump);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;

                case PumpState.ON:
                case PumpState.OFF:
                    let = StateOfPump == PumpState.ON ? EngineRoomServiceDB.LogEventType.ON: EngineRoomServiceDB.LogEventType.OFF;
                    desc = String.Format("Pump is: {0}", StateOfPump);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;
            }
            if (msg != null && (pumpState != StateOfPump || !returnEventsOnly))
            {
                messages.Add(msg);
                if (returnEventsOnly) erdb.LogEvent(let, ID, desc);
            }
        }
    } //end pump
}
