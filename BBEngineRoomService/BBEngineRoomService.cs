using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino;
using Chetch.Arduino.Devices.Counters;
using Chetch.Arduino.Devices.Temperature;
using Chetch.Arduino.Devices;
using Chetch.Messaging;
using System.Diagnostics;
using Chetch.Utilities;
using Chetch.Database;

namespace BBEngineRoomService
{
    public class BBEngineRoomService : ADMService
    {
        public class Pump : SwitchSensor
        {
            public const String SENSOR_NAME = "PUMP";

            public Pump(int pinNumber, String id) : base(pinNumber, 250, id, SENSOR_NAME) { }

            public void initialise(EngineRoomServiceDB erdb)
            {
                //get latest data
                DBRow row = erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, ID);
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

        public class OilSensor : SwitchSensor
        {
            public const String SENSOR_NAME = "OIL";

            public OilSensor(int pinNumber, String id) : base(pinNumber, 250, id, SENSOR_NAME) { }
        } //end oil sensor

        public const int TIMER_STATE_LOG_INTERVAL = 60 * 1000;

        public const String INDUK_ID = "idk";
        public const String BANTU_ID = "bnt";
        public const String GENSET1_ID = "gs1";
        public const String GENSET2_ID = "gs2";
        
        public const double RPM_CALIBRATION_BANTU = 0.47; // 17/8
        public const double RPM_CALIBRATION_INDUK = 0.47; // / 17/8;
        public const double RPM_CALIBRATION_GENSET1 = 0.545;
        public const double RPM_CALIBRATION_GENSET2 = 0.56; 
        public const int RPM_SAMPLE_SIZE = 7;
        public const int RPM_SAMPLE_INTERVAL = 2000; //ms
        public const Sampler.SamplingOptions RPM_SAMPLING_OPTIONS = Sampler.SamplingOptions.MEAN_INTERVAL_PRUNE_MIN_MAX;

        public const String POMPA_CELUP_ID = "pmp_clp";
        public const String POMPA_SOLAR_ID = "pmp_sol";


        public const int TEMP_SAMPLE_INTERVAL = 20000; //temp changes very slowly in the engine so no need to sample frequently
        public const int TEMP_SAMPLE_SIZE = 3;
        
        private EngineRoomServiceDB _erdb;
        private Pump _pompaCelup;
        private Pump _pompaSolar;

        private Dictionary<String, Engine> engines = new Dictionary<String, Engine>();

        private System.Timers.Timer _timerStateLog;

        public bool PauseOutput = false; //TODO: REMOVE THIS!!!

        public BBEngineRoomService() : base("BBEngineRoom", "ADMTestServiceClient", "ADMTestService", null) // base("BBEngineRoom", "BBERClient", "BBEngineRoomService", "BBEngineRoomServiceLog") //
        {
            SupportedBoards = ArduinoDeviceManager.DEFAULT_BOARD_SET;
            AddAllowedPorts(Properties.Settings.Default.AllowedPorts);
            RequiredBoards = "ER1,ER2"; // Properties.Settings.Default.RequiredBoards;
            MaxPingResponseTime = 100;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connecting to Engine Room database...");
                _erdb = EngineRoomServiceDB.Create(Properties.Settings.Default, "EngineRoomDBName");
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected to Engine Room database");

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
            foreach(Engine engine in engines)
            {
                if (!engine.Online) continue;
                _erdb.LogState(engine.ID, "Running", engine.Running);
                if(engine.RPM != null)_erdb.LogState(engine.ID, "RPM", engine.RPM.AverageRPM);
                if(engine.OilSensor != null)_erdb.LogState(engine.ID, "OilSensor", engine.OilSensor.State);
                if (engine.TempSensor != null) _erdb.LogState(engine.ID, "Temperature", engine.TempSensor.Temperature);
            }
        }

        protected List<Engine> GetEngines()
        {
            List<Engine> engines = new List<Engine>();
            foreach (var adm in ADMS.Values)
            {
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
            if (adm == null || adm.BoardID == null)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, "adm is null or does not have a BoardID value");
                return;
            }

            adm.Tracing = Tracing;
            //adm.Sampler.SampleProvided += HandleSampleProvided;
            adm.Sampler.SampleError += HandleSampleError;

            DS18B20Array temp;
            Engine engine;
            RPMCounter rpm;
            OilSensor oilSensor;
            String desc;
            
            if (adm.BoardID.Equals("ER1"))
            {
                //Pompa celup
                /*_pompaCelup = new Pump(6, POMPA_CELUP_ID);
                _pompaCelup.initialise(_erdb);
                adm.AddDevice(_pompaCelup);
                
                //Pompa solar
                /*_pompaSolar = new Pump(5, POMPA_SOLAR_ID);
                _pompaSolar.initialise(_erdb);
                adm.AddDevice(_pompaSolar);
                */

                //temperature array for all engines connected to a board
                temp = new DS18B20Array(5, "temp_arr");
                temp.SampleInterval = TEMP_SAMPLE_INTERVAL;
                temp.SampleSize = TEMP_SAMPLE_SIZE;
                temp.AddSensor(INDUK_ID + "_temp");
                temp.AddSensor(BANTU_ID + "_temp");
                adm.AddDevice(temp);
                
                //Induk
                rpm = new RPMCounter(4, INDUK_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_INDUK;
                rpm.SampleIntervalDeviation = 15;

                //oilSensor = new OilSensor(6, GENSET1_ID + "_oil");
                //adm.AddDevice(oilSensor);

                engine = new Engine(INDUK_ID, rpm, null, temp.GetSensor(INDUK_ID + "_temp"));
                engine.initialise(_erdb);
                adm.AddDeviceGroup(engine);
                desc = String.Format("Added engine {0} to {1} .. engine is {2}", engine.ID, adm.BoardID, engine.Online ? "online" : "offline");
                Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADDED, engine.ID, desc);

                //genset 2
                rpm = new RPMCounter(8, BANTU_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_BANTU;
                rpm.SampleIntervalDeviation = 15;

                //oilSensor = new OilSensor(9, GENSET2_ID + "_oil");

                engine = new Engine(BANTU_ID, rpm, null, temp.GetSensor(BANTU_ID + "_temp"));
                engine.initialise(_erdb);
                adm.AddDeviceGroup(engine);
                desc = String.Format("Added engine {0} to {1} .. engine is {2}", engine.ID, adm.BoardID, engine.Online ? "online" : "offline");
                Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADDED, engine.ID, desc);
            }
            else if (adm.BoardID.Equals("ER2")) //TODO: change to switch to determine which ADM we are dealing with
            {
                //temperature array for all engines connected to a board
                temp = new DS18B20Array(5, "temp_arr");
                temp.SampleInterval = TEMP_SAMPLE_INTERVAL;
                temp.SampleSize = TEMP_SAMPLE_SIZE;
                temp.AddSensor(GENSET2_ID + "_temp");
                temp.AddSensor(GENSET1_ID + "_temp");
                adm.AddDevice(temp);
                
                //genset 1
                rpm = new RPMCounter(4, GENSET1_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_GENSET1;
                rpm.SampleIntervalDeviation = 15;
                
                //oilSensor = new OilSensor(6, GENSET1_ID + "_oil");
                
                engine = new Engine(GENSET1_ID, rpm, null, temp.GetSensor(GENSET1_ID + "_temp"));
                engine.initialise(_erdb);
                adm.AddDeviceGroup(engine);
                desc = String.Format("Added engine {0} to {1} .. engine is {2}", engine.ID, adm.BoardID, engine.Online ? "online" : "offline");
                Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADDED, engine.ID, desc);

                //genset 2
                rpm = new RPMCounter(8, GENSET2_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.SampleIntervalDeviation = 15; //permiited devication (ms) from the expected interval (ms)
                rpm.Calibration = RPM_CALIBRATION_GENSET2;
                
                //oilSensor = new OilSensor(9, GENSET2_ID + "_oil");
                
                engine = new Engine(GENSET2_ID, rpm, null, temp.GetSensor(GENSET2_ID + "_temp"));
                engine.initialise(_erdb);
                adm.AddDeviceGroup(engine);
                desc = String.Format("Added engine {0} to {1} .. engine is {2}", engine.ID, adm.BoardID, engine.Online ? "online" : "offline");
                Tracing?.TraceEvent(TraceEventType.Information, 0, desc);
                _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ADDED, engine.ID, desc);
            }
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

        private void HandleSampleError(ISampleSubject subject, Exception e)
        {
            String desc = String.Format("Error when sampling subject {0}: {1} {2}", subject.GetType(), e.GetType(), e.Message);
            Tracing?.TraceEvent(TraceEventType.Error, 0, desc);
            _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ERROR, subject.GetType().ToString(), desc);
        }
        private void OnOilCheckRequired(Engine engine)
        {
            if (engine == null) return;
            Message message = null;
            String msg = null;
            switch(engine.CheckOil()){
                case Engine.OilState.LEAK:
                    msg = "Oil leak detected";
                    message = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(engine.OilSensor.ID, BBAlarmsService.AlarmState.CRITICAL, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT, engine.OilSensor.ID, msg);
                    break;
                case Engine.OilState.NORMAL:
                    msg = "Oil state normal";
                    message = BBAlarmsService.AlarmsMessageSchema.AlertAlarmStateChange(engine.OilSensor.ID, BBAlarmsService.AlarmState.OFF, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT_OFF, engine.OilSensor.ID, msg);
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
                                Console.WriteLine("------------------------------> Average temp {0}: {1}", sensor.ID, sensor.AverageTemperature);
                            }
                        }
                    }
                    else
                    {
                        dev = adm.GetDevice(message.Sender);
                        if (dev == _pompaCelup)
                        {
                            schema.AddPompaCelup(_pompaCelup);
                            _erdb.LogEvent(_pompaCelup.IsOn ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF, _pompaCelup.ID, "Pompa Celup");
                        }

                        if (dev is OilSensor)
                        {
                            Engine engine = GetEngineForDevice(dev.ID);
                            //OnOilCheckRequired(engine);
                            Console.WriteLine("+++++++++++++++> Oil Sensor {0} {1}", dev.ID, ((SwitchSensor)dev).IsOn);
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
                        Console.WriteLine("===============================> RPM {0}: {1}", rpm.ID, rpm.AverageRPM);

                        //determine engine running state
                        Engine engine = GetEngineForDevice(rpm.ID);
                        if (engine == null) throw new Exception("No engine found for RPM device " + rpm.ID);
                        if (engine.Online)
                        {
                            bool running = rpm.AverageRPM > Engine.IS_RUNNING_RPM_THRESHOLD;
                            if (running != engine.Running)
                            {
                                engine.Running = running;
                                EngineRoomServiceDB.LogEventType let = engine.Running ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF;
                                _erdb.LogEvent(let, engine.ID, "Engine now " + let.ToString());

                                //OnOilCheckRequired(engine);
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
                        Tracing?.TraceEvent(TraceEventType.Information, 0, "Temperature array {0} on board {1} configured {2} sensors", dev.ID, adm.BoardID, ((DS18B20Array)dev).ConnectedSensors.Count);
                    }
                    break;
            }
            base.HandleADMMessage(message, adm);
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
