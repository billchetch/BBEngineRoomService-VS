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
        public RPMState StateOfRPM { get; internal set; } = RPMState.OFF;

        

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
            DBRow row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, ID); 
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

            EngineRoomServiceDB.LogEventType let = EngineRoomServiceDB.LogEventType.INFO;
            String desc = null;
            Message msg = null;
            //see if engine is Running (and log)
            bool running = Running; // record to test change of state
            Running = RPM.RPM > IS_RUNNING_RPM_THRESHOLD;

            if(running != Running) //log
            {
                let = Running ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF;
                erdb.LogEvent(let, ID, String.Format("RPM = {0}", RPM.RPM));
            }

            //some useful durations...
            long secsSinceLastOn = LastOn == default(DateTime) ? -1 : (long)DateTime.Now.Subtract(LastOn).TotalSeconds;
            long secsSinceLastOff = LastOff == default(DateTime) ? -1 : (long)DateTime.Now.Subtract(LastOff).TotalSeconds;

            //Oil State
            //Problems arise because of the dependence of the oil state upon the state of the Engine.Running property
            //as well as the sensor state.  Both of these are independent to some degree so are not guaranteed to be in sync in a short time frame.
            //Hence we ask for the current state and see if it matches the previous recorded state to ensure some stability
            //Only if it is consistent over this period of time do we update the StateOfOil propery, log and produce messages/alerts
            OilState oilState = StateOfOil;
            if (Running && secsSinceLastOn > 5)
            {
                oilState = OilSensor.IsOn ? OilState.NO_PRESSURE : OilState.OK_ENGINE_ON;
            }
            else if (!Running && secsSinceLastOff > 5)
            {
                oilState = OilSensor.IsOff ? OilState.SENSOR_FAULT : OilState.OK_ENGINE_OFF;
            }

            if(oilState == _prevOilState)
            {
                _oilStateStableCount = Math.Min(_oilStateStableCount + 1, 5);
            } else
            {
                _oilStateStableCount = 0;
            }
            _prevOilState = oilState;

            msg = null;
            if (_oilStateStableCount >= 5) //this condition is to ensure that the state is 'stable'
            {  
                bool isEvent = oilState != StateOfOil;
                StateOfOil = oilState;
                switch (StateOfOil)
                {
                    case OilState.NO_PRESSURE:
                        let = EngineRoomServiceDB.LogEventType.WARNING;
                        desc = String.Format("Engine {0} Oil sensor: {1}", Running ? "running for " + secsSinceLastOn : "off for " + secsSinceLastOff, StateOfOil);
                        msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
                        break;

                    case OilState.SENSOR_FAULT:
                        let = EngineRoomServiceDB.LogEventType.WARNING;
                        desc = String.Format("Engine {0} Oil sensor: {1}", Running ? "running for " + secsSinceLastOn : "off for " + secsSinceLastOff, StateOfOil);
                        msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.MODERATE, desc);
                        break;

                    case OilState.OK_ENGINE_OFF:
                    case OilState.OK_ENGINE_ON:
                        let = EngineRoomServiceDB.LogEventType.INFO;
                        desc = String.Format("Engine {0} Oil sensor: {1}", Running ? "running for " + secsSinceLastOn : "off for " + secsSinceLastOff, StateOfOil);
                        msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
                        break;
                }

                if (msg != null && (isEvent || !returnEventsOnly))
                {
                    messages.Add(msg);
                    if (returnEventsOnly) erdb.LogEvent(let, OilSensor.ID, desc);
                }
            }


            //Temp state
            msg = null;
            TemperatureState tempState = StateOfTemperature; //keep a record
            if (!Running || TempSensor.AverageTemperature <= 50)
            {
                StateOfTemperature = TemperatureState.OK;
            }
            else if (TempSensor.AverageTemperature <= 65)
            {
                StateOfTemperature = TemperatureState.HOT;
            }
            else
            {
                StateOfTemperature = TemperatureState.TOO_HOT;
            }
            switch (StateOfTemperature)
            {
                case TemperatureState.TOO_HOT:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.CRITICAL, desc);
                    break;
                case TemperatureState.HOT:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;
                case TemperatureState.OK:
                    let = EngineRoomServiceDB.LogEventType.INFO;
                    desc = String.Format("Temp sensor: {0} {1}", TempSensor.AverageTemperature, StateOfTemperature);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;
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
            } else if (secsSinceLastOn > 10) {
                if (RPM.AverageRPM < 500)
                {
                    StateOfRPM = RPMState.SLOW;
                }
                else if (RPM.AverageRPM < 1650)
                {
                    StateOfRPM = RPMState.NORMAL;
                }
                else if (RPM.AverageRPM < 1800)
                {
                    StateOfRPM = RPMState.FAST;
                }
                else
                {
                    StateOfRPM = RPMState.TOO_FAST;
                }
            }
            switch (StateOfRPM)
            {
                case RPMState.OFF:
                case RPMState.SLOW:
                case RPMState.NORMAL:
                    let = EngineRoomServiceDB.LogEventType.INFO;
                    desc = String.Format("RPM (Instant/Average): {0}/{1} gives state {2}", RPM.RPM, RPM.AverageRPM, StateOfRPM);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.OFF, desc);
                    break;

                case RPMState.FAST:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("RPM (Instant/Average): {0}/{1} gives state {2}", RPM.RPM, RPM.AverageRPM, StateOfRPM);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.MODERATE, desc);
                    break;

                case RPMState.TOO_FAST:
                    let = EngineRoomServiceDB.LogEventType.WARNING;
                    desc = String.Format("RPM (Instant/Average): {0}/{1} gives state {2}", RPM.RPM, RPM.AverageRPM, StateOfRPM);
                    msg = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, desc);
                    break;
            }
            if (msg != null && (rpmState != StateOfRPM || !returnEventsOnly))
            {
                messages.Add(msg);
                if (returnEventsOnly) erdb.LogEvent(let, RPM.ID, desc);
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
