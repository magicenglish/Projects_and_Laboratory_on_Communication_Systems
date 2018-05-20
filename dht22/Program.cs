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

using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using Gadgeteer.Modules.GHIElectronics;

// Correct time
using System.Net;
using Microsoft.SPOT.Time;

// MQTT
using uPLibrary.Networking.M2Mqtt;

// JSON
using Json.NETMF;

namespace dht22 {
    /// <summary>
    /// Class to read the single wire Humitidy/Temperature DHT22 sensor
    /// It uses 2 interrupt pins connected together to access the sensor quick enough
    /// </summary>
    public class DHT22 : IDisposable {

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
        public DHT22(Cpu.Pin In, Cpu.Pin Out) {
            _dht22out = new TristatePort(Out, false, false, Port.ResistorMode.PullUp);
            _dht22in = new SignalCapture(In, Port.ResistorMode.PullUp, Port.InterruptMode.InterruptEdgeBoth);
            if (_dht22out.Active == false) _dht22out.Active = true; // Make tristateport "output" 
            _dht22out.Write(true);   //"high up" (standby state)
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
        /// If false, please check the LastError value for reason.
        /// </summary>
        public bool ReadSensor() {
            uint[] buffer = new uint[80];
            int nb, i;

            /* REQUEST SENSOR MEASURE */
            // Testing if the 2 pins are connected together
            bool rt = _dht22in.InternalPort.Read();  // Should be true
            _dht22out.Write(false);  // "low down" : initiate transmission
            bool rf = _dht22in.InternalPort.Read();  // Should be false
            if (!rt || rf) {
                LastError = "The 2 pins are not hardwired together !";
                _dht22out.Write(true);   //"high up" (standby state)
                return false;
            }
            Thread.Sleep(1);       // For "at least 1ms" as per the documentation
            _dht22out.Write(true);   //"high up" then listen

            /* READ MEASURE */
            _dht22out.Active = false; //Tristate Read
            _dht22in.ReadTimeout = 500; // Timeout value in ms
            nb = _dht22in.Read(false, buffer, 0, 80);  // get the sensor answer
            _dht22out.Active = true; // Tristate Write
            _dht22out.Write(true);   //"high up" 
            if (nb < 71) {
                LastError = "Did not receive enough data from the sensor";
                return false;
            }

            /* CONVERT MEASURE */
            nb -= 2; // skip last 50us down          
            byte checksum = 0;
            uint T = 0, H = 0;
            // Convert CheckSum
            for (i = 0; i < 8; i++, nb -= 2) checksum |= (byte)(buffer[nb] > 35 ? 1 << i : 0);
            // Convert Temperature
            for (i = 0; i < 16; i++, nb -= 2) T |= (uint)(buffer[nb] > 35 ? 1 << i : 0);
            Temperature = ((float)(T & 0x7FFF)) * ((T & 0x8000) > 0 ? -1 : 1) / 10;
            // Convert Humidity
            for (i = 0; i < 11; i++, nb -= 2) H |= (uint)(buffer[nb] > 35 ? 1 << i : 0);
            Humidity = ((float)H) / 10;
            // Control CheckSum
            if ((((H & 0xFF) + (H >> 8) + (T & 0xFF) + (T >> 8)) & 0xFF) != checksum) {
                LastError = "Checksum Error";
                return false;
            }
            // No error case
            LastError = "";
            return true;
        }
    }


    public partial class Program {
        // Timer
        private GT.Timer gcTimer = new GT.Timer(3000);

        // Sensor and relative data
        DHT22 tempSensor;
        String temperature, humidity;

        // HTTP page of board
        byte[] HTML;

        // JSON string to be saved and sent
        String json;

        // SD_Card saved file index
        uint saveIndex;

        // This method is run when the mainboard is powered up or reset.   
        void ProgramStarted() {

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

            // Create temperature and humidity sensor
            tempSensor = new DHT22(GT.Socket.GetSocket(11, true, breakout, null).CpuPins[3], GT.Socket.GetSocket(11, true, breakout, null).CpuPins[6]);
            saveIndex = 0;

            // Use Debug.Print to show messages in Visual Studio's "Output" window during debugging.
            Debug.Print("Program Started");

            // Timer settings and start
            gcTimer.Tick += gcTimer_Tick;
            gcTimer.Start();

            // Ethernet settings and basic debug
            ethernetJ11D.UseThisNetworkInterface();
            ethernetJ11D.NetworkDown += ethernetJ11D_NetworkDown;
            ethernetJ11D.NetworkUp += ethernetJ11D_NetworkUp;

            // Web server start
            new Thread(RunWebServer).Start();

        }

        // Tick Handler
        void gcTimer_Tick(GT.Timer timer) {
            bool error_sensor = tempSensor.ReadSensor();

            if (error_sensor) {
                // Measure
                temperature = tempSensor.Temperature.ToString("F");
                Debug.Print("Temperature:\t" + temperature);
                humidity = tempSensor.Humidity.ToString("F");
                Debug.Print("Humidity:\t\t" + humidity);

                // Update web data
                HTML = Encoding.UTF8.GetBytes("<html><body>" +
                    "<h1>Hosted on .NET Gadgeteer</h1>" +
                    "<p>Temperature:" + temperature + "</p>" +
                    "<p>Humidity:" + humidity + "</p>" +
                    "</body></html>");

                // Get JSON
                Hashtable json_dict = new Hashtable();
                json_dict.Add("Temperature", temperature);
                json_dict.Add("Humidity", humidity);
                json_dict.Add("Date", DateTime.Now);
                json_dict.Add("SensorID", "Gianni_DHT22");
                json = JsonSerializer.SerializeObject(json_dict);
                Debug.Print(json);

                SD_Write(json);
            }
            else {
                Debug.Print(tempSensor.LastError);
            }
        }

        // Network up handler (just prints on debug)
        void ethernetJ11D_NetworkDown(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state) {
            Debug.Print("Network is down");
        }

        // Network down handler (prints IP)
        void ethernetJ11D_NetworkUp(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state) {
            Debug.Print("Network is up");
            Debug.Print("IP is: " + ethernetJ11D.NetworkSettings.IPAddress);

            //const string ntpServer = "pool.ntp.org";
            //var ntpData = new byte[48];
            //ntpData[0] = 0x1B; //LeapIndicator = 0 (no warning), VersionNum = 3 (IPv4 only), Mode = 3 (Client Mode)

            //var addresses = Dns.GetHostEntry(ntpServer).AddressList;
            //var ipEndPoint = new IPEndPoint(addresses[0], 123);
            ////var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            ////socket.Connect(ipEndPoint);
            ////socket.Send(ntpData);
            ////socket.Receive(ntpData);
            ////socket.Close();

            //ulong intPart = (ulong)ntpData[40] << 24 | (ulong)ntpData[41] << 16 | (ulong)ntpData[42] << 8 | (ulong)ntpData[43];
            //ulong fractPart = (ulong)ntpData[44] << 24 | (ulong)ntpData[45] << 16 | (ulong)ntpData[46] << 8 | (ulong)ntpData[47];

            //var milliseconds = (intPart * 1000) + ((fractPart * 1000) / 0x100000000L);
            //var networkDateTime = (new DateTime(1900, 1, 1)).AddMilliseconds((long)milliseconds);

            ////return networkDateTime;

            TimeServiceSettings settings = new TimeServiceSettings();
            settings.RefreshTime = 10; // every 10 seconds
            settings.ForceSyncAtWakeUp = true;

            TimeService.SystemTimeChanged += TimeService_SystemTimeChanged;
            TimeService.TimeSyncFailed += TimeService_TimeSyncFailed;
            TimeService.SetTimeZoneOffset(60);

            IPHostEntry hostEntry = Dns.GetHostEntry("time.nist.gov");
            IPAddress[] address = hostEntry.AddressList;
            if (address != null)
                settings.PrimaryServer = address[0].GetAddressBytes();

            hostEntry = Dns.GetHostEntry("time.windows.com");
            address = hostEntry.AddressList;
            if (address != null)
                settings.AlternateServer = address[0].GetAddressBytes();

            TimeService.Settings = settings;

            TimeService.Start();
        }

        void TimeService_TimeSyncFailed(object sender, TimeSyncFailedEventArgs e) {
            Debug.Print("DateTime Sync Failed");
        }

        void TimeService_SystemTimeChanged(object sender, SystemTimeChangedEventArgs e) {
            Debug.Print("DateTime = " + DateTime.Now.ToString());
        }

        // Web server thread
        void RunWebServer() {
            // Wait for the network...
            while (ethernetJ11D.IsNetworkUp == false) {
                Debug.Print("Waiting...");
                Thread.Sleep(1000);
            }
            // Start the server
            WebServer.StartLocalServer(ethernetJ11D.NetworkSettings.IPAddress, 80);
            WebServer.DefaultEvent.WebEventReceived += DefaultEvent_WebEventReceived;
            while (true) {
                Thread.Sleep(1000);
            }
        }

        // HTTP request handler
        void DefaultEvent_WebEventReceived(string path, WebServer.HttpMethod method, Responder responder) {
            // We always send the same page back
            responder.Respond(HTML, "text/html;charset=utf-8");
        }

        // SDCard Write
        void SD_Write(string toSave) {
            if (toSave == null) {
                Debug.Print("Nothing to save on SDCARD");
                return;
            }
            try {
                String filename = "save_" + saveIndex + ".txt";
                Debug.Print("Saving .....");
                sdCard.StorageDevice.WriteFile(filename, UTF8Encoding.UTF8.GetBytes(toSave));
                Debug.Print("string saved to: " + filename);
                saveIndex++;
            }
            catch (Exception ex) {
                Debug.Print("SD Card Error");
            }
        }
        /* ToDo: Who I am
         * Send ID, Name, Category (4), GPS Coordinates, Sensor (DHT22)
         * */

    }
}
