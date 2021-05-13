using System;
using Serilog.Core;
using Serilog.Events;

namespace Net.Bluewalk.OpenThermGateway2Mqtt
{
    public class EnvironmentVariableLoggingLevelSwitch : LoggingLevelSwitch
    {
        public EnvironmentVariableLoggingLevelSwitch(string environmentVariable,
            LogEventLevel defaultLevel = LogEventLevel.Information)
        {
            MinimumLevel =
                Enum.TryParse<LogEventLevel>(Environment.GetEnvironmentVariable(environmentVariable), true,
                    out var level)
                    ? level
                    : defaultLevel;

#if DEBUG
        MinimumLevel = LogEventLevel.Debug;
#endif
        }
    }
}