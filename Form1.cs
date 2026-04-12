using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PTZ_Camera_Keep_Alive
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {

        }

        private void textBox3_TextChanged(object sender, EventArgs e)
        {

        }

        private void label2_Click(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            // Check if all fields are filled out before proceeding
            if (textBox1.Text == "") { MessageBox.Show("Must enter camera IP address.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (textBox2.Text == "") { MessageBox.Show("Must enter camera's admin username.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (textBox3.Text == "") { MessageBox.Show("Must enter camera's admin password.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (textBox4.Text == "") { MessageBox.Show("Must enter ping duration (in seconds.)", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            // Create, open, and write to the script file
            string path = @"c:\test\ptz-keep-alive.ps1";

            // Ensure target directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? @"c:\test");
            using (var writer = new StreamWriter(path))
            {
                writer.WriteLine("$CameraIP = \"" + textBox1.Text + ("\"")); // "192.168.1.78\"");
                writer.WriteLine("$Port     = " + textBox5.Text);
                writer.WriteLine("$Username = \"" + textBox2.Text + "\"");
                writer.WriteLine("$Password = \"" + textBox3.Text + "\"");
                writer.WriteLine("$IntervalSeconds = \"" + textBox4.Text + "\"");

                writer.WriteLine("");

                writer.WriteLine("$pair = \"$Username`:$Password\"");
                writer.WriteLine("$encodedCreds = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes($pair))");

                writer.WriteLine("");

                writer.WriteLine("function Get-ProfileToken {");
                writer.WriteLine("    $soapBody = @\"");
                writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                writer.WriteLine("<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://www.w3.org/2003/05/soap-envelope\"");
                writer.WriteLine("                   xmlns:trt=\"http://www.onvif.org/ver10/media/wsdl\">");
                writer.WriteLine("<SOAP-ENV:Body>");
                writer.WriteLine("    <trt:GetProfiles/>");
                writer.WriteLine("  </SOAP-ENV:Body>");
                writer.WriteLine("</SOAP-ENV:Envelope>");
                writer.WriteLine("\"@");

                writer.WriteLine("");

                writer.WriteLine("   try {");
                writer.WriteLine("        $response = Invoke-WebRequest -Uri \"http://${CameraIP}:${Port}/onvif/media_service\" `");
                writer.WriteLine("            -Method POST `");
                writer.WriteLine("            -Body $soapBody `");
                writer.WriteLine("            -ContentType \"application/soap+xml; charset=utf-8\" `");
                writer.WriteLine("            -Headers @{ Authorization = \"Basic $encodedCreds\" } `");
                writer.WriteLine("            -TimeoutSec 10");

                writer.WriteLine("");

                writer.WriteLine("if ($response.StatusCode -eq 200) {");
                writer.WriteLine("# Extract first ProfileToken from XML");
                writer.WriteLine("$xml = [xml]$response.Content");
                writer.WriteLine("$token = $xml.SelectSingleNode(\"//*[local-name()='Profiles']\").token");
                writer.WriteLine("if (-not $token) {");

                writer.WriteLine("           throw \"No profile token found in response.\"");
                writer.WriteLine("}");

                writer.WriteLine("    return $token");
                writer.WriteLine("} else");
                writer.WriteLine("{");

                writer.WriteLine("   throw \"HTTP $($response.StatusCode)\"");
                writer.WriteLine("}");
                writer.WriteLine("}");
                writer.WriteLine("catch {");

                writer.WriteLine("           Write-Host \"[ERROR] Failed to get profile token: $($_.Exception.Message)\"");
                writer.WriteLine("exit 1");
                writer.WriteLine("}");
                writer.WriteLine("}");

                writer.WriteLine("# Function: Send Keep-Alive");

                writer.WriteLine("function Send-KeepAlive($profileToken) {");

                writer.WriteLine("$soapBody = @\"");
                writer.WriteLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
                writer.WriteLine("<SOAP-ENV:Envelope xmlns:SOAP-ENV=\"http://www.w3.org/2003/05/soap-envelope\"");
                writer.WriteLine("   xmlns:wsdl=\"http://www.onvif.org/ver20/ptz/wsdl\"> ");
                writer.WriteLine("<SOAP-ENV:Body>");
                writer.WriteLine("<wsdl:GetStatus>");
                writer.WriteLine("<wsdl:ProfileToken>$profileToken</wsdl:ProfileToken>");
                writer.WriteLine("</wsdl:GetStatus>");
                writer.WriteLine("</SOAP-ENV:Body>");
                writer.WriteLine("</SOAP-ENV:Envelope>");
                writer.WriteLine("\"@");

                writer.WriteLine("try {");
                writer.WriteLine("$response = Invoke-WebRequest -Uri \"http://${CameraIP}:${Port}/onvif/ptz_service\" `");
                writer.WriteLine("-Method POST `");
                writer.WriteLine("-Body $soapBody `");
                writer.WriteLine("-ContentType \"application/soap+xml; charset=utf-8\" `");
                writer.WriteLine("-Headers @{ Authorization = \"Basic $encodedCreds\" } `");
                writer.WriteLine("-TimeoutSec 10");

                writer.WriteLine("if ($response.StatusCode -eq 200) {");

                writer.WriteLine("    Write-Host \"[KEEP-ALIVE] Sent at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')\"");
                writer.WriteLine("} else");
                writer.WriteLine("{");

                writer.WriteLine("   Write-Host \"[WARNING] Response code: $($response.StatusCode)\"");

                writer.WriteLine("}");
                writer.WriteLine("}");
                writer.WriteLine("catch {");

                writer.WriteLine("Write-Host \"[ERROR] $($_.Exception.Message)\"");

                writer.WriteLine("  }");
                writer.WriteLine("}");

                writer.WriteLine("# Main Execution");

                writer.WriteLine("Write-Host \"[INFO] Discovering profile token from $CameraIP...\"");
                writer.WriteLine("$profileToken = Get-ProfileToken");
                writer.WriteLine("Write-Host \"[INFO] Using profile token: $profileToken\"");
                writer.WriteLine("Write-Host \"[INFO] Starting keep-alive loop every $IntervalSeconds seconds...\"");

                writer.WriteLine("while ($true) {");
                writer.WriteLine("    Send-KeepAlive -profileToken $profileToken");

                writer.WriteLine("    Start-Sleep -Seconds $IntervalSeconds");
                writer.WriteLine("}");
            }

            PowerShell ps = PowerShell.Create();
            ps.AddScript(File.ReadAllText(@path)).Invoke();

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }
    }
}
