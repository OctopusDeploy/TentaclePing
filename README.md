TentaclePing is a command line tool that helps to diagnose troubled connections between an Octopus server and a listening Tentacle. 

[Download](https://github.com/OctopusDeploy/TentaclePing/releases)

## TentaclePing

Unlike regular Ping, TentaclePing makes an SSL connection to the Tentacle service, meaning it will only work if the Tentacle service is running and responding. 

The command takes four arguments:

    TentaclePing <ip/hostname> [<port>] [<datasize>] [<chunksize>] [<ssl_protocol> {None|Ssl2|Ssl3|Tls|Tls11|Tls12|Default}]
    
Examples:

    TentaclePing.exe MyServer                   # Uses default port (10933)
    TentaclePing.exe 10.0.0.1 10934             # Explicit port
    TentaclePing.exe 10.0.0.1 10933 0 2 Tls12   # Specified IP address, port, data size, chunk size, and SSL Protocol

If you are experiencing slow deployments or package uploads with Octopus, you can use TentaclePing to determine whether there is a connectivity problem:

1. Download TentaclePing.zip and extract it on your Octopus server
2. Open a command prompt, and run TentaclePing as shown above
3. Let it run for a few hours. If it encounters failures, it will log them and count the number of failures
4. Send the results to the Octopus Deploy team

## TentaclePong

Normally, TentaclePing is pointed at a listening Tentacle or Octopus Server polling endpoint. If you have run TentaclePing and determinied that you have issues with connections being dropped, the next step is to determine whether the problem is with the Tentacle/Octopus server, or just a network issue. TentaclePong is the server equivalent for TentaclePing, listening on a port and waiting for requests from TentaclePing. 

Usage:

    TentaclePong 10945              		# On server 1, listen on a port
    TentaclePing YourServer 10945   		# Server 2 will ping server 1
    TentaclePing YourServer 10945 100		# Server 2 will send 100Mb of data using default chunk size (2Mb) to server 1
    TentaclePing YourServer 10945 100 10  	# Server 2 will send 100Mb of data using a chunk size of 10Mb to server 1


If TentaclePing reports problems when pointed at a listening Tentacle/Octopus server, but there are no problems with TentaclePong, then the issue is most likely a bug or resource utilization problem on the Tentacle/Octopus server (e.g., high CPU usage, limited memory, etc.). 

However, if the connection problems persist with TentaclePong, then the problem is most likely down to a network issue. Unfortunately there's really not much the Octopus team can do to help with that. 

## Capturing output

If you don't plan to watch TentaclePing run, you may want to pipe the results to a text file:

    TentaclePing.exe MyServer 10933 > Output.txt
    
This file can then easily be sent to the Octopus support team (support@octopusdeploy.com) for analysis.


## Contributing

This project is managed by Github Actions. 

Create a new branch for your changes and open a PR to main to build assests

Pushing a tag with the format v1.2.3 will trigger a new Release to be created