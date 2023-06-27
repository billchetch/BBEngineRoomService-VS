using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino2;
using Chetch.Messaging;
using Chetch.Arduino2.Devices;
using Chetch.Arduino2.Devices.Temperature;


namespace BBEngineRoomService
{
    public class EngineRoomMessageSchema : ADMService.MessageSchema
    {
        public const String COMMAND_TEST = "test";
        public const String COMMAND_LIST_ENGINES = "list-engines";
        public const String COMMAND_ENGINE_STATUS = "engine-status";
        public const String COMMAND_ENABLE_ENGINE = "enable-engine";
        public const String COMMAND_PUMP_STATUS = "pump-status";
        public const String COMMAND_ENABLE_PUMP = "enable-pump";
        public const String COMMAND_WATER_STATUS = "water-status";
        public const String COMMAND_ENABLE_WATER = "enable-water";
        public const String COMMAND_WATER_TANK_STATUS = "water-tank-status";

        public EngineRoomMessageSchema() { }

        public EngineRoomMessageSchema(Message message) : base(message) { }

        /*public void AddPump(Pump pump)
        {
            Message.AddValue(ADMService.MessageSchema.DEVICE_ID, pump.ID);
            Message.AddValue(ADMService.MessageSchema.DEVICE_NAME, pump.Name);
            Message.AddValue("Enabled", pump.Enabled);
            Message.AddValue("State", pump.State);
            Message.AddValue("LastOn", pump.LastOn);
            Message.AddValue("LastOff", pump.LastOff);
        }

        public void AddRPM(RPMCounter rpm)
        {
            Message.AddValue(ADMService.MessageSchema.DEVICE_ID, rpm.ID);
            Message.AddValue(ADMService.MessageSchema.DEVICE_NAME, rpm.Name);
            Message.AddValue("AverageRPM", rpm.AverageRPM);
            Message.AddValue("RPM", rpm.RPM);
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
            Message.AddValue("EngineEnabled", engine.Enabled);
            Message.AddValue("EngineRunning", engine.Running);
            Message.AddValue("EngineLastOn", engine.LastOn);
            Message.AddValue("EngineLastOff", engine.LastOff);
            if (engine.RPM != null) Message.AddValue("RPMDeviceID", engine.RPM.ID);
            if (engine.TempSensor != null) Message.AddValue("TempSensorID", engine.TempSensor.ID);
            if (engine.OilSensor != null) Message.AddValue("OilSensorDeviceID", engine.OilSensor.ID);

        }

        public void AddWaterTank(WaterTanks.FluidTank waterTank)
        {
            Message.AddValue(ADMService.MessageSchema.DEVICE_ID, waterTank.ID);
            Message.AddValue(ADMService.MessageSchema.DEVICE_NAME, waterTank.Name);
            Message.AddValue("Capacity", waterTank.Capacity);
            Message.AddValue("PercentFull", waterTank.PercentFull);
            Message.AddValue("Remaining", waterTank.Remaining);
            Message.AddValue("Level", waterTank.Level);
            Message.AddValue("Distance", waterTank.Distance);
            Message.AddValue("Enabled", waterTank.Enabled);
        }

        public void AddWaterTanks(WaterTanks waterTanks)
        {
            List<String> ids = new List<String>();
            foreach(WaterTanks.FluidTank tank in waterTanks.Tanks)
            {
                ids.Add(tank.ID);
            }
            Message.AddValue("Tanks", ids);
            Message.AddValue("Capacity", waterTanks.Capacity);
            Message.AddValue("PercentFull", waterTanks.PercentFull);
            Message.AddValue("Remaining", waterTanks.Remaining);
            Message.AddValue("Level", waterTanks.Level);
            Message.AddValue("Enabled", waterTanks.Enabled);
        }*/
    } //end MessageSchema class
}
