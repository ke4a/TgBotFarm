namespace BotFarm.Core.Models
{
    public class Language
    {
        public string Locale { get; set; }

        public Dictionary<string, string> Mapping { get; set; }
    }
}
