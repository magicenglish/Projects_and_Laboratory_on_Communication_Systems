#define DEBUG

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
using Microsoft.SPOT.IO;
using GHI.IO;
using GHI.IO.Storage;
using GHI.Processor;

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
        /********************* CONFIGURATION INFO **********************/
        /// <summary>
        /// JSON Format Version
        /// </summary>
        private const uint json_version = 2;

        /// <summary>
        /// Device ID
        /// </summary>
        private const string device_id = "FEZ26";

        /// <summary>
        /// AWS EC2 Endpoint: IPv4 Address of the machine
        /// </summary>
        // Todo: DNS?
        private const string IotEndpoint = "18.184.193.48";

        /// <summary>
        /// Unencrypted port used by AWS EC2 machine
        /// </summary>
        private const int BrokerPort = 1883;
        /// <summary>
        /// Mqtt Topic that will be received from the EC2 (MosquittoBridge) and forwarded to AWS IoT
        /// </summary>
        private const string Topic = "FEZ26/data";

        /*********************** Global variables *********************/
        /// <summary>
        /// Timer
        /// </summary>
        //private GT.Timer gcTimer = new GT.Timer(120000);
        private GT.Timer gcTimer = new GT.Timer(20000);

        /// <summary>
        /// Timer to avoid waiting for ACK in case of lost connection
        /// </summary>
        GT.Timer skipTimer = new GT.Timer(10000);

        /// <summary>
        /// Declare client instance for MQTT communication
        /// </summary>
        MqttClient client;

        /// <summary>
        /// Brightness sensor
        /// </summary>
        LightSensor brightSensor;

        /// <summary>
        /// Temperature and Humidity sensor
        /// </summary>
        DHT22 tempSensor;

        /// <summary>
        /// Last measured values, to avoid redundancy
        /// </summary>
        float last_temperature, last_humidity, last_brightness;

        /// <summary>
        /// Redundancy repetition count
        /// </summary>
        uint temp_count, hum_count, bright_count;

        /// <summary>
        /// Measurements object
        /// </summary>
        Measurements meas;

        /// <summary>
        /// iso_timestamp of last published message
        /// </summary>
        DateTime last_published_timestamp;

        /// <summary>
        /// Synchronizing network events
        /// </summary>
        private static ManualResetEvent network_up_event = new ManualResetEvent(false);

        /// <summary>
        /// Synchronizing MQTT Publish
        /// </summary>
        private static ManualResetEvent MQTT_connected_event = new ManualResetEvent(false);

        /// <summary>
        /// Synchronizing MQTT Publish
        /// </summary>
        private static AutoResetEvent network_down_up_event = new AutoResetEvent(false);

        /// <summary>
        /// Waiting for MQTT Acknowledge
        /// </summary>
        private static AutoResetEvent mqtt_ack_event = new AutoResetEvent(false);

        /// <summary>
        /// Waiting for MQTT Acknowledge
        /// </summary>
        private static AutoResetEvent mqtt_pub_event = new AutoResetEvent(false);

        /// <summary>
        /// Synchronizing network events
        /// </summary>
        private static ManualResetEvent correct_time_event = new ManualResetEvent(false);

        /// <summary>
        /// SD ready event
        /// </summary>
        private static ManualResetEvent SD_ready_event = new ManualResetEvent(false);

        /// <summary>
        /// Lock of instance of Measurament class, to avoid modifications by other threads
        /// </summary>
        readonly Object meas_lock = new Object();

        /// <summary>
        /// Lock to handle correct timing correction
        /// </summary>
        readonly Object time_lock = new Object();

        /// <summary>
        /// Lock to handle SD_Card access
        /// </summary>
        readonly Object sd_access = new Object();

        /// <summary>
        /// Lock to handle correct use of "skip" to avoid problem with missing ack
        /// </summary>
        readonly Object skip_lock = new Object();

        /// <summary>   
        /// Boolean value to check Date and time correctness
        /// </summary>
        bool correct_time;

        /// <summary>   
        /// Boolean value to signal the timeout of the ack waiting. In order to avoid deadlock. 
        /// </summary>
        bool restarted;

        /// <summary>
        /// Skip one saving (and do it again the next round)
        /// when internet breaks (therefore the ack could have been lost)
        /// </summary>
        bool skip_deletion;

        /// <summary>
        /// Last Datetime of wrong "time" (the one internally saved in the FEZ Spider II).
        /// Used to calculate the offset to adjust the time
        /// </summary>
        DateTime last_wrong_time;

        /// <summary>
        /// Time offset used to adjust the time to the Measurements stored in SD_Card
        /// </summary>
        TimeSpan time_offset;

        /// <summary>
        /// HTTP page of board, hosted on it
        /// </summary>
        byte[] HTML;

        /// <summary>
        /// Main: This method is run when the mainboard is powered up or reset.
        /// </summary>
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

#if (DEBUG)
            /* Use Debug.Print to show messages in Visual Studio's "Output" window during debugging. */
            Debug.Print("Program Started");
#endif
            /* Create temperature and humidity sensor */
            tempSensor = new DHT22(GT.Socket.GetSocket(11, true, breakout, null).CpuPins[3], GT.Socket.GetSocket(11, true, breakout, null).CpuPins[6]);
            brightSensor = new LightSensor(GT.Socket.GetSocket(10, true, extender, null).CpuPins[4]);

            lock (time_lock)
                /* Startup: the time is wrong */
                correct_time = false;

            //lock (skip_lock)
            ///* Startup: no need to skip the publish */
            //skip = false;

            lock (meas_lock)
                /* Initialize values to write on SD */
                meas = new Measurements(json_version, device_id);

            /* Initialize value to handle the redundancy of identical values taken from sensor */
            temp_count = 15;
            hum_count = 15;
            bright_count = 15;

            /* Measurement skip publish handler */
            skipTimer.Tick += skipTimer_Tick;
            skipTimer.Start();

            /* Initialise MQTT Client */
            client = new MqttClient(IotEndpoint);

            /* Measurement Timer handler and start */
            gcTimer.Tick += gcTimer_Tick;
            gcTimer.Start();

            /* Ethernet settings and basic debug */
            ethernetJ11D.UseThisNetworkInterface();
            ethernetJ11D.NetworkDown += ethernetJ11D_NetworkDown;
            ethernetJ11D.NetworkUp += ethernetJ11D_NetworkUp;

            /* MQTT Handlers */
            client.MqttMsgSubscribed += client_MqttMsgSubscribed;
            client.MqttMsgUnsubscribed += client_MqttMsgUnsubscribed;
            client.MqttMsgPublished += client_MqttMsgPublished;
            client.MqttMsgPublishReceived += client_MqttMsgPublishReceived;

            try {
                /* SD_Card Clean-up */
                new Thread(Clean_Up).Start();
                /* Connect to EC2 Mosquitto Bridge Thread Start */
                new Thread(MQTTConnect).Start();
                /* Start MQTT publishing */
                new Thread(Publish).Start();
                /* Web server start*/
                new Thread(RunWebServer).Start();
                /* Time adjust server start */
                new Thread(Re_Time).Start();
                /* SD Write start thread */
                new Thread(SD_Write).Start();
            } catch (Exception) {
#if (DEBUG)
                Debug.Print("Error: Fail to start one or more threads");
#endif
            }

            /* Enable 15 mins Watchdog */
            GHI.Processor.Watchdog.Enable(GHI.Processor.Watchdog.MaxTimeoutPeriod);

        }

        /// <summary>
        /// Timer to skip stall on ACK wait
        /// </summary>
        /// <param name="timer"></param>
        void skipTimer_Tick(GT.Timer timer) {
            /* When timer ticks, skip = true -> will skip Publish wait for ACK 
             * (and will avoid deletion of original file in SD_Card) */
            lock (skip_lock) {
                if (restarted) {
                    skip_deletion = true;
                    /* Start ack in order to avoid stalling in case of error. 
                     * It will try again to send the data */
                    mqtt_ack_event.Set();
                }
                restarted = false;
            }
        }

        /// <summary>
        /// MQTT message published in subscripted topic - Handler 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void client_MqttMsgPublishReceived(object sender, MqttMsgPublishEventArgs e) {
#if (DEBUG)
            Debug.Print("topic: " + e.Topic);
            Debug.Print("message: " + new string(UTF8Encoding.UTF8.GetChars(e.Message)));
#endif

            /* Check if is an acknowledge message or an handshake one */
            if (e.Topic == "FEZ26/ack") {
                /* Handle ACKNOWLEDGE OF LAST MESSAGE */
                try {
                    /* take Datetime of sent message in order to compare with the ack */
                    Hashtable mess = JsonSerializer.DeserializeString(new string(UTF8Encoding.UTF8.GetChars(e.Message))) as Hashtable;
                    if (mess["device_id"] as string == device_id) {
                        if (DateTimeExtensions.FromIso8601(mess["iso_timestamp"] as string) == last_published_timestamp) {
                            /* make Datetime impossible in order to avoid repetitions */
                            last_published_timestamp = new DateTime();

                            /* Signal Publish to go ahead */
                            mqtt_ack_event.Set();
                        }
                    }
                } catch (Exception) {
#if (DEBUG)
                    Debug.Print("Error reading ack message");
#endif
                }
            }
        }

        /// <summary>
        /// MQTT message published by this client - Handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void client_MqttMsgPublished(object sender, MqttMsgPublishedEventArgs e) {
#if (DEBUG)
            Debug.Print("Msg Correctly published");
            /* Signal that message has been correctly published */
            //mqtt_pub_event.Set();
#endif
        }

        /// <summary>
        /// MQTT unsubscription from topic - Handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void client_MqttMsgUnsubscribed(object sender, MqttMsgUnsubscribedEventArgs e) {
#if (DEBUG)
            Debug.Print("Unsubscribed to topic! Watch out");
#endif

        }

        /// <summary>
        /// MQTT Subscription to topic - Handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void client_MqttMsgSubscribed(object sender, MqttMsgSubscribedEventArgs e) {
#if (DEBUG)
            Debug.Print("Subscribed to topic!");
#endif
        }

        /// <summary>
        /// Timer Thread: called every "timer" time elapsed
        /// Takes the measuraments from the sensors
        /// </summary>
        /// <param name="timer"></param>
        void gcTimer_Tick(GT.Timer timer) {
            Measurement[] sensor = new Measurement[3];

            /* Temperature and Humidity measurement */
            if (tempSensor.ReadSensor()) {
                /* Reset Watchdog counter */
                GHI.Processor.Watchdog.ResetCounter();
                if (tempSensor.Temperature != last_temperature || temp_count == 15) {
                    sensor[0] = new Measurement(0, tempSensor.Temperature, "OK");
                    last_temperature = tempSensor.Temperature;
                    temp_count = 0;
                } else {
                    sensor[0] = null;
                }
                if (tempSensor.Humidity != last_humidity || hum_count == 15) {
                    sensor[1] = new Measurement(1, tempSensor.Humidity, "OK");
                    last_humidity = tempSensor.Humidity;
                    hum_count = 0;
                } else {
                    sensor[1] = null;
                }
            } else {
                sensor[0] = new Measurement(1, -999, "FAIL");
                sensor[1] = new Measurement(1, -999, "FAIL");
            }

            //sensor2_json += "\"iso_timestamp\": \"" + DateTime.UtcNow.ToString("s") + "+00:00\", ";
            //humidity = tempSensor.Humidity; //.ToString("F");
#if (DEBUG)
            Debug.Print("Temperature:\t" + tempSensor.Temperature.ToString("F"));
            Debug.Print("Humidity:\t\t" + tempSensor.Humidity.ToString("F"));
#endif

#if (DEBUG)
            Debug.Print(tempSensor.LastError);
#endif

            /* Brightness measurement */
            brightSensor.ReadSensor();

            if (brightSensor.Brightness != last_brightness || bright_count == 15) {
                sensor[2] = new Measurement(2, brightSensor.Brightness, "OK");
                bright_count = 0;
                last_brightness = brightSensor.Brightness;
            } else {
                sensor[2] = null;
            }
#if (DEBUG)
            Debug.Print("Brightness:\t\t" + brightSensor.Brightness.ToString("F"));
#endif

            /* Update web data */
            HTML = Encoding.UTF8.GetBytes("<html><body>" +
                "<h1>Hosted on .NET Gadgeteer</h1>" +
                "<p>Temperature:" + tempSensor.Temperature + "</p>" +
                "<p>Humidity:" + tempSensor.Humidity + "</p>" +
                "<p>Brightness:" + brightSensor.Brightness + "</p>" +
                "</body></html>");

            /* Send data? */
            if (sensor[0] != null || sensor[1] != null || sensor[2] != null)
                lock (meas_lock)
                    meas.Add_Measurements(sensor);
            else {
#if (DEBUG)
                Debug.Print("No need to send message");
#endif
            }
            /* Starting preparing JSON file to send to AWS */
            String to_send = JsonSerializer.SerializeObject(meas);

#if (DEBUG)
            Debug.Print(to_send);
#endif
            /* Increase counter of same read data */
            temp_count++;
            hum_count++;
            bright_count++;
        }

        /// <summary>
        /// Network Down Handler: Resets signal "network_up_event" to block threads that need internet
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="state"></param>
        void ethernetJ11D_NetworkDown(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state) {
#if (DEBUG)
            Debug.Print("Network is down");
#endif
            network_up_event.Reset();
            network_down_up_event.Reset();
            MQTT_connected_event.Reset();
        }

        /// <summary>
        /// Network Up Handler (prints IP): signals through event "network_up_event" that internet is up and running
        /// </summary>
        /// <param name="sender">GTM network module</param>
        /// <param name="state">GTM Network state</param>
        void ethernetJ11D_NetworkUp(GTM.Module.NetworkModule sender, GTM.Module.NetworkModule.NetworkState state) {
            while (ethernetJ11D.NetworkSettings.IPAddress == IPAddress.Any.ToString()) {
#if (DEBUG)
                Debug.Print("Waiting for network...");
#endif
            }
#if (DEBUG)
            Debug.Print("Network is up");
            Debug.Print("IP is: " + ethernetJ11D.NetworkSettings.IPAddress);
#endif

            /* Signal that network is up */
            network_up_event.Set();
            network_down_up_event.Set();

            /* Setting up correct time */
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

            /* Save last wrong time*/
            lock (time_lock)
                if (!correct_time)
                    last_wrong_time = DateTime.UtcNow;
            TimeService.Start();

        }

        /// <summary>
        /// TimeService Sync Failure Handler
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void TimeService_TimeSyncFailed(object sender, TimeSyncFailedEventArgs e) {
            lock (time_lock)
                correct_time = false;
            correct_time_event.Reset();

#if (DEBUG)
            Debug.Print("DateTime Sync Failed");
#endif
        }

        /// <summary>
        /// TimeService System Time Changed Handler: signals through event "correct_time_event" the time correctness
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void TimeService_SystemTimeChanged(object sender, SystemTimeChangedEventArgs e) {
            lock (time_lock) {
                time_offset = DateTime.UtcNow - last_wrong_time;
                correct_time = true;
            }
            /* Signal correct time */
            correct_time_event.Set();
#if (DEBUG)
            Debug.Print("DateTime = " + DateTime.Now.ToString());
#endif
        }

        //Web server thread
        /// <summary>
        /// Web Server Thread
        /// </summary>
        void RunWebServer() {
            // Wait for the network...
            while (!ethernetJ11D.IsNetworkUp) {
                network_up_event.WaitOne();
#if (DEBUG)
                Debug.Print("Server waiting to start...");
#endif
            }
            // Start the server
            WebServer.StartLocalServer(ethernetJ11D.NetworkSettings.IPAddress, 80);
            WebServer.DefaultEvent.WebEventReceived += DefaultEvent_WebEventReceived;
            for (; ; ) {
                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// HTTP request handler 
        /// </summary>
        /// <param name="path"></param>
        /// <param name="method"></param>
        /// <param name="responder"></param>
        void DefaultEvent_WebEventReceived(string path, WebServer.HttpMethod method, Responder responder) {
            // We always send the same page back
            responder.Respond(HTML, "text/html;charset=utf-8");
        }

        /// <summary>
        /// SD Card write
        /// </summary>
        void SD_Write() {
            for (; ; ) {
                SD_ready_event.WaitOne();
                lock (meas_lock) {
                    if (meas.measurements != null && meas.measurements.Count != 0) {
                        /* Wait for something to save */
                        try {
                            lock (time_lock) {
                                //String filename = DateTime.UtcNow.ToString() + ".txt";
                                /* Create filename: add 'x' at the beginning to show it has wrong time */
                                String filename = ((correct_time) ? "" : "x") + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + ".json";
#if (DEBUG)
                                Debug.Print("Saving on SDCard...");
#endif
                                lock (sd_access) {
                                    if (correct_time)
                                        sdCard.StorageDevice.WriteFile(filename, UTF8Encoding.UTF8.GetBytes(JsonSerializer.SerializeObject(meas, DateTimeFormat.ISO8601)));
                                    else
                                        sdCard.StorageDevice.WriteFile(filename, UTF8Encoding.UTF8.GetBytes(JsonSerializer.SerializeObject(meas.measurements, DateTimeFormat.ISO8601)));
                                    /* Force emptying of the SD buffer -> immediate action */
                                    VolumeInfo.GetVolumes()[0].FlushAll();
                                }
#if (DEBUG)
                                Debug.Print("string saved to: " + filename);
#endif
                            }

                            // Empty buffer;
                            meas.measurements.Clear();
                        } catch (Exception e) {
#if (DEBUG)
                            Debug.Print("SD Card Error");
                            Debug.Print("EXCEPTION CAUGHT: " + e.Message);
#endif
                        } finally {
                            Thread.Sleep(1000);
                        }
                    }
                }
            }
        }

        /* ToDo: Who I am: send config file
        * Send ID, Name, Category (4), GPS Coordinates, Sensor (DHT22)
        * */

        /// <summary>
        /// MQTT Connect Thread
        /// </summary>
        void MQTTConnect() {
            for (; ; ) {
                network_down_up_event.WaitOne();
                try {
                    // Client naming has to be unique if there was more than one publisher
                    string clientId = Guid.NewGuid().ToString();

                    do {
#if (DEBUG)
                        Debug.Print("Connecting to the broker...");
#endif
                        client.Connect(clientId);
                        Thread.Sleep(100);
                    } while (!client.IsConnected);
#if (DEBUG)
                    Debug.Print("Successfully connected to the broker");
#endif
                    client.Subscribe(new String[] { "FEZ26/ack" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });
                    client.Subscribe(new String[] { "FEZ26/handshake" }, new byte[] { MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE });

                    /* Signal correct establishment of MQTT channel */
                    MQTT_connected_event.Set();
                } catch (Exception e) {
#if (DEBUG)
                    Debug.Print("Connection to broker failed");
                    Debug.Print("EXCEPTION CAUGHT: " + e.Message);
#endif
                }
            }
        }

        /// <summary>
        /// Configure client and publish a message
        /// </summary>
        void Publish() {
            for (; ; ) {
                /* If the network is down, block thread */
                MQTT_connected_event.WaitOne();
                try {
                    while (!sdCard.IsCardMounted)
                        Thread.Sleep(100);
                    /* Retrieve data */
                    foreach (string filename in sdCard.StorageDevice.ListFiles(sdCard.StorageDevice.RootDirectory)) {
                        if (filename[0] != 'x') {
                            try {
                                byte[] to_send;
                                lock (sd_access)
                                    to_send = sdCard.StorageDevice.ReadFile(filename);
                                if (to_send != null) {
                                    Hashtable hashtable = JsonSerializer.DeserializeString(new string(UTF8Encoding.UTF8.GetChars(to_send))) as Hashtable;
                                    last_published_timestamp = DateTimeExtensions.FromIso8601(hashtable["iso_timestamp"] as string);

                                    lock (skip_lock) {
                                        restarted = true;
                                        skipTimer.Start();
                                    }
                                    /* publish a message on topic with QoS 1 */
                                    client.Publish(Topic, to_send, MqttMsgBase.QOS_LEVEL_AT_LEAST_ONCE, false);

                                    Thread.Sleep(10);

                                    /* Wait message published */
                                    //mqtt_pub_event.WaitOne();

                                    /* Wait message acknowledged */
                                    mqtt_ack_event.WaitOne();

                                    lock (skip_lock) {
                                        if (skip_deletion) {
                                            skip_deletion = false;
                                            break;
                                        }
                                    }
#if (DEBUG)
                                    Debug.Print("Acknowledge of last MQTT sent!");
#endif
                                }
                            } catch (Exception ex_pub) {
#if (DEBUG)
                                Debug.Print("Error publishing");
                                Debug.Print("EXCEPTION CAUGHT: " + ex_pub.Message);
#endif
                                /* Avoid deleting file */
                                continue;
                            }
                            try {
                                lock (sd_access) {
                                    /* Delete SD file */
                                    sdCard.StorageDevice.Delete(filename);
                                    /* Force emptying of the SD buffer -> immediate action */
                                    VolumeInfo.GetVolumes()[0].FlushAll();
                                }
                            } catch (Exception ec_del) {
#if (DEBUG)
                                Debug.Print("Error deleting from SD");
                                Debug.Print("EXCEPTION CAUGHT: " + ec_del.Message);
#endif
                            }
                        }
                    }
                } catch (Exception e) {
#if (DEBUG)
                    Debug.Print("Error retrieving files from SDCard");
                    Debug.Print("EXCEPTION CAUGHT: " + e.Message);
#endif
                } finally {
                    Thread.Sleep(2000);
                }
            }
        }

        /// <summary>
        /// Adjust wrong time changing it from the SD_Card
        /// </summary>
        void Re_Time() {
            for (; ; ) {
                /* Now the SD is ready to be worked with */
                SD_ready_event.WaitOne();
                correct_time_event.WaitOne();
                if (correct_time) {
                    try {
                        /* Retrieve data */
                        foreach (string filename in sdCard.StorageDevice.ListFiles(sdCard.StorageDevice.RootDirectory)) {
                            if (filename[0] == 'x') {
                                try {
                                    Object sd_read_lock = new Object();
                                    Measurement[] to_retime;
                                    lock (sd_access) {
                                        to_retime = (Measurement[])JsonSerializer.DeserializeString(sdCard.StorageDevice.ReadFile(filename).ToString());
                                    }
                                    lock (meas_lock) {
                                        meas.Add_Measurements(to_retime, time_offset);
                                    }
                                    lock (sd_access) {
                                        /* Delete SD file */
                                        sdCard.StorageDevice.Delete(filename);
                                        /* Force emptying of the SD buffer -> immediate action */
                                        VolumeInfo.GetVolumes()[0].FlushAll();
                                    }
                                } catch (Exception ec_del) {
#if (DEBUG)
                                    Debug.Print("Error deleting from SD");
                                    Debug.Print("EXCEPTION CAUGHT: " + ec_del.Message);
#endif
                                }
                            }
                        }
                    } catch (Exception e) {
#if (DEBUG)
                        Debug.Print("Error retrieving files from SDCard");
                        Debug.Print("EXCEPTION CAUGHT: " + e.Message);
#endif
                    }
                }
                Thread.Sleep(60000);
            }
        }

        /// <summary>
        /// Delete old files with wrong timing
        /// </summary>
        void Clean_Up() {
            while (!sdCard.IsCardMounted) {
                Thread.Sleep(10);
            }
            /* Retrieve data */
            try {
                foreach (string filename in sdCard.StorageDevice.ListRootDirectoryFiles()) {
#if (DEBUG)
                    Debug.Print("filename: " + filename);
#endif
                    if (filename[0] == 'x') {
                        try {
                            lock (sd_access) {
                                /* Delete SD file */
                                sdCard.StorageDevice.Delete(filename);
                                /* Force emptying of the SD buffer -> immediate action */
                                VolumeInfo.GetVolumes()[0].FlushAll();
#if (DEBUG)
                                Debug.Print("Old wrong file deleted from SD");
#endif
                            }
                        } catch (Exception ec_del) {
#if (DEBUG)
                            Debug.Print("Error deleting from SD_Card");
                            Debug.Print("EXCEPTION CAUGHT: " + ec_del.Message);
#endif
                        }
                    }
                }
            } catch (Exception) {
#if (DEBUG)
                Debug.Print("SD_Card was clean: no old wrong files to delete from SD");
#endif
            } finally {
                /* Ready to write on SD and Publish */
                SD_ready_event.Set();
            }
        }
    }
}
