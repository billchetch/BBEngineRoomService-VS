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
        public enum OilState
        {
            NORMAL,
            LEAK,
            SENSOR_FAULT
        }
        public const int IS_RUNNING_RPM_THRESHOLD = 100;

        private bool _online = true;
        public bool Online
        {
            get { return _online; }
            set
            {
                Running = false;
                _online = value;
            }
        }

        private bool _running;
        public bool Running
        {
            get { return _running; }
            set
            {
                if (_running != value)
                {
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
        public SwitchSensor OilSensor { get; internal set; }
        public DS18B20Array.DS18B20Sensor TempSensor { get; internal set; }
        public DateTime LastOn { get; set; }
        public DateTime LastOff { get; set; }

        public Engine(String id, RPMCounter rpm, SwitchSensor oilSensor, DS18B20Array.DS18B20Sensor tempSensor) : base(id, null)
        {
            RPM = rpm;
            OilSensor = oilSensor;
            TempSensor = tempSensor;
            AddDevice(RPM);
            AddDevice(OilSensor);
        }

        public void initialise(EngineRoomServiceDB erdb)
        {
            DBRow row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, ID);
            if (row != null) LastOn = row.GetDateTime("created");
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);
            if (row != null) LastOff = row.GetDateTime("created");
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);

            DBRow offline = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFFLINE, ID);
            DBRow online = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ONLINE, ID);

            if(offline != null)
            {
                Online = online == null ? false : online.GetDateTime("created").Ticks > offline.GetDateTime("created").Ticks;
            }
        }

        public OilState CheckOil()
        {
            if (Running)
            {
                return OilSensor.IsOn ? OilState.NORMAL : OilState.LEAK;
            }
            else
            {

                return OilSensor.IsOff ? OilState.NORMAL : OilState.SENSOR_FAULT;
            }
        }
    }
}
