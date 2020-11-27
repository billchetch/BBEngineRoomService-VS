using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino;
using Chetch.Arduino.Devices.Counters;
using Chetch.Arduino.Devices.Temperature;
using Chetch.Arduino.Devices.RangeFinders;
using Chetch.Arduino.Devices;
using Chetch.Messaging;
using System.Diagnostics;
using Chetch.Utilities;
using Chetch.Database;
using System.Runtime.InteropServices;

namespace BBEngineRoomService
{
    public class BBEngineRoomService : ADMService
    {
        public const int TIMER_STATE_LOG_INTERVAL = 60 * 1000;
        public const int REQUEST_STATE_INTERVAL = 30 * 1000; //the interval by which to wait to request state of things like oil sensors and pumps

        public const byte BOARD_ER1 = 1;
        public const byte BOARD_ER2 = 2;
        public const byte BOARD_ER3 = 3;

        public const String INDUK_ID = "idk";
        public const String BANTU_ID = "bnt";
        public const String GENSET1_ID = "gs1";
        public const String GENSET2_ID = "gs2";
        
        public const double RPM_CALIBRATION_BANTU = 0.47; // 17/8
        public const double RPM_CALIBRATION_INDUK = 0.47; // / 17/8;
        public const double RPM_CALIBRATION_GENSET1 = 0.55;
        public const double RPM_CALIBRATION_GENSET2 = 0.56; 
        public const int RPM_SAMPLE_SIZE = 5;
        public const int RPM_SAMPLE_INTERVAL = 4000; //ms
        public const Sampler.SamplingOptions RPM_SAMPLING_OPTIONS = Sampler.SamplingOptions.MEAN_COUNT_PRUNE_MIN_MAX;
        
        public const String POMPA_CELUP_ID = "pmp_clp";
        public const String POMPA_SOLAR_ID = "pmp_sol";


        public const int TEMP_SAMPLE_INTERVAL = 20000; //temp changes very slowly in the engine so no need to sample frequently
        public const int TEMP_SAMPLE_SIZE = 3;
        
        private EngineRoomServiceDB _erdb;
        private Pump _pompaCelup;
        private Pump _pompaSolar;
        private WaterTanks _waterTanks;

        private Dictionary<String, Engine> engines = new Dictionary<String, Engine>();

        private System.Timers.Timer _timerStateLog;

        public bool PauseOutput = false; //TODO: REMOVE THIS!!!
        public bool Output2Console = false; //TODO: REMOVE THIS!!!
        
        public BBEngineRoomService() : base("BBEngineRoom", null, "ADMTestService", null) // base("BBEngineRoom", "BBERClient", "BBEngineRoomService", "BBEngineRoomServiceLog") //
        {
            AddAllowedPorts(Properties.Settings.Default.AllowedPorts);
            PortSharing = true;
            if (PortSharing)
            {
                SupportedBoards = ArduinoDeviceManager.XBEE_DIGI;
                RequiredBoards = "BBED1,BBED2,BBED3";  //For connection purposes Use XBee NodeIDs to identify boards rather than their ID
            }
            else
            {
                SupportedBoards = ArduinoDeviceManager.DEFAULT_BOARD_SET;
                RequiredBoards = "1";
            }

            ADMInactivityTimeout = 20000; //To allow for BBED3 sampling at 10secs ADM_INACTIVITY_TIMEOUT; //default of 10,000

            Sampler.SampleProvided += HandleSampleProvided;
            Sampler.SampleError += HandleSampleError;

            Output2Console = true; //TODO: remove this
            //AutoStartADMTimer = false;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connecting to Engine Room database...");
                _erdb = EngineRoomServiceDB.Create(Properties.Settings.Default, "EngineRoomDBName");
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected to Engine Room database");

                _erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.CONNECT, EngineRoomServiceDB.LogEventType.DISCONNECT, "BBEngineRoom");

                Tracing?.TraceEvent(TraceEventType.Information, 0, "Creating state log timer at {0} intervals", TIMER_STATE_LOG_INTERVAL);
                _timerStateLog = new System.Timers.Timer();
                _timerStateLog.Interval = TIMER_STATE_LOG_INTERVAL;
                _timerStateLog.Elapsed += OnStateLogTimer;
                _timerStateLog.Start();
            }
            catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, e.Message);
                throw e;
            }

            base.OnStart(args);
        }

        protected void OnStateLogTimer(Object sender, System.Timers.ElapsedEventArgs eventArgs)
        {
            //build up a picture of the state of the engine room and log it
            List<Engine> engines = GetEngines();
            if (engines != null)
            {
                foreach (Engine engine in engines)
                {
                    if (!engine.Online) continue;
                    _erdb.LogState(engine.ID, "Running", engine.Running);
                    if (engine.RPM != null) _erdb.LogState(engine.ID, "RPM", engine.RPM.AverageRPM);
                    if (engine.OilSensor != null) _erdb.LogState(engine.ID, "OilSensor", engine.OilSensor.State);
                    if (engine.TempSensor != null) _erdb.LogState(engine.ID, "Temperature", engine.TempSensor.Temperature);
                }
            }

            //do pumps
            if(_pompaCelup != null)
            {
                _erdb.LogState(_pompaCelup.ID, "Pump", _pompaCelup.State);
            }
            if(_pompaSolar != null)
            {
                _erdb.LogState(_pompaSolar.ID, "Pump", _pompaCelup.State);
            }

            //do the water tanks
            if (_waterTanks != null)
            {
                foreach (WaterTanks.WaterTank wt in _waterTanks.Tanks)
                {
                    _erdb.LogState(wt.ID, "Water Tank", wt.PercentageFull);
                }
            }
        }

        protected List<Engine> GetEngines()
        {
            List<Engine> engines = new List<Engine>();
            foreach (var adm in ADMS.Values)
            {
                if (adm == null) continue;

                foreach (var dg in adm.DeviceGroups)
                {
                    if (dg is Engine)
                    {
                        engines.Add((Engine)dg);
                    }
                }
            }
            return engines;
        }

        protected Engine GetEngineForDevice(String deviceID)
        {
            var engines = GetEngines();
            foreach(var engine in engines) { 
                var dev = engine.GetDevice(deviceID);
                if (dev != null) return engine;
            }
            return null;
        }

        public Engine GetEngine(String engineID)
        {
            List<Engine> engines = GetEngines();
            foreach(Engine engine in engines)
            {
                if (engine.ID != null && engine.ID.Equals(engineID)) return engine;
            }
            return null;
        }

        protected override void AddADMDevices(ArduinoDeviceManager adm, ADMMessage message)
        {
            if (adm == null || adm.BoardID == 0)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, "adm is null or does not have a BoardID value");
                return;
            }

            adm.Tracing = Tracing;
            
            DS18B20Array temp;
            Engine engine;
            RPMCounter rpm;
            Engine.OilSensorSwitch oilSensor;
            String desc;
            
            switch(adm.BoardID)
            {
                case BOARD_ER1:
                    //Temperature array for all engines connected to a board
                    //Important!: this must come first as it disrupts subsequent messages if not first
                    temp = new DS18B20Array(4, "temp_arr");
                    temp.SampleInterval = TEMP_SAMPLE_INTERVAL;
                    temp.SampleSize = TEMP_SAMPLE_SIZE;
                    temp.AddSensor(INDUK_ID + "_temp");
                    temp.AddSensor(BANTU_ID + "_temp");
                    adm.AddDevice(temp);

                    //Pompa celup
                    _pompaCelup = new Pump(10, POMPA_CELUP_ID);
                    _pompaCelup.initialise(_erdb);
                    _pompaCelup.SampleInterval = REQUEST_STATE_INTERVAL;
                    _pompaCelup.SampleSize = 1;
                    adm.AddDevice(_pompaCelup);

                    //Pompa solar
                    _pompaSolar = new Pump(11, POMPA_SOLAR_ID);
                    _pompaSolar.initialise(_erdb);
                    _pompaSolar.SampleInterval = REQUEST_STATE_INTERVAL;
                    _pompaSolar.SampleSize = 1;
                    adm.AddDevice(_pompaSolar);

                    //Induk
                    rpm = new RPMCounter(5, INDUK_ID + "_rpm", "RPM");
                    rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                    rpm.SampleSize = RPM_SAMPLE_SIZE;
                    rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                    rpm.Calibration = RPM_CALIBRATION_INDUK;
                    
                    oilSensor = new Engine.OilSensorSwitch(8, INDUK_ID + "_oil");
                    oilSensor.SampleInterval = REQUEST_STATE_INTERVAL;
                    oilSensor.SampleSize = 1;
                    
                    engine = new Engine(INDUK_ID, rpm, oilSensor, temp.GetSensor(INDUK_ID + "_temp"));
                    engine.initialise(_erdb);
                    adm.AddDeviceGroup(engine);
                    desc = String.Format("Added engine {0} to {1} ({2}) .. engine is {3}", engine.ID, adm.BoardID, adm.PortAndNodeID, engine.Online ? "online" : "offline");
                    Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADD, engine.ID, desc);
                    
                    //Bantu
                    rpm = new RPMCounter(6, BANTU_ID + "_rpm", "RPM");
                    rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                    rpm.SampleSize = RPM_SAMPLE_SIZE;
                    rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                    rpm.Calibration = RPM_CALIBRATION_BANTU;
                    
                    oilSensor = new Engine.OilSensorSwitch(9, BANTU_ID + "_oil");
                    oilSensor.SampleInterval = REQUEST_STATE_INTERVAL;
                    oilSensor.SampleSize = 1;

                    engine = new Engine(BANTU_ID, rpm, oilSensor, temp.GetSensor(BANTU_ID + "_temp"));
                    engine.initialise(_erdb);
                    adm.AddDeviceGroup(engine);
                    desc = String.Format("Added engine {0} to {1} ({2}) .. engine is {3}", engine.ID, adm.BoardID, adm.PortAndNodeID, engine.Online ? "online" : "offline");
                    Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADD, engine.ID, desc);

                    break;

                case BOARD_ER2:
                    //temperature array for all engines connected to a board
                    temp = new DS18B20Array(4, "temp_arr");
                    temp.SampleInterval = 2*TEMP_SAMPLE_INTERVAL;
                    temp.SampleSize = TEMP_SAMPLE_SIZE;
                    temp.AddSensor(GENSET2_ID + "_temp");
                    temp.AddSensor(GENSET1_ID + "_temp");
                    adm.AddDevice(temp);
                
                    //genset 1
                    rpm = new RPMCounter(5, GENSET1_ID + "_rpm", "RPM");
                    rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                    rpm.SampleSize = RPM_SAMPLE_SIZE;
                    rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                    rpm.Calibration = RPM_CALIBRATION_GENSET1;

                    oilSensor = new Engine.OilSensorSwitch(8, GENSET1_ID + "_oil");
                    oilSensor.SampleInterval = REQUEST_STATE_INTERVAL;
                    oilSensor.SampleSize = 1;

                    engine = new Engine(GENSET1_ID, rpm, oilSensor, temp.GetSensor(GENSET1_ID + "_temp"));
                    engine.initialise(_erdb);
                    adm.AddDeviceGroup(engine);
                    desc = String.Format("Added engine {0} to {1} ({2}) .. engine is {3}", engine.ID, adm.BoardID, adm.PortAndNodeID, engine.Online ? "online" : "offline");
                    Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADD, engine.ID, desc);

                    //genset 2
                    rpm = new RPMCounter(6, GENSET2_ID + "_rpm", "RPM");
                    rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                    rpm.SampleSize = RPM_SAMPLE_SIZE;
                    rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                    rpm.Calibration = RPM_CALIBRATION_GENSET2;

                    oilSensor = new Engine.OilSensorSwitch(9, GENSET2_ID + "_oil");
                    oilSensor.SampleInterval = REQUEST_STATE_INTERVAL;
                    oilSensor.SampleSize = 1;

                    engine = new Engine(GENSET2_ID, rpm, oilSensor, temp.GetSensor(GENSET2_ID + "_temp"));
                    engine.initialise(_erdb);
                    adm.AddDeviceGroup(engine);
                    desc = String.Format("Added engine {0} to {1} ({2}) .. engine is {3}", engine.ID, adm.BoardID, adm.PortAndNodeID, engine.Online ? "online" : "offline");
                    Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADD, engine.ID, desc);
                    break;

                case BOARD_ER3:
                    _waterTanks = new WaterTanks();
                    //_waterTanks.AddTank("wt1", 4, 5, 30, 110);
                    _waterTanks.AddTank("wt2", 6, 7, 30, 100);
                    //_waterTanks.AddTank("wt3", 8, 9, 30, 100);
                    _waterTanks.AddTank("wt4", 10, 11, 30, 100);
                    adm.AddDeviceGroup(_waterTanks);
                    break;
            } //end board switch
        }
        
        private void outputSampleData(Sampler.SubjectData sd)
        {
            String l1 = "";
            String l2 = "";
            String l3 = "";
            for (int i = 0; i < sd.Samples.Count; i++)
            {
                String dl = (l1 == String.Empty ? "" : ", ");
                l1 += dl + sd.Samples[i];
                //l2 += dl + sd.SampleTimes[i];
                l3 += dl + sd.SampleIntervals[i];
            }
            Console.WriteLine("Samples: {0}", l1);
            Console.WriteLine("Intervals: {0}", l3);
            Console.WriteLine("SampleCount: {0}", sd.SampleCount);
            Console.WriteLine("SampleTotal: {0}", sd.SampleTotal);
            Console.WriteLine("SampleDuration: {0}", sd.DurationTotal);
        }

        private void HandleSampleProvided(Sampler sampler, ISampleSubject subject)
        {
            Sampler.SubjectData sd = sampler.GetSubjectData(subject);
            //outputSampleData(sd);
        }

        private void HandleSampleError(ISampleSubject subject, Exception e)
        {
            String desc = String.Format("Error when sampling subject {0}: {1} {2}", subject.GetType(), e.GetType(), e.Message);
            String source;
            if(subject is ArduinoDevice)
            {
                source = ((ArduinoDevice)subject).ID;
            } else
            {
                source = subject.GetType().ToString();
            }
            _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ERROR, source, desc);
        }
        private void OnOilCheckRequired(Engine engine)
        {
            if (engine == null) return;
            Message message = null;
            String msg = null;
            switch(engine.CheckOil()){
                case Engine.OilState.NO_PRESSURE:
                    msg = "Oil pressure drop detected";
                    message = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(engine.OilSensor.ID, BBAlarmsService.AlarmState.CRITICAL, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT, engine.OilSensor.ID, msg);
                    break;
                case Engine.OilState.NORMAL:
                    msg = "Oil state normal";
                    message = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(engine.OilSensor.ID, BBAlarmsService.AlarmState.OFF, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.INFO, engine.OilSensor.ID, msg);
                    break;
                case Engine.OilState.SENSOR_FAULT:
                    msg = "Oil sensor faulty";
                    message = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(engine.OilSensor.ID, BBAlarmsService.AlarmState.SEVERE, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT, engine.OilSensor.ID, msg);
                    break;
            }

            if(message != null)
            {
                //Console.WriteLine("Oil Sensor message: {0}", message);
                Broadcast(message);
            }
        }

        protected override void OnADMDevicesConnected(ArduinoDeviceManager adm, ADMMessage message)
        {
            base.OnADMDevicesConnected(adm, message);

            if (Sampler.IsRunning)
            {
                _erdb.LogEvent(EngineRoomServiceDB.LogEventType.START, "Sampler", String.Format("Sampling started with timer interval of {0} for {1} subjects", Sampler.TimerInterval, Sampler.SubjectCount ));
            }
        }

        //React to data coming from ADM
        protected override void HandleADMMessage(ADMMessage message, ArduinoDeviceManager adm)
        {
            ArduinoDevice dev;
            EngineRoomMessageSchema schema = new EngineRoomMessageSchema(message);

            switch (message.Type)
            {
                case MessageType.DATA:
                    if (message.Sender == null)
                    {
                        dev = adm.GetDeviceByBoardID(message.TargetID);
                        if (dev is DS18B20Array)
                        {
                            schema.AddDS18B20Array((DS18B20Array)dev);
                            foreach(var sensor in ((DS18B20Array)dev).ConnectedSensors)
                            {
                                //outputSampleData(sensor.Sampler.GetSubjectData(sensor));
                                if(Output2Console)Console.WriteLine("------------------------------> Average temp {0}: {1}", sensor.ID, sensor.AverageTemperature);
                            }
                        }
                        if(dev is WaterTanks.WaterTank)
                        {
                            WaterTanks.WaterTank wt = ((WaterTanks.WaterTank)dev);
                            if(Output2Console)Console.WriteLine("****************>: Water Tank distance / average distance / percent / average percent: {0} / {1} / {2} / {3}", wt.Distance, wt.AverageDistance, wt.Percentage, wt.AveragePercentage);
                        }
                    }
                    else
                    {
                        dev = adm.GetDevice(message.Sender);
                        if (dev == _pompaCelup)
                        {
                            schema.AddPompaCelup(_pompaCelup);
                            _erdb.LogEvent(_pompaCelup.IsOn ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF, _pompaCelup.ID, "Pompa Celup");
                            if (Output2Console) Console.WriteLine("+++++++++++++++> Pump {0} {1}", dev.ID, _pompaCelup.IsOn);
                        }

                        if (dev == _pompaSolar)
                        {
                            //schema.AddPop(_pompaCelup);
                            _erdb.LogEvent(_pompaSolar.IsOn ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF, _pompaSolar.ID, "Pompa Solar");
                            if (Output2Console) Console.WriteLine("+++++++++++++++> Pump {0} {1}", dev.ID, _pompaSolar.IsOn);
                        }

                        if (dev is Engine.OilSensorSwitch)
                        {
                            Engine.OilSensorSwitch os = (Engine.OilSensorSwitch)dev;
                            _erdb.LogEvent(os.IsOn ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF, os.ID, "Oil Sensor");
                            Engine engine = GetEngineForDevice(os.ID);
                            //OnOilCheckRequired(engine);
                            if (Output2Console) Console.WriteLine("+++++++++++++++> Oil Sensor {0} {1}", os.ID, os.IsOn);
                        }

                    }
                    break;

                case MessageType.COMMAND_RESPONSE:
                    dev = adm.GetDeviceByBoardID(message.TargetID);
                    if (dev is RPMCounter)
                    {
                        RPMCounter rpm = (RPMCounter)dev;
                        schema.AddRPM(rpm);
                        message.Type = MessageType.DATA; //change the type so that it's more meaingful for listeners...

                        //TODO: remove
                        if (Output2Console) Console.WriteLine("===============================> RPM {0}: {1}", rpm.ID, rpm.AverageRPM);

                        //determine engine running state
                        Engine engine = GetEngineForDevice(rpm.ID);
                        //if (engine == null) throw new Exception("No engine found for RPM device " + rpm.ID);
                        if (engine != null && engine.Online)
                        {
                            bool running = rpm.AverageRPM > Engine.IS_RUNNING_RPM_THRESHOLD;
                            if (running != engine.Running)
                            {
                                engine.Running = running;
                                EngineRoomServiceDB.LogEventType let = engine.Running ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF;
                                _erdb.LogEvent(let, engine.ID, "Engine now " + let.ToString());

                                //OnOilCheckRequixred(engine);
                                schema.AddEngine(engine); //add engine data to provide running/not running event changes
                            }
                        }
                    }
                    break;

                case MessageType.NOTIFICATION:
                    break;

                case MessageType.CONFIGURE_RESPONSE:
                    dev = adm.GetDeviceByBoardID(message.TargetID);
                    if (dev is DS18B20Array)
                    {
                        Tracing?.TraceEvent(TraceEventType.Information, 0, "////////////Temperature array {0} on board {1} configured {2} sensors on one wire pin {3}", dev.ID, adm.BoardID, ((DS18B20Array)dev).ConnectedSensors.Count, message.GetInt(DS18B20Array.PARAM_ONE_WIRE_PIN));
                    }
                    break;

                case MessageType.PING_RESPONSE:
                    //Console.WriteLine("Ping received from {0} ... gives AI={1}, FM={2},  MS={3}", adm.BoardID, message.GetValue("AI"), message.GetValue("FM"), message.GetValue("MS")); ;
                    break;
            }
            base.HandleADMMessage(message, adm);
        }

        protected override void ConnectADM(String port, String nodeID = null)
        {
            base.ConnectADM(port, nodeID);
            String msg = nodeID == null ? String.Format("ADMs on port {0} connected", port) : String.Format("ADM @ {0} connected", port + ":" + nodeID);
            _erdb.LogEvent(EngineRoomServiceDB.LogEventType.CONNECT, "BBEngineRoom", msg);
        }

        protected override void DisconnectADM(String port, String nodeID = null)
        {
            base.DisconnectADM(port, nodeID);
            String msg = nodeID == null ? String.Format("ADMs on port {0} disconnected", port) : String.Format("ADM @ {0} disconnected", port + ":" + nodeID);
            _erdb.LogEvent(EngineRoomServiceDB.LogEventType.DISCONNECT, "BBEngineRoom", msg);
        }

        protected override bool OnADMInactivityTimeout(ArduinoDeviceManager adm, long msQuiet)
        {
            bool success = base.OnADMInactivityTimeout(adm, msQuiet);
            String desc = String.Format("ADM (BDID={0}) @ {1} had not received a message in {2} ms. Clearing success = {3}", adm.PortAndNodeID, adm.BoardID, msQuiet, success);
            _erdb.LogEvent(EngineRoomServiceDB.LogEventType.WARNING, "BBEngineRoom", desc);

            return success;
        }

        bool monitor = true;
        protected override void MonitorADM(object sender, System.Timers.ElapsedEventArgs eventArgs)
        {
            if (monitor)
            {
                base.MonitorADM(sender, eventArgs);
            }
        }

        //Respond to incoming commands
        public override void AddCommandHelp()
        {
            base.AddCommandHelp();

            AddCommandHelp(EngineRoomMessageSchema.COMMAND_TEST, "Used during development to test stuff");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_LIST_ENGINES, "List online engines");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_ENGINE_STATUS, "Gets status of <engineID>");
            AddCommandHelp(EngineRoomMessageSchema.COMMAND_SET_ENGINE_ONLINE, "Set engine online status to <true/false>");
        }

        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
        {
            //return value of true/false determines whether response message is broadcast or not

            EngineRoomMessageSchema schema = new EngineRoomMessageSchema(response);
            Engine engine;
            switch (cmd)
            {
                case EngineRoomMessageSchema.COMMAND_TEST:
                    //schema.AddPompaCelup(_pompaCelup);
                    //Message alert = BBAlarmsService.BBAlarmsService.EngineRoomMessageSchema.RaiseAlarm(_pompaCelup.ID, true, "Test raising alarm");
                    //Broadcast(alert);

                    //Message 
                    return false;

                case EngineRoomMessageSchema.COMMAND_LIST_ENGINES:
                    List<Engine> engines = GetEngines();
                    List<String> engineIDs = new List<String>();

                    foreach(Engine eng in engines)
                    {
                        if (eng.Online) engineIDs.Add(eng.ID);
                    }
                    response.AddValue("Engines", engineIDs);
                    return true;

                case EngineRoomMessageSchema.COMMAND_ENGINE_STATUS:
                    if (args == null || args.Count == 0 || args[0] == null) throw new Exception("No engine specified");
                    engine = GetEngine(args[0].ToString());
                    if (engine == null) throw new Exception("Cannot find engine with ID " + args[0]);
                    schema.AddEngine(engine);
                    if(engine.RPM != null)message.AddValue("RPMDeviceID", engine.RPM.ID);
                    if(engine.TempSensor != null)message.AddValue("TempSensorID", engine.TempSensor.ID);
                    if (engine.OilSensor != null)message.AddValue("OilSensorDeviceID", engine.OilSensor.ID);
                    return true;

                case EngineRoomMessageSchema.COMMAND_SET_ENGINE_ONLINE:
                    if (args == null || args.Count < 1) throw new Exception("No engine specified");
                    engine = GetEngine(args[0].ToString());
                    if (engine == null) throw new Exception("Cannot find engine with ID " + args[0]);
                    if (args[1] == null) throw new Exception("Online/Offline status not specified");
                    bool online = Chetch.Utilities.Convert.ToBoolean(args[1]);
                    if (online != engine.Online)
                    {
                        engine.Online = online;
                        EngineRoomServiceDB.LogEventType let = engine.Online ? EngineRoomServiceDB.LogEventType.ONLINE : EngineRoomServiceDB.LogEventType.OFFLINE;
                        _erdb.LogEvent(let, engine.ID, "Engine now " + let.ToString());
                        schema.AddEngine(engine);
                    }
                    return true;

                default:
                    return base.HandleCommand(cnn, message, cmd, args, response);
            }
        }
    } //end service class
}
