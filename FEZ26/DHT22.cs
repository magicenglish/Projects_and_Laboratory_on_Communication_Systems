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
    public class DHT22 : IDisposable {

        /// <summary>
        /// Temperature in Celsius degrees
        /// </summary>
        public float Temperature { get; private set; }

        /// <summary>
        /// Humidity percentage
        /// </summary>
        public float Humidity { get; private set; }

        /// <summary>
        /// If not empty, gives the last error that occured
        /// </summary>
        public string LastError { get; private set; }

        private TristatePort _dht22out;
        private SignalCapture _dht22in;

        /// <summary>
        /// Constructor. Needs to interrupt pins to be provided and linked together in Hardware.
        /// Blocking call for 1s to give sensor time to initialize.
        /// </summary>
        public DHT22(Cpu.Pin In, Cpu.Pin Out) {
            _dht22out = new TristatePort(Out, false, false, Port.ResistorMode.PullUp);
            _dht22in = new SignalCapture(In, Port.ResistorMode.PullUp, Port.InterruptMode.InterruptEdgeBoth);
            if (_dht22out.Active == false) _dht22out.Active = true; // Make tristateport "output" 
            _dht22out.Write(true); // "high up" (standby state)
            Thread.Sleep(1000); // 1s to pass the "unstable status" as per the documentation
        }

        #region IDisposable Members

        public void Dispose() {
            _dht22out.Dispose();
            _dht22in.Dispose();
        }

        #endregion

        /// <summary>
        /// Access the sensor. Returns true if successful, false if it fails.
        /// If false, please check the LastError value for more info.
        /// </summary>
        public bool ReadSensor() {
            uint[] buffer = new uint[80];
            int nb, i;

            /* REQUEST SENSOR MEASURE */
            // Test if the 2 pins are connected together
            bool rt = _dht22in.InternalPort.Read();  // Should be true
            _dht22out.Write(false);  // "low down": initiate transmission
            bool rf = _dht22in.InternalPort.Read();  // Should be false
            if (!rt || rf) {
                LastError = "The 2 pins are not hardwired together !";
                _dht22out.Write(true);   // "high up" (standby state)
                return false;
            }
            Thread.Sleep(1);         // For "at least 1ms" as per the documentation
            _dht22out.Write(true);   // "high up" then listen

            /* READ MEASURE */
            _dht22out.Active = false; // Tristate Read
            _dht22in.ReadTimeout = 500; // Timeout value in ms
            nb = _dht22in.Read(false, buffer, 0, 80);  // get the sensor answer
            _dht22out.Active = true; // Tristate Write
            _dht22out.Write(true);   // "high up" 
            if (nb < 69) {
                LastError = "Did not receive enough data from the sensor";
                return false;
            }

            /* CONVERT MEASURE */
            nb -= 2; // skip last 50us down          
            byte checksum = 0;
            uint T = 0, H = 0;
            // Convert CheckSum
            for (i = 0; i < 8; i++, nb -= 2)
                checksum |= (byte)(buffer[nb] > 35 ? 1 << i : 0);

            // Convert Temperature
            for (i = 0; i < 16; i++, nb -= 2) 
                T |= (uint)(buffer[nb] > 35 ? 1 << i : 0);
            Temperature = ((float)(T & 0x7FFF)) * ((T & 0x8000) > 0 ? -1 : 1) / 10;

            // Convert Humidity
            for (i = 0; i < 10; i++, nb -= 2) 
                H |= (uint)(buffer[nb] > 35 ? 1 << i : 0);
            Humidity = ((float)H) / 10;

            // Check CheckSum
            if ((((H & 0xFF) + (H >> 8) + (T & 0xFF) + (T >> 8)) & 0xFF) != checksum) {
                LastError = "Checksum Error";
                return false;
            }
            // No error case
            LastError = "";
            return true;
        }
    }
}