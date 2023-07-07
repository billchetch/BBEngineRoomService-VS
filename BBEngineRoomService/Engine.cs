using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2.Devices;
using Chetch.Arduino2.Devices.Temperature;
using Chetch.Arduino2;
using Chetch.Arduino2.Devices.Temperature;
using Chetch.Messaging;
using Chetch.Database;
using BBAlarmsService;
using System.Reflection;

namespace BBEngineRoomService
{
    public class Engine : ArduinoDeviceGroup, AlarmManager.IAlarmRaiser
    {
        //Device definitions
        public class RPMCounter : Counter
        {
            public double ConversionFactor { get; set; }  = 1.0;

            public RPMCounter(String id, byte pinNumber, InterruptMode mode) : base(id, pinNumber, mode)
            {
                //Tolerance = 1000; //in micros .. means don't count fluctuations occuring in lntervals less than this
            }

            public int RPM
            {
                get
                {
                    double r = IntervalsPerSecond * 60.0;
                    return (int)Math.Round(r * ConversionFactor);
                }
            }

            //for testing purposes (replace with above later)
            /*private int _trpm = 0;

            public int RPM { get { return _trpm; }
                set {
                    _trpm = value;
                    
                } } */
        }

        public class OilSensorSwitch : SwitchDevice
        {
            public OilSensorSwitch(String id, byte pinNumber) : base(id, SwitchMode.PASSIVE, pinNumber, SwitchPosition.OFF, 100) { }

            public bool DetectedPressure => IsOn;

        } //end oil sensor

        public enum EngineRPMState
        {
            OFF = 0,
            SLOW = 250,
            NORMAL = 1000,
            FAST = 2000,
            TOO_FAST = 2500,
        }

        //enums for states
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
            HOT = 32,
            TOO_HOT = 33,
        }

        //Constants
        public const int IS_RUNNING_RPM_THRESHOLD = 250;
        public const int RUNNING_FOR_THRESHOLD = 5; //in seconds
        public const int STOPPED_RUNNING_FOR_THRESHOLD = 5; //in seconds

        //Alarm manager
        public AlarmManager AlarmManager { get; set; }

        //Engine Components
        public RPMCounter RPMSensor { get; internal set; }

        public OilSensorSwitch OilSensor { get; internal set; }

        public TemperatureSensor TempSensor { get; internal set; }


        //Engine Properties
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
                    if (OilSensor.IsReady)
                    {
                        int delay = ((_running ? RUNNING_FOR_THRESHOLD : STOPPED_RUNNING_FOR_THRESHOLD) * 1000) + 500;
                        Task.Delay(delay).ContinueWith(_ =>
                        {
                            checkOilPressure(); //this is a delayed method see method body
                        });
                    }
                } //end check value change
            } //end get
        }

        public bool IsRunning => Running;


        private EngineRPMState _rpmState = EngineRPMState.OFF;
        public EngineRPMState RPMState
        {
            get { return _rpmState;  }
            set
            {
                _rpmState = value;
                String alarmID = RPMSensor.ID;
                switch (_rpmState)
                {
                    case EngineRPMState.TOO_FAST:
                        AlarmManager?.Raise(alarmID, AlarmState.SEVERE, String.Format("Engine {0} is running too fast @ {1} RPM", UID, RPM));
                        break;
                    case EngineRPMState.FAST:
                        AlarmManager?.Raise(alarmID, AlarmState.MODERATE, String.Format("Engine {0} is running fast @ {1} RPM", UID, RPM));
                        break;
                    case EngineRPMState.SLOW:
                        AlarmManager?.Raise(alarmID, AlarmState.MODERATE, String.Format("Engine {0} is running slow @ {1} RPM", UID, RPM));
                        break;
                    default:
                        AlarmManager?.Lower(alarmID, String.Format("Engine {0} is running acceptable RPM  @ {1} RPM", UID, RPM));
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


        private TemperatureState _tempState = TemperatureState.OK;
        public TemperatureState TempState
        {
            get { return _tempState;  }
            set
            {
                _tempState = value;
                String alarmID = TempSensor.ID;
                switch (_tempState)
                {
                    case TemperatureState.TOO_HOT:
                        AlarmManager?.Raise(alarmID, AlarmState.CRITICAL, String.Format("Engine {0} is running too hot @ {1}", UID, Temp));
                        break;
                    case TemperatureState.HOT:
                        AlarmManager?.Raise(alarmID, AlarmState.SEVERE, String.Format("Engine {0} is running hot @ {1}", UID, Temp));
                        break;
                    default:
                        AlarmManager?.Lower(alarmID,String.Format("Engine {0} returned to acceptable temperature of {1}", UID, Temp));
                        break;
                }
            }
        }
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

        public int RPM { get; internal set; } = 0;

        public double Temp { get; internal set; } = 0;

        public Engine(String id, byte rpmPin, byte oilSensorPin, byte tempSensorPin) : base(id, null)
        {

            RPMSensor = new RPMCounter(CreateDeviceID("rpm"), rpmPin, ArduinoDevice.InterruptMode.FALLING); 
            RPMSensor.ReportInterval = 2000;
            RPMSensor.Tolerance = 10000;
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

            TempSensor = new TemperatureSensor(CreateDeviceID("temp"), tempSensorPin, DS18B20Array.BitResolution.VERY_LOW);
            TempSensor.ReportInterval = 3000;
            TempSensor.TemperatureUpdated += (Object sender, float temp) =>
            {
                monitorTemperature();
            };

            AddDevice(RPMSensor);
            AddDevice(OilSensor);
            AddDevice(TempSensor);
        }

        //TODO: change to private
        public void monitorRPM()
        {
            Console.WriteLine("RPM: {0} Count: {1} CountPerSecond: {2}, CountDuration: {3}, IntervalDuration: {4}, State: {5}", 
                (int)Math.Round(RPMSensor.IntervalsPerSecond * 60), 
                RPMSensor.Count,
                RPMSensor.CountPerSecond,
                RPMSensor.CountDuration,
                RPMSensor.IntervalDuration,
                RPMState);

            //assign the sensor value to the engine property
            RPM = RPMSensor.RPM;

            //determine Running status
            Running = RPM >= IS_RUNNING_RPM_THRESHOLD;

            //Now update the RPM State
            if (Running)
            {
                if (RunningFor >= RUNNING_FOR_THRESHOLD)
                {
                    if(RPM > (int)EngineRPMState.TOO_FAST)
                    {
                        RPMState = EngineRPMState.TOO_FAST;
                    }
                    else if(RPM > (int)EngineRPMState.FAST)
                    {
                        RPMState = EngineRPMState.FAST;
                    } 
                    else if(RPM > (int)EngineRPMState.NORMAL)
                    {
                        RPMState = EngineRPMState.NORMAL;
                    }
                    else
                    {
                        RPMState = EngineRPMState.SLOW;
                    }
                }
            } 
            else
            {
                RPMState = EngineRPMState.OFF;
            }
        }


        //TODO: change to private
        public void checkOilPressure()
        {
            if(IsRunning && OilSensor.DetectedPressure) //if running for however long and oil sensor is off then that's correct
            {
                OilPressure = OilPressureState.OK_ENGINE_ON;
            } 
            else if(RunningFor >= RUNNING_FOR_THRESHOLD && !OilSensor.DetectedPressure) //if running for a while and oll esnsor is on whoa danger
            {
                OilPressure = OilPressureState.NO_PRESSURE;
            }
            else if (!IsRunning && StoppedRunningFor >= STOPPED_RUNNING_FOR_THRESHOLD)
            {
                if (!OilSensor.DetectedPressure) //if stopped for a while and oil sensor is on then correct
                {
                    OilPressure = OilPressureState.OK_ENGINE_OFF;
                }
                else //but if the oil sensor is off then this suggests a sensor fault
                {
                    OilPressure = OilPressureState.SENSOR_FAULT;
                }
            }
        }

        //TODO: change to private
        public void monitorTemperature()
        {
            Console.WriteLine("Temp: {0}", TempSensor.Temperature);
            Temp = TempSensor.Temperature;
            if (Temp > (int)TemperatureState.TOO_HOT)
            {
                TempState = TemperatureState.TOO_HOT;
            }
            else if(Temp > (int)TemperatureState.HOT)
            {
                TempState = TemperatureState.HOT;
            }
            else
            {
                TempState = TemperatureState.OK;
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
            AlarmManager.RegisterAlarm(this, TempSensor.ID);
        }

        public void RequestUpdateAlarms()
        {
            RequestStatus();
        }
    }
}
