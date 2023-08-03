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
        public const String INDUK_ID = "idk";
        public const String BANTU_ID = "bnt";
        public const String GENSET1_ID = "gs1";
        public const String GENSET2_ID = "gs2";
        
        public const String POMPA_CELUP_ID = "pmp_clp";
        public const String POMPA_SOLAR_ID = "pmp_sol";

        
        private AlarmManager _alarmManager = new AlarmManager();

        private EngineRoomServiceDB _erdb;
        
        private Pump _pompaCelup;
        private Pump _pompaSolar;
        //private WaterTanks _waterTanks;

        private Engine _induk;
        private Engine _bantu;
        private Engine _gs1;
        private Engine _gs2;

        ArduinoDeviceManager _enginesADM; //one board manages Induk and Bantu
        ArduinoDeviceManager _gensetsADM; //One board manages gs1 and gs2


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
        

        private Engine _testEngine;
        public Engine TestEngine { get { return _testEngine;  } }



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
                return true;
            }
            return false;
        }

        protected override List<ADMServiceDB.SnapshotLogEntry> GetSnapshotLogEntries(ArduinoObject ao)
        {
            List<ADMServiceDB.SnapshotLogEntry> entries = new List<ADMServiceDB.SnapshotLogEntry>();
            if(ao is Engine)
            {
                var engine = (Engine)ao;
                entries.Add(new ADMServiceDB.SnapshotLogEntry(engine.RPMSensor.UID, "RPM", engine.RPM, String.Format("RPMState: {0}", engine.RPMState)));
                entries.Add(new ADMServiceDB.SnapshotLogEntry(engine.OilSensor.UID, "Oil", engine.OilPressure));
                entries.Add(new ADMServiceDB.SnapshotLogEntry(engine.TempSensor.UID, "Temp", engine.Temp, String.Format("TempState: {0}", engine.TempState)));
            }

            return entries;
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

        protected override bool CreateADMs()
        {
            try
            {
                String networkServiceURL = (String)Settings["NetworkServiceURL"];
                String enginesServiceName = "crayfish";
                String gensetsServiceName = "";

                _enginesADM = ArduinoDeviceManager.Create(enginesServiceName, networkServiceURL, 256, 256);
                //_enginesADM = ArduinoDeviceManager.Create(ArduinoSerialConnection.BOARD_ARDUINO, 115200, 64, 64);
                _testEngine = new Engine("gs1", 19, 5, 9);
                _testEngine.RPMSensor.ConversionFactor = 0.537;
                _testEngine.EngineStarted += onEngineStarted;
                _testEngine.EngineStopped += onEngineStopped;

                //_testEngine2 = new Engine("gs2", 18, 6, 10);



                _enginesADM.AddDeviceGroup(_testEngine);
                
                AddADM(_enginesADM);
                
                //add alarm raisers
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
                
                return true;
            } catch (Exception e)
            {
                return false;
            }
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

                case AlarmsMessageSchema.COMMAND_TEST_ALARM:
                    if (args.Count == 0)
                    {
                        throw new Exception("Please pecify an alarm ID");
                    }
                    alarmID = args[0].ToString();

                    _alarmManager.StartTest(alarmID, AlarmState.MODERATE, "Raising the alarm as a test baby for a short time...");
                    
                    return true;

                default:
                    return base.HandleCommand(cnn, message, cmd, args, response);
            }
        }
    
        /*
            override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
            {
                //return value of true/false determines whether response message is broadcast or not

                EngineRoomMessageSchema schema = new EngineRoomMessageSchema(response);
                Engine engine;
                List<Engine> engines;
                bool enable;

                Pump pump;
                switch (cmd)
                {
                    case EngineRoomMessageSchema.COMMAND_TEST:
                        //schema.AddPompaCelup(_pompaCelup);
                        //Message alert = BBAlarmsService.BBAlarmsService.EngineRoomMessageSchema.RaiseAlarm(_pompaCelup.ID, true, "Test raising alarm");
                        //Broadcast(alert);

                        //Message 
                        return false;

                    case EngineRoomMessageSchema.COMMAND_LIST_ENGINES:
                        engines = GetEngines();
                        List<String> engineIDs = new List<String>();

                        foreach(Engine eng in engines)
                        {
                            if (eng.Enabled) engineIDs.Add(eng.ID);
                        }
                        response.AddValue("Engines", engineIDs);
                        return true;

                    case EngineRoomMessageSchema.COMMAND_ENGINE_STATUS:
                        if (args == null || args.Count == 0 || args[0] == null) throw new Exception("No engine specified");
                        engine = GetEngine(args[0].ToString());
                        if (engine == null) throw new Exception("Cannot find engine with ID " + args[0]);
                        schema.AddEngine(engine);

                        if (engine.RPM != null)
                        {
                            Task.Run(() => {
                                System.Threading.Thread.Sleep(250);
                                EngineRoomMessageSchema sc = new EngineRoomMessageSchema(new Message(MessageType.DATA, response.Target));
                                sc.AddRPM(engine.RPM);
                                SendMessage(sc.Message);
                            });

                        }

                        if (engine.OilSensor != null)
                        {
                            Task.Run(() => {
                                System.Threading.Thread.Sleep(250);
                                EngineRoomMessageSchema sc = new EngineRoomMessageSchema(new Message(MessageType.DATA, response.Target));
                                sc.AddOilSensor(engine.OilSensor);
                                SendMessage(sc.Message);
                            });
                        }

                        if(engine.TempSensor != null)
                        {
                            Task.Run(() => {
                                System.Threading.Thread.Sleep(250);
                                EngineRoomMessageSchema sc = new EngineRoomMessageSchema(new Message(MessageType.DATA, response.Target));
                                sc.AddDS18B20Sensor(engine.TempSensor);
                                SendMessage(sc.Message);
                            });
                        }

                        return true;

                    case EngineRoomMessageSchema.COMMAND_ENABLE_ENGINE:
                        if (args == null || args.Count < 1) throw new Exception("No engine specified");
                        engine = GetEngine(args[0].ToString());
                        if (engine == null) throw new Exception("Cannot find engine with ID " + args[0]);
                        enable = args.Count > 1 ? System.Convert.ToBoolean(args[1]) : true;
                        if (enable != engine.Enabled)
                        {
                            engine.Enable(enable);
                            EngineRoomServiceDB.LogEventType let = engine.Enabled ? EngineRoomServiceDB.LogEventType.ENABLE : EngineRoomServiceDB.LogEventType.DISABLE;
                            _erdb.LogEvent(let, engine.ID, let.ToString() + " engine " + engine.ID);
                            schema.AddEngine(engine);
                        }
                        return true;

                    case EngineRoomMessageSchema.COMMAND_PUMP_STATUS:
                        if (args == null || args.Count == 0 || args[0] == null) throw new Exception("No pump specified");
                        pump = GetPump(args[0].ToString());
                        schema.AddPump(pump);
                        response.Type = MessageType.DATA;
                        return true;

                    case EngineRoomMessageSchema.COMMAND_ENABLE_PUMP:
                        if (args == null || args.Count == 0 || args[0] == null) throw new Exception("No pump specified");
                        pump = GetPump(args[0].ToString());
                        enable = args.Count > 1 ? System.Convert.ToBoolean(args[1]) : true;
                        if (enable != pump.Enabled)
                        {
                            pump.Enable(enable);
                            EngineRoomServiceDB.LogEventType let = pump.Enabled ? EngineRoomServiceDB.LogEventType.ENABLE : EngineRoomServiceDB.LogEventType.DISABLE;
                            _erdb.LogEvent(let, pump.ID, let.ToString() + " pump " + pump.ID);
                            schema.AddPump(pump);
                        }
                        return true;

                    case EngineRoomMessageSchema.COMMAND_WATER_TANK_STATUS:
                        if (args == null || args.Count == 0 || args[0] == null) throw new Exception("No tank specified");
                        WaterTanks.FluidTank waterTank = (WaterTanks.FluidTank)_waterTanks.GetDevice(args[0].ToString());
                        schema.AddWaterTank(waterTank);
                        response.Type = MessageType.DATA;
                        return true;

                    case EngineRoomMessageSchema.COMMAND_WATER_STATUS:
                        schema.AddWaterTanks(_waterTanks);
                        return true;

                    case EngineRoomMessageSchema.COMMAND_ENABLE_WATER:
                        enable = args.Count > 0 ? System.Convert.ToBoolean(args[0]) : true;
                        if(enable != _waterTanks.Enabled)
                        {
                            _waterTanks.Enable(enable);
                            EngineRoomServiceDB.LogEventType let = _waterTanks.Enabled ? EngineRoomServiceDB.LogEventType.ENABLE : EngineRoomServiceDB.LogEventType.DISABLE;
                            _erdb.LogEvent(let, _waterTanks.ID, let.ToString() + " water tanks");
                            schema.AddWaterTanks(_waterTanks);
                        }
                        return true;

                    case BBAlarmsService.AlarmsMessageSchema.COMMAND_ALARM_STATUS:
                        OnMonitorEngineRoomTimer(null, null);
                        return true;

                    case BBAlarmsService.AlarmsMessageSchema.COMMAND_RAISE_ALARM:
                        if (args == null || args.Count < 1) throw new Exception("No alarm specified");
                        String alarmID = args[0].ToString();
                        BBAlarmsService.AlarmState alarmState = BBAlarmsService.AlarmState.CRITICAL;
                        BBAlarmsService.AlarmsMessageSchema.RaiseAlarm(this, alarmID, alarmState, "Raised alarm", true);
                        return true;

                   case BBAlarmsService.AlarmsMessageSchema.COMMAND_LOWER_ALARM:
                        if (args == null || args.Count < 1) throw new Exception("No alarm specified");
                        BBAlarmsService.AlarmsMessageSchema.LowerAlarm(this, args[0].ToString(), BBAlarmsService.AlarmState.OFF, "Lowered alarm", true);
                        return true;

                    default:
                        return base.HandleCommand(cnn, message, cmd, args, response);
                }
            }
            */


    } //end service class
}
