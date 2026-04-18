using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace PTZ_Camera_Keep_Alive
{
    public partial class howto : Form
    {
        public howto()
        {
            InitializeComponent();
            label1.Text = "Camera IP: Your camera's local IP address." + Environment.NewLine +
                "Port (80 deault): Your camera's remote access port. In most cases, this will be" + Environment.NewLine +
                "                  80. If you are not sure, we will discuss this later in this guide." + Environment.NewLine +
                "Username: Your camera's admin username." + Environment.NewLine +
                "Password: Your camera's admin password." + Environment.NewLine +
                "Ping Duration: The time interval for the script to ping the camera. Most cameras seem" + Environment.NewLine +
                "               to time-out after 5 minutes, so the default is set to 4 minutes (240 seconds.)" + Environment.NewLine +
                "Run on startup: You have the option to have the script automatically start when your PC starts." + Environment.NewLine +
                "                This lets this utility be a one-time process unless any of your configuration" + Environment.NewLine +
                "                ever changes." + Environment.NewLine + Environment.NewLine +
                "Scan network: This utility includes a very basic no-frills IP and port scanner. At this time," + Environment.NewLine +
                "              only scanning of your current IP domain range is possible. If your camera is" + Environment.NewLine +
                "              outside of this range, the scanner likely will not find it. It is implemented" + Environment.NewLine +
                "              to help with the discovery of your local camera if you're not sure what the IP is." + Environment.NewLine +
                "              Double-click the IP once the scan is complete to auto-fill the camera IP info." + Environment.NewLine + Environment.NewLine +
                "Create and Run Script: When you're ready, click this button to generate the script. Once" + Environment.NewLine +
                "                       complete, the script will run and the utility will close. If the" + Environment.NewLine +
                "                       option to run at startup was not selected, you will need to run" + Environment.NewLine +
                "                       this utility as needed to keep the camera awake." + Environment.NewLine + Environment.NewLine +
                "                       A Powershell window will open, and the script will run, giving" + Environment.NewLine +
                "                       updates as it pings. If it encounters an error, adjust settings" + Environment.NewLine +
                "                       as needed.";
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.Close();
            Form main = new Form1();
            main.Show();
        }
    }
}
