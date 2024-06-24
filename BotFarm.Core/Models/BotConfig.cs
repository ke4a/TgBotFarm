namespace BotFarm.Core.Models
{
    public class BotConfig
    {
        public string Token { get; set; }

        public string Handle { get; set; }

        public WebDAVSettings WebDAVSettings { get; set; }

        public long AdminChatId { get; set; }
    }
}
