namespace Net.Bluewalk.OpenThermGateway2Mqtt
{
    public class Config
    {
        public string TcpHost { get; set; }
        public int TcpPort { get; set; }
        public string MqttBroker { get; set; }
        public int MqttPort { get; set; }
        public string MqttUsername { get; set; }
        public string MqttPassword { get; set; }
        public string MqttPrefix { get; set; }

        public Config()
        {
            TcpPort = 2323;
            MqttPort = 1883;
        }
    }
}