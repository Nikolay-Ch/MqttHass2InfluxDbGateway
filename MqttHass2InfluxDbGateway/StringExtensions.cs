using System.Text.RegularExpressions;

namespace MqttHass2InfluxDbGateway
{
    public static class StringExtensions
    {
        public static bool LikeMqttHassTopic(this string sourceTopic, string wildCardedMqttTopic) =>
        //{
        //    var sourceTopicSplitted = sourceTopic.Split("/");
        //    var wildCardedMqttTopicSplitted = wildCardedMqttTopic.Split("/");

        //    if (sourceTopicSplitted.Length != wildCardedMqttTopicSplitted.Length)
        //        return false;

        //    for (int i = 0; i < wildCardedMqttTopicSplitted.Length; i++)
        //        if (wildCardedMqttTopicSplitted[i] != "+" && sourceTopicSplitted[i] != wildCardedMqttTopicSplitted[i])
        //            return false;

        //    return true;
        //}
            Regex.IsMatch(sourceTopic, "^" + wildCardedMqttTopic.Replace("+", ".*") + "$");
    }
}
