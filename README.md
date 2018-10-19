v# Projects and Laboratory on Communication Systems
Course project @ Politecnico di Torino, Turin, Italy.
Group FEZ26, Andrea Casalino ([@andreac94](https://github.com/andreac94)), Pietro Inglese ([@magicenglish](https://github.com/magicenglish)), Flavio Tanese ([@Rookie64](https://github.com/Rookie64v))

Design of a system taking data from sensor, sending it to AWS IoT using MQTT protocol, to finally plot and show data on custom website.

## Environment used
Since the system used has been a Gadgeteer FEZ Spider II, it has been used C# with the .NET micro framework 4.3, together with the Gadgeteer libraries and a few other libraries from NuGet.
It has been used Visual Studio 2013 for compatibility reasons.

## Sensors used

* DHT22 Temperature and Humidity sensor
* Photoresistor

## JSON schema

In the folder JSON can be found the different schemas used for the MQTT trasmission handling.

On the C# code has been written classes to handle more conveniently the JSON creation:

### Measurements class

-   *uint* `version`: json schema version;
-   *string* `device_id`: unique group identifier (FEZ26);
-   *Datetime* `iso_timestamp`: Datetime (used to recognize the Acknowledge from AWS IoT);
-   *Stack<Measurements>* `measurements`: Stack containing a variable (1 - #sensors) number of instances of the class Measurement;
-   *void* `Add_Measurements(Measurement[] to_add)`: Method used to add the measurements (and it also updates the timestamp value in order to have it up to date and correct).

### Measurement class

-   *string* `sensor_id`: unique sensor identifier;
-   *Datetime* `iso_timestamp`: Datetime (used to store the time of measurement);
-   *float* `value`: measured value;
-   *string* `status`: `OK`, `FAIL`, `OUTOFRANGE`;
-   *void* `Fix_Time`: increments the actual timestamp value of a `timespan` value.

## C# code
In the C# code are handled the sensor reads and the communication with AWS, together with various events that could be triggered during the normal execution of the program.
The Visual Studio project is under `/FEZ26`.

## MQTT Bridge
Since the .NETMF 4.3 has not an implementation of TLS 1.2, it was impossible to directly publish on AWS. Therefore the solution adopted has been to host on a t2.micro Amazon EC2 instance running a Mosquitto broker with the bridge configuration.

On the EC2 machine have been put:
* rootCA
* client Certificate
* client Private Key
* Mosquitto bridge configuration (bridge.conf)

The steps to install and configure the bridge are explained in the [ How to Bridge Mosquitto MQTT Broker to AWS IoT Guide ](https://aws.amazon.com/blogs/iot/how-to-bridge-mosquitto-mqtt-broker-to-aws-iot/)

Finally, in order to start the mosquitto server, on the console, write:
```
sudo mosquitto -d -c /etc/mosquitto/conf.d/bridge.conf
```

The used bridge configuration can be found in `/AWS/bridge.conf`

## AWS IoT
To take the data from the MQTT, has been created a "Thing" on AWS IoT core, together with the permission to connect to it.
The steps taken are explained in the [ Getting Started with AWS IoT Guide ](https://docs.aws.amazon.com/iot/latest/developerguide/iot-gs.html)

## AWS IoT acknowledge of received packet and save on DynamoDB
In order to save and acknowledge the MQTT client, it has been written a Lambda function, in `/AWS/lambda.js`.

It has been created a DynamoDB table in order to store the incoming sensor data.

This function had the permissions described in the role `AWS/lambda` and it was enabled in the _Act_ section of AWS IoT Core.

## Website
Finally, the data stored on the DynamoDB database has been queried from the table and showed on a custom website, in `/Website`
It has been used Javascript, HTML and CSS.
The website has been stored on an AWS S3 and made public.
