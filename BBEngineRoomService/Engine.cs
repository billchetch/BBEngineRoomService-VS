using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2.Devices;
using Chetch.Arduino2.Devices.Temperature;
using Chetch.Arduino2;
using Chetch.Messaging;
using Chetch.Database;
using BBAlarmsService;
using System.Reflection;

namespace BBEngineRoomService
{
    public class Engine : ArduinoDeviceGroup, AlarmManager.IAlarmRaiser
    {

        public class RPMCounter : Counter
        {
            public RPMCounter(String id, byte pinNumber) : base(id, pinNumber, InterruptMode.RISING)
            {
                //Tolerance = 1000; //in micros .. means don't count fluctuations occuring in lntervals less than this
            }

            /*public int RPM
            {
                get
                {
                    return (int)Math.Round(IntervalsPerSecond * 60);
                }
            }*/

            //for testing purposes (replace with above later)
            private int _trpm = 0;

            public int RPM { get { return _trpm; }
                set {
                    _trpm = value;
                    
                } } 
        }

        public class OilSensorSwitch : SwitchDevice
        {
            public OilSensorSwitch(String id, byte pinNumber) : base(id, SwitchMode.PASSIVE, pinNumber, SwitchPosition.ON, 100) { }
        } //end oil sensor

        
        public enum OilPressureState
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
        public const int RUNNING_FOR_THRESHOLD = 5; //in seconds
        public const int STOPPED_RUNNING_FOR_THRESHOLD = 5; //in seconds

        public AlarmManager AlarmManager { get; set; }

        private bool _running = false;
        public bool Running
        {
            get { return _running; }
            set
            {
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

                    //wait a certain time and check oil pressure
                    int delay = ((_running ? RUNNING_FOR_THRESHOLD : STOPPED_RUNNING_FOR_THRESHOLD) * 1000) + 500;
                    Task.Delay(delay).ContinueWith(_ =>
                    {
                        checkOilPressure(); //this is a delayed method see method body
                    });
                }
            }
        }

        public bool IsRunning => Running;

        private RPMState _rpm = RPMState.OFF;

        public RPMState RPM
        {
            get { return _rpm;  }
            set
            {
                _rpm = value;
                String alarmID = RPMSensor.ID;
                switch (_rpm)
                {
                    case RPMState.TOO_FAST:
                        AlarmManager?.Raise(alarmID, AlarmState.SEVERE, "Engine is running too fast");
                        break;
                    case RPMState.SLOW:
                        AlarmManager?.Raise(alarmID, AlarmState.MODERATE, "Engine is running slow");
                        break;
                    default:
                        AlarmManager?.Lower(alarmID, "Engine is running at an acceptable RPM");
                        break;
                }
            }
        }

        private OilPressureState _oilPressure = OilPressureState.OK_ENGINE_OFF;
        public OilPressureState OilPressure
        {
            get { return _oilPressure; }
            set 
            { 
                _oilPressure = value;
                String alarmID = OilSensor.ID;
                switch (_oilPressure)
                {
                    case OilPressureState.OK_ENGINE_OFF:
                    case OilPressureState.OK_ENGINE_ON:
                        AlarmManager?.Lower(alarmID, "balik ke normal dong");
                        break;
                    case OilPressureState.NO_PRESSURE:
                        AlarmManager?.Raise(alarmID, AlarmState.CRITICAL, "Parah ini");
                        break;
                    case OilPressureState.SENSOR_FAULT:
                        AlarmManager?.Raise(alarmID, AlarmState.MODERATE, "kok sensor fautlnya");
                        break;
                }
            }
        }

        public RPMCounter RPMSensor { get; internal set; }

        public OilSensorSwitch OilSensor { get; internal set; }

        //public DS18B20Array.DS18B20Sensor TempSensor { get; internal set; }

        public DateTime LastOn { get; set; }
        public DateTime LastOff { get; set; } 

        public int RunningFor
        {
            get
            {
                return IsRunning ? (int)(DateTime.Now - LastOn).TotalSeconds : 0;
            }
        }

        public int StoppedRunningFor
        {
            get
            {
                if (IsRunning)
                {
                    return 0;
                } else
                {
                    return LastOff == default(DateTime) ? STOPPED_RUNNING_FOR_THRESHOLD : (int)(DateTime.Now - LastOff).TotalSeconds;
                }
            }
        }

        public Engine(String id, byte rpmPin, byte oilSensorPin, byte tempSensorPin) : base(id, null)
        {

            RPMSensor = new RPMCounter(CreateDeviceID("rpm"), rpmPin);
            RPMSensor.ReportInterval = 2000;
            RPMSensor.Tolerance = 0;
            RPMSensor.DataReceived += (Object sender, MessageReceivedArgs ea) =>
            {
                monitorRPM();
            };

            OilSensor = new OilSensorSwitch(CreateDeviceID("oil"), oilSensorPin);
            OilSensor.Tolerance = 100; //designed to prevent bounce
            OilSensor.Switched += (Object sender, SwitchDevice.SwitchPosition pos) =>
            {
                checkOilPressure();
            };

            AddDevice(RPMSensor);
            AddDevice(OilSensor);
        }


        //TODO: change to private
        public void monitorRPM()
        {
            Running = RPMSensor.RPM >= IS_RUNNING_RPM_THRESHOLD;
            if (Running)
            {
                if (RunningFor >= RUNNING_FOR_THRESHOLD)
                {
                    if(RPMSensor.RPM > 2500)
                    {
                        RPM = RPMState.TOO_FAST;
                    }
                    else if(RPMSensor.RPM > 2000)
                    {
                        RPM = RPMState.FAST;
                    } 
                    else if(RPMSensor.RPM > 1000)
                    {
                        RPM = RPMState.NORMAL;
                    }
                    else
                    {
                        RPM = RPMState.SLOW;
                    }
                }
            } else
            {
                RPM = RPMState.OFF;
            }
        }


        //TODO: change to private
        public void checkOilPressure()
        {

            if(IsRunning && OilSensor.IsOff) //if running for however long and oil sensor is off then that's correct
            {
                OilPressure = OilPressureState.OK_ENGINE_ON;
            } 
            else if(RunningFor >= RUNNING_FOR_THRESHOLD && OilSensor.IsOn) //if running for a while and oll esnsor is on whoa danger
            {
                OilPressure = OilPressureState.NO_PRESSURE;
            }
            else if (!IsRunning && StoppedRunningFor >= STOPPED_RUNNING_FOR_THRESHOLD)
            {
                if (OilSensor.IsOn) //if stopped for a while and oil sensor is on then correct
                {
                    OilPressure = OilPressureState.OK_ENGINE_OFF;
                }
                else //but if the oil sensor is off then this suggests a sensor fault
                {
                    OilPressure = OilPressureState.SENSOR_FAULT;
                }
            }
        }

        protected override void HandleDevicePropertyChange(ArduinoDevice device, PropertyInfo property)
        {
            //throw new NotImplementedException();
        }

        public void RegisterAlarms()
        {
            AlarmManager.RegisterAlarm(this, RPMSensor.ID);
            AlarmManager.RegisterAlarm(this, OilSensor.ID);
            //AlarmManager.RegisterAlarm(this, ID + "_tmp");
        }

        public void RequestUpdateAlarms()
        {
            OilSensor.RequestStatus();
        }
    }
}
