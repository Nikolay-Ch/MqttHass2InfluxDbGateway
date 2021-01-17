using System.Text.RegularExpressions;

namespace MqttHass2InfluxDbGateway
{
    public static class StringExtensions
    {
        public static bool LikeMqttHassTopic(this string sourceTopic, string wildCardedMqttTopic) =>
            Regex.IsMatch(sourceTopic, "^" + wildCardedMqttTopic.Replace("+", ".*") + "$");
    }
}
