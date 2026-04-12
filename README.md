PTZ Camera Keep-Alive (Work in progress.)

This is a front-end for a script I wrote in a hurry during a church service, as our PTZ camera times out the HTTP-based control connection after 5 minutes or so. This was
causing some chaos while I scrambled to get the camera reconnected - thus, the Keep-Alive script was born. 

Why? The option to disable the time-out does not exist in the camera's firmware.

The script itself is simple in that it sends the camera a ping (specifically, it sends a useless request once it logs into the camera) at a specified interval, keeping the camera from timing out due to inactivity. The user will need to know the 
camera's IP address, the port (defaulting to 80 as most of them are,) the admin username and password, and enter in the desired interval. I recommend 4 minutes, or 240 seconds, 
as the default time-out seems to be 5 minutes.

The front-end consists of basic controls - as mentioned, configuration settings such as camera's IP address, port, and username/password. It also gives the option of whether or
not to have the script run automatically when Windows starts, useful for me in a church environment so that I don't have to walk someone through on how to execute a Powershell
script!

The front-end also includes a basic IP scanning interface (though not implemented yet.) The idea is to help with canera IP identifiation obviously, making the process even
easier for the average user, but they will need to know what their local domain range is.

This is a work in progress - drop me a line at rmorrow@breezelineohio.net if you have any feature suggestions. 

To-Do:

Fix automatic script execution (script builds and executes manually fine, but subroutine on executing the script automatically isn't working)
Clean up the code (Good lord am I rusty.)
Implement IP scanning, returning IP addresses and machine names
Flesh out configuration menu which would include domain ranges for IP scanning and other less important settings

