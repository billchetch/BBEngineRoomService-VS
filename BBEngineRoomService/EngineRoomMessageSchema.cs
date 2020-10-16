﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino;
using Chetch.Messaging;
using Chetch.Arduino.Devices;
using Chetch.Arduino.Devices.Counters;
using Chetch.Arduino.Devices.Temperature;


namespace BBEngineRoomService
{
    public class EngineRoomMessageSchema : ADMService.MessageSchema
    {
        public const String COMMAND_TEST = "test";
        public const String COMMAND_LIST_ENGINES = "list-engines";
        public const String COMMAND_ENGINE_STATUS = "engine-status";
        public const String COMMAND_SET_ENGINE_ONLINE = "engine-online";

        public EngineRoomMessageSchema() { }

        public EngineRoomMessageSchema(Message message) : base(message) { }

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

        public void AddDS18B20Sensor(DS18B20Array.DS18B20Sensor sensor)
        {
            Message.AddValue("SensorID", sensor.ID);
            Message.AddValue("Temperature", sensor.AverageTemperature);
        }

        public void AddOilSensor(SwitchSensor oilSensor)
        {
            Message.AddValue(ADMService.MessageSchema.DEVICE_ID, oilSensor.ID);
            Message.AddValue(ADMService.MessageSchema.DEVICE_NAME, oilSensor.Name);
            Message.AddValue("State", oilSensor.State);
        }

        public void AddEngine(Engine engine)
        {
            Message.AddValue("Engine", engine.ID);
            Message.AddValue("EngineOnline", engine.Online);
            Message.AddValue("EngineRunning", engine.Running);
            Message.AddValue("EngineLastOn", engine.LastOn);
            Message.AddValue("EngineLastOff", engine.LastOff);
        }
    } //end MessageSchema class
}