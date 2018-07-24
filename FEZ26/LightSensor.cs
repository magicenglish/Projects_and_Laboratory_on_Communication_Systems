using System;
using System.Collections;
using System.Threading;
using System.Text;
using Microsoft.SPOT;
using Microsoft.SPOT.Presentation;
using Microsoft.SPOT.Presentation.Controls;
using Microsoft.SPOT.Presentation.Media;
using Microsoft.SPOT.Presentation.Shapes;
using Microsoft.SPOT.Touch;
using Microsoft.SPOT.Hardware;
using GHI.IO;

using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using Gadgeteer.Modules.GHIElectronics;

namespace FEZ26 {
    /// <summary>
    /// Class to read the single wire Humidity/Temperature DHT22 sensor
    /// It uses 2 interrupt pins connected together to access the sensor quick enough
    /// </summary>
    public class LightSensor : IDisposable {

        /// <summary>
        /// Temperature in Celsius degrees
        /// </summary>
        public float Brightness { get; private set; }

        /// <summary>
        /// If not empty, gives the last error that occured
        /// </summary>
        public string LastError { get; private set; }

        private AnalogInput _lightin;

        /// <summary>
        /// Constructor. Needs to interrupt pins to be provided and linked together in Hardware.
        /// Blocking call for 1s to give sensor time to initialize.
        /// </summary>
        public LightSensor(Cpu.Pin In) {
            _lightin = new AnalogInput(Cpu.AnalogChannel.ANALOG_1);
            Thread.Sleep(5);
        }

        #region IDisposable Members

        public void Dispose() {
            _lightin.Dispose();
        }

        #endregion

        /// <summary>
        /// Access the sensor. Returns true if successful, false if it fails.
        /// If false, please check the LastError value for more info.
        /// </summary>
        public void ReadSensor() {
            uint[] buffer = new uint[80];
            //int nb, i;

            ///* CONVERT MEASURE */
            int measure_raw = _lightin.ReadRaw();
            double measured_voltage = (measure_raw * 3.3 / 4095);
            double res = 10000 * (3.3 - measured_voltage) / 3.3;
            double illuminance = (double)5 * 100000000 * System.Math.Pow(res, (double)(-2));
            Brightness = (int)(illuminance);

#if (DEBUG)
            Debug.Print("Light_Measure (raw) = " + measure_raw);
            Debug.Print("illuminance = " + illuminance);
#endif
        }
    }
}
