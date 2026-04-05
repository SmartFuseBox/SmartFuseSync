using CommandLinePlus;
using System.IO.Ports;

namespace SmartFuseSync;

[CmdLineDescription("Manages configuration updates for SmartFuseBox devices via COM PORT")]
internal class SerialConfigProcessor : BaseCommandLine, IDisposable
{
    private SerialPort? _serialPort;

    public override string Name => "Config";

    public override int SortOrder => 0;

    public override bool IsEnabled => true;

    public override void DisplayHelp()
    {
        Display.WriteLine(VerbosityLevel.Quiet, "Configuration management commands for SmartFuseBox devices");
        Display.WriteLine(VerbosityLevel.Quiet, "Usage: config <command> [options]");
    }

    public override int Execute(string[] args)
    {
        return 0;
    }

    [CmdLineDescription("Updates configuration from a file to SmartFuseBox device")]
    public int Update(
        [CmdLineAbbreviation("f", "Path to the configuration file")] string filePath,
        [CmdLineAbbreviation("p", "COM port name (e.g., COM3)")] string portName,
        [CmdLineAbbreviation("b", "Baud rate for COM port connection")] int baudRate = 9600)
    {
        if (!IsEnabled)
            return -1;

        try
        {
            // Validate file exists
            if (!File.Exists(filePath))
            {
                Display.WriteLine(VerbosityLevel.Quiet, $"Error: Configuration file not found: {filePath}");
                return -1;
            }

            Display.WriteLine(VerbosityLevel.Normal, $"Reading configuration from: {filePath}");

            // Read all lines and ignore blank lines
            string[] lines = File.ReadAllLines(filePath);

            // Initialize COM port
            Display.WriteLine(VerbosityLevel.Normal, $"Opening COM port: {portName} at {baudRate} baud");
            _serialPort = new SerialPort(portName, baudRate)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 5000
            };

            _serialPort.Open();

            if (!_serialPort.IsOpen)
            {
                Display.WriteLine(VerbosityLevel.Quiet, $"Error: Failed to open COM port: {portName}");
                return -1;
            }

            // Send configuration data line-by-line and wait for ACK for each non-blank line
            foreach (string rawLine in lines)
            {
                string line = rawLine?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(line))
                    continue;

                // Extract command name for display and ACK matching, e.g. "M0:v=1" -> "M0"
                string commandName = line.Split(new[] { ':', '=' }, 2)[0];

                Display.WriteLine(VerbosityLevel.Normal, $"Sending {commandName}");

                // Send the full line to the device
                _serialPort.WriteLine(line);

                // Loop reading lines until we find the ACK or ERR for this specific command.
                // Other lines (telemetry, status) are logged at Verbose and discarded.
                string? ackResponse = null;
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);

                while (DateTime.UtcNow < deadline)
                {
                    string response;
                    try
                    {
                        response = _serialPort.ReadLine().Trim();
                    }
                    catch (TimeoutException)
                    {
                        continue;
                    }

                    if (response.Contains($"ACK:{commandName}") || response.Contains($"ERR:{commandName}"))
                    {
                        ackResponse = response;
                        break;
                    }

                    Display.WriteLine(VerbosityLevel.Full, $"  [{commandName}] {response}");
                }

                if (ackResponse is null)
                {
                    Display.WriteLine(VerbosityLevel.Quiet, $"Error: Timeout waiting for ACK from {commandName}");
                    return -1;
                }

                Display.WriteLine(VerbosityLevel.Normal, $"{ackResponse} received");

                if (ackResponse.Contains($"ERR:{commandName}"))
                    Display.WriteLine(VerbosityLevel.Quiet, $"Warning: Device reported error for {commandName}: {ackResponse}");
            }

            Display.WriteLine(VerbosityLevel.Quiet, "Configuration update completed successfully");
            return 0;
        }
        catch (UnauthorizedAccessException ex)
        {
            Display.WriteLine(VerbosityLevel.Quiet, $"Error: Access denied to COM port {portName}. {ex.Message}");
            return -1;
        }
        catch (IOException ex)
        {
            Display.WriteLine(VerbosityLevel.Quiet, $"Error: Communication error: {ex.Message}");
            return -1;
        }
        catch (Exception ex)
        {
            Display.WriteLine(VerbosityLevel.Quiet, $"Error: {ex.Message}");
            return -1;
        }
        finally
        {
            ClosePort();
        }
    }

    [CmdLineDescription("Lists available COM ports on the system")]
    public void ListPorts()
    {
        if (!IsEnabled)
            return;

        Display.WriteLine(VerbosityLevel.Quiet, "Available COM ports:");
        string[] ports = SerialPort.GetPortNames();

        if (ports.Length == 0)
        {
            Display.WriteLine(VerbosityLevel.Quiet, "  No COM ports found");
        }
        else
        {
            foreach (string port in ports)
            {
                Display.WriteLine(VerbosityLevel.Quiet, $"  {port}");
            }
        }
    }

    [CmdLineDescription("Tests connection to a SmartFuseBox device")]
    public int Test(
        [CmdLineAbbreviation("p", "COM port name (e.g., COM3)")] string portName,
        [CmdLineAbbreviation("b", "Baud rate for COM port connection")] int baudRate = 9600)
    {
        if (!IsEnabled)
            return -1;

        try
        {
            Display.WriteLine(VerbosityLevel.Normal, $"Testing connection to {portName} at {baudRate} baud...");

            _serialPort = new SerialPort(portName, baudRate)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = 3000,
                WriteTimeout = 3000
            };

            _serialPort.Open();

            if (_serialPort.IsOpen)
            {
                Display.WriteLine(VerbosityLevel.Quiet, $"Success: Connected to {portName}");
                return 0;
            }
            else
            {
                Display.WriteLine(VerbosityLevel.Quiet, $"Error: Failed to connect to {portName}");
                return -1;
            }
        }
        catch (Exception ex)
        {
            Display.WriteLine(VerbosityLevel.Quiet, $"Error: {ex.Message}");
            return -1;
        }
        finally
        {
            ClosePort();
        }
    }

    [CmdLineDescription("Reads current configuration from SmartFuseBox device")]
    public int Read(
        [CmdLineAbbreviation("p", "COM port name (e.g., COM3)")] string portName,
        [CmdLineAbbreviation("o", "Output file path to save configuration")] string? outputPath = null,
        [CmdLineAbbreviation("b", "Baud rate for COM port connection")] int baudRate = 9600)
    {
        if (!IsEnabled)
            return -1;

        try
        {
            Display.WriteLine(VerbosityLevel.Normal, $"Reading configuration from device on {portName}...");

            _serialPort = new SerialPort(portName, baudRate)
            {
                Parity = Parity.None,
                DataBits = 8,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                ReadTimeout = 5000,
                WriteTimeout = 5000
            };

            _serialPort.Open();

            if (!_serialPort.IsOpen)
            {
                Display.WriteLine(VerbosityLevel.Quiet, $"Error: Failed to open COM port: {portName}");
                return -1;
            }

            // Request configuration from device
            _serialPort.WriteLine("READ_CONFIG");
            string configData = _serialPort.ReadLine();

            Display.WriteLine(VerbosityLevel.Normal, "Configuration data received:");
            Display.WriteLine(VerbosityLevel.Normal, configData);

            // Save to file if output path specified
            if (!string.IsNullOrEmpty(outputPath))
            {
                File.WriteAllText(outputPath, configData);
                Display.WriteLine(VerbosityLevel.Quiet, $"Configuration saved to: {outputPath}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Display.WriteLine(VerbosityLevel.Quiet, $"Error: {ex.Message}");
            return -1;
        }
        finally
        {
            ClosePort();
        }
    }

    private void ClosePort()
    {
        if (_serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                _serialPort.Close();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
    }

    public void Dispose()
    {
        ClosePort();
        _serialPort?.Dispose();
        GC.SuppressFinalize(this);
    }
}
