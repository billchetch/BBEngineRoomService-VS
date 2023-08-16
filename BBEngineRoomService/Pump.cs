using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2.Devices;
using Chetch.Utilities;
using Chetch.Database;
using Chetch.Messaging;
using BBAlarmsService;

namespace BBEngineRoomService
{
    public class Pump : SwitchDevice, AlarmManager.IAlarmRaiser
    {
        public enum PumpState
        {
            ON,
            OFF,
            ON_TOO_LONG = -1,
            OFF_TOO_LONG = -2,
        }

        public EventHandler PumpeStarted;
        public EventHandler PumpStopped;

        public AlarmManager AlarmManager { get; set; }

        private PumpState _pumpState = PumpState.OFF;
        public PumpState PumpActivityState 
        { 
            get { return _pumpState; } 
            internal set
            {
                PumpState oldState = _pumpState;
                bool changed = value != _pumpState;
                _pumpState = value;
                if (changed)
                {
                    String alarmID = ID;
                    String msg;
                    switch (_pumpState)
                    {
                        case PumpState.ON_TOO_LONG:
                            String duration = TimeSpan.FromSeconds((int)RunningFor.TotalSeconds).ToString("c");
                            msg = String.Format("Pump {0} has been on too long ({1})", UID, duration);
                            AlarmManager?.Raise(alarmID, AlarmState.SEVERE, msg);
                            break;

                        case PumpState.OFF:
                            if(oldState == PumpState.ON_TOO_LONG)
                            {
                                msg = String.Format("Pump {0} switched off after being on too long", UID);
                                AlarmManager?.Lower(alarmID, msg);
                            }
                            break;
                    }
                }
            }
        }

        public ThresholdMap<PumpState, int> PumpStateThresholds = new ThresholdMap<PumpState, int>();


        [ArduinoProperty(ArduinoPropertyAttribute.DATA)]
        public DateTime StartedOn { get; internal set; }
        public DateTime StoppedOn { get; internal set; }

        public TimeSpan RunningFor
        {
            get
            {
                if (IsOn)
                {
                    return DateTime.Now - StartedOn;
                } else
                {
                    return default(TimeSpan);
                }
            }
        }

        public TimeSpan RanFor
        {
            get
            {
                if ((StartedOn != default(DateTime)) && (StoppedOn != default(DateTime)))
                {
                    return StoppedOn - StartedOn;
                } 
                else 
                {
                    return default(TimeSpan);
                }
            }
        }

        private System.Timers.Timer _monitorTimer = new System.Timers.Timer();


        public Pump(String id, byte pinNumber) : base(id, SwitchMode.PASSIVE, pinNumber, SwitchPosition.OFF, 250) 
        {
            _monitorTimer.AutoReset = false;
            _monitorTimer.Elapsed += (Object sender, System.Timers.ElapsedEventArgs e) =>
            {
                monitorPumpState();
            };
        }

        protected override void OnSwitched(SwitchPosition oldValue)
        {
            base.OnSwitched(oldValue);

            _monitorTimer.Stop();
            if (IsOn)
            {
                StartedOn = DateTime.Now;
                PumpActivityState = PumpState.ON;
                PumpeStarted?.Invoke(this, null);

                //start a timer to monitor
                if (PumpStateThresholds[PumpState.ON_TOO_LONG] > 0)
                {
                    _monitorTimer.Interval = PumpStateThresholds[PumpState.ON_TOO_LONG] * 1000;
                    _monitorTimer.Start();
                }
            } else
            {
                StoppedOn = DateTime.Now;
                PumpActivityState = PumpState.OFF;
                PumpStopped?.Invoke(this, null);

                if (PumpStateThresholds[PumpState.OFF_TOO_LONG] > 0)
                {
                    //TODO:
                }
            }
        }

        private void monitorPumpState()
        {
            if (IsOn && PumpStateThresholds[PumpState.ON_TOO_LONG] > 0 && RunningFor.TotalSeconds > PumpStateThresholds[PumpState.ON_TOO_LONG])
            {
                PumpActivityState = PumpState.ON_TOO_LONG;
            } //TODO: handle the off too long state
        }

        public void RegisterAlarms()
        {
            AlarmManager.RegisterAlarm(this, ID);
        }
    } //end pump
}
