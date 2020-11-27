using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino.Devices.Counters;
using Chetch.Arduino.Devices.Temperature;
using Chetch.Arduino.Devices;
using Chetch.Arduino;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class Engine : ArduinoDeviceGroup
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

        public Engine(String id, RPMCounter rpm, OilSensorSwitch oilSensor, DS18B20Array.DS18B20Sensor tempSensor) : base(id, null)
        {
            RPM = rpm;
            OilSensor = oilSensor;
            TempSensor = tempSensor;
            AddDevice(RPM);
            AddDevice(OilSensor);
        }

        public void initialise(EngineRoomServiceDB erdb)
        {
            DBRow row = erdb.GetFirstOnAfterLastOff(ID); //this is to allow for board resets (which will naturally create an ON event if it happens while engine is running
            if (row != null) LastOn = row.GetDateTime("created");
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);
            if (row != null) LastOff = row.GetDateTime("created");
            
            DBRow enabled = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ENABLE, ID);
            DBRow disabled = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.DISABLE, ID);

            bool enable = true;
            if(disabled != null)
            {
                enable = enabled == null ? false : enabled.GetDateTime("created").Ticks > enabled.GetDateTime("created").Ticks;
            } else if(enabled != null)
            {
                enable = true;
            }
            Enable(enable);
        }

        public OilState CheckOil()
        {
            if (Running && OilSensor.IsOn)
            {
                return OilState.NO_PRESSURE;
            }
            else if (!Running && OilSensor.IsOff)
            {
                return OilState.SENSOR_FAULT;
            } else
            {
                return OilState.NORMAL;
            }
        }
    }
}
