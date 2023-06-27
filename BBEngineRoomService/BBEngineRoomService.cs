﻿using System;
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

            }
            catch (Exception e)
            {
                Tracing?.TraceEvent(TraceEventType.Error, 0, e.Message);
                throw e;
            }

        }

        private System.Timers.Timer _testTimer = new System.Timers.Timer();
        private SwitchDevice _testSwitch;
        private bool _flag = true;
        private int _onCount = 0;
        private Chetch.Arduino2.Devices.Ticker _ticker;
        private Chetch.Arduino2.Devices.Diagnostics.TestDevice01 _testDevice;
        private TestBandwidth _testBandwidth;
        private Engine _testEngine;
        public Engine TestEngine { get { return _testEngine;  } }

        virtual protected void OnTestTimer(Object sender, EventArgs earg)
        {
            if (!_enginesADM.IsReady) return;
            /*if (!_testSwitch.IsOn)
            {
                _testSwitch.TurnOn(10);
            } else
            {
                Console.WriteLine("oh no off...");
            }*/

            /*if (_flag)
            {
                _testSwitch.TurnOn();
                _onCount++;
            }
            else
            {
                _testSwitch.TurnOff();
                //Console.WriteLine("Turning it off");
            }*/
            //_testBandwidth.RequestStatus();
            //Console.WriteLine("Sent status request...");

            /*String s2e = "Hey...";
            _testBandwidth.Echo(s2e);
            Console.WriteLine("Echo {0}", s2e);


            String s2a = "Yip...";
            _testBandwidth.Analyse(s2a);
            Console.WriteLine("Analayse {0}", s2a);*/
            _flag = !_flag;
        }

        protected void OnTestResults(object sender, TestBandwidth.TestEventArgs ea)
        {
            if (!ea.Results.IsTesting && ea.Results.MessagesReceived == ea.Results.MessagesSent)
            {

            }
            else
            {
                String s = TestBandwidth.FormatResultsAsString(ea.Results);
                Console.WriteLine(s);

                TestBandwidth tbw = (TestBandwidth)sender;
                Console.WriteLine("Remote: mrec: {0}, msent: {1}, cts: {2}, rb used: {3}, sb used: {4}, b2r: {5}", tbw.RemoteMessagesReceived, tbw.RemoteMessagesSent, tbw.RemoteCTS, tbw.RemoteRBUsed, tbw.RemoteSBUsed, tbw.RemoteBytesToRead);
                
             }
        }

        override public void Test(String[] args = null)
        {
            Console.WriteLine("Setting RPM to 2000 and waiting for threhsold then mointoring RPM...");
            _testEngine.RPMSensor.RPM = 2000;
            System.Threading.Thread.Sleep(Engine.RUNNING_FOR_THRESHOLD*1000 + 500);
            _testEngine.monitorRPM();

            Console.WriteLine("Wait for a while...");
            System.Threading.Thread.Sleep(3000);

            Console.WriteLine("Now set RPM to 0...");
            _testEngine.RPMSensor.RPM = 0;

            /*Console.WriteLine("Setting RPM to 3000 and waiting...");
            _testEngine.RPMSensor.RPM = 3000;
            _testEngine.monitorRPM();
            System.Threading.Thread.Sleep(2000);

            Console.WriteLine("Setting RPM to 500 and waiting...");
            _testEngine.RPMSensor.RPM = 500;
            _testEngine.monitorRPM();
            System.Threading.Thread.Sleep(2000);

            Console.WriteLine("Setting RPM to 1500 and waiting...");
            _testEngine.RPMSensor.RPM = 1500;
            _testEngine.monitorRPM();
            System.Threading.Thread.Sleep(2000);*/


        }

        protected override bool CreateADMs()
        {
            try
            {
                String networkServiceURL = (String)Settings["NetworkServiceURL"];
                String enginesServiceName = "crayfish";
                String gensetsServiceName = "";

                _enginesADM = ArduinoDeviceManager.Create(enginesServiceName, networkServiceURL, 256, 256);
                //_gensetsADM = ArduinoDeviceManager.Create(gensetsServiceName, networkServiceURL, 256, 256);

                _testEngine = new Engine("gs1", 18, 4, 12);

                _testEngine.RPMSensor.ReportInterval = 2000;
                _testEngine.RPMSensor.DataReceived += (Object sender, Chetch.Arduino2.ArduinoObject.MessageReceivedArgs ea) =>
                {
                    var r = (Engine.RPMCounter)sender;
                    //Console.WriteLine(" >>>>>>>>>>>>>>>>> {0}  count {1},  intervals per sec {2}, count per sec {3}, rpm {4}", r.UID, r.Count, r.IntervalsPerSecond, r.CountPerSecond, r.RPM);
                };
                

                _ticker = new Ticker("tk1", 6, 20);
                _ticker.ReportInterval = 1000;
                _ticker.DataReceived += (Object sender, Chetch.Arduino2.ArduinoObject.MessageReceivedArgs ea) =>
                {
                    var tk = (Ticker)sender;
                    //Console.WriteLine(" >>>>>>>>>>>>>>>>> {0} tick count {1}", tk.UID, tk.TickCount);
                };

                
                //_testBandwidth.ReportInterval = 1000;

                _enginesADM.AddDeviceGroup(_testEngine);
                //_enginesADM.AddDevice(oilSensor);
                //_enginesADM.AddDevice(_testSwitch);
                //_enginesADM.AddDevice(_testBandwidth);
                _enginesADM.AddDevice(_ticker);
                


                AddADM(_enginesADM);
                //AddADM(_gensetsADM);

                //add alarm raisers
                _alarmManager.AddRaisers(GetArduinoObjects());
                _alarmManager.AlarmStateChanged += (Object sender, AlarmManager.Alarm alarm) =>
                {
                    _alarmManager.NotifyAlarmsService(this, alarm);
                };
                
                return true;
            } catch (Exception e)
            {
                return false;
            }
        }

        protected override void OnADMsReady()
        {
            Console.WriteLine("All adms are ready to use brah...");

            //_testBandwidth.StartTest(6*60*60, 200, 50);
        }

        protected override void OnStop()
        {
            _testBandwidth?.StopTest();

            base.OnStop();
        }

        protected override void HandleAOPropertyChange(object sender, PropertyChangedEventArgs eargs)
        {
            base.HandleAOPropertyChange(sender, eargs);

            if(sender is Engine.OilSensorSwitch)
            {
                Console.WriteLine("Hey oil sensor found");
            }
            if(sender is Engine.RPMCounter)
            {
                var rpm = ((Engine.RPMCounter)sender);
                //Console.WriteLine("RPM count {0} cps {1} rpm {2}", rpm.Count, rpm.CountPerSecond, rpm.RPM);
            }
            if(sender is TestDevice01)
            {
                var td = (TestDevice01)sender;
                Console.WriteLine("TestDevice returns {0}", td.TestValue);
            }
            if (sender is TestBandwidth)
            {
                var bw = (TestBandwidth)sender;
                //Console.WriteLine("Test bandwidth returns...{0} / {1} messages received / sent", bw.MessagesReceived, bw.MessagesSent);
            }
        }

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
            switch (cmd)
            {
                case BBAlarmsService.AlarmsMessageSchema.COMMAND_ALARM_STATUS:
                    if(args.Count == 0)
                    {
                        throw new Exception("Please pecify an alarm ID");
                    }
                    var alarmID = args[0].ToString();
                    Tracing?.TraceEvent(TraceEventType.Information, 1000, "Requesting update alarm {0}", alarmID);
                    try
                    {
                        _alarmManager.RequestUpdateAlarms(alarmID);
                    } catch (Exception e)
                    {
                        Tracing?.TraceEvent(TraceEventType.Error, 1000, "Requesting update alarm {0} error: {1}", alarmID, e.Message);
                    }
                    return false; //no need to send a response (save on bandwidth)

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
