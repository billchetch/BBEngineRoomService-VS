using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino.Devices.Counters;
using Chetch.Arduino.Devices.Temperature;
using Chetch.Arduino.Devices;
using Chetch.Arduino;
using Chetch.Messaging;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class Engine : ArduinoDeviceGroup, IMonitorable
    {
        public class OilSensorSwitch : SwitchSensor
        {
            public const String SENSOR_NAME = "OIL";

            public OilSensorSwitch(int pinNumber, String id) : base(pinNumber, 500, id, SENSOR_NAME) { }
        } //end oil sensor

        public enum OilState
        {
            OK_ENGINE_ON,
            OK_ENGINE_OFF,
            NO_PRESSURE,
            SENSOR_FAULT
        }

        public enum TemperatureState
        {
            OK,
            HOT,
            TOO_HOT
        }

        public enum RPMState
        {
            OFF,
            SLOW,
            NORMAL,
            FAST,
            TOO_FAST
        }

        public const int IS_RUNNING_RPM_THRESHOLD = 250;

        private bool _running = false;
        public bool Running
        {
            get { return Enabled ? _running : false; }
            set
            {
                if (!Enabled) return;

                if (_running != value)
                {
                    _running = value;
                    if (_running)
                    {
                        LastOn = DateTime.Now;
                    }
                    else
                    {
                        LastOff = DateTime.Now;
                    }
                }
            }
        }
        public RPMCounter RPM { get; internal set; }
        public OilSensorSwitch OilSensor { get; internal set; }
        public DS18B20Array.DS18B20Sensor TempSensor { get; internal set; }
        public DateTime LastOn { get; set; }
        public DateTime LastOff { get; set; }

        //states
        public OilState StateOfOil { get; internal set; } = OilState.OK_ENGINE_OFF;
        private int _oilStateStableCount = 0;
        private OilState _prevOilState = OilState.OK_ENGINE_OFF;

        public TemperatureState StateOfTemperature { get; internal set; } = TemperatureState.OK;
        public Dictionary<TemperatureState, int> TemperatureThresholds { get; internal set; } = new Dictionary<TemperatureState, int>();

        public RPMState StateOfRPM { get; internal set; } = RPMState.OFF;
        public Dictionary<RPMState, int> RPMThresholds { get; internal set; } = new Dictionary<RPMState, int>();


        public Engine(String id, RPMCounter rpm, OilSensorSwitch oilSensor, DS18B20Array.DS18B20Sensor tempSensor) : base(id, null)
        {
            RPM = rpm;
            OilSensor = oilSensor;
            TempSensor = tempSensor;
            AddDevice(RPM);
            AddDevice(OilSensor);

            SetTempertureThresholds(50, 65);
            SetRPMThresholds(500, 1650, 1800);
        }

        public void SetTempertureThresholds(int thresholdOk, int thresholdHot)
        {
            TemperatureThresholds[TemperatureState.OK] = thresholdOk;
            TemperatureThresholds[TemperatureState.HOT] = thresholdHot; 
        }

        public void SetRPMThresholds(int thresholdSlow, int thresholdNormal, int thresholdFast)
        {
            RPMThresholds[RPMState.SLOW] = thresholdSlow;
            RPMThresholds[RPMState.NORMAL] = thresholdNormal;
            RPMThresholds[RPMState.FAST] = thresholdFast;
        }

        public void Initialise(EngineRoomServiceDB erdb)
        {
            DBRow row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, ID); 
            if (row != null) LastOn = row.GetDateTime("created");
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);
            if (row != null) LastOff = row.GetDateTime("created");

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

            String desc = String.Format("Initialised engine {0} ... engine is {1}", ID, Enabled ? "enabled" : "disabled");
            erdb.LogEvent(EngineRoomServiceDB.LogEventType.INITIALISE, ID, desc);
        }

        public void Monitor(EngineRoomServiceDB erdb, List<Message> messages, bool returnEventsOnly)
        {
            if (!Enabled) return;

            EngineRoomServiceDB.LogEventType let = EngineRoomServiceDB.LogEventType.INFO;
            String desc = null;
            Message msg = null;

            //see if engine is Running (and log)
            bool running = Running; // record to test change of state
            Running = RPM.AverageRPM > IS_RUNNING_RPM_THRESHOLD;
            bool isEvent = running != Running;

            //Running or not
            if(isEvent) //log
            {
                if (Running)
                {
                    erdb.LogEvent(EngineRoomServiceDB.LogEventType.ON, ID, String.Format("Current RPM = {0}", RPM.RPM));
                    LastOn = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, ID).GetDateTime("created");
                } else
                {
                    desc = LastOn == default(DateTime) ? "N/A" : String.Format("Running for {0}", (DateTime.Now - LastOn).ToString("c"));
                    erdb.LogEvent(EngineRoomServiceDB.LogEventType.OFF, ID, desc);
                    LastOff = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID).GetDateTime("created");
                }
            }

            if (isEvent || !returnEventsOnly)
            {
                EngineRoomMessageSchema schema = new EngineRoomMessageSchema(new Message(MessageType.DATA));
                schema.AddEngine(this);
                messages.Add(schema.Message);
            }


            //some useful durations...
            long secsSinceLastOn = LastOn == default(DateTime) ? -1 : (long)DateTime.Now.Subtract(LastOn).TotalSeconds;
            long secsSinceLastOff = LastOff == default(DateTime) ? -1 : (long)DateTime.Now.Subtract(LastOff).TotalSeconds;

            //Oil State
            OilState oilState = StateOfOil;
            if (Running && RPM.RPM > 0 && secsSinceLastOn > 30)
            {
                oilState = OilSensor.IsOn ? OilState.NO_PRESSURE : OilState.OK_ENGINE_ON;
            }
            else if (!Running && RPM.RPM == 0 && secsSinceLastOff > 30)
            {
                oilState = OilSensor.IsOff ? OilState.SENSOR_FAULT : OilState.OK_ENGINE_OFF;
            }

            msg = null;
            isEvent = oilState != StateOfOil;
            StateOfOil = oilState;
            switch (StateOfOil)
            {
                case OilState.NO_PRESSURE:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Engine {0} Oil sensor {1} gives {2}", Running ? "running for " + secsSinceLastOn : "off for " + secsSinceLastOff, OilSensor.State, StateOfOil);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;

                case OilState.SENSOR_FAULT:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Engine {0} Oil sensor {1} gives {2}", Running ? "running for " + secsSinceLastOn : "off for " + secsSinceLastOff, OilSensor.State, StateOfOil);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.MODERATE, desc);
                    break;

                case OilState.OK_ENGINE_OFF:
                case OilState.OK_ENGINE_ON:
                    let = EngineRoomServiceDB.LogEventType.INFO;
                    desc = String.Format("Engine {0} Oil sensor {1} gives {2}", Running ? "running for " + secsSinceLastOn : "off for " + secsSinceLastOff, OilSensor.State, StateOfOil);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;
            }

            if (msg != null && (isEvent || !returnEventsOnly))
            {
                messages.Add(msg);
                if (isEvent) erdb.LogEvent(let, OilSensor.ID, desc);
            }


            //Temp state
            TemperatureState tempState = StateOfTemperature; //keep a record
            if (!Running || (TempSensor.AverageTemperature <= TemperatureThresholds[TemperatureState.OK]))
            {
                StateOfTemperature = TemperatureState.OK;
            }
            else if (TempSensor.AverageTemperature <= TemperatureThresholds[TemperatureState.HOT])
            {
                StateOfTemperature = TemperatureState.HOT;
            }
            else
            {
                StateOfTemperature = TemperatureState.TOO_HOT;
            }

            msg = null;
            isEvent = tempState != StateOfTemperature;
            switch (StateOfTemperature)
            {
                case TemperatureState.TOO_HOT:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(TempSensor.ID, BBAlarmsService.AlarmState.CRITICAL, desc);
                    break;
                case TemperatureState.HOT:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(TempSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;
                case TemperatureState.OK:
                    let = EngineRoomServiceDB.LogEventType.INFO;
                    desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(TempSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;
            }
            if (msg != null && (isEvent || !returnEventsOnly))
            {
                messages.Add(msg);
                if (isEvent) erdb.LogEvent(let, TempSensor.ID, desc);
            }

            //RPM state
            RPMState rpmState = StateOfRPM;
            if (!Running)
            {
                StateOfRPM = RPMState.OFF;
            } else if (secsSinceLastOn > 10) {
                if (RPM.AverageRPM < RPMThresholds[RPMState.SLOW])
                {
                    StateOfRPM = RPMState.SLOW;
                }
                else if (RPM.AverageRPM < RPMThresholds[RPMState.NORMAL])
                {
                    StateOfRPM = RPMState.NORMAL;
                }
                else if (RPM.AverageRPM < RPMThresholds[RPMState.FAST])
                {
                    StateOfRPM = RPMState.FAST;
                }
                else
                {
                    StateOfRPM = RPMState.TOO_FAST;
                }
            }

            msg = null;
            isEvent = rpmState != StateOfRPM;
            switch (StateOfRPM)
            {
                case RPMState.OFF:
                case RPMState.SLOW:
                case RPMState.NORMAL:
                    let = EngineRoomServiceDB.LogEventType.INFO;
                    desc = String.Format("RPM (Instant/Average): {0}/{1} gives state {2}", RPM.RPM, RPM.AverageRPM, StateOfRPM);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(RPM.ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;

                case RPMState.FAST:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("RPM (Instant/Average): {0}/{1} gives state {2}", RPM.RPM, RPM.AverageRPM, StateOfRPM);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(RPM.ID, BBAlarmsService.AlarmState.MODERATE, desc);
                    break;

                case RPMState.TOO_FAST:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("RPM (Instant/Average): {0}/{1} gives state {2}", RPM.RPM, RPM.AverageRPM, StateOfRPM);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(RPM.ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;
            }
            if (msg != null && (isEvent || !returnEventsOnly))
            {
                messages.Add(msg);
                if (isEvent) erdb.LogEvent(let, RPM.ID, desc);
            }
        }

        public void LogState(EngineRoomServiceDB erdb)
        {
            if (!Enabled) return;

            erdb.LogState(ID, "Running", Running);
            if (RPM != null)
            {
                erdb.LogState(ID, "RPM", RPM.RPM);
                erdb.LogState(ID, "RPM Average", RPM.AverageRPM);
            }
            if (OilSensor != null) erdb.LogState(ID, "OilSensor", OilSensor.State);
            if (TempSensor != null)
            {
                erdb.LogState(ID, "Temperature", TempSensor.Temperature);
                erdb.LogState(ID, "Temperature Average", TempSensor.AverageTemperature);
            }
        }
    }
}
