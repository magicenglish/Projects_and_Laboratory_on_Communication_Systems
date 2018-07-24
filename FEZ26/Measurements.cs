using System;
using System.Collections;
using Microsoft.SPOT;

namespace FEZ26 {
    class Measurements {
        /// <summary>
        /// Class Constructor
        /// </summary>
        public Measurements(uint json_version, string id) {
            version = json_version;
            device_id = id;
            measurements = new Stack();
        }

        /// <summary>
        /// JSON Schema version
        /// </summary>
        public uint version { get; private set; }

        /// <summary>
        /// Device ID: used to uniquely identify the device
        /// </summary>
        public string device_id { get; private set; }

        /// <summary>
        /// UTC time of Measurements creation
        /// </summary>
        public DateTime iso_timestamp { get; private set; }

        /// <summary>
        /// Measurements to be saved and then sent
        /// </summary>
        public Stack measurements { get; private set; }

        /// <summary>
        /// Add a new measurement to the measurement stack
        /// </summary>
        public void Add_Measurements(Measurement[] to_add) {
            iso_timestamp = DateTime.UtcNow;
            /* Keep not saved data */
            foreach (Measurement measurement in to_add) {
                if (measurement != null) {
                    /* Discard last measurement */
                    if (measurements.Count > 500) {
                        measurements.Pop();
                        measurements.Pop();
                        measurements.Pop();
                    }
                    measurements.Push(measurement);
                }
            }
        }
        /// <summary>
        /// Add a new measurement to the measurement stack
        /// </summary>
        public void Add_Measurements(Measurement[] to_add, TimeSpan diff) {
            iso_timestamp = DateTime.UtcNow;
            /* Keep not saved data */
            foreach (Measurement measurement in to_add) {
                if (measurement != null) {
                    /* Discard last measurements */
                    if (measurements.Count > 500) {
                        measurements.Pop();
                        measurements.Pop();
                        measurements.Pop();
                    }
                    measurement.Fix_Time(diff);
                    measurements.Push(measurement);
                }
            }
        }
    }

    class Measurement {
        /// <summary>
        /// Class Constructor
        /// </summary>
        public Measurement(uint id, float val, string reading_status) {
            sensor_id = id;
            iso_timestamp = DateTime.UtcNow;
            value = val;
            status = reading_status;
        }

        /// <summary>
        /// Sensor ID: to uniquely identify the sensor
        /// </summary>
        public uint sensor_id { get; private set; }

        /// <summary>
        /// UTC time of Measurement
        /// </summary>
        public DateTime iso_timestamp { get; private set; }

        /// <summary>
        /// Measured value (-999 if FAIL)
        /// </summary>
        public float value { get; private set; }

        /// <summary>
        /// Measurement status
        /// </summary>
        public string status { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="diff"></param>
        public void Fix_Time(TimeSpan diff) {
            iso_timestamp += diff;
        }
    }
}