using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino.Devices.Counters;
using Chetch.Arduino.Devices.Temperature;
using Chetch.Arduino.Devices;
using Chetch.Arduino;

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

        private bool _running;
        public bool Running
        {
            get { return _running; }
            set
            {
                _running = value;
                if (_running)
                {
                    LastOn = DateTime.Now;
                } else
                {
                    LastOff = DateTime.Now;
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
