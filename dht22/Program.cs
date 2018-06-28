/* ToDo:
 * 1) Handle networkDown and publish: make a FIFO in order to avoid publishing when the network is off
 *      - Directly in Publish method?
 *      - While calling Publish function?
 * 2) Check QOS: is it respected? Does it work?
 * 3) Check absence of SDCard: do a local buffer again?
 * 4) Try to organise the buffer in order to use the SDCard when it is attached and the local one when it isn't
 */
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

// Network security
using System.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.SPOT.Net.Security;
using Microsoft.SPOT.Cryptoki;

using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using Gadgeteer.Modules.GHIElectronics;

// Correct time
using System.Net;
using Microsoft.SPOT.Time;

// MQTT
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

// JSON
using Json.NETMF;

//temp
using System.Net.Sockets;

namespace dht22 {
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

        /// <summary>
        /// AWS EC2 Endpoint: IPv4 Address of the machine
        /// </summary>
        private const string IotEndpoint = "18.195.215.55";

        /// <summary>
        /// Unencrypted port used by AWS EC2 machine
        /// </summary>
        private const int BrokerPort = 1883;
        /// <summary>
        /// Mqtt Topic that will be received from the EC2 (MosquittoBridge) and forwarded to AWS IoT
        /// </summary>
        private const string Topic = "t1";


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

                /* Write data on SDCard */
                SD_Write(json);
                /* Publish Mqtt topic*/
                Publish(json);
            } else {
                Debug.Print(tempSensor.LastError);
            }
        }

        // Network down handler (just prints on debug)
        void ethernetJ11D_NetworkDown(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state) {
            Debug.Print("Network is down");
        }

        // Network up handler (prints IP)
        void ethernetJ11D_NetworkUp(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state) {
            while (ethernetJ11D.NetworkSettings.IPAddress == IPAddress.Any.ToString()) {
                Debug.Print("Waiting for network...");
            }
            Debug.Print("Network is up");
            Debug.Print("IP is: " + ethernetJ11D.NetworkSettings.IPAddress);

            TimeServiceSettings time = new TimeServiceSettings() {
                ForceSyncAtWakeUp = true
            };

            IPAddress[] address = Dns.GetHostEntry("time.windows.com").AddressList;
            if (address != null)
                time.PrimaryServer = address[0].GetAddressBytes();

            address = Dns.GetHostEntry("time.nist.gov").AddressList;
            if (address != null)
                time.AlternateServer = address[0].GetAddressBytes();

            TimeService.SystemTimeChanged += TimeService_SystemTimeChanged;
            TimeService.TimeSyncFailed += TimeService_TimeSyncFailed;
            TimeService.Settings = time;
            TimeService.SetTimeZoneOffset(0);
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
                Debug.Print("Saving.....");
                sdCard.StorageDevice.WriteFile(filename, UTF8Encoding.UTF8.GetBytes(toSave));
                Debug.Print("string saved to: " + filename);
                saveIndex++;
            } catch (Exception ex) {
                Debug.Print("SD Card Error");
            }
        }
        /* ToDo: Who I am
         * Send ID, Name, Category (4), GPS Coordinates, Sensor (DHT22)
         * */

        /// <summary>
        /// Configure client and publish a message
        /// </summary>
        public void Publish(string message) {
            try {
                // create client instance 
                MqttClient client = new MqttClient(IPAddress.Parse(IotEndpoint));

                //client naming has to be unique if there was more than one publisher
                //client.Connect("GIANNI");
                string clientId = Guid.NewGuid().ToString();
                client.Connect(clientId);

                // publish a message on topic with QoS 1 
                client.Publish(Topic, Encoding.UTF8.GetBytes(message), MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);
                // this was in for debug purposes but it's useful to see something in the console
                if (client.IsConnected) {
                    Debug.Print("SUCCESS!");
                }
            } catch (Exception e) {
                Debug.Print("EXCEPTION CAUGHT: " + e.Message);
            }
        }
    }
}
