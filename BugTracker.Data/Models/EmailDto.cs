using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Data.Models
{
    public class EmailRequest
    {
        public string To { get; set; }
        public string Subject { get; set; }

        public string? HtmlBody { get; set; }
        public string? TextBody { get; set; }

        public List<string>? CC { get; set; } = new();
        public List<string>? BCC { get; set; } = new();
    }

    public class EmailRequests
    {
        public List<string> To { get; set; }
        public string Subject { get; set; }

        public string? HtmlBody { get; set; }
        public string? TextBody { get; set; }

        public List<string>? CC { get; set; } = new();
        public List<string>? BCC { get; set; } = new();
    }
}
