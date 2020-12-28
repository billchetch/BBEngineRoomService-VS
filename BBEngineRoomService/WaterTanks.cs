using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino;
using Chetch.Arduino.Devices.RangeFinders;
using Chetch.Messaging;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class WaterTanks : Chetch.Arduino.DeviceGroups.FluidTanks, IMonitorable
    {
        
        private DateTime _initialisedAt;

        public WaterTanks() : base("wts") { }

        public void Initialise(EngineRoomServiceDB erdb)
        {
            DBRow enabled = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ENABLE, ID);
            DBRow disabled = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.DISABLE, ID);

            bool enable = true;
            if (disabled != null)
            {
                enable = enabled == null ? false : enabled.GetDateTime("created").Ticks > disabled.GetDateTime("created").Ticks;
            }
            else if (enabled != null)
            {
                enable = true;
            }
            Enable(enable);

            _initialisedAt = DateTime.Now;

            String desc = String.Format("Initialised {0} water tanks ... tanks are {1}", Tanks.Count, Enabled ? "enabled" : "disabled");
            erdb.LogEvent(EngineRoomServiceDB.LogEventType.INITIALISE, ID, desc);
        }

        public void Monitor(EngineRoomServiceDB erdb, List<Message> messages, bool returnEventsOnly)
        {
            //if not enabled OR it has only just been initialised then don't monitor ... the distance sensor device needs time to build an accurate reading
            if (!Enabled || DateTime.Now.Subtract(_initialisedAt).TotalSeconds < 45) return;

            Message msg = null;
            String desc = null;
            EngineRoomServiceDB.LogEventType let;

            FluidLevel waterLevel = Level; //old level
            Level = FluidTank.GetFluidLevel(PercentFull); //current level
            bool fillingUp = waterLevel < Level;
            switch (Level)
            {
                case FluidLevel.VERY_LOW:
                    let = fillingUp ? EngineRoomServiceDB.LogEventType.INFO : EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Water Level: {0} @ {1}% and {2}L remaining", Level, PercentFull, Remaining);
                    BBAlarmsService.AlarmState alarmState = fillingUp ? BBAlarmsService.AlarmState.OFF : BBAlarmsService.AlarmState.MODERATE;
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(ID, alarmState, desc);
                    break;
                case FluidLevel.EMPTY:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Water Level: {0} @ {1}% and {2}L remaining", Level, PercentFull, Remaining);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;
                default:
                    let = EngineRoomServiceDB.LogEventType.INFO;
                    desc = String.Format("Water Level: {0} @ {1}% and {2}L remaining", Level, PercentFull, Remaining);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;
            }

            if (msg != null && (waterLevel != Level || !returnEventsOnly))
            {
                messages.Add(msg);
                if (returnEventsOnly) erdb.LogEvent(let, ID, desc);
            }

        }

        public void LogState(EngineRoomServiceDB erdb)
        {
            if (Tanks.Count == 0 || !Enabled || DateTime.Now.Subtract(_initialisedAt).TotalSeconds < 45) return;

            String desc;
            foreach (FluidTank ft in Tanks)
            {
                desc = String.Format("Water tank {0} is {1}% full (distance = {2}) and has {3}L remaining ... level is {4}", ft.ID, ft.PercentFull, ft.AverageDistance, ft.Remaining, ft.Level);
                erdb.LogState(ft.ID, "Water Tanks", ft.PercentFull, desc);
            }

            desc = String.Format("Remaining water @ {0}%, {1}L ... level is {2}", PercentFull, Remaining, Level);
            erdb.LogState(ID, "Water Tanks", PercentFull, desc);
        }
    }
}
