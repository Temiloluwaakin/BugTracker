using BugTracker.Data;
using BugTracker.Data.Models;
using Microsoft.Extensions.Options;
using SendGrid;
using SendGrid.Helpers.Mail;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Services.Helpers
{
    public interface IEmailHelper
    {
        Task<ApiResponse<object>> SendAsync(EmailRequest request);
    }
    public class EmailHelper : IEmailHelper
    {
        private readonly AppConfig _appConfig;
        private readonly string _fromEmail;
        private readonly string _fromName;

        public EmailHelper(
            IOptions<AppConfig> options
            )
        {
            _appConfig = options.Value;
            _fromEmail = "temmyak896@gmail.com";
            _fromName = "Test-Orbit";
        }

        public async Task<ApiResponse<object>> SendAsync(EmailRequest request)
        {
            try
            {
                var client = new SendGridClient(_appConfig.SendGridKey);

                var from = new EmailAddress(_fromEmail, _fromName);
                var to = new EmailAddress(request.To);

                SendGridMessage msg;

                if (!string.IsNullOrEmpty(request.HtmlBody))
                {
                    msg = MailHelper.CreateSingleEmail(
                        from,
                        to,
                        request.Subject,
                        request.TextBody,
                        request.HtmlBody
                    );
                }
                else
                {
                    msg = MailHelper.CreateSingleEmail(
                        from,
                        to,
                        request.Subject,
                        request.TextBody,
                        null
                    );
                }

                // Add CC
                if (request.CC != null)
                {
                    foreach (var cc in request.CC)
                    {
                        msg.AddCc(new EmailAddress(cc));
                    }
                }

                // Add BCC
                if (request.BCC != null)
                {
                    foreach (var bcc in request.BCC)
                    {
                        msg.AddBcc(new EmailAddress(bcc));
                    }
                }

                var response = await client.SendEmailAsync(msg);

                if ((int)response.StatusCode >= 400)
                {
                    var body = await response.Body.ReadAsStringAsync();
                    return new ApiResponse<object>
                    {
                        ResponseCode = ResponseCodes.Failed.ResponseCode,
                        ResponseMessage = "Failed to send Mail"
                    };
                }

                return new ApiResponse<object>
                {
                    ResponseCode = ResponseCodes.Success.ResponseCode,
                    ResponseMessage = "EMail Sent"
                };
            }
            catch (Exception ex)
            {
                Log.Error("Failed to send email");
                return null;
            }
        }

    }
}
