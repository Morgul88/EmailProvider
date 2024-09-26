using System;
using System.Threading.Tasks;
using Azure;
using Azure.Communication.Email;
using Azure.Messaging.ServiceBus;
using EmailProvider.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace EmailProvider.Functions
{
	public class EmailSender
	{
		private readonly ILogger<EmailSender> _logger;
		private readonly EmailClient _emailClient;

		public EmailSender(ILogger<EmailSender> logger, EmailClient emailClient)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
			_emailClient = emailClient ?? throw new ArgumentNullException(nameof(emailClient));
		}

		[Function(nameof(EmailSender))]
		public async Task Run(
			[ServiceBusTrigger("email_request", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
			ServiceBusMessageActions messageActions)
		{
			try
			{
				var request = UnPackEmailRequest(message);

				if (request != null && !string.IsNullOrEmpty(request.To))
				{
					if (await SendEmailAsync(request))
					{
						await messageActions.CompleteMessageAsync(message);
					}
					else
					{
						await messageActions.AbandonMessageAsync(message);
					}
				}
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error : EmailSender.Run :: {ex.Message}");
				await messageActions.AbandonMessageAsync(message);
			}
		}

		public EmailRequest? UnPackEmailRequest(ServiceBusReceivedMessage message)
		{
			if (message?.Body == null)
			{
				_logger.LogError("Message body is null.");
				return null;
			}

			try
			{
				var request = JsonConvert.DeserializeObject<EmailRequest>(message.Body.ToString());
				return request;
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error unpacking email request: {ex.Message}");
			}

			return null;
		}

		public async Task<bool> SendEmailAsync(EmailRequest request)
		{
			try
			{
				var senderAddress = Environment.GetEnvironmentVariable("SenderAddress");
				if (string.IsNullOrEmpty(senderAddress))
				{
					_logger.LogError("Sender address environment variable is not set.");
					throw new ArgumentNullException("SenderAddress");
				}

				var result = await _emailClient.SendAsync(
					WaitUntil.Completed,
					senderAddress: senderAddress,
					recipientAddress: request.To,
					subject: request.Subject,
					htmlContent: request.HtmlBody,
					plainTextContent: request.PlainText);

				if (result.HasCompleted)
					return true;
			}
			catch (Exception ex)
			{
				_logger.LogError($"Error : SendEmailAsync.Run :: {ex.Message}");
			}

			return false;
		}
	}
}
