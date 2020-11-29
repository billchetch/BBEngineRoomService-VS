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

            public OilSensorSwitch(int pinNumber, String id) : base(pinNumber, 250, id, SENSOR_NAME) { }
        } //end oil sensor

        public enum OilState
        {
            NORMAL,
            NO_PRESSURE,
            SENSOR_FAULT
        }

        public enum TemperatureState
        {
            NORMAL,
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

        public const int IS_RUNNING_RPM_THRESHOLD = 100;

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
        OilState StateOfOil = OilState.NORMAL;
        TemperatureState StateOfTemperature = TemperatureState.NORMAL;
        RPMState StateOfRPM = RPMState.OFF;

        public Engine(String id, RPMCounter rpm, OilSensorSwitch oilSensor, DS18B20Array.DS18B20Sensor tempSensor) : base(id, null)
        {
            RPM = rpm;
            OilSensor = oilSensor;
            TempSensor = tempSensor;
            AddDevice(RPM);
            AddDevice(OilSensor);
        }

        public void Initialise(EngineRoomServiceDB erdb)
        {
            DBRow row = erdb.GetFirstOnAfterLastOff(ID); //this is to allow for board resets (which will naturally create an ON event if it happens while engine is running
            if (row != null) LastOn = row.GetDateTime("created");
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);
            if (row != null) LastOff = row.GetDateTime("created");

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

            String desc = String.Format("Initialised engine {0} ... engine is {1}", ID, Enabled ? "enabled" : "disabled");
            erdb.LogEvent(EngineRoomServiceDB.LogEventType.INITIALISE, ID, desc);
        }

        public void Monitor(EngineRoomServiceDB erdb, List<Message> messages, bool returnEventsOnly)
        {
            if (!Enabled) return;

            EngineRoomServiceDB.LogEventType let;
            String desc;
            Message msg;

            //see if engine is Running (and log)
            bool running = Running; // record to test change of state
            Running = RPM.RPM > IS_RUNNING_RPM_THRESHOLD;

            if(running != Running) //log
            {
                let = Running ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF;
                erdb.LogEvent(let, ID);
            }

            //oil state
            msg = null;
            OilState oilState = StateOfOil;
            if (Running && DateTime.Now.Subtract(LastOn).TotalSeconds > 5 &&  OilSensor.IsOn)
            {
                StateOfOil = OilState.NO_PRESSURE;
                let = EngineRoomServiceDB.LogEventType.WARNING;
                desc = String.Format("Oil sensor: {0}", StateOfOil);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
            }
            else if (!Running && DateTime.Now.Subtract(LastOff).TotalSeconds > 5 && OilSensor.IsOff)
            {
                StateOfOil = OilState.SENSOR_FAULT;
                let = EngineRoomServiceDB.LogEventType.WARNING;
                desc = String.Format("Oil sensor: {0}", StateOfOil);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.MODERATE, desc);
            }
            else
            {
                StateOfOil = OilState.NORMAL;
                let = EngineRoomServiceDB.LogEventType.INFO;
                desc = String.Format("Oil sensor: {0}", StateOfOil);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
            }
            if (msg != null && (oilState != StateOfOil || !returnEventsOnly))
            {
                messages.Add(msg);
                if (returnEventsOnly) erdb.LogEvent(let, OilSensor.ID, desc);
            }

            //Temp state
            msg = null;
            TemperatureState tempState = StateOfTemperature;
            if (!Running || TempSensor.AverageTemperature <= 50)
            {
                StateOfTemperature = TemperatureState.NORMAL;
                let = EngineRoomServiceDB.LogEventType.INFO;

            }
            else if (TempSensor.AverageTemperature <= 65)
            {
                StateOfTemperature = TemperatureState.HOT;
                let = EngineRoomServiceDB.LogEventType.WARNING;
                desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
            }
            else
            {
                StateOfTemperature = TemperatureState.TOO_HOT;
                let = EngineRoomServiceDB.LogEventType.WARNING;
                desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.CRITICAL, desc);
            }
            if (msg != null && (tempState != StateOfTemperature || !returnEventsOnly))
            {
                messages.Add(msg);
                if (returnEventsOnly) erdb.LogEvent(let, TempSensor.ID, desc);
            }

            //RPM state
            msg = null;
            RPMState rpmState = StateOfRPM;
            if (!Running)
            {
                StateOfRPM = RPMState.OFF;
                let = EngineRoomServiceDB.LogEventType.INFO;
                desc = String.Format("RPM: {0} {1}", RPM.AverageRPM, StateOfRPM);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
            }
            else if (RPM.AverageRPM < 500)
            {
                StateOfRPM = RPMState.SLOW;
                let = EngineRoomServiceDB.LogEventType.INFO;
                desc = String.Format("RPM: {0} {1}", RPM.AverageRPM, StateOfRPM);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
            }
            else if (RPM.AverageRPM < 1600)
            {
                StateOfRPM = RPMState.NORMAL;
                let = EngineRoomServiceDB.LogEventType.INFO;
                desc = String.Format("RPM: {0} {1}", RPM.AverageRPM, StateOfRPM);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
            }
            else if (RPM.AverageRPM < 1800)
            {
                StateOfRPM = RPMState.FAST;
                let = EngineRoomServiceDB.LogEventType.WARNING;
                desc = String.Format("RPM: {0} {1}", RPM.AverageRPM, StateOfRPM);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.MODERATE, desc);
            }
            else
            {
                StateOfRPM = RPMState.TOO_FAST;
                let = EngineRoomServiceDB.LogEventType.INFO;
                desc = String.Format("RPM: {0} {1}", RPM.AverageRPM, StateOfRPM);
                msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.CRITICAL, desc);
            }
            if (msg != null && (rpmState != StateOfRPM || !returnEventsOnly))
            {
                messages.Add(msg);
                if(returnEventsOnly) erdb.LogEvent(let, RPM.ID, desc);
            }
        }

        public void LogState(EngineRoomServiceDB erdb)
        {
            if (!Enabled) return;

            erdb.LogState(ID, "Running", Running);
            if (RPM != null) erdb.LogState(ID, "RPM", RPM.AverageRPM);
            if (OilSensor != null) erdb.LogState(ID, "OilSensor", OilSensor.State);
            if (TempSensor != null) erdb.LogState(ID, "Temperature", TempSensor.Temperature);
        }
    }
}
