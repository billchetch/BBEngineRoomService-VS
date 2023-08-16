using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2;
using Chetch.Arduino2.Devices;
using Chetch.Arduino2.Devices.Diagnostics;
using Chetch.Messaging;

using System.Diagnostics;
using Chetch.Utilities;
using Chetch.Database;
using System.Runtime.InteropServices;
using System.ComponentModel;
using BBAlarmsService;

namespace BBEngineRoomService
{
    public class BBEngineRoomService : ADMService
    {
        public const String LOBSTER_SERVICE_NAME = "lobster";
        public const String CRAYFISH_SERVICE_NAME = "crayfish";
        
        public const String INDUK_ID = "idk";
        public const String BANTU_ID = "bnt";

        public const String GENSET1_ID = "gs1";
        public const String GENSET2_ID = "gs2";
        public const double GENSET_CONVERSION_FACTOR = 0.537;

        public const String POMPA_CELUP_ID = "pmp-clp";
        public const String POMPA_SOLAR_ID = "pmp-sol";

        
        private AlarmManager _alarmManager = new AlarmManager();

        private EngineRoomServiceDB _erdb;
        
        private Pump _pompaCelup;
        private Pump _pompaSolar;
        //private WaterTanks _waterTanks;

        private Engine _induk;
        private Engine _bantu;
        private Engine _gs1;
        private Engine _gs2;

        ArduinoDeviceManager _lobsterADM; //one board manages Induk and Bantu and pumps
        ArduinoDeviceManager _crayfishADM; //One board manages gs1 and gs2


        public BBEngineRoomService(bool test = false) :  base("BBEngineRoom", test ? null : "BBERClient", test ? "ADMServiceTest" : "BBEngineRoomService", test ? null : "BBEngineRoomServiceLog") 
        {
            AboutSummary = "BB Engine Room Service v1.01";

            try { 

                Tracing?.TraceEvent(TraceEventType.Information, 0, "Constructing Service class for {0} ...", AboutSummary);

                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connecting to Engine Room database...");
                _erdb = EngineRoomServiceDB.Create(Properties.Settings.Default, "EngineRoomDBName");
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected to Engine Room database");

                Tracing?.TraceEvent(TraceEventType.Information, 0, "Setting service DB to {0} and settings to default", _erdb.DBName);
                ServiceDB = _erdb;
                Settings = Properties.Settings.Default;

                LogSnapshotTimerInterval = 30 * 1000;
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Setting snapshot timer interval to {0}", LogSnapshotTimerInterval);
            }
            catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, e.Message);
                throw e;
            }

        }
        

        protected override bool CanLogEvent(ArduinoObject ao, string eventName)
        {
            return true;
        }

        protected override ADMServiceDB.EventLogEntry GetEventLogEntry(ArduinoObject ao, DSOPropertyChangedEventArgs dsoArgs)
        {
            var entry = base.GetEventLogEntry(ao, dsoArgs);

            if(ao is Engine.OilSensorSwitch)
            {
                var oss = (Engine.OilSensorSwitch)ao;
                entry.Info = String.Format("Pos: {0}, PinState: {1}, Pressure: {2}", oss.Position, oss.PinState, oss.DetectedPressure ? "Y" : "N");
            }

            return entry;
        }

        protected override bool CanLogToSnapshot(ArduinoObject ao)
        {
            if(ao is Engine)
            {
                return ((Engine)ao).IsRunning;
            }

            return false;
        }

        protected override void AddSnapshotLogEntries(ArduinoObject ao, List<ADMServiceDB.SnapshotLogEntry> entries)
        {
            if(ao is Engine)
            {
                var engine = (Engine)ao;
                String desc = String.Format("RPMState: {0}", engine.RPMState);
                entries.Add(new ADMServiceDB.SnapshotLogEntry(engine.RPMSensor.UID, "RPM", engine.RPM, desc));
                desc = String.Format("OilState: {0}", engine.OilPressure);
                entries.Add(new ADMServiceDB.SnapshotLogEntry(engine.OilSensor.UID, "Oil", engine.OilPressure, desc));
                desc = String.Format("TempState: {0}, Sensor State: {1}", engine.TempState, engine.TempSensor.TemperatureSensorState);
                entries.Add(new ADMServiceDB.SnapshotLogEntry(engine.TempSensor.UID, "Temp", engine.Temp, desc));
            }
        }

        protected override bool CanDispatch(ArduinoObject ao, string propertyName)
        {
            if(ao is Engine)
            {
                return true;
            }


            return base.CanDispatch(ao, propertyName);
        }

        private void onEngineStarted(Object sender, double rpm)
        {
            Engine engine = (Engine)sender;
            String info = String.Format("Engine started with rpm {0}", rpm);
            ServiceDB.LogEvent("Engine Started", engine.UID, rpm, info);
        }

        private void onEngineStopped(Object sender, double rpm)
        {
            Engine engine = (Engine)sender;
            TimeSpan duration = TimeSpan.FromSeconds(engine.RanFor);
            String info = String.Format("Engine stopped with rpm {0}, ran for: {1}", rpm, duration.ToString("c"));
            ServiceDB.LogEvent("Engine Stopped", engine.UID, rpm, info);
        }

        private void onPumpStarted(Object sender, EventArgs ea)
        {
            Pump pump = (Pump)sender;
            String info = String.Format("Pump {0} has started", pump.UID);
            ServiceDB.LogEvent("Pump Started", pump.UID, pump.PinState, info);
        }

        private void onPumpStopped(Object sender, EventArgs ea)
        {
            Pump pump = (Pump)sender;
            String duration = TimeSpan.FromSeconds((int)pump.RanFor.TotalSeconds).ToString("c");
            String info = String.Format("Pump {0} has stopped, it ran for {1}", pump.UID, duration);
            ServiceDB.LogEvent("Pump Stopped", pump.UID, pump.PinState, info);
        }


        protected override void CreateADMs()
        {
            String networkServiceURL = (String)Settings["NetworkServiceURL"];
            
            _lobsterADM = ArduinoDeviceManager.Create(LOBSTER_SERVICE_NAME, networkServiceURL, 256, 256);
            _crayfishADM = ArduinoDeviceManager.Create(CRAYFISH_SERVICE_NAME, networkServiceURL, 256, 256);

            //Induk
            _induk = new Engine(INDUK_ID, 18, 6, 9);
            _induk.RPMSensor.ConversionFactor = 1.0; //TODO: find conversion factor
            _induk.EngineStarted += onEngineStarted;
            _induk.EngineStopped += onEngineStopped;
            //_lobsterADM.AddDeviceGroup(_induk);

            //Bantu
            _bantu = new Engine(BANTU_ID, 19, 5, 8);
            _bantu.RPMSensor.ConversionFactor = 1.0; //TODO: find conversion factor
            _bantu.EngineStarted += onEngineStarted;
            _bantu.EngineStopped += onEngineStopped;
            //_lobsterADM.AddDeviceGroup(_bantu);

            //Diesel pump
            _pompaSolar = new Pump(POMPA_SOLAR_ID, 10);
            _pompaSolar.PumpStateThresholds[Pump.PumpState.ON_TOO_LONG] = 2;
            _pompaSolar.PumpeStarted += onPumpStarted;
            _pompaSolar.PumpStopped += onPumpStopped;
            //_lobsterADM.AddDevice(_pompaSolar);

            //Bilge pump
            _pompaCelup = new Pump(POMPA_CELUP_ID, 11);
            _pompaCelup.PumpStateThresholds[Pump.PumpState.ON_TOO_LONG] = 2;
            _pompaCelup.PumpeStarted += onPumpStarted;
            _pompaCelup.PumpStopped += onPumpStopped;
            _lobsterADM.AddDevice(_pompaCelup);


            //Genset 1
            _gs1 = new Engine(GENSET1_ID, 19, 5, 9);
            _gs1.RPMSensor.ConversionFactor = GENSET_CONVERSION_FACTOR; //TODO: Should be same for both gensets
            _gs1.EngineStarted += onEngineStarted;
            _gs1.EngineStopped += onEngineStopped;
            _crayfishADM.AddDeviceGroup(_gs1);

            //Genset 2
            _gs2 = new Engine(GENSET2_ID, 18, 6, 10);
            _gs2.RPMSensor.ConversionFactor = GENSET_CONVERSION_FACTOR; //TODO: Should be same for both gensets
            _gs2.EngineStarted += onEngineStarted;
            _gs2.EngineStopped += onEngineStopped;
            _crayfishADM.AddDeviceGroup(_gs2);

            //AddADM(_lobsterADM);
            AddADM(_crayfishADM);

            //Add alarm raisers and state change handler
            _alarmManager.AddRaisers(GetArduinoObjects());
            _alarmManager.AlarmStateChanged += (Object sender, AlarmManager.Alarm alarm) =>
            {
                _alarmManager.NotifyAlarmsService(this, alarm);
                try
                {
                    Tracing?.TraceEvent(TraceEventType.Warning, 999, "Alarm {0} changed state to {1} - {2}", alarm.ID, alarm.State, alarm.Message);
                    ServiceDB?.LogEvent("Alarm", alarm.ID, alarm.State, alarm.Message);
                }
                catch
                {
                    //fail silently
                }
            };
        }

        /*protected override void HandleAOPropertyChange(object sender, PropertyChangedEventArgs eargs)
        {
            base.HandleAOPropertyChange(sender, eargs);
        }*/

        public Pump GetPump(String pumpID)
        {
            Pump pump;
            switch (pumpID)
            {
                case POMPA_CELUP_ID:
                    pump = _pompaCelup;
                    break;
                case POMPA_SOLAR_ID:
                    pump = _pompaSolar;
                    break;
                default:
                    throw new Exception(String.Format("Unrecognised pump {0}", pumpID));
            }
            return pump;
        }


        //Respond to incoming commands
        public override void AddCommandHelp()
        {
            base.AddCommandHelp();

            AddCommandHelp(EngineRoomMessageSchema.COMMAND_TEST, "Used during development to test stuff");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_LIST_ENGINES, "List online engines");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_ENGINE_STATUS, "Gets status of <engineID>");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_PUMP_STATUS, "Gets status of <pumpID>");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_ENABLE_ENGINE, "Set engine <engineID> enabled to <true/false>");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_WATER_STATUS, "Gets status of water tanks group");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_ENABLE_WATER, "Set water tanks enabled to <true/false>");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_WATER_TANK_STATUS, "Gets status of <water tank ID>");
        }

        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
        {
            String alarmID;
            switch (cmd)
            {
                case AlarmsMessageSchema.COMMAND_ALARM_STATUS:
                    _alarmManager.NotifyAlarmsService(this);
                    return false; //no need to send a response (save on bandwidth)

                default:
                    return base.HandleCommand(cnn, message, cmd, args, response);
            }
        }
    

    } //end service class
}
