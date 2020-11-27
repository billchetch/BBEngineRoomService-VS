using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino.Devices;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class Pump : SwitchSensor
    {
        public const String SENSOR_NAME = "PUMP";
        public Pump(int pinNumber, String id) : base(pinNumber, 250, id, SENSOR_NAME) { }

        public void initialise(EngineRoomServiceDB erdb)
        {
            //get latest data
            DBRow row = erdb.GetFirstOnAfterLastOff(ID); //to allow for reconnections which will naturally create an ON evnt
            if (row != null)
            {
                LastOn = row.GetDateTime("created");
            }
            row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, ID);
            if (row != null)
            {
                LastOff = row.GetDateTime("created");
            }
        }
    } //end pump
}
