# AutoParkingControl
A Dapr demo of an ANPR system that automatically sends fee when no paid parking session is active.

## Services
### Licence plate recognition API
A service to determine the licence plate of parked cars in photo's using OCR.
http://localhost:5221/swagger

### Parking session API
Manages the parking using the detection of the parked vehicle and the start and stop of the paid session.
http://localhost:5131/swagger

### Parking fee API
Handels the delivery of parking fees for non paying vehicles.
http://localhost:5261/swagger

## Dependencies
### MailDev
Fake mail server to accept all outgoing e-mails.
http://localhost:1080 (smtp: 1025)

### Zipkin
http://localhost:9411