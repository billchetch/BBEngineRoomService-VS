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
    class BBEngineRoomService : ADMService
    {
        new public class MessageSchema : Chetch.Messaging.MessageSchema
        {
            public const String COMMAND_TEST = "test";

            static public Message Alert(String deviceID, bool testing = false)
            {
                Message msg = new Message(MessageType.ALERT);
                msg.AddValue(ADMService.MessageSchema.DEVICE_ID, deviceID);
                msg.AddValue("Testing", testing);

                return msg;
            }

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
        }

        public const double RPM_CALIBRATION_BANTU = 1.0; // / 2.25;
        public const double RPM_CALIBRATION_INDUK = 1.0; // / 2.25;
        public const double RPM_CALIBRATION_GENSET1 = 1; //0.552;
        public const double RPM_CALIBRATION_GENSET2 = 0.552;
        public const int RPM_SAMPLE_SIZE = 7;
        public const int RPM_SAMPLE_INTERVAL = 3000; //ms
        public const Sampler.SamplingOptions RPM_SAMPLING_OPTIONS = Sampler.SamplingOptions.MEAN_INTERVAL_PRUNE_MIN_MAX;

        public const String POMPA_CELUP_ID = "pmp_clp";

        public const int TEMP_SAMPLE_INTERVAL = 20000;
        public const int TEMP_SAMPLE_SIZE = 3;

        private EngineRoomServiceDB _erdb;

        public ArduinoDeviceManager MesinADM { get; internal set; }
        public ArduinoDeviceManager GensetADM { get; internal set; }
        private SwitchSensor _pompaCelup;

        public BBEngineRoomService() : base("BBEngineRoom", "BBERClient", "BBEngineRoomService", "BBEngineRoomServiceLog") //base("BBEngineRoom", "ADMTestServiceClient", "ADMTestService", "ADMTestServiceLog") // 
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

        protected override void AddADMDevices(ArduinoDeviceManager adm, ADMMessage message)
        {
            if (adm != null) //TODO: change to switch to determine which ADM we are dealing with
            {
                //Pompa celup
                _pompaCelup = new SwitchSensor(5, 250, POMPA_CELUP_ID, "CELUP");
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
                }


                //temperature array for all engines connected to a board
                /*DS18B20Array temp = new DS18B20Array(4, "tmp_arr");
                temp.SampleInterval = TEMP_SAMPLE_INTERVAL; //we have to do this to avoid eating too many cycles and reducing RPM accuracy
                temp.SampleSize = TEMP_SAMPLE_SIZE;
                adm.AddDevice(temp);*/

                //genset 1
                RPMCounter rpm = new RPMCounter(4, "gs1_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_GENSET1;
                adm.AddDevice(rpm);

                //Chetch.Arduino.Devices.SwitchSensor oli = new Chetch.Arduino.Devices.SwitchSensor(7, 250, "gs1_oli", "OLI");
                //adm.AddDevice(oli);

                //genset 2
                /*rpm = new RPMCounter(8, "gs2_rpm", "RPM");
                rpm.SampleInterval = RPM_SAMPLE_INTERVAL;
                rpm.SampleSize = RPM_SAMPLE_SIZE;
                rpm.SamplingOptions = RPM_SAMPLING_OPTIONS;
                rpm.Calibration = RPM_CALIBRATION_GENSET2;
                adm.AddDevice(rpm);

                oli = new Chetch.Arduino.Devices.SwitchSensor(9, 250, "gs2_oli", "OLI");
                adm.AddDevice(oli);*/

                adm.Sampler.SampleProvided += HandleSampleProvided;
                GensetADM = adm; //keep a named ref

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

        protected override void HandleADMMessage(ADMMessage message, ArduinoDeviceManager adm)
        {
            ArduinoDevice dev;
            MessageSchema schema = new MessageSchema(message);

            switch (message.Type)
            {
                case MessageType.DATA:
                    dev = adm.GetDevice(message.Sender);
                    if (dev == _pompaCelup)
                    {
                        schema.AddPompaCelup(_pompaCelup);
                        _erdb.LogEvent(_pompaCelup.IsOn ? "ON" : "OFF", _pompaCelup.ID, "Pompa Celup");

                    }
                    break;

                case MessageType.COMMAND_RESPONSE:
                    //Console.WriteLine("Command Response: {0}", message);
                    dev = adm.GetDeviceByBoardID(message.TargetID);
                    if (dev is RPMCounter)
                    {
                        RPMCounter rpm = (RPMCounter)dev;
                        schema.AddRPM(rpm);
                        message.Type = MessageType.DATA; //change the type so that it's more meaingful for listeners...
                        Console.WriteLine("===============================> RPM: {0}", rpm.AverageRPM);
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
