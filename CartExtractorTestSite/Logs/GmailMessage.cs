using System;
using System.Net;
using System.Net.Mail;

namespace _4_Tell.Logs
{
	/// <summary>
	/// Provides a message object that sends the email through gmail. 
	/// GmailMessage is inherited from <c>System.Web.Mail.MailMessage</c>, so all the mail message features are available.
	/// </summary>
	public class GmailMessage : MailMessage
	{
		#region CDO Configuration Constants

		//private const string SMTP_SERVER		= "http://schemas.microsoft.com/cdo/configuration/smtpserver";
		//private const string SMTP_SERVER_PORT	= "http://schemas.microsoft.com/cdo/configuration/smtpserverport";
		//private const string SEND_USING			= "http://schemas.microsoft.com/cdo/configuration/sendusing";
		//private const string SMTP_USE_SSL		= "http://schemas.microsoft.com/cdo/configuration/smtpusessl";
		//private const string SMTP_AUTHENTICATE	= "http://schemas.microsoft.com/cdo/configuration/smtpauthenticate";
		//private const string SEND_USERNAME		= "http://schemas.microsoft.com/cdo/configuration/sendusername";
		//private const string SEND_PASSWORD		= "http://schemas.microsoft.com/cdo/configuration/sendpassword";

		#endregion

		#region Private Variables

		private static string _gmailServer = "smtp.gmail.com";
		private static int _gmailPort = 587;
		private string _gmailUserName = string.Empty;
		private string _gmailPassword = string.Empty;
		private string _fromDisplayName = string.Empty;

		#endregion

		#region Public Members

		/// <summary>
		/// Constructor, creates the GmailMessage object
		/// </summary>
		/// <param name="gmailUserName">The username of the gmail account that the message will be sent through</param>
		/// <param name="gmailPassword">The password of the gmail account that the message will be sent through</param>
		public GmailMessage(string gmailUserName, string gmailPassword)
		{
			//this.Fields[SMTP_SERVER] = GmailMessage.GmailServer; 
			//this.Fields[SMTP_SERVER_PORT] = GmailMessage.GmailServerPort; 
			//this.Fields[SEND_USING] = 2;
			//this.Fields[SMTP_USE_SSL] = true;
			//this.Fields[SMTP_AUTHENTICATE] = 1;
			//this.Fields[SEND_USERNAME] = gmailUserName;
			//this.Fields[SEND_PASSWORD] = gmailPassword;

			_gmailUserName = gmailUserName;
			_gmailPassword = gmailPassword;
		}

		/// <summary>
		/// Sends the message. If no from address is given the message will be from <c>GmailUserName</c>@Gmail.com
		/// </summary>
		public void Send(string fromDisplay = null, string fromId = null)
		{
			try
			{
				//set from address
				if (fromDisplay == null)
					fromDisplay = _fromDisplayName; //see above to set default
				if (fromId != null)
					fromDisplay += " " + fromId;
				var fromAddress = GmailUserName;
				if (GmailUserName.IndexOf('@') == -1) fromAddress += "@gmail.com";
				this.From = new MailAddress(fromAddress, fromDisplay);

				//System.Web.Mail.SmtpMail.Send(this);
				var smtp = new SmtpClient(_gmailServer, _gmailPort);
				smtp.Credentials = new NetworkCredential(_gmailUserName, _gmailPassword);
				smtp.EnableSsl = true;
				smtp.Send(this);
			}
			catch (Exception ex)
			{
				//TODO: Add error handling
				throw ex;
			}
		}

		/// <summary>
		/// The username of the gmail account that the message will be sent through
		/// </summary>
		public string GmailUserName
		{
			get { return _gmailUserName; }
			set { _gmailUserName = value; }
		}

		/// <summary>
		/// The password of the gmail account that the message will be sent through
		/// </summary>
		public string GmailPassword
		{
			get { return _gmailPassword; }
			set { _gmailPassword = value; }
		}

		#endregion

		#region Static Members

		/// <summary>
		/// Send a <c>MailMessage</c> through the specified gmail account
		/// </summary>
		/// <param name="gmailUserName">The username of the gmail account that the message will be sent through</param>
		/// <param name="gmailPassword">The password of the gmail account that the message will be sent through</param>
		/// <param name="message"><c>System.Web.Mail.MailMessage</c> object to send</param>
		public static void SendMailMessageFromGmail(string gmailUserName, string gmailPassword, MailMessage message)
		{
			try
			{
				//message.Fields[SMTP_SERVER] = GmailMessage.GmailServer; 
				//message.Fields[SMTP_SERVER_PORT] = GmailMessage.GmailServerPort; 
				//message.Fields[SEND_USING] = 2;
				//message.Fields[SMTP_USE_SSL] = true;
				//message.Fields[SMTP_AUTHENTICATE] = 1;
				//message.Fields[SEND_USERNAME] = gmailUserName;
				//message.Fields[SEND_PASSWORD] = gmailPassword;
				//SmtpMail.Send(message);

				var smtp = new SmtpClient(_gmailServer, _gmailPort);
				smtp.Credentials = new NetworkCredential(gmailUserName, gmailPassword);
				smtp.EnableSsl = true;
				smtp.Send(message);
			}
			catch (Exception ex)
			{
				//TODO: Add error handling
				throw ex;
			}
		}

		/// <summary>
		/// Sends an email through the specified gmail account
		/// </summary>
		/// <param name="gmailUserName">The username of the gmail account that the message will be sent through</param>
		/// <param name="gmailPassword">The password of the gmail account that the message will be sent through</param>
		/// <param name="toAddress">Recipients email address</param>
		/// <param name="subject">Message subject</param>
		/// <param name="messageBody">Message body</param>
		public static void SendFromGmail(string gmailUserName, string gmailPassword, string toAddress, string subject,
		                                 string messageBody, string fromDisplayName = null, bool inThread = false)
		{
			try
			{
				if ((fromDisplayName == null) || (fromDisplayName.Length < 1))
					fromDisplayName = gmailUserName;
				var i = fromDisplayName.IndexOf("@");
				if (i > 0) fromDisplayName = fromDisplayName.Substring(0, i);

				var gMessage = new GmailMessage(gmailUserName, gmailPassword);
				gMessage.To.Add(toAddress);
				gMessage.Subject = subject;
				gMessage.Body = messageBody;
				gMessage.Send(fromDisplayName);
			}
			catch (Exception ex)
			{
				//Ignore errors if running in a thread
				if (!inThread)
					throw ex;
			}
		}

		/// <summary>
		/// The name of the gmail server, the default is "smtp.gmail.com"
		/// </summary>
		public static string GmailServer
		{
			get { return _gmailServer; }
			set { _gmailServer = value; }
		}

		/// <summary>
		/// The port to use when sending the email, the default is 465
		/// </summary>
		public static int GmailServerPort
		{
			get { return _gmailPort; }
			set { _gmailPort = value; }
		}

		#endregion
	}

	//GmailMessage
}

//RC.Gmail