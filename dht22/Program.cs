using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Presentation;
using Microsoft.SPOT.Presentation.Controls;
using Microsoft.SPOT.Presentation.Media;
using Microsoft.SPOT.Presentation.Shapes;
using Microsoft.SPOT.Touch;
using Microsoft.SPOT.Hardware;
using GHI.IO;

using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using Gadgeteer.Modules.GHIElectronics;

namespace dht22
{
    /// <summary>
    /// Class to read the single wire Humitidy/Temperature DHT22 sensor
    /// It uses 2 interrupt pins connected together to access the sensor quick enough
    /// </summary>
    public class DHT22 : IDisposable
    {

        /// <summary>
        /// Temperature in celcius degres
        /// </summary>
        public float Temperature { get; private set; }

        /// <summary>
        /// Humidity in percents
        /// </summary>
        public float Humidity { get; private set; }

        /// <summary>
        /// If not empty, gives the last error that occured
        /// </summary>
        public string LastError { get; private set; }

        private TristatePort _dht22out;
        private SignalCapture _dht22in;

        /// <summary>
        /// Constructor. Needs to interrupt pins to be provided and linked together in Hardware. *
        /// Blocking call for 1s to give sensor time to initialize.
        /// </summary>
        public DHT22(Cpu.Pin In, Cpu.Pin Out)
        {
            _dht22out = new TristatePort(Out, false, false, Port.ResistorMode.PullUp);
            _dht22in = new SignalCapture(In, Port.ResistorMode.PullUp, Port.InterruptMode.InterruptEdgeBoth);
            if (_dht22out.Active == false) _dht22out.Active = true; // Make tristateport "output" 
            _dht22out.Write(true);   //"high up" (standby state)
            Thread.Sleep(1000); // 1s to pass the "unstable status" as per the documentation
        }

        #region IDisposable Members

        public void Dispose()
        {
            _dht22out.Dispose();
            _dht22in.Dispose();
        }

        #endregion

        /// <summary>
        /// Access the sensor. Returns true if successful, false if it fails.
        /// If false, please check the LastError value for reason.
        /// </summary>
        public bool ReadSensor()
        {
            uint[] buffer = new uint[90];
            int nb, i;

            // Testing if the 2 pins are connected together
            bool rt = _dht22in.InternalPort.Read();  // Should be true
            _dht22out.Write(false);  // "low down" : initiate transmission
            bool rf = _dht22in.InternalPort.Read();  // Should be false
            if (!rt || rf)
            {
                LastError = "The 2 pins are not hardwired together !";
                _dht22out.Write(true);   //"high up" (standby state)
                return false;
            }
            Thread.Sleep(6);       // For "at least 1ms" as per the documentation
            _dht22out.Write(true);   //"high up" then listen
            //nb = _dht22in.Read(false, buffer, 0, 90, 10);  // get the sensor answer
            nb = _dht22in.Read(false, buffer, 0, 40);  // get the sensor answer
            //_dht22in.ReadTimeout = ;
            if (nb < 30)
            {
                LastError = "Did not receive enough data from the sensor";
                return false;
            }
            nb -= 2; // skip last 50us down          
            byte checksum = 0;
            uint T = 0, H = 0;
            for (i = 0; i < 8; i++, nb -= 2) checksum |= (byte)(buffer[nb] > 50 ? 1 << i : 0);
            for (i = 0; i < 16; i++, nb -= 2) T |= (uint)(buffer[nb] > 50 ? 1 << i : 0);
            Temperature = ((float)(T & 0x7FFF)) * ((T & 0x8000) > 0 ? -1 : 1) / 10;
            for (i = 0; i < 16; i++, nb -= 2) H |= (uint)(buffer[nb] > 50 ? 1 << i : 0);
            Humidity = ((float)H) / 10;

            if ((((H & 0xFF) + (H >> 8) + (T & 0xFF) + (T >> 8)) & 0xFF) != checksum)
            {
                LastError = "Checksum Error";
                return false;
            }
            LastError = "";
            return true;
        }
    }


    public partial class Program
    {
        private GT.Timer gcTimer = new GT.Timer(5000);

        // Create sensor
        DHT22 tempSensor;
        String tmpStr;

        // This method is run when the mainboard is powered up or reset.   
        void ProgramStarted()
        {

            /*******************************************************************************************
            Modules added in the Program.gadgeteer designer view are used by typing 
            their name followed by a period, e.g.  button.  or  camera.
            
            Many modules generate useful events. Type +=<tab><tab> to add a handler to an event, e.g.:
                button.ButtonPressed +=<tab><tab>
            
            If you want to do something periodically, use a GT.Timer and handle its Tick event, e.g.:
                GT.Timer timer = new GT.Timer(1000); // every second (1000ms)
                timer.Tick +=<tab><tab>
                timer.Start();
            *******************************************************************************************/

            tempSensor = new DHT22(GT.Socket.GetSocket(11, true, breakout, null).CpuPins[3], GT.Socket.GetSocket(11, true, breakout, null).CpuPins[6]);
            //tempSensor = new DHT22( (Cpu.Pin)  );

            // Use Debug.Print to show messages in Visual Studio's "Output" window during debugging.
            Debug.Print("Program Started");

            gcTimer.Tick += gcTimer_Tick;
            gcTimer.Start();

        }

        //Tick Handler
        void gcTimer_Tick(GT.Timer timer)
        {
            Debug.Print("tick");

            tempSensor.ReadSensor();

            // if(error_sensor){
            Debug.Print("Staying ALIIIIIIIVE!");
            //tmpStr = "Temperature:\t" + tempSensor.Temperature.ToString();
            //Debug.Print(tmpStr);
            //tmpStr = "Humidity:\t\t" + tempSensor.Humidity.ToString();
            //Debug.Print(tmpStr);
            //Thread.Sleep(1000);
            //}
            // else {
            Debug.Print("ERROR :( ");
            //Debug.Print(tempSensor.LastError);
            //}
        }

    }
}