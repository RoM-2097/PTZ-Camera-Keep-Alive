using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Runtime.InteropServices;

namespace PTZ_Camera_Keep_Alive
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            // Ensure ListView is configured and has columns (some designer edits cleared them)
            try
            {
                if (listView1 != null)
                {
                    listView1.View = View.Details;
                    listView1.FullRowSelect = true;
                    listView1.GridLines = true;
                    listView1.HeaderStyle = ColumnHeaderStyle.Nonclickable;
                    listView1.ShowItemToolTips = true;

                    if (listView1.Columns.Count == 0)
                    {
                        listView1.Columns.Add("IP", 120, HorizontalAlignment.Left);
                        listView1.Columns.Add("Hostname", 180, HorizontalAlignment.Left);
                        listView1.Columns.Add("MAC", 160, HorizontalAlignment.Left);
                        listView1.Columns.Add("Ports", 200, HorizontalAlignment.Left);
                    }

                    AdjustListViewColumns();
                }
            }
            catch { }
        }

        private async Task<string> ScanPortsAsync(string ipAddress, int[] ports, int timeoutMs)
        {
            var open = new List<int>();
            var sem = new SemaphoreSlim(30);
            var tasks = new List<Task>();

            foreach (var port in ports)
            {
                await sem.WaitAsync();
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using (var tcp = new TcpClient())
                        {
                            var connectTask = tcp.ConnectAsync(ipAddress, port);
                            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
                            if (completed == connectTask && tcp.Connected)
                            {
                                lock (open)
                                {
                                    open.Add(port);
                                }
                            }
                        }
                    }
                    catch { }
                    finally { sem.Release(); }
                }));
            }

            await Task.WhenAll(tasks);
            open.Sort();
            return open.Count == 0 ? string.Empty : string.Join(",", open);
        }

        private async void button2_Click(object sender, EventArgs e)
        {
            await ScanNetworkAsync();
        }

        // Simple /24 network scanner: pings 1..254 on the local /24 and resolves hostnames when possible.
        private async Task ScanNetworkAsync()
        {
            listView1.Items.Clear();
            button2.Enabled = false;

            var localIp = GetLocalIPv4();
            if (localIp == null)
            {
                MessageBox.Show("No active IPv4 network interface found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button2.Enabled = true;
                return;
            }

            var parts = localIp.GetAddressBytes();
            if (parts.Length != 4)
            {
                MessageBox.Show("Unexpected IP address format.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                button2.Enabled = true;
                return;
            }

            string prefix = $"{parts[0]}.{parts[1]}.{parts[2]}."; // assume /24

            const int timeout = 300; // ms

            var tasks = new List<Task>();

            int total = 254;
            this.Invoke(() =>
            {
                progressBar1.Minimum = 0;
                progressBar1.Maximum = total;
                progressBar1.Value = 0;
            });

            for (int i = 1; i <= 254; i++)
            {
                string ipStr = prefix + i;
                if (ipStr == localIp.ToString())
                    continue;

                // Limit concurrent pings to avoid flooding the network
                tasks.Add(Task.Run(async () =>
                {
                    bool success = false;
                    try
                    {
                        using (var p = new Ping())
                        {
                            var reply = await p.SendPingAsync(ipStr, timeout);
                            if (reply.Status == IPStatus.Success)
                            {
                                success = true;
                                string host = "(unknown)";
                                try
                                {
                                    var entry = await Dns.GetHostEntryAsync(ipStr);
                                    host = entry.HostName;
                                }
                                catch { }

                                string mac = GetMacAddress(ipStr) ?? "(unknown)";
                                int[] portsToCheck = new[] { 22, 23, 80, 443, 554, 8000, 8080, 8554 };
                                string openPorts = await ScanPortsAsync(ipStr, portsToCheck, 300);
                                if (string.IsNullOrEmpty(openPorts)) openPorts = "(none)";
                                this.Invoke(() =>
                                {
                                    var item = new ListViewItem(new[] { ipStr, host, mac, openPorts });
                                    item.ToolTipText = $"Host: {host}\r\nMAC: {mac}\r\nOpen ports: {openPorts}";
                                    listView1.Items.Add(item);
                                });
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        // update progress regardless of success
                        this.Invoke(() => { if (progressBar1.Value < progressBar1.Maximum) progressBar1.Value++; });
                    }
                }));

                // Throttle concurrency a bit
                if (tasks.Count >= 100)
                {
                    await Task.WhenAll(tasks);
                    tasks.Clear();
                }
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);

            // after scanning, refresh ARP table and hostnames once to fill any missing info
            await RefreshArpAndHostnamesAsync();

            // adjust columns to fit content
            AdjustListViewColumns();

            button2.Enabled = true;
        }

        private IPAddress GetLocalIPv4()
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var ip = ua.Address;
                        // ignore APIPA
                        if (!ip.ToString().StartsWith("169."))
                            return ip;
                    }
                }
            }

            return null;
        }

        private void listView1_DoubleClick(object sender, EventArgs e)
        {
            if (listView1.SelectedItems.Count == 0) return;
            var item = listView1.SelectedItems[0];
            var ip = item.Text; // first column
            textBox1.Text = ip;
        }

        private async Task RefreshArpAndHostnamesAsync()
        {
            try
            {
                // build arp dictionary
                var arpDict = new Dictionary<string, string>();
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = await p.StandardOutput.ReadToEndAsync();
                    p.WaitForExit();
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var l = line.Trim();
                        if (string.IsNullOrWhiteSpace(l)) continue;
                        // parse lines like:  192.168.1.1          00-11-22-33-44-55     dynamic
                        var parts = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && IPAddress.TryParse(parts[0], out _))
                        {
                            var mac = parts[1].Replace('-', ':').ToUpperInvariant();
                            arpDict[parts[0]] = mac;
                        }
                    }
                }

                // Update list entries
                for (int i = 0; i < listView1.Items.Count; i++)
                {
                    var item = listView1.Items[i];
                    var ip = item.Text;
                    // update MAC
                    if (item.SubItems.Count < 3 || string.IsNullOrWhiteSpace(item.SubItems[2].Text) || item.SubItems[2].Text == "(unknown)")
                    {
                        if (arpDict.TryGetValue(ip, out var mac))
                            item.SubItems[2].Text = mac;
                    }

                    // update hostname
                    if (item.SubItems.Count < 2 || string.IsNullOrWhiteSpace(item.SubItems[1].Text) || item.SubItems[1].Text == "(unknown)")
                    {
                        try
                        {
                            var entry = await Dns.GetHostEntryAsync(ip);
                            if (entry != null && !string.IsNullOrWhiteSpace(entry.HostName))
                            {
                                if (item.SubItems.Count < 2)
                                    item.SubItems.Add(entry.HostName);
                                else
                                    item.SubItems[1].Text = entry.HostName;
                            }
                        }
                        catch { }
                    }

                    // refresh tooltip to include updated info and ports column if present
                    string hostText = item.SubItems.Count >= 2 ? item.SubItems[1].Text : "(unknown)";
                    string macText = item.SubItems.Count >= 3 ? item.SubItems[2].Text : "(unknown)";
                    string portsText = item.SubItems.Count >= 4 ? item.SubItems[3].Text : "(none)";
                    item.ToolTipText = $"Host: {hostText}\r\nMAC: {macText}\r\nOpen ports: {portsText}";

                }
            }
            catch { }
        }

        private void AdjustListViewColumns()
        {
            try
            {
                if (listView1.InvokeRequired)
                {
                    listView1.Invoke(new Action(AdjustListViewColumns));
                    return;
                }

                listView1.BeginUpdate();
                // Auto-size each column to its content first
                for (int i = 0; i < listView1.Columns.Count; i++)
                {
                    listView1.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.ColumnContent);
                    // Make sure header fits as well
                    int headerWidth = TextRenderer.MeasureText(listView1.Columns[i].Text, listView1.Font).Width + 16;
                    if (listView1.Columns[i].Width < headerWidth)
                    {
                        listView1.AutoResizeColumn(i, ColumnHeaderAutoResizeStyle.HeaderSize);
                        if (listView1.Columns[i].Width < headerWidth)
                            listView1.Columns[i].Width = headerWidth;
                    }
                }

                // If there is leftover space, expand the last column to fill the control
                if (listView1.Columns.Count > 0)
                {
                    int totalColsWidth = 0;
                    for (int i = 0; i < listView1.Columns.Count; i++)
                        totalColsWidth += listView1.Columns[i].Width;

                    int clientWidth = listView1.ClientSize.Width;
                    // Account for potential vertical scrollbar
                    int extra = 0;
                    if (listView1.Items.Count > listView1.ClientSize.Height / Math.Max(1, listView1.Font.Height))
                        extra = SystemInformation.VerticalScrollBarWidth;

                    int remaining = clientWidth - totalColsWidth - extra - 8;
                    if (remaining > 20)
                    {
                        // enlarge last column
                        var last = listView1.Columns[listView1.Columns.Count - 1];
                        last.Width = last.Width + remaining;
                    }
                }
                listView1.EndUpdate();
            }
            catch { }
        }

        private string GetMacAddress(string ipAddress)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains(ipAddress))
                        {
                            var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                var mac = parts[1].Replace('-', ':').ToUpperInvariant();
                                return mac;
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
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

            var startInfo = new ProcessStartInfo()
            {
                // Start the PowerShell executable and show its window so the user can see output.
                FileName = "powershell.exe",
                // Keep -NoExit if you want the window to remain open after the script finishes.
                Arguments = $"-NoExit -NoProfile -ExecutionPolicy Bypass -File \"{path}\"",
                UseShellExecute = true,
                CreateNoWindow = false
            };

            // Start PowerShell and do not block the UI thread by waiting on output.
            Process.Start(startInfo);

        }

        private void textBox2_TextChanged(object sender, EventArgs e)
        {

        }

        private void viewCurrentScriptToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string path = @"c:\test\ptz-keep-alive.ps1";
            if (!File.Exists(path))
            {
                MessageBox.Show($"Script not found at {path}", "Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    Arguments = $"\"{path}\"",
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open script: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void listView1_SelectedIndexChanged(object sender, EventArgs e)
        {

        }

        private void maskedTextBox1_MaskInputRejected(object sender, MaskInputRejectedEventArgs e)
        {

        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("PTZ Camera Keep-Alive Script Generator" + Environment.NewLine + "Version 1.0" + Environment.NewLine + "Copyright Nova Software Industries", "About PTZ Camera Keep-Alive",
                MessageBoxButtons.OK);
        }

        private void configureToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // howto.Show;
        }

        private void howtoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Hide();
            Form frm = new howto();
            frm.ShowDialog();
        }
    }
}
