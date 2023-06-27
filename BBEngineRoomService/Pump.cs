using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2.Devices;
using Chetch.Database;
using Chetch.Messaging;

namespace BBEngineRoomService
{
    public class Pump : SwitchDevice
    {
        public enum PumpState
        {
            ON,
            OFF,
            ON_TOO_LONG,
            OFF_TOO_LONG,
            ON_TOO_FREQUENTLY
        }

        public const String SENSOR_NAME = "PUMP";

        public int MaxOnDuration = -1; //at a maximum on time to raise alarms (time in secs)
        
        public PumpState StateOfPump = PumpState.OFF;

        public Pump(byte pinNumber, String id) : base(id, SwitchMode.PASSIVE, pinNumber, SwitchPosition.OFF, 250) { }

        
    } //end pump
}
