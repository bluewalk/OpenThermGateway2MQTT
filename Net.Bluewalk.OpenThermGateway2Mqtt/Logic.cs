using System;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Client.Options;
using MQTTnet.Extensions.ManagedClient;
using Net.Bluewalk.OpenThermGateway2Mqtt.Tcp;

namespace Net.Bluewalk.OpenThermGateway2Mqtt
{
    public class Logic : IHostedService
    {
        private readonly ILogger<Logic> _logger;
        private readonly OtgwClient _otgwClient;
        private readonly Config _config;
        private readonly IManagedMqttClient _mqttClient;
        private readonly TcpServer _otgwServer;
        private readonly string _mqttPrefix;

        private string MqttStatusTopic => _mqttPrefix + "otgw/status";
        private string MqttCommandTopic => _mqttPrefix + "otgw/command/+";

        public Logic(ILogger<Logic> logger, IOptions<Config> config, OtgwClient otgwClient)
        {
            _logger = logger;
            _otgwClient = otgwClient;
            _config = config.Value;
            _mqttPrefix = _config.MqttPrefix;

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            _otgwClient.RawDataReceived += data => _logger.LogDebug("Received: {0}", data);

            _mqttClient = new MqttFactory().CreateManagedMqttClient();
            _mqttClient.UseApplicationMessageReceivedHandler(MqttClientOnApplicationMessageReceived);
            _mqttClient.UseConnectedHandler(e => { _logger.LogInformation("MQTT Client: Connected"); });
            _mqttClient.UseDisconnectedHandler(e =>
            {
                if (e.ClientWasConnected)
                    _logger.LogInformation($"MQTT Client: Disconnected ({e.Exception?.Message ?? "clean"})");
                else
                    _logger.LogWarning($"MQTT Client: Unable to connect ({e.Exception?.Message ?? "clean"})");
            });
            _mqttClient.ConnectingFailedHandler = new ConnectingFailedHandlerDelegate(args =>
                _logger.LogWarning("MQTT Client: Unable to connect ({0})", args.Exception.Message)
            );
            _mqttClient.SynchronizingSubscriptionsFailedHandler = new SynchronizingSubscriptionsFailedHandlerDelegate(
                args =>
                    _logger.LogWarning("MQTT Client: Synchronizing subscriptions failed: {0}", args.Exception.Message)
            );

            // OTGWServer
            _otgwServer = new TcpServer
            {
                Port = config.Value.TcpPort
            };
            _otgwServer.OnDataAvailable += OtgwServer_OnDataAvailable;
            _otgwClient.DataReceived += OtgwClient_DataReceived;
            _otgwClient.RawDataReceived += OtgwClient_RawDataReceived;
            _otgwClient.CmdResponseReceived += OtgwClient_CmdResponseReceived;
        }

        private void MqttClientOnApplicationMessageReceived(MqttApplicationMessageReceivedEventArgs arg)
        {
            var command = arg.ApplicationMessage.Topic.ToUpper().Split('/').Last();
            var data = Encoding.ASCII.GetString(arg.ApplicationMessage.Payload);

            _otgwClient.SendCommand(command, data);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            _logger.LogError((Exception) e.ExceptionObject, "Unhandled exception");
        }

        private void Publish(string topic, string data)
        {
            if (!_mqttClient.IsConnected) return;

            var msg = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(data)
                .WithExactlyOnceQoS()
                .WithRetainFlag();

            _mqttClient.PublishAsync(msg.Build()).Wait();
        }

        #region OtgwClient callbacks

        private void OtgwClient_DataReceived(int id, string name, object data)
        {
            Publish(_mqttPrefix + MqttStatusTopic + "/" + name, data.ToString());
        }

        private void OtgwClient_RawDataReceived(string data)
        {
            _otgwServer.Send(data + "\r\n");
        }

        private void OtgwClient_CmdResponseReceived(string cmd, string result)
        {
            Publish(_mqttPrefix + MqttStatusTopic + "/command_" + cmd.ToLower(), result);
        }

        #endregion

        #region OtgwServer callbacks

        private void OtgwServer_OnDataAvailable(TcpServerConnection connection)
        {
            try
            {
                // var ourStream = connection.Socket.GetStream();
                // var data = new byte[connection.Socket.ReceiveBufferSize];
                //
                // var bytesRead = ourStream.Read(data, 0, connection.Socket.ReceiveBufferSize);

                var reader = new StreamReader(connection.Socket.GetStream());
                var command = reader.ReadLine();
                _otgwClient.SendRaw(command + "\r\n");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error on processing TCP data");
            }
        }

        #endregion

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var options = new ManagedMqttClientOptionsBuilder()
                .WithAutoReconnectDelay(TimeSpan.FromSeconds(15))
                .WithClientOptions(new MqttClientOptionsBuilder()
                    .WithClientId($"OpenThermGatewat2Mqtt-{Environment.MachineName}")
                    .WithCredentials(_config.MqttUsername, _config.MqttPassword)
                    .WithTcpServer(_config.MqttBroker, _config.MqttPort))
                .Build();

            if (!string.IsNullOrEmpty(_config.MqttBroker) && !string.IsNullOrEmpty(_config.TcpHost))
            {
                _logger.LogInformation("Connecting to {0}:{1}", _config.MqttBroker, _config.MqttPort);
                await _mqttClient.StartAsync(options);

                await _mqttClient.SubscribeAsync(new MqttTopicFilterBuilder().WithTopic(MqttCommandTopic)
                    .WithExactlyOnceQoS()
                    .Build());
            }
            else
                _logger.LogError("Configuration missing");

            _otgwClient.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_mqttClient is not {IsConnected: true}) return;

            await _mqttClient.StopAsync();

            _otgwClient.Stop();
        }
    }
}