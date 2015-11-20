using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Timers;
using System.Xml.Linq;
using _4_Tell.CommonTools;
using _4_Tell.IO;
using _4_Tell.Logs;

namespace _4_Tell.CartExtractors
{
	public sealed class JsonFeedExtractor : CartExtractor
	{
		private static Timer _cleanupTimer = null;

		#region FileReaders
		//private IEnumerable<SalesRecord> m_orderHistory;

		//private IEnumerable<SalesRecord> OrderHistory
		//{
		//  get
		//  {
		//    if (m_orderHistory == null)
		//    {
		//      m_orderHistory = LoadTabDelimitedFile(DataPath + OrderDetailsFileName)
		//        .Select(order => new SalesRecord
		//                          {
		//                            OrderId = order["ORDER_ID"],
		//                            ProductId = order["PROD_CODE"],
		//                            CustomerId = GetHash(order["BILL_EMAIL"]),
		//                            Quantity = order["PROD_QUANT"],
		//                            Date = order["ORDER_DATE"]
		//                          }).ToList();
		//    }
		//    return m_orderHistory;
		//  }
		//}

		//private readonly IEnumerable<Dictionary<string, string>> m_categoryDictionary;
		//public IEnumerable<Dictionary<string, string>> Catalog
		//{
		//  get
		//  {
		//    //If no live catalog avaialable, look for a catalog.xml file instead
		//    //WARNING: Delete catalog.xml once live catalog is available so connection issue doesn't cause a revert to old data
		//    if (m_catalogDictionary == null)
		//      m_catalogDictionary = LoadTabDelimitedFile(m_productsFilePath);
		//    if (m_categoryDictionary == null)
		//      m_categoryDictionary = LoadTabDelimitedFile(m_categoriesFilePath);

		//    return m_catalogDictionary;
		//  }
		//  set { m_catalogDictionary = value; }
		//}

		//private XElement m_catalogXml = null;

		//public XElement Catalog
		//{
		//  get
		//  {
		//    var progress = string.Empty;
		//    m_catalogXml = json.RequestFromService(MivaMerchantJsonBridge.DataGroup.Catalog, ref progress);

		//    //If no live catalog avaialable, look for a catalog.xml file instead
		//    //WARNING: Delete catalog.xml once live catalog is available so connection issue doesn't cause a revert to old data

		//    //if (m_catalogXml == null)
		//    //    m_catalogXml = XElement.Load(DataPath + CatalogExportFileName);
		//    ////if (m_categoryXml == null)
		//    ////  m_categoryXml = LoadXmlFile(DataPath + CategoriesFileName);
		//    //if (m_categoryDictionary == null)
		//    //    m_categoryDictionary = LoadTabDelimitedFile(m_categoriesFilePath);

		//    return m_catalogXml;
		//  }
		//  set { m_catalogXml = value; }
		//}
		#endregion

		public JsonFeedExtractor(SiteRules rules)
			: base(rules)
		{
			//determine feed type
			SetFeedTypes();

			//if api key is not provided, assume license key instead
			if (string.IsNullOrEmpty(Rules.ApiKey))
			{
				//default to service key based on license
				Rules.ApiKey = ClientData.Instance.GetServiceKey(Alias);
			}
		}

		#region Overrides of CartExtractor

		public override bool ValidateCredentials(out string status)
		{
			throw new NotImplementedException();
		}

		protected override void ReleaseCartData()
		{
			//Timer will get restarted after each extraction is complete. 
			//If no new extraction events occur before the timer fires then the feedclient will be deleted
			if (_feedClient != null)
			{
				_cleanupTimer = new Timer {Interval = 600 * 1000}; //10 min converted to ms
				_cleanupTimer.Elapsed += new ElapsedEventHandler(OnCleanupTimer);
				_cleanupTimer.Enabled = true;
			}
		}

		private void OnCleanupTimer(object sender, ElapsedEventArgs elapsedEventArgs)
		{
			_feedClient = null;
		}

		public override void LogSalesOrder(string orderID)
		{
			throw new NotImplementedException();
		}

		#endregion


		#region Utilities


		//private static IEnumerable<Dictionary<string, string>> LoadTabDelimitedFile(string path)
		//{
		//  var contents = new List<Dictionary<string, string>>();
		//  var keys = new List<string>();

		//  using (var sr = new StreamReader(path))
		//  {
		//    string line;
		//    if (!string.IsNullOrEmpty(line = sr.ReadLine()))
		//      keys.AddRange(line.Split(new[] {'\t'}, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()));

		//    var keyCount = keys.Count();
		//    while (!string.IsNullOrEmpty(line = sr.ReadLine()))
		//    {
		//      var content = new Dictionary<string, string>();
		//      var values = line.Replace("<br>", "").Split(new[] {'\t'});

		//      var columnCount = values.Length;
		//      if (columnCount > keyCount)
		//      {
		//        //Extra tabs in the description column cause problems 
		//        var extraColumns = values.Length - keyCount;
		//        for (var i = 8; i < keyCount; i++) //descrptions are in column 7
		//          values[i] = values[i + extraColumns];
		//        columnCount = keyCount;
		//      }

		//      for (var i = 0; i < columnCount; i++)
		//      {
		//        content.Add(keys[i], values[i].Trim());
		//      }

		//      contents.Add(content);
		//    }
		//  }

		//  return contents;
		//}

		//private static IEnumerable<XElement> LoadXmlFile(string path)
		//{
		//  var catalogDoc = new XDocument();
		//  IEnumerable<XElement> resultXml = null;

		//  catalogDoc = XDocument.Load(path);
		//  resultXml = catalogDoc.Ancestors();

		//  return resultXml;
		//}

		//private static string CleanJsonValue(string col)
		//{
		//  return col.Replace("\\", String.Empty).Replace("/", String.Empty).Replace('"', ' ').Trim();
		//}

		//final version

		//version using HttpWebRequest
//    private List<string> RequestFromServiceHwrSsl3(DataGroup group, string range = "", string extraFields = "")
//    {
//      var query = new NameValueCollection(_queryParams);
//      query.Add("DataGroup", group.ToString());
//      if (!string.IsNullOrEmpty(range))
//      {
//        if (group.Equals(DataGroup.Catalog))
//          query.Add("IdRange", range);
//        else if (group.Equals(DataGroup.Sales))
//          query.Add("DateRange", range);
//      }
//      if (!string.IsNullOrEmpty(extraFields))
//      {
//        query.Add("ExtraFields", extraFields);
//      }

//      //string uri = Rules.ApiUrl +group.ToString() + range;
//      ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
//      if (Alias.Equals("SexToyD"))
//        ServicePointManager.SecurityProtocol = SecurityProtocolType.Ssl3;
//      var result = string.Empty;

//      HttpWebRequest request = null;
//      HttpWebResponse response = null;

//      try
//      {
//        var serviceUriFormat = Rules.ApiUrl;
//        if (query.Count > 0)
//        {
//          var first = true;
//          for (var i = 0; i < query.Count; i++)
//          {
//            if (first)
//            {
//              serviceUriFormat += "?";
//              first = false;
//            }
//            else serviceUriFormat += "&";
//            serviceUriFormat += string.Format("{0}={1}", query.GetKey(i), query.Get(i));
//          }
//        }
//        var serviceURI = new Uri(serviceUriFormat);
//        request = WebRequest.Create(serviceURI) as HttpWebRequest;
//        if (request == null) 
//          throw new Exception(string.Format("Unable to create service request for {0} {1}", Alias, group));

//        request.Method = "GET";
//        //request.ContentType = "text/plain";
//        request.ContentType = "application/json"; 
//        request.ServicePoint.Expect100Continue = false;
//        request.Timeout = 10000;
//        request.KeepAlive = false;
//        request.ContentLength = 0;
//        request.Accept = "*/*";
//        request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; rv:18.0) Gecko/20100101 Firefox/18.0";


//        // Get response  
//        using (response = request.GetResponse() as HttpWebResponse)
//        {
//          StreamReader reader = new StreamReader(response.GetResponseStream());
//          result = reader.ReadToEnd();
//        }

//        //int b1;
//        //var memoryStream = new MemoryStream();
//        //byte[] xmlBytes;
//        //using (response = request.GetResponse() as HttpWebResponse)
//        //{
//        //  while ((b1 = response.GetResponseStream().ReadByte()) != -1)
//        //    memoryStream.WriteByte(((byte)b1));

//        //  xmlBytes = memoryStream.ToArray();
//        //  response.Close();
//        //  memoryStream.Close();
//        //}
//        ////convert to string
		//        //result = ASCIIEncoding.UTF8.GetString(xmlBytes);
//      }
//      #region Faultcatching

//      catch (WebException wex)
//      {
//        var errMsg = "WebFaultException = " + wex.Message;
//        // Get the response stream  
//        var wexResponse = (HttpWebResponse)wex.Response;
//        if (wexResponse != null)
//        {
//          var reader = new StreamReader(wexResponse.GetResponseStream());
//          errMsg += "\nException Response = " + reader.ReadToEnd();
//        }
//        errMsg += "\nStatus = " + wex.Status.ToString();
//        if (wex.InnerException != null)
//          errMsg += "\nInner Exception = " + wex.InnerException.Message;
//        if (response != null)
//          if (response.ContentLength > 0)
//            errMsg += "\n\nResponse = " + response.StatusDescription;
//        throw new Exception(errMsg);
//      }
//      catch (Exception ex)
//      {
//        var errMsg = "Exception = " + ex.Message;
//        if (ex.InnerException != null)
//          errMsg += "\nInner Exception = " + ex.InnerException.Message;
//        errMsg += "\nStackTrace = " + ex.StackTrace.ToString();
//        throw new Exception(errMsg);
//      }
//#if DEBUG
//      finally
//      {
//        if (request != null)
//        {
//          var reqDetails = "\nREST Endpoint---"
//                          + "\nConection: " + request.Connection
//                          + "\nAddress: " + request.Address.AbsoluteUri
//                          + "\nHeaders: ";
//          WebHeaderCollection headers = request.Headers;
//          foreach (string s in headers)
//            reqDetails += "\n  " + s;
//          Debug.Write(reqDetails);
//        }
//      }
//#endif
//      #endregion

//      var rows = new List<string>();
//      rows.AddRange(result.Trim().Trim('\r','\n').Split(']').ToList().Select(x => x.TrimStart(',')).ToList());
//      return rows;
//    }

		//version2 using WebClient
		//private List<string> RequestFromServiceWC(DataGroup group, string range = "", string extraFields = "")
		//{
		//  var query = new NameValueCollection(_queryParams);
		//  query.Add("DataGroup", group.ToString());
		//  if (!string.IsNullOrEmpty(range))
		//  {
		//    if (group.Equals(DataGroup.Catalog))
		//      query.Add("IdRange", range);
		//    else if (group.Equals(DataGroup.Sales))
		//      query.Add("DateRange", range);
		//  }
		//  if (!string.IsNullOrEmpty(extraFields))
		//  {
		//    query.Add("ExtraFields", extraFields);				
		//  }

		//  //string uri = Rules.ApiUrl +group.ToString() + range;
		//  ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
		//  var sContent = string.Empty;
		//  using (var downloader = new WebClient())
		//  {
		//    //var serviceUri = new Uri(Rules.ApiUrl);
		//    if (query.Count > 0)
		//      downloader.QueryString.Add(query);
		//    downloader.Encoding = Encoding.UTF8;
		//    downloader.BaseAddress = "";
		//    downloader.Proxy = null;
		//    downloader.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
		//    downloader.Headers.Add("accept", "*/*");
		//    try
		//    {
		//      sContent = downloader.DownloadString(Rules.ApiUrl);
		//    }
		//    catch (Exception)
		//    {
		//    }
		//    try
		//    {
		//      var data = downloader.OpenRead(Rules.ApiUrl);
		//      if (data != null)
		//      {
		//        var reader = new StreamReader(data);
		//        sContent = reader.ReadToEnd();
		//        data.Close();
		//        reader.Close();
		//      }
		//    }
		//    catch (Exception)
		//    {
		//    }
		//    if (downloader.ResponseHeaders != null)
		//    {
		//      var rh = string.Empty;
		//      for (var i = 0; i < downloader.ResponseHeaders.Count; i++)
		//        rh += string.Format("{1} = {2}{0}", Environment.NewLine, downloader.ResponseHeaders.GetKey(i), downloader.ResponseHeaders.Get(i));
		//      Debug.Write(rh);
		//    }
		//  }
		//  var rows = new List<string>();
		//  rows.AddRange(sContent.Trim().Split(']').ToList().Select(x => x.TrimStart(',')).ToList());
		//  return rows;
		//}

		//version1 using WebClient
		//private List<string> RequestFromServiceWC(DataGroup group, string extraQuery = "")
		//{
		//  string uri = Rules.ApiUrl + group.ToString() + extraQuery;
		//  ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
		//  var serviceUri = new Uri(uri);
		//  var downloader = new WebClient();
		//  var sContent = string.Empty;
		//  using (var content = downloader.OpenRead(serviceUri))
		//  {
		//    if (content == null) return null;

		//    var rdr = new StreamReader(content);
		//    //var sContent = rdr.ReadToEnd();
		//    //replaced with bufered read to handle large catalogs
		//    var index = 0;
		//    var buffer = new char[5001];
		//    do
		//    {
		//      var readCount = 0;
		//      try
		//      {
		//        readCount = rdr.ReadBlock(buffer, 0, 5000);
		//      }
		//      catch (Exception)
		//      {
		//        break;
		//      }
		//      if (readCount < 1) break;

		//      index += readCount;
		//      sContent += new string(buffer, 0, readCount);
		//    } while (true);
		//  }
		//  var rows = new List<string>();
		//  rows.AddRange(sContent.Trim().Split(']').ToList().Select(x => x.TrimStart(',')).ToList());
		//  return rows;
		//}

		//version2 using HttpWebRequest (plus using a buffered read)
		//private List<string> RequestFromServiceHWRBuffer(DataGroup group, string extraQuery = "")
		//{
		//  string uri = Rules.ApiUrl + group.ToString() + extraQuery;
		//  ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
		//  var serviceUri = new Uri(uri);
		//  HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
		//  request.Method = "GET";
		//  request.Credentials = CredentialCache.DefaultCredentials;
		//  request.ContentType = "text/xml"; //depending on the data u are sending this will change.
		//  request.Timeout = 20000; //20 sec
		//  var sContent = string.Empty;

		//  using (var response = (HttpWebResponse)request.GetResponse())
		//  {
		//    using (var s = response.GetResponseStream())
		//    {
		//      if (s != null)
		//      {
		//        var rdr = new StreamReader(s, Encoding.UTF8);

		//        //var sContent = rdr.ReadToEnd();
		//        //replaced with bufered read to handle large catalogs
		//        var total = 0;
		//        var buffer = new char[5001];
		//        do
		//        {
		//          var readCount = 0;
		//          try
		//          {
		//            readCount = rdr.ReadBlock(buffer, 0, 5000);
		//          }
		//          catch (Exception)
		//          {
		//            break;
		//          }
		//          if (readCount < 1) break;

		//          total += readCount;
		//          sContent += new string(buffer, 0, readCount);
		//        } while (true);
		//        rdr.Close();
		//        rdr.Dispose();
		//      }
		//    }
		//  }

		//  var rows = new List<string>();
		//  rows.AddRange(sContent.Trim().Split(']').ToList().Select(x => x.TrimStart(',')).ToList());
		//  return rows;
		//}

		//version1 using HttpWebRequest
		//private List<string> RequestFromServiceHWR(DataGroup group, string extraQuery = "")
		//{
		//  string uri = Rules.ApiUrl + group.ToString() + extraQuery;
		//  ServicePointManager.ServerCertificateValidationCallback = ((sender, certificate, chain, sslPolicyErrors) => true);
		//  var sContent = string.Empty;

		//  HttpWebRequest request = WebRequest.Create(uri) as HttpWebRequest;
		//  request.Method = "GET";
		//  request.CookieContainer = new CookieContainer();
		//  request.Timeout = 600000;
		//  request.Accept = "*/*";
		//  request.UserAgent = "Mozilla/5.0 (Windows NT 6.1; rv:18.0) Gecko/20100101 Firefox/18.0";

		//  WebResponse response;
		//  try
		//  {
		//    response = request.GetResponse();
		//    string StatusDescription = ((HttpWebResponse)response).StatusDescription;

		//    if (StatusDescription.ToLower() == "ok")
		//    {
		//      Stream dataStream = response.GetResponseStream();
		//      //XmlDocument XmlResponse = new XmlDocument();
		//      //XmlResponse.Load(dataStream);            
		//      //XmlResponse.Save(Server.MapPath("~/XMLResponse.xml"));
		//      StreamReader reader = new StreamReader(dataStream);
		//      sContent = reader.ReadToEnd();
		//      sContent = sContent.Replace("&nbsp;", "");
		//      sContent = sContent.Replace("&", "&amp;");
		//      reader.Close();
		//      dataStream.Dispose();
		//      response.Close();
		//    }
		//    else
		//    {
		//      //ToDo : add code for notify error 
		//    }
		//  }
		//  catch (Exception ex)
		//  {
		//    WebClient webclient = new WebClient();
		//    using (StreamReader reader = new StreamReader(webclient.OpenRead(uri)))
		//    {
		//      sContent = reader.ReadToEnd();
		//      sContent = sContent.Replace("&nbsp;", "");
		//      sContent = sContent.Replace("&", "&amp;");
		//      reader.Close();
		//      webclient.Dispose();
		//    }
		//  }
		//  var rows = new List<string>();
		//  rows.AddRange(sContent.Trim().Split(']').ToList().Select(x => x.TrimStart(',')).ToList());
		//  return rows;
		//}

		#endregion
	}

}

//END namespace