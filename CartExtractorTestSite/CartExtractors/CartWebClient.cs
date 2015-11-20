using System;
using System.Collections.Specialized;
using System.Net;
using System.IO;
using System.Xml.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using _4_Tell.CommonTools;
using _4_Tell.Logs;


namespace _4_Tell.CartExtractors
{
	public class CartWebClientConfig
	{
		private const int DefaultSessionTimeout = 600;	//timeout in seconds 600 = 10 min
		private const int DefaultResponseTimeout = 600;
		private const int DefaultRetryDelay = 2000; //milliseconds
		private const int DefaultMaxTries = 1;	//Maximum number of ResponseTimeouts allows
		private const int DefaultConnectionLimit = 48; //recommended to be 12 * number of logical CPUs
		//private const int DefaultBufferSize = 10000000; //10 MB
		private const SecurityProtocolType DefaultSecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls;
		private System.Version DefaultProtocolVersion = System.Net.HttpVersion.Version11; //for some reason this one is not allowed to be declared as a const...


		public int SessionTimeout;
		public int ResponseTimeout;
		public int RetryDelay;
		public int MaxTries;
		public int ConnectionLimit;
		//public int BufferSize;
		public SecurityProtocolType SecurityProtocol;
		public System.Version ProtocolVersion;
		public bool KeepAlive;
		public bool Expect100Continue;
		public bool AllowUnsafeHeaderParsing;

		public CartWebClientConfig(XElement configXml = null)
		{
			//Set defaults
			SessionTimeout = DefaultSessionTimeout;
			ResponseTimeout = DefaultResponseTimeout;
			RetryDelay = DefaultRetryDelay;
			MaxTries = DefaultMaxTries;
			//BufferSize = DefaultBufferSize;
			SecurityProtocol = DefaultSecurityProtocol;
			ProtocolVersion = DefaultProtocolVersion;
			KeepAlive = false;
			Expect100Continue = false;
			AllowUnsafeHeaderParsing = false;
			ConnectionLimit = DefaultConnectionLimit;

			//configXml will override defaults
			if (configXml != null) Xml = configXml;
		}

		public XElement Xml
		{
			set
			{
				if (value == null) return;

				var tempVal = Input.GetValue(value, "sessionTimeout");
				if (string.IsNullOrEmpty(tempVal)) tempVal = Input.GetValue(value, "apiSessionTimeout"); //depricated
				if (!string.IsNullOrEmpty(tempVal)) SessionTimeout = Input.SafeIntConvert(tempVal, DefaultSessionTimeout); 
				tempVal = Input.GetValue(value, "responseTimeout");
				if (string.IsNullOrEmpty(tempVal)) tempVal = Input.GetValue(value, "apiResponseTimeout"); //depricated
				if (!string.IsNullOrEmpty(tempVal)) ResponseTimeout = Input.SafeIntConvert(tempVal, DefaultResponseTimeout);
				tempVal = Input.GetValue(value, "retryDelay");
				if (!string.IsNullOrEmpty(tempVal)) RetryDelay = Input.SafeIntConvert(tempVal, DefaultRetryDelay);
				tempVal = Input.GetValue(value, "maxTries");
				if (string.IsNullOrEmpty(tempVal)) tempVal = Input.GetValue(value, "apiMaxTries"); //depricated
				if (!string.IsNullOrEmpty(tempVal)) MaxTries = Input.SafeIntConvert(tempVal, DefaultMaxTries);
				tempVal = Input.GetValue(value, "connectionLimit");
				if (!string.IsNullOrEmpty(tempVal)) ConnectionLimit = Input.SafeIntConvert(tempVal, DefaultConnectionLimit);
				//tempVal = Input.GetValue(value, "bufferSize");
				//if (!string.IsNullOrEmpty(tempVal)) BufferSize = Input.SafeIntConvert(tempVal, DefaultBufferSize);
				tempVal = Input.GetValue(value, "securityProtocol");
				if (!string.IsNullOrEmpty(tempVal))
				{
					//there are only three choices: Ssl3, Tls, or Ssl3|Tls
					if (tempVal.Equals("Ssl3,Tls") || tempVal.Equals("Ssl3|Tls")) SecurityProtocol = SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls;
					else if (tempVal.Contains("Tls")) SecurityProtocol = SecurityProtocolType.Tls;
					else if (tempVal.Contains("Ssl3")) SecurityProtocol = SecurityProtocolType.Ssl3;
					else if (Input.GetValue(value, "forceProtocolSsl3").Equals("true", StringComparison.OrdinalIgnoreCase)) //depricated
						SecurityProtocol = SecurityProtocolType.Ssl3;
				}
				tempVal = Input.GetValue(value, "protocolVersion");
				if (!string.IsNullOrEmpty(tempVal))
				{
					//there are only two choices: Version10 (1.0) and Version11 (1.1)
					//note: HttpVersion.Version10 seems to just be a renaming of System.Version = "1.0"
					tempVal = tempVal.ToLower();
					if (tempVal.Equals("1.0") || tempVal.Equals("version10")) ProtocolVersion = HttpVersion.Version10;
					else ProtocolVersion = HttpVersion.Version11;
				}
				tempVal = Input.GetValue(value, "keepAlive");
				if (!string.IsNullOrEmpty(tempVal)) KeepAlive = tempVal.Equals("true", StringComparison.OrdinalIgnoreCase);
				tempVal = Input.GetValue(value, "expect100Continue");
				if (!string.IsNullOrEmpty(tempVal)) Expect100Continue = tempVal.Equals("true", StringComparison.OrdinalIgnoreCase);
				tempVal = Input.GetValue(value, "allowUnsafeHeaderParsing");
				if (!string.IsNullOrEmpty(tempVal)) AllowUnsafeHeaderParsing = tempVal.Equals("true", StringComparison.OrdinalIgnoreCase);
			}
			get
			{
				//always fill in the values, even if they are defaults --easier to edit that way
				var config = new XElement("cartWebClientConfig");
				config.Add(new XElement("sessionTimeout", SessionTimeout));
				config.Add(new XElement("responseTimeout", ResponseTimeout));
				config.Add(new XElement("retryDelay", RetryDelay));
				config.Add(new XElement("maxTries", MaxTries));
				config.Add(new XElement("connectionLimit", ConnectionLimit));
				//config.Add(new XElement("bufferSize", BufferSize));
				string protocol =
					SecurityProtocol.Equals(SecurityProtocolType.Ssl3) ? "Ssl3" :
					SecurityProtocol.Equals(SecurityProtocolType.Tls) ? "Tls" :
					"Ssl3,Tls"; // &#124; = "|" --using comma instead for easier readability
				config.Add(new XElement("securityProtocol", protocol));
				config.Add(new XElement("protocolVersion", ProtocolVersion.ToString()));
				config.Add(new XElement("keepAlive", KeepAlive));
				config.Add(new XElement("expect100Continue", Expect100Continue));
				config.Add(new XElement("allowUnsafeHeaderParsing", AllowUnsafeHeaderParsing));
				return config;
			}
		}
	}

	public class CartWebClient: WebClient
	{
		private readonly int _requestTimeout;
		private readonly int _sessionTimeout;
		//private readonly int _bufferSize;
		private DateTime _lastuse;
		private CartWebClientConfig _config;
		public CookieContainer CookieContainer { get; private set; }
		 

		public CartWebClient(CartWebClientConfig config = null)
			: base()
		{
			CookieContainer = new CookieContainer();
			//_requestTimeout = 30;
			//_sessionTimeout = 30;
			//_bufferSize = 10000000; //10 MB
			_lastuse = DateTime.MinValue;
			_config = config == null ? new CartWebClientConfig() : config;
		}

		public CartWebClient(CartWebClient source)
			: base()
		{
			CookieContainer = new CookieContainer();
			//_requestTimeout = source._requestTimeout;
			//_sessionTimeout = source._sessionTimeout;
			//_bufferSize = source._bufferSize;
			_lastuse = source._lastuse;
			_config = source._config;
			QueryString = new NameValueCollection(source.QueryString);
			Headers = new WebHeaderCollection(); 
			Headers.Add(source.Headers);
		}

		public void SetConfig(XElement configXml)
		{
			_config.Xml = configXml;
		}

		//public CartWebClient(int requestTimeout, int sessionTimeout, int bufferSize = 10000000)
		//  : base ()
		//{
		//  CookieContainer = new CookieContainer();
		//  _requestTimeout = requestTimeout;
		//  _sessionTimeout = sessionTimeout;
		//  _bufferSize = bufferSize;
		//  _lastuse = DateTime.MinValue;
		//}

		private void InitServicePointManager()
		{
			ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
			ServicePointManager.DefaultConnectionLimit = _config.ConnectionLimit;
			ServicePointManager.SecurityProtocol = _config.SecurityProtocol;
			ServicePointManager.Expect100Continue = _config.Expect100Continue;
			if (_config.AllowUnsafeHeaderParsing) SetAllowUnsafeHeaderParsing();
		}

		public bool SessionExpired()
		{
			var elapsed = (DateTime.Now - _lastuse).TotalSeconds;
			return elapsed > _config.SessionTimeout;
		}

		protected override WebRequest GetWebRequest(Uri address)
		{
			var request = (HttpWebRequest)base.GetWebRequest(address);
			if (request != null)
			{
				request.CookieContainer = CookieContainer;
				request.Timeout = _config.ResponseTimeout * 1000;
				request.ProtocolVersion = _config.ProtocolVersion;
				request.KeepAlive = _config.KeepAlive;
				_lastuse = DateTime.Now;
			}
			return request;
		}

		public void AddOrSetHeader(string name, string value)
		{
			try
			{
				var test = Headers[name];
				if (test == null)
					Headers.Add(name, value);
				else
					Headers.Set(name, value);
			}
			catch (Exception)
			{
				Headers.Add(name, value);
			}
		}

		private static bool SetAllowUnsafeHeaderParsing()
		{
			//Get the assembly that contains the internal class
			Assembly aNetAssembly = Assembly.GetAssembly(typeof(System.Net.Configuration.SettingsSection));
			if (aNetAssembly == null) return false;

			//Use the assembly in order to get the internal type for the internal class
			Type aSettingsType = aNetAssembly.GetType("System.Net.Configuration.SettingsSectionInternal");
			if (aSettingsType == null) return false;

			//Use the internal static property to get an instance of the internal settings class.
			//If the static instance isn't created allready the property will create it for us.
			object anInstance = aSettingsType.InvokeMember("Section",
				BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.NonPublic, null, null, new object[] { });
			if (anInstance == null) return false;
					
			//Locate the private bool field that tells the framework if unsafe header parsing should be allowed or not
			FieldInfo aUseUnsafeHeaderParsing = aSettingsType.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
			if (aUseUnsafeHeaderParsing == null) return false;

			aUseUnsafeHeaderParsing.SetValue(anInstance, true);
			return true;
		}

		/// <summary>
		/// Recursive method to read from the feed client. 
		/// Method will retry on timeout if ApiMaxTries is greater than 1
		/// </summary>
		/// <param name="feedUrl"></param>
		/// <param name="tryCount"></param>
		/// <returns></returns>
		public Stream TryOpenRead(string feedUrl, ref ExtractorProgress progress, int tryCount = 1)
		{
			Stream resultStream = null;
			var details = "";
			try
			{
				InitServicePointManager();
				resultStream = OpenRead(feedUrl);
			}
			catch (TimeoutException tex)
			{
				if (resultStream != null) resultStream.Close();
				resultStream = null;
				details = tex.Message;
				if (tryCount >= _config.MaxTries)
					throw new Exception(details);
			}
			catch (WebException wex)
			{
				if (resultStream != null) resultStream.Close();
				resultStream = null;
				details = wex.Message;
				if (wex.Status != WebExceptionStatus.Timeout || tryCount >= _config.MaxTries)
					throw new Exception(details);
			}
#if DEBUG
			catch (Exception ex)
			{
				if (resultStream != null) resultStream.Close();
				resultStream = null;
				details = string.Format("Exception in TryOpenRead (trycount = {0}): {1}", tryCount, ex.Message);
				throw ex;
			}
			finally
			{
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Error, details, feedUrl);
			}
#endif

			if (resultStream == null && tryCount < _config.MaxTries)
			{
				tryCount++;
				var msg = string.Format("Timeout. Retry {0} of {1}", tryCount, _config.MaxTries);
				progress.UpdateTable(-1, -1, msg);
#if DEBUG
				if (BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, msg, feedUrl);
#endif
				Thread.Sleep(_config.RetryDelay);
				return TryOpenRead(feedUrl, ref progress, tryCount);
			}
			return resultStream;
		}


		//NOTE: This code is problematic because the result string will be copied to a new block of memory every time the chunk is appended
		//      This cuases a huge memory spike since the old block are not cleaned up immediately.
		//public string GetData(string url)
		//{
		//  var stream = OpenRead(url);
		//  if (stream == null) return null;

		//  var reader = new StreamReader(stream);
		//  //var memstream = new MemoryStream(_bufferSize); //constructor takes initial size but it's expandable
		//  var buffer = new byte[_bufferSize];
		//  int bytesReceived;
		//  var result = "";
		//  while ((bytesReceived = stream.Read(buffer, 0, buffer.Length)) != 0)
		//  {
		//    //memstream.Write(buffer, 0, bytesReceived);
		//    result += Encoding.UTF8.GetString(buffer);
		//  }
		//  reader.Close();
		//  stream.Close();

		//  //var result = memstream.ToArray();
		//  //memstream.Close();
		//  if (result == null) return null;
		//  //return Encoding.UTF8.GetString(result).Trim();
		//  return result;
		//}

	}

}