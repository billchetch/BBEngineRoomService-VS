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

        
        public const String GENSET1_ID = "gs1";
        public const String GENSET2_ID = "gs2";
        public const String INDUK_ID = "idk";
        public const String BANTU_ID = "bnt";

        public const double RPM_CALIBRATION_BANTU = 1.0; // / 2.25;
        public const double RPM_CALIBRATION_INDUK = 1.0; // / 2.25;
        public const double RPM_CALIBRATION_GENSET1 = 1; //0.552;
        public const double RPM_CALIBRATION_GENSET2 = 0.552;
        public const int RPM_SAMPLE_SIZE = 7;
        public const int RPM_SAMPLE_INTERVAL = 3000; //ms
        public const Sampler.SamplingOptions RPM_SAMPLING_OPTIONS = Sampler.SamplingOptions.MEAN_INTERVAL_PRUNE_MIN_MAX;

        public const String POMPA_CELUP_ID = "pmp_clp";

        public const int TEMP_SAMPLE_INTERVAL = 5000;
        public const int TEMP_SAMPLE_SIZE = 3;

        public const String OIL_SENSOR_NAME = "OIL";


        private EngineRoomServiceDB _erdb;
        private SwitchSensor _pompaCelup;

        private Dictionary<String, Engine> engines = new Dictionary<String, Engine>();

        public BBEngineRoomService() : base("BBEngineRoom", "ADMTestServiceClient", "ADMTestService", "ADMTestServiceLog") // base("BBEngineRoom", "BBERClient", "BBEngineRoomService", "BBEngineRoomServiceLog") //
        {
            SupportedBoards = ArduinoDeviceManager.DEFAULT_BOARD_SET;
            AddAllowedPorts(Properties.Settings.Default.AllowedPorts);
            RequiredBoards = Properties.Settings.Default.RequiredBoards;
            MaxPingResponseTime = 100;
        }

        protected override void OnStart(string[] args)
        {
            try
            {
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connecting to Engine Room database...");
                _erdb = EngineRoomServiceDB.Create(Properties.Settings.Default, "EngineRoomDBName");
                Tracing?.TraceEvent(TraceEventType.Information, 0, "Connected to Engine Room database");
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

        protected void AddEngine(Engine engine)
        {
            engines[engine.ID] = engine;
        }

        protected Engine GetEngineForDevice(String deviceID)
        {
            if (deviceID == null) return null;
            String[] parts = deviceID.Split('_');
            if(parts.Length > 0)
            {
                return engines.ContainsKey(parts[0]) ? engines[parts[0]] : null;
            } else
            {
                return null;
            }
        }

        protected override void AddADMDevices(ArduinoDeviceManager adm, ADMMessage message)
        {
            if (adm != null) //TODO: change to switch to determine which ADM we are dealing with
            {
                //Pompa celup
                /*_pompaCelup = new SwitchSensor(10, 250, POMPA_CELUP_ID, "CELUP");
                adm.AddDevice(_pompaCelup);
                //get latest data
                DBRow row = _erdb.GetLatestEvent("ON", POMPA_CELUP_ID);
                if (row != null)
                {
                    _pompaCelup.LastOn = row.GetDateTime("created");
                }
                row = _erdb.GetLatestEvent("OFF", POMPA_CELUP_ID);
                if (row != null)
                {
                    _pompaCelup.LastOff = row.GetDateTime("created");
                }*/


                //temperature array for all engines connected to a board
                DS18B20Array temp = new DS18B20Array(3, GENSET1_ID + "_arr");
                temp.SampleInterval = TEMP_SAMPLE_INTERVAL;
                temp.SampleSize = TEMP_SAMPLE_SIZE;
                temp.SensorIDs.Add("gs1_temp");
                adm.AddDevice(temp);

                //genset 1
                RPMCounter rpm = new RPMCounter(4, GENSET1_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_GENSET1;
                adm.AddDevice(rpm);

                SwitchSensor oilSensor = new SwitchSensor(5, 250, GENSET1_ID + "_oil", OIL_SENSOR_NAME);
                adm.AddDevice(oilSensor);

                Engine engine = new Engine(GENSET1_ID, rpm, oilSensor);
                AddEngine(engine);

                //genset 2
                /*rpm = new RPMCounter(8, GENSET2_ID + "_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_GENSET2;
                adm.AddDevice(rpm);

                oli = new Chetch.Arduino.Devices.SwitchSensor(9, 250, GENSET2_ID + "_oil", OIL_SENSOR_NAME);
                adm.AddDevice(oli);

                AddEngine(GENSET2_ID);*/

                adm.Sampler.SampleProvided += HandleSampleProvided;
            }
        }

        private void HandleSampleProvided(ISampleSubject sampleSubject)
        {
            if (sampleSubject is RPMCounter)
            {
                RPMCounter rpm = (RPMCounter)sampleSubject;
                /*Sampler.SubjectData sd = GensetADM.Sampler.GetSubjectData(rpm);
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
                //Console.WriteLine("Times: {0}", l2);
                Console.WriteLine("Intervals: {0}", l3);
                Console.WriteLine("SampleCount: {0}", sd.SampleCount);
                Console.WriteLine("SampleTotal: {0}", sd.SampleTotal);
                Console.WriteLine("SampleDuration: {0}", sd.DurationTotal);*/

                //Console.WriteLine("===============================> RPM: {0}", rpm.AverageRPM);

            }

            /*if (sampleSubject is DS18B20Array.DS18B20Sensor)
            {
                DS18B20Array.DS18B20Sensor sensor = (DS18B20Array.DS18B20Sensor)sampleSubject;
                Console.WriteLine("Average temp: {0}", sensor.AverageTemperature);
            }*/
        }

        private void OnOilCheckRequired(Engine engine)
        {
            if (engine == null) return;
            Message message = null;
            String msg = null;
            switch(engine.CheckOil()){
                case Engine.OilState.LEAK:
                    msg = "Oil leak detected";
                    message = MessageSchema.RaiseAlarm(engine.OilSensor.ID, true, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT, engine.OilSensor.ID, msg);
                    break;
                case Engine.OilState.NORMAL:
                    msg = "Oil state normal";
                    message = MessageSchema.RaiseAlarm(engine.OilSensor.ID, false, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT_OFF, engine.OilSensor.ID, msg);
                    break;
                case Engine.OilState.SENSOR_FAULT:
                    msg = "Oil sensor faulty";
                    message = MessageSchema.RaiseAlarm(engine.OilSensor.ID, true, msg);
                    _erdb.LogEvent(EngineRoomServiceDB.LogEventType.ALERT, engine.OilSensor.ID, msg);
                    break;
            }

            if(message != null)
            {
                Broadcast(message);
            }
        }

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
                        if(dev is DS18B20Array)
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

                        if(dev.Name == OIL_SENSOR_NAME)
                        {
                            Engine engine = GetEngineForDevice(dev.ID);
                            OnOilCheckRequired(engine);
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
                        if(running != engine.Running)
                        {
                            engine.Running = running;
                            EngineRoomServiceDB.LogEventType let = engine.Running ? EngineRoomServiceDB.LogEventType.ON : EngineRoomServiceDB.LogEventType.OFF;
                            _erdb.LogEvent(let, engine.ID, "Engine");

                            OnOilCheckRequired(engine);
                            schema.AddEngine(engine); //add engine data to provide running/not running event changes
                        }
                        
                        //Console.WriteLine("===============================> RPM: {0}", rpm.AverageRPM);
                    }
                    break;

                case MessageType.NOTIFICATION:
                    break;

                case MessageType.CONFIGURE_RESPONSE:
                    break;
            }

            base.HandleADMMessage(message, adm);
        }

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
