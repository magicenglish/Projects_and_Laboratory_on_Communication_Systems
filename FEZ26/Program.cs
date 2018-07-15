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

namespace FEZ26 {

    public partial class Program {
        // JSON Format Version
        uint json_version = 2;

        // Timer
        private GT.Timer gcTimer = new GT.Timer(3000);

        // Sensor and relative data
        DHT22 tempSensor;
        String temperature, humidity, brightness;

        // HTTP page of board
        byte[] HTML;

        // JSON string to be saved and sent
        String sensor1_json, sensor2_json, sensor3_json;

        // SD_Card saved file index
        uint saveIndex;

        /// <summary>
        /// AWS EC2 Endpoint: IPv4 Address of the machine
        /// </summary>
        private const string IotEndpoint = "18.184.253.247";

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

            // Preparing JSON file to send to AWS
            String sensor1_json = "{ \"id\": 1, ";
            String sensor2_json = "{ \"id\": 2, ";
            String sensor3_json = "{ \"id\": 3, ";

            /* Temperature and Humidity measurement */
            bool readTH = tempSensor.ReadSensor();
            sensor1_json += "\"iso_timestamp\": \"" + DateTime.UtcNow.ToString("s") +"+00:00\", ";
            sensor2_json += "\"iso_timestamp\": \"" + DateTime.UtcNow.ToString("s") +"+00:00\", ";

            if (readTH) {
                /* Measure read and save */
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


                sensor1_json += "\"value\": " + temperature + ", ";
                sensor2_json += "\"value\": " + humidity + ", ";
                /* Sensor Status OK" */ 
                sensor1_json += "\"status\": \"OK\" }";
                sensor2_json += "\"status\": \"OK\" }";
            } else {
                Debug.Print(tempSensor.LastError);
                /* Sensor Status FAIL" */ 
                sensor1_json += "\"value\": -999, ";
                sensor2_json += "\"value\": -999, ";
                sensor1_json += "\"status\": \"FAIL\" }";
                sensor2_json += "\"status\": \"FAIL\" }";
            }

            Debug.Print(sensor1_json);
            Debug.Print(sensor2_json);

            //ToDO: insert brightness measurement 
            /* Brightness measurement */
            // ToDO: call read function
            /* TEMP */
            bool readB = false; // to change with the one in the next line
            // bool readB = brightSensor.ReadSensor();
            sensor3_json += "\"iso_timestamp\": \"" + DateTime.UtcNow.ToString("s") +"+00:00\", ";

            if (readB) {
                /* TEMP */
                // brightness = brightSensor.Brightness.ToString("F");
                Debug.Print("Brightness:\t\t" + brightness);
                sensor3_json += "\"value\": " + brightness + ", ";
                /* Sensor Status OK" */ 
                sensor3_json += "\"status\": \"OK\" }";
            } else {
                /* Sensor Status FAIL */ 
                sensor3_json += "\"value\": -999, ";
                sensor3_json += "\"status\": \"FAIL\" }";
            }

            Debug.Print(sensor3_json);

            String measurements_json = 
                "{ \"version\": " + json_version + ", " +
                "\"device_id\": \"FEZ26\", " +
                "\"iso_timestamp\": \"" + DateTime.UtcNow.ToString("s") +"+00:00\", " +
                "\"measurements\": [" +
                sensor1_json + ", " + sensor2_json + ", " + sensor3_json + " ] }";

            Debug.Print(measurements_json);

            /* Write data on SDCard */
            SD_Write(measurements_json);
            /* Publish Mqtt topic*/
            Publish(measurements_json);

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

                //ToDO: check if this works
                //MqttClient client = new MqttClient(IPAddress.Parse(IotEndpoint));
                MqttClient client = new MqttClient(IotEndpoint);

                //client naming has to be unique if there was more than one publisher
                //client.Connect("GIANNI");
                string clientId = Guid.NewGuid().ToString();
                //ToDO: Handle exception - MQTT not working
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
