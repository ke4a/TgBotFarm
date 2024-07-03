using System.Text.Json.Serialization;

namespace BotFarm.Core.Models.Inputs
{
    public class MessageInput
    {
        [JsonPropertyName("chatId")]
        public long ChatId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
