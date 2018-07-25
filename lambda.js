console.log('Loading function');

// Load the AWS SDK
const AWS = require("aws-sdk");
const dynamo = new AWS.DynamoDB.DocumentClient({ region: 'eu-central-1' });
const table = "lambda_data";

var iotdata = new AWS.IotData({ endpoint: 'a1wdztjz6m7mla.iot.eu-central-1.amazonaws.com' });

exports.handler = function(event, context) {

    if (event.device_id == "FEZ26" || event.version == 2) {
        for (var i = 0; i < event.measurements.length; i++) {
            if (event.measurements[i].sensor_id >= 0 && event.measurements[i].sensor_id <= 3) {
                var params = {
                    TableName: table,
                    Item: {
                        "device_id": event.device_id,
                        "iso_timestamp": event.measurements[i].iso_timestamp,
                        "sensor_id": event.measurements[i].sensor_id,
                        "status": event.measurements[i].status,
                        "value": event.measurements[i].value,
                    }
                };

                console.log(params);
                console.log("Adding a new IoT device...");
                dynamo.put(params, function(err, data) {
                    if (err) {
                        console.error("Unable to add device. Error JSON:", JSON.stringify(err, null, 2));
                        context.fail();
                    }
                    else {
                        console.log("Added device:", JSON.stringify(data, null, 2));
                    }
                });
            }
        }


        // Prepare MQTT ACK
        console.log("Preparing MQTT ACK");
        var to_send = {
            device_id: "FEZ26",
            iso_timestamp: event.iso_timestamp
        };
        console.log(to_send);
        console.log(JSON.stringify(to_send));

        var parameters = {
            topic: 'ack',
            payload: JSON.stringify(to_send),
            qos: 1
        };

        // Send MQTT ACK
        console.log("Prepare to publish");
        iotdata.publish(parameters, function(err, data) {
            console.log("publish");
            if (err) {
                console.log(err);
            }
            else {
                console.log("success");
                context.succeed();
            }
        });
    }
};
