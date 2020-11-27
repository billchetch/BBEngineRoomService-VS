using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Arduino;
using Chetch.Arduino.Devices.RangeFinders;

namespace BBEngineRoomService
{
    public class WaterTanks : Chetch.Arduino.ArduinoDeviceGroup
    {
        public class WaterTank : JSN_SR04T
        {
            public int PercentFull { 
                get
                {
                    return 100 - ((int)Math.Round(AveragePercentage / (double)PERCENTAGE_PRECISION) * PERCENTAGE_PRECISION);
                } 
            }
            public WaterTank(int transmitPin, int receivePin, String id) : base(transmitPin, receivePin, id) { }
        }

        public const int PERCENTAGE_PRECISION = 5;
        public const int DEFAULT_SAMPLE_INTERVAL = 10000;
        public const int DEFAULT_SAMPLE_SIZE = 20;

        public int SampleInterval { get; set; } = DEFAULT_SAMPLE_INTERVAL;
        public int SampleSize { get; set; } = DEFAULT_SAMPLE_SIZE;

        public List<WaterTank> Tanks { get; } = new List<WaterTank>();

        public int PercentFull
        {
            get
            {
                if (Tanks.Count == 0) return 0;

                int total = 0;
                foreach (var wt in Tanks)
                {
                    total += wt.PercentFull;
                }

                return ((int)Math.Round(total / Tanks.Count / (double)PERCENTAGE_PRECISION) * PERCENTAGE_PRECISION);
            }
        }

        public WaterTanks() : base("wts", null) { }

        public WaterTank AddTank(String id, int transmitPin, int receivePin, int minDistance = JSN_SR04T.MIN_DISTANCE, int maxDistance = JSN_SR04T.MAX_DISTANCE)
        {
            WaterTank wt = new WaterTank(transmitPin, receivePin, id);
            wt.MinDistance = minDistance;
            wt.MaxDistance = maxDistance;

            wt.SampleInterval = SampleInterval;
            wt.SampleSize = SampleSize;

            Tanks.Add(wt);
            AddDevice(wt);

            return wt;
        }
    }
}
