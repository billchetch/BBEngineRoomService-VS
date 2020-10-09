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
        new public class MessageSchema : ADMService.MessageSchema
        {
            public const String COMMAND_TEST = "test";

            public MessageSchema() { }

            public MessageSchema(Message message) : base(message) { }

            public void AddPompaCelup(SwitchSensor pompaCelup)
            {
                Message.AddValue(ADMService.MessageSchema.DEVICE_ID, pompaCelup.ID);
                Message.AddValue("State", pompaCelup.State);
                Message.AddValue("LastOn", pompaCelup.LastOn);
                Message.AddValue("LastOff", pompaCelup.LastOff);
            }


            public void AddRPM(RPMCounter rpm)
            {
                Message.AddValue(ADMService.MessageSchema.DEVICE_ID, rpm.ID);
                Message.AddValue(ADMService.MessageSchema.DEVICE_NAME, rpm.Name);
                Message.AddValue("AverageRPM", rpm.AverageRPM);
            }

            public void AddDS18B20Array(DS18B20Array ta)
            {
                Message.AddValue(ADMService.MessageSchema.DEVICE_NAME, ta.Name);
                Message.AddValue(ADMService.MessageSchema.DEVICE_ID, ta.ID);
                Dictionary<String, double> tmap = new Dictionary<String, double>();

                foreach (DS18B20Array.DS18B20Sensor sensor in ta.Sensors)
                {
                    tmap[sensor.ID] = sensor.AverageTemperature;
                }
                Message.AddValue("Sensors", tmap);
            }

            public void AddEngine(Engine engine)
            {
                Message.AddValue("Engine", engine.ID);
                Message.AddValue("EngineRunning", engine.Running);
                Message.AddValue("EngineLastOn", engine.LastOn);
                Message.AddValue("EngineLastOff", engine.LastOff);
            }
        }

        public const int TIMER_STATE_LOG_INTERVAL = 10 * 1000;

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

        public const int TEMP_SAMPLE_INTERVAL = 30000; //temp changes very slowly in the engine so no need to sample frequently
        public const int TEMP_SAMPLE_SIZE = 3;

        public const String OIL_SENSOR_NAME = "OIL";

        private ArduinoDeviceManager _er1ADM;
        private ArduinoDeviceManager _er2ADM;
        private EngineRoomServiceDB _erdb;
        private SwitchSensor _pompaCelup;

        private Dictionary<String, Engine> engines = new Dictionary<String, Engine>();

        private System.Timers.Timer _timerStateLog;

        public bool PauseOutput = false; //TODO: REMOVE THIS!!!

        public BBEngineRoomService() : base("BBEngineRoom", "ADMTestServiceClient", "ADMTestService", "ADMTestServiceLog") // base("BBEngineRoom", "BBERClient", "BBEngineRoomService", "BBEngineRoomServiceLog") //
        {
            SupportedBoards = ArduinoDeviceManager.DEFAULT_BOARD_SET;
            AddAllowedPorts(Properties.Settings.Default.AllowedPorts);
            RequiredBoards = "ER2"; // Properties.Settings.Default.RequiredBoards;
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

        public override void AddCommandHelp()
        {
            base.AddCommandHelp();

            AddCommandHelp(MessageSchema.COMMAND_TEST, "Used during development to test stuff");
        }

        protected void OnStateLogTimer(Object sender, System.Timers.ElapsedEventArgs eventArgs)
        {
            //build up a picture of the state of the engine room and log it
            List<Engine> engines = GetEngines();
            foreach(Engine engine in engines)
            {
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

        protected override void AddADMDevices(ArduinoDeviceManager adm, ADMMessage message)
        {
            if (adm == null || adm.BoardID == null)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, "adm is null or does not have a BoardID value");
                return;
            }

            adm.Tracing = Tracing;
            adm.Sampler.SampleProvided += HandleSampleProvided;

            DS18B20Array temp;
            Engine engine;
            RPMCounter rpm;
            SwitchSensor oilSensor;
            
            
            if (adm.BoardID.Equals("ER1"))
            {
                //Pompa celup
                /*_pompaCelup = new SwitchSensor(6, 250, POMPA_CELUP_ID, "CELUP");
                adm.AddDevice(_pompaCelup);
                //get latest data
                DBRow row = _erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.ON, POMPA_CELUP_ID);
                if (row != null)
                {
                    _pompaCelup.LastOn = row.GetDateTime("created");
                }
                row = _erdb.GetLatestEvent(EngineRoomServiceDB.LogEventType.OFF, POMPA_CELUP_ID);
                if (row != null)
                {
                    _pompaCelup.LastOff = row.GetDateTime("created");
                }*/


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
                
                //SwitchSensor oilSensor = new SwitchSensor(6, 250, GENSET1_ID + "_oil", OIL_SENSOR_NAME);
                //adm.AddDevice(oilSensor);

                engine = new Engine(INDUK_ID, rpm, null, temp.GetSensor(INDUK_ID + "_temp"));
                adm.AddDeviceGroup(engine);

                //genset 2
                rpm = new RPMCounter(8, BANTU_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_BANTU;
                
                //SwitchSensor oilSensor = new Chetch.Arduino.Devices.SwitchSensor(9, 250, GENSET2_ID + "_oil", OIL_SENSOR_NAME);
                
                engine = new Engine(BANTU_ID, rpm, null, temp.GetSensor(BANTU_ID + "_temp"));
                adm.AddDeviceGroup(engine);

                _er1ADM = adm;
            } else if (adm.BoardID.Equals("ER2")) //TODO: change to switch to determine which ADM we are dealing with
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
                rpm.SampleIntervalDeviation = -1;
                
                //SwitchSensor oilSensor = new SwitchSensor(6, 250, GENSET1_ID + "_oil", OIL_SENSOR_NAME);
                
                engine = new Engine(GENSET1_ID, rpm, null, temp.GetSensor(GENSET1_ID + "_temp"));
                adm.AddDeviceGroup(engine);

                //genset 2
                rpm = new RPMCounter(8, GENSET2_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.SampleIntervalDeviation = 15; //permiited devication (ms) from the expected interval (ms)
                rpm.Calibration = RPM_CALIBRATION_GENSET2;
                
                //SwitchSensor oilSensor = new Chetch.Arduino.Devices.SwitchSensor(9, 250, GENSET2_ID + "_oil", OIL_SENSOR_NAME);
                
                engine = new Engine(GENSET2_ID, rpm, null, temp.GetSensor(GENSET2_ID + "_temp"));
                adm.AddDeviceGroup(engine);

                _er2ADM = adm;
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

        private void HandleSampleProvided(ISampleSubject sampleSubject)
        {
            if (sampleSubject is RPMCounter)
            {
                RPMCounter rpm = (RPMCounter)sampleSubject;
                if (rpm.ID.Equals("gs2_rpm") && !PauseOutput)
                {
                    outputSampleData(rpm.Sampler.GetSubjectData(rpm));
                    Console.WriteLine("===============================> RPM {0}: {1}", rpm.ID, rpm.AverageRPM);
                }
            }

            if (sampleSubject is DS18B20Array.DS18B20Sensor)
            {
                DS18B20Array.DS18B20Sensor sensor = (DS18B20Array.DS18B20Sensor)sampleSubject;
                if (sensor.ID.Equals("gs2_temp") && !PauseOutput)
                {
                    outputSampleData(sensor.Sampler.GetSubjectData(sensor));
                    Console.WriteLine("------------------------------> Average temp {0}: {1}", sensor.ID, sensor.AverageTemperature);
                }
            }
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
            MessageSchema schema = new MessageSchema(message);

            switch (message.Type)
            {
                case MessageType.DATA:
                    if (message.Sender == null)
                    {
                        dev = adm.GetDeviceByBoardID(message.TargetID);
                        if (dev is DS18B20Array)
                        {
                            schema.AddDS18B20Array((DS18B20Array)dev);
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

                        if (dev.Name == OIL_SENSOR_NAME)
                        {
                            Engine engine = GetEngineForDevice(dev.ID);
                            //OnOilCheckRequired(engine);
                            Console.WriteLine("Oil Sensor {0} {1}", dev.ID, ((SwitchSensor)dev).IsOn);
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

                        //determine engine running state
                        Engine engine = GetEngineForDevice(rpm.ID);
                        if (engine == null) throw new Exception("No engine found for RPM device " + rpm.ID);
                        bool running = rpm.AverageRPM > Engine.IS_RUNNING_RPM_THRESHOLD;
                        if (running != engine.Running)
                        {
                            engine.Running = running;
                            EngineRoomServiceDB.LogEventType let = engine.Running ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF;
                            _erdb.LogEvent(let, engine.ID, "Engine");

                            //OnOilCheckRequired(engine);
                            schema.AddEngine(engine); //add engine data to provide running/not running event changes
                        }
                    }
                    break;

                case MessageType.NOTIFICATION:
                    break;

                case MessageType.CONFIGURE_RESPONSE:
                    //Console.WriteLine("===============================> MESG: {0}", message);
                    break;
            }
            base.HandleADMMessage(message, adm);
        }

        //Respond to incoming commands
        override public bool HandleCommand(Connection cnn, Message message, String cmd, List<Object> args, Message response)
        {
            MessageSchema schema = new MessageSchema(response);
            switch (cmd)
            {
                case MessageSchema.COMMAND_TEST:
                    //schema.AddPompaCelup(_pompaCelup);
                    //Message alert = BBAlarmsService.BBAlarmsService.MessageSchema.RaiseAlarm(_pompaCelup.ID, true, "Test raising alarm");
                    //Broadcast(alert);

                    //Message 
                    return false;

                default:
                    return base.HandleCommand(cnn, message, cmd, args, response);
            }
        }
    } //end service class
}
