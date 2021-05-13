using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Net.Bluewalk.OpenThermGateway2Mqtt.Tcp;

namespace Net.Bluewalk.OpenThermGateway2Mqtt
{
    public class OtgwClient
    {
        private readonly ILogger<OtgwClient> _logger;
        private readonly TcpClientAsync _tcpClient;
        private readonly string _tcpHost;
        private readonly int _tcpPort;
        private readonly System.Timers.Timer _tmrReconnect = new(15000); // 15 seconds

        private readonly Dictionary<int, string> _openthermIds = new() {
            {0, "flame_status"}, {1, "control_setpoint"}, {9, "remote_override_setpoint"}, {14, "max_relative_modulation_level"},
            {16, "room_setpoint"}, {17, "relative_modulation_level"}, {18, "ch_water_pressure"}, {24, "room_temperature"},
            {25, "boiler_water_temperature"}, {26, "dhw_temperature"}, {27, "outside_temperature"}, {28, "return_water_temperature"},
            {56, "dhw_setpoint"}, {57, "max_ch_water_setpoint"}, {116, "burner_starts"}, {117, "ch_pump_starts"}, {118, "dhw_pump_starts"},
            {119, "dhw_burner_starts"}, {120, "burner_operation_hours"}, {121, "ch_pump_operation_hours"}, {122, "dhw_pump_valve_operation_hours"},
            {123, "dhw_burner_operation_hours"}, {1001, "cooling_mode"}, {1002, "burner_on"}, {1003, "central_heating_mode"}, {1004, "domestic_hot_water_mode"},
            {1005, "domestic_hot_water_enabled"}, {1006, "fault_indication"}
        };
        private readonly Dictionary<int, string> _openthermIdsTypes = new() {
            {0, "flag8"}, {1, "f8.8"}, {9, "f8.8"}, {14, "f8.8"}, {16, "f8.8"}, {17, "f8.8"}, {18, "f8.8"}, {24, "f8.8"}, {25, "f8.8"},
            {26, "f8.8"}, {27, "f8.8"}, {28, "f8.8"}, {56, "f8.8"}, {57, "f8.8"}, {116, "u16"}, {117, "u16"}, {118, "u16"}, {119, "u16"},
            {120, "u16"}, {121, "u16"}, {122, "u16"}, {123, "u16"}
        };
        private Dictionary<int, object> _values = new() {
            {0, null}, {1, null}, {9, null}, {14, null}, {16, null}, {17, null}, {18, null}, {24, null}, {25, null},
            {26, null}, {27, null}, {28, null}, {56, null}, {57, null}, {116, null}, {117, null}, {118, null}, {119, null},
            {120, null}, {121, null}, {122, null}, {123, null}, {1001, null}, {1002, null}, {1003, null}, {1004, null}, {1005, null}, {1006, null}
        };
        private Dictionary<int, object> _previousValues = new() {
            {0, null}, {1, null}, {9, null}, {14, null}, {16, null}, {17, null}, {18, null}, {24, null}, {25, null},
            {26, null}, {27, null}, {28, null}, {56, null}, {57, null}, {116, null}, {117, null}, {118, null}, {119, null},
            {120, null}, {121, null}, {122, null}, {123, null}, {1001, null}, {1002, null}, {1003, null}, {1004, null}, {1005, null}, {1006, null}
        };

        public delegate void OtgwDataReceived(int id, string name, object data);
        public delegate void OtgwRawDataReceived(string data);
        public delegate void OtgwCmdResponseReceived(string cmd, string result);

        public event OtgwDataReceived DataReceived;
        public event OtgwRawDataReceived RawDataReceived;
        public event OtgwCmdResponseReceived CmdResponseReceived;

        public OtgwClient(ILogger<OtgwClient> logger, IOptions<Config> config)
        {
            _logger = logger;
            _tcpClient = new TcpClientAsync();
            _tcpClient.DataRead += client_DataRead;
            _tcpClient.ClientReadException += tcpClient_Exception;
            _tcpClient.ClientWriteException += tcpClient_Exception;
            _tcpHost = config.Value.TcpHost;
            _tcpPort = config.Value.TcpPort;
            _tmrReconnect.Elapsed += tmrReconnect_Elapsed;
        }

        public void Start()
        {
            _logger.LogInformation("Attempting to open connection to OTGW via {0}:{1}", _tcpHost, _tcpPort);

            _tcpClient?.Connect(_tcpHost, _tcpPort);
        }

        public void Stop()
        {
            _logger.LogInformation("Disconnecting from OTGW");

            _tcpClient?.Disconnect();
        }

        void tmrReconnect_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            _tmrReconnect.Stop();
            Start();
        }

        private void tcpClient_Exception(object sender, ExceptionEventArgs e)
        {
            _logger.LogError(e.Exception, "Exception during reading/writing from TCP stream");

            // Exception received, disconnect and try to reconnect after 15 seconds
            Stop();

            _logger.LogInformation("Scheduling reconnect after 15 seconds");
            _tmrReconnect.Start();
        }

        private void client_DataRead(object sender, DataReceiveEventArgs e)
        {
            ParseData(e.Data);
        }

        private void ParseData(string data)
        {
            data = data.Replace("\r", string.Empty);

            try
            {
                // Forward raw data
                RawDataReceived?.Invoke(data);

                // Result of a command
                if (data.Contains(':'))
                {
                    var cmd = data.Substring(0, 2);
                    var result = data.Substring(4);

                    switch (cmd)
                    {
                        case "PS": // Received status summary, change it back to continues status
                            if (result.Equals("1"))
                                SendCommand("PS", "0");
                            break;

                        default: // Other results, publish result to MQTT
                            _logger.LogInformation("Received ACK from OTGW for command {0}, result {1}", cmd, result);

                            CmdResponseReceived?.Invoke(cmd, result);
                            break;
                    }

                    return;
                }

                // Can parse the message, make sure it's 9 characters long
                if (data.Length != 9) return;

                var target = data.Substring(0, 1); // B, T, A, R, E
                var type = data.Substring(1, 1);
                var id = int.Parse(data.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
                var payload = data.GetLast(4);

                if (!target.Equals("B") && !target.Equals("T") && !target.Equals("A")) return;

                if (!type.Equals("1") && !type.Equals("4") && !type.Equals("C") && !type.Equals("9")) return;

                if (!_openthermIds.ContainsKey(id)) return;

                switch (_openthermIdsTypes[id])
                {
                    case "flag8":
                        if (!target.Equals("A"))
                        {
                            _values[id] = Convert.ToString(
                                             int.Parse(payload.Substring(0, 2), System.Globalization.NumberStyles.HexNumber),
                                             2).PadLeft(8, '0')
                                         + "/" +
                                         Convert.ToString(
                                             int.Parse(payload.Substring(2, 2), System.Globalization.NumberStyles.HexNumber),
                                             2).PadLeft(8, '0');

                            _values[1001] = (_values[id] as string).Substring(12, 1); // Cooling room MsgID=0, LWB, 00000000/00010000
                            _values[1002] = (_values[id] as string).Substring(13, 1); // OpenTherm MsgID=0, LWB, 00000000/00001000
                            _values[1003] = (_values[id] as string).Substring(15, 1); // Heating up room - OpenTherm MsgID=0, LWB, 0000 0100
                            _values[1004] = (_values[id] as string).Substring(14, 1); // Heating up boiler OpenTherm MsgID=0, LWB, 0000 0010
                            _values[1005] = (_values[id] as string).Substring(6, 1); // OpenTherm MsgID=0, HGB, 0000 0100
                            _values[1006] = (_values[id] as string).Substring(16, 1); // Fault indication, LWB 0000 00001
                        }
                        break;
                    case "f8.8":
                        _values[id] = Math.Round((float)int.Parse(payload, System.Globalization.NumberStyles.HexNumber) / 256, 2);

                        break;
                    case "u16":
                        _values[id] = int.Parse(payload, System.Globalization.NumberStyles.HexNumber);
                        break;
                }

                foreach (var key in _openthermIds.Keys)
                {
                    if (_values[key] == null || _values[key].Equals(_previousValues[key])) continue;
                    _previousValues[key] = _values[key];

                    DataReceived?.Invoke(key, _openthermIds[key], _values[key]);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An error occurred parsing message '{0}'", data);
            }
        }

        public void SendCommand(string command, string value)
        {
            if (_tcpClient == null) return;
            _logger.LogInformation("Sending command {0}={1} to TCP {2}:{3}", command, value, _tcpHost, _tcpPort);
            _tcpClient.Send($"{command}={value}\r\n");
        }

        public void SendRaw(byte[] data)
        {
            _tcpClient?.Send(data);
        }

        public void SendRaw(string data)
        {
            _tcpClient?.Send(data);
        }
    }

    #region StringExtension

    public static class StringExtension
    {
        public static string GetLast(this string source, int tailLength)
        {
            return tailLength >= source.Length ? source : source.Substring(source.Length - tailLength);
        }

        public static bool ToBoolean(this string value, string trueString, string falseString)
        {
            if (value == null)
                return false;

            if (value.ToUpper().Equals(trueString.ToUpper()))
                return true;

            if (value.ToUpper().Equals(falseString.ToUpper()))
                return false;

            return false;
        }
    }

    #endregion
}
