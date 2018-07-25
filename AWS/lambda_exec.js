{
    "Version": "2012-10-17",
    "Statement": [
        {
            "Sid": "VisualEditor0",
            "Effect": "Allow",
            "Action": [
                "logs:CreateLogStream",
                "dynamodb:PutItem",
                "logs:PutLogEvents",
                "iot:Publish"
            ],
            "Resource": [
                "arn:aws:dynamodb:<region>:<endpoint>:table/<table>",
                "arn:aws:iot:<region>:<endpoint>:<ack_topic>",
                "arn:aws:iot:<region>:<endpoint>:<config_topic>",
                "arn:aws:logs:*:*:*"
            ]
        },
        {
            "Sid": "VisualEditor1",
            "Effect": "Allow",
            "Action": "logs:CreateLogGroup",
            "Resource": "arn:aws:logs:*:*:*"
        }
    ]
}
