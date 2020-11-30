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
    public class WaterTanks : Chetch.Arduino.ArduinoDeviceGroup, IMonitorable
    {
        public enum WaterLevel
        {
            EMPTY,
            VERY_LOW,
            LOW,
            OK,
            FULL
        }

        public class WaterTank : JSN_SR04T
        {
            public static WaterLevel GetWaterLevel(int percentFull)
            {
                if (percentFull <= PERCENTAGE_PRECISION)
                {
                    return WaterLevel.EMPTY;
                }
                else if (percentFull <= 2 * PERCENTAGE_PRECISION)
                {
                    return WaterLevel.VERY_LOW;
                }
                else if (percentFull <= 4 * PERCENTAGE_PRECISION)
                {
                    return WaterLevel.LOW;
                }
                else if (percentFull <= 100 - PERCENTAGE_PRECISION)
                {
                    return WaterLevel.OK;
                }
                else
                {
                    return WaterLevel.FULL;
                }
            }

            public int Capacity { get; set; } = 0; //Capacity in L

            public int PercentFull 
            { 
                get
                {
                    return 100 - ((int)Math.Round(AveragePercentage / (double)PERCENTAGE_PRECISION) * PERCENTAGE_PRECISION);
                } 
            }

            public int Remaining
            {
                get
                {
                    return (int)(((double)PercentFull / 100.0) * Capacity);
                }
            }

            public WaterLevel Level
            {
                get
                {
                    return GetWaterLevel(PercentFull);
                }
            }

            public WaterTank(int transmitPin, int receivePin, String id) : base(transmitPin, receivePin, id) { }
        
            
        }

        public const int PERCENTAGE_PRECISION = 5;
        public const int DEFAULT_SAMPLE_INTERVAL = 10000;
        public const int DEFAULT_SAMPLE_SIZE = 12;

        public int SampleInterval { get; set; } = DEFAULT_SAMPLE_INTERVAL;
        public int SampleSize { get; set; } = DEFAULT_SAMPLE_SIZE;

        public List<WaterTank> Tanks { get; } = new List<WaterTank>();

        public int PercentFull
        {
            get
            {
                if (Tanks.Count == 0) return 0;
                double percentFull = 100.0 * ((double)Remaining / (double)Capacity);
                return ((int)Math.Round(percentFull / (double)PERCENTAGE_PRECISION) * PERCENTAGE_PRECISION);
            }
        }

        public int Remaining
        {
            get
            {
                int totalRemaining = 0;
                foreach (var wt in Tanks)
                {
                    totalRemaining += wt.Remaining;
                }
                return totalRemaining;
            }
        }

        public int Capacity
        {
            get
            {
                int totalCapacity = 0;
                foreach (var wt in Tanks)
                {
                    totalCapacity += wt.Capacity;
                }
                return totalCapacity;
            }
        }

        public WaterLevel Level { get; set; }

        private DateTime _initialisedAt;

        public WaterTanks() : base("wts", null) { }

        public WaterTank AddTank(String id, int transmitPin, int receivePin, int capacity, int minDistance = JSN_SR04T.MIN_DISTANCE, int maxDistance = JSN_SR04T.MAX_DISTANCE)
        {
            WaterTank wt = new WaterTank(transmitPin, receivePin, id);
            wt.Capacity = capacity;
            wt.MinDistance = minDistance;
            wt.MaxDistance = maxDistance;
            wt.Offset = 3;

            wt.SampleInterval = SampleInterval;
            wt.SampleSize = SampleSize;

            Tanks.Add(wt);
            AddDevice(wt);

            return wt;
        }

        public void Initialise(EngineRoomServiceDB erdb)
        {
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

            WaterLevel waterLevel = Level;
            Level = WaterTank.GetWaterLevel(PercentFull);
            switch(Level)
            {
                case WaterLevel.VERY_LOW:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Water Level: {0} @ {1}% and {2}L remaining", Level, PercentFull, Remaining);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(ID, BBAlarmsService.AlarmState.MODERATE, desc);
                    break;
                case WaterLevel.EMPTY:
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
            foreach (WaterTank wt in Tanks)
            {
                desc = String.Format("Water tank {0} is {1}% full (distance = {2}) and has {3}L remaining ... level is {4}", wt.ID, wt.PercentFull, wt.AverageDistance, wt.Remaining, wt.Level);
                erdb.LogState(wt.ID, "Water Tanks", wt.PercentFull, desc);
            }

            desc = String.Format("Remaining water @ {0}%, {1}L ... level is {2}", PercentFull, Remaining, Level);
            erdb.LogState(ID, "Water Tanks", PercentFull, desc);
        }
    }
}
