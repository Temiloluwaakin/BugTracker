namespace BugTracker.Data
{
    public class AppConfig
    {
        public string SendGridKey { get; set; }
    }

    public class Gemini
    {
        public string ApiKey { get; set; }
        public string Model { get; set; }
    }
}
