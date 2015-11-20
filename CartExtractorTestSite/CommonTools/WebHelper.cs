using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Web;
using _4_Tell.IO;
using _4_Tell.Logs;

namespace _4_Tell.CommonTools
{
	public class WebHelper
	{
		/// <summary>
		/// Compares the current context to the passed parameters to see if the security requirements are met
		/// </summary>
		/// <param name="credentials"></param>
		/// <param name="approvedIps"></param>
		public bool VerifyCredentials(AuthCredentials credentials, List<string> approvedIps = null)
		{
			//get the current request object
			var context = OperationContext.Current;
			if (context == null) return false;
			var messageProperties = context.IncomingMessageProperties;
			if (messageProperties == null) return false;
			if (credentials.RequireSsl)
				if (!messageProperties.Via.OriginalString.StartsWith("https"))
					return false;
			var request = messageProperties["httpRequest"] as HttpRequestMessageProperty;
			if (request == null) return false;

			//Check for IP restrictions
			if (approvedIps != null && approvedIps.Count > 0)
			{
				string ip = null;
#if GOGRID
				ip = request.Headers["X-Forwarded-For"];
#else
				var endpointProperty = messageProperties[RemoteEndpointMessageProperty.Name]
															 as RemoteEndpointMessageProperty;
				if (endpointProperty != null)
					ip = endpointProperty.Address;
#endif
				if (!string.IsNullOrEmpty(ip))
					if (approvedIps.FindIndex(x => x.Equals(ip)) < 0)
						return false;
			}

			//Confirm Credentials
			switch (credentials.Type)
			{
				case AuthCredentials.AuthType.BasicAuth:
					var authHeader = request.Headers["Authorization"];
					if (string.IsNullOrEmpty(authHeader))
					{
						authHeader = request.Headers["Authentication"];
						if (string.IsNullOrEmpty(authHeader)) break;
					}
					if (!authHeader.StartsWith("Basic ")) break;
					//var encodedAsBytes = Convert.FromBase64String(authHeader.Substring(6));
					//var unencoded = System.Text.Encoding.ASCII.GetString(encodedAsBytes);
					var unencoded = Input.Base64Decode(authHeader.Substring(6));
					var index = unencoded.IndexOf(":", StringComparison.Ordinal);
					if (index < 1) break;
					var usr = unencoded.Substring(0, index);
					if (!usr.Equals(credentials.UserName)) break;
					var pwd = unencoded.Substring(index + 1);
					if (!pwd.Equals(credentials.Password)) break;
					return true;
				case AuthCredentials.AuthType.None:
					return true;
				default:
					if (BoostLog.Instance != null)
						BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Unknown AuthType requested: " + credentials.Type.ToString());
					return false;
			}
			return false;
		}

		public static string CreateAuthHeader(AuthCredentials credentials)
		{
			switch (credentials.Type)
			{
				case AuthCredentials.AuthType.BasicAuth:
					var toEncode = string.Format("{0}:{1}", credentials.UserName, credentials.Password);
					//var toEncodeAsBytes = System.Text.Encoding.ASCII.GetBytes(toEncode);
					//var encoded = System.Convert.ToBase64String(toEncodeAsBytes);
					var encoded = Input.Base64Encode(toEncode);
					return "Basic " + encoded;
				case AuthCredentials.AuthType.None:
					return "";
				default:
					throw new ArgumentOutOfRangeException("Unknown AuthType: " + credentials.Type.ToString());
			}
		}

#if !CART_EXTRACTOR_TEST_SITE
        public WebContextProxy GetContextOfRequest()
		{
			var wc = new WebContextProxy();
			var context = OperationContext.Current;
			if (context == null) return wc;
			//Message msg = OperationContext.Current.RequestContext.RequestMess  age.CreateBufferedCopy(Int32.MaxValue).CreateMessage();
			var messageProperties = context.IncomingMessageProperties;
			if (messageProperties == null) return wc;

			try
			{
				var via = messageProperties.Via;
				if (via != null)
				{
					wc.Parameters = via.Query;
					wc.Operation = via.LocalPath;
				}
			}
			catch { }

			try
			{
				object request;
				if (messageProperties.TryGetValue("httpRequest", out request))
				{
					var msgProperty = (HttpRequestMessageProperty)request;
					wc.Parameters = msgProperty.QueryString;
					wc.Verb = msgProperty.Method;
					wc.ContentType = msgProperty.Headers["Content-Type"]; //need to store this separately from other headers so it can be set
					wc.Headers = new Dictionary<string,string>();
					for (var i = 0; i < msgProperty.Headers.Count; i++)
						wc.Headers.Add(msgProperty.Headers.GetKey(i), msgProperty.Headers.Get(i));

#if GOGRID
				var ip = msgProperty.Headers["X-Forwarded-For"];
				if (!string.IsNullOrEmpty(ip))
					wc.IP = "request." + ip;
			}
			if (string.IsNullOrEmpty(wc.IP))
			//not sure if this is ever needed
			{
				var webContext = WebOperationContext.Current;
				if (webContext != null)
				{
					var webRequest = webContext.IncomingRequest;
					if (webRequest != null)
					{
						wc.Verb = webRequest.Method;
						var ip = webRequest.Headers["X-Forwarded-For"];
						if (!string.IsNullOrEmpty(ip))
							wc.IP = "webRequest." + ip;
					}
				}
			}

			//if not forwarded through a loadbalancer, then treat as an internal call (from a replicated server)
			if (string.IsNullOrEmpty(wc.IP))
#else
				}
#endif

				{
					object endpointProperty;
					if (messageProperties.TryGetValue(RemoteEndpointMessageProperty.Name, out endpointProperty))
					{
						wc.IP = ((RemoteEndpointMessageProperty)endpointProperty).Address;
						wc.IsInternal = Replicator.Instance != null && Replicator.Instance.IsInternal(wc.IP);
						if (wc.IsInternal) wc.IP = "internal." + wc.IP;
					}
					if (string.IsNullOrEmpty(wc.ContentType) && WebOperationContext.Current != null)
					//NOTE: WebOperationContext not OperationContext
					{
						wc.ContentType = WebOperationContext.Current.OutgoingResponse.Format == WebMessageFormat.Xml
															 ? "text/xml"
															 : "application/json";
					}
				}
			}
			catch { }
			return wc;
		}
#endif
	}
}