using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml.Linq;
using System.ServiceModel.Web;
using System.IO;
using _4_Tell.IO;
using _4_Tell.Logs;

namespace _4_Tell.CommonTools
{
	public static class Input
	{
		/// <summary>
		/// Strip starting and ending quotes off of a parameter
		/// Also convert nulls to empty strings
		/// </summary>
		/// <param name="input"></param>
		public static void StripQuotes(ref string input)
		{
			if (input == null)
			{
				input = ""; //input was missing this parameter tag so set to empty string
				return;
			}

			const string doubleQuote = "\"";
			const string singleQuote = "'";
			if (input.StartsWith(doubleQuote))
			{
				if (input.EndsWith(doubleQuote)) 
					input = input.Substring(1, input.Length - 2);
			}
			else if (input.StartsWith(singleQuote))
			{
				if (input.EndsWith(singleQuote))
					input = input.Substring(1, input.Length - 2);
			}
		}

		/// <summary>
		/// Check to see if input text can be converted to a bool
		/// Only look for input that would override the default
		/// Default is false if not specified
		/// Accept true/false or 1/0 inputs
		/// </summary>
		/// <param name="input"></param>
		/// <param name="defaultOut"></param>
		/// <returns>Never throws an exception</returns>
		public static bool CheckBool(string input, bool defaultOut = false)
		{
			if (string.IsNullOrEmpty(input)) return defaultOut;

			var output = defaultOut; //set default
			if (defaultOut)
			{
				if (input.ToLower() == "false" || input == "0") output = false;
			}
			else if (input.ToLower() == "true" || input == "1") output = true;
			return output;
		}

		/// <summary>
		/// Check to see if input text can be converted to an int
		/// if not, return the default value
		/// Default is 0 if not specified
		/// </summary>
		/// <param name="input"></param>
		/// <param name="defaultOut"></param>
		/// <returns>Never throws an exception</returns>
		public static int CheckInt(string input, int defaultOut = 0)
		{
			if (string.IsNullOrEmpty(input)) return defaultOut;

			int output;
			if (!int.TryParse(input, out output))
			{
				var warning = string.Format("Illegal input value: {0}{1}", input, Environment.NewLine);
				if (!string.IsNullOrEmpty(warning) && BoostLog.Instance != null)
					BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, warning, "");
				output = defaultOut;
			}

			return output;
		}

		/// <summary>
		/// Check to see if input text can be converted to a list of integers
		/// if not, return the default value
		/// Default is 0 if not specified
		/// </summary>
		/// <param name="input"></param>
		/// <param name="defaultOut"></param>
		/// <returns>Never throws an exception</returns>
		public static List<int> CheckIntList(string input, int defaultOut = 0)
		{
			var output = new List<int>();
			if (string.IsNullOrEmpty(input)) return output;

			var warning = "";
			var split = input.Split(new[] {','});
			var count = 0;
			foreach (var s in split)
			{
				int val;
				if (int.TryParse(s, out val))
					output.Add(val);
				else
				{
					warning += string.Format("Illegal input value: {0}{1}", s, Environment.NewLine);
					output.Add(defaultOut);
				}
			}

			if (!string.IsNullOrEmpty(warning) && BoostLog.Instance != null)
				BoostLog.Instance.WriteEntry(EventLogEntryType.Warning, warning, "");
			return output;
		}

		/// <summary>
		/// Check to see if input text can be converted to a list of strings
		/// if not, return the default value
		/// Default is 0 if not specified
		/// </summary>
		/// <param name="input"></param>
		/// <returns>Never throws an exception</returns>
		public static List<string> CheckStringList(string input)
		{
			var output = new List<string>();
			if (string.IsNullOrEmpty(input)) return output;

			var split = input.Split(new[] { ',' });
			output.AddRange(split);
			return output;
		}

		/// <summary>
		/// Safe value parsing from an XElement container to return an int
		/// Default value is -1
		/// Boolean response indicates success or failure
		/// </summary>
		/// <param name="value"></param>
		/// <param name="container"></param>
		/// <param name="elementName"></param>
		/// <returns>Never throws an exception</returns>
		public static bool GetValue(out int value, XElement container, string elementName)
		{
			value = -1; //value only valid if return is true
			if (string.IsNullOrEmpty(elementName)) return false;
			string s = GetValue(container, elementName);
			return int.TryParse(s, out value);
		}

		/// <summary>
		/// Safe value parsing from an XElement container to return a float
		/// Default value is -1
		/// Boolean response indicates success or failure
		/// </summary>
		/// <param name="value"></param>
		/// <param name="container"></param>
		/// <param name="elementName"></param>
		/// <returns>Never throws an exception</returns>
		public static bool GetValue(out float value, XElement container, string elementName)
		{
			value = -1; //value only valid if return is true
			if (string.IsNullOrEmpty(elementName)) return false;
			string s = GetValue(container, elementName);
			return float.TryParse(s, out value);
		}

		public static bool GetValue(out List<string> value, XElement container, string elementName)
		{
			value = null; //value only valid if return is true
			if (string.IsNullOrEmpty(elementName)) return false;
			try
			{
				string s = GetValue(container, elementName);
				value = s.Split(new[] {','}).ToList();
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Safe value parsing from an XElement container to return a string
		/// Default response is an empty string
		/// </summary>
		/// <param name="container"></param>
		/// <param name="elementName"></param>
		/// <returns>Never throws an exception</returns>
		public static string GetValue(XElement container, string elementName)
		{
			if (string.IsNullOrEmpty(elementName)) return "";
			//return container.Descendants().Where(x => x.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).DefaultIfEmpty("").Single();
			try
			{
				var e = container.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
				//var e = container.Element(elementName); //doesn't work if there is a case mismatch --problem because API returns titlecase tags and manual export is lowercase
				if (e != null)
					return e.Value;
			}
			catch { }
			return "";
		}

		/// <summary>
		/// Safe value parsing from data list to return a string
		/// Default response is an empty string
		/// </summary>
		/// <param name="container"></param>
		/// <param name="elementName"></param>
		/// <returns>Never throws an exception</returns>
		public static string GetValue(List<string> header, List<string> data, string elementName)
		{
			if (string.IsNullOrEmpty(elementName)) return "";
			try
			{
				var headerIndex = header.FindIndex(x => x.Equals(elementName, StringComparison.OrdinalIgnoreCase));
				if (headerIndex < 0 || headerIndex >= data.Count) return "";

				return data[headerIndex];
			}
			catch { }
			return "";
		}

		/// <summary>
		/// Read a CSV list from an XElement
		/// NOTE: the elements in this list are required to be surrounded by double quotes and separated by commas
		/// </summary>
		/// <param name="container"></param>
		/// <param name="elementName"></param>
		/// <returns>Array of elements in the CSV or null if not found</returns>
		public static string[] GetCsvStringList(XElement container, string elementName)
		{
			var elementVal = GetValue(container, elementName);
			if (string.IsNullOrEmpty(elementVal)) return null;

			var trimChars = new[] {'\"'};
			var split = elementVal.Split(new[] { "\",\"", "\", \"" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim(trimChars)).Distinct();
			var newSplit = new List<string>();
			foreach (var s in split)
			{
				//handle special cases to ease xml encoding
				if (s.Equals("CR-LF")) newSplit.Add("\r\n");
				else if (s.Equals("CR")) newSplit.Add("\r");
				else if (s.Equals("LF")) newSplit.Add("\n");
				else newSplit.Add(s);
			}
			return newSplit.ToArray();
		}

		/// <summary>
		/// Read a CSV list from an XElement
		/// NOTE: the elements in this list are required to be surrounded by single quotes and separated by commas
		/// </summary>
		/// <param name="container"></param>
		/// <param name="elementName"></param>
		/// <returns>Array of elements in the CSV or null if not found</returns>
		public static char[] GetCsvCharList(XElement container, string elementName)
		{
			var elementVal = GetValue(container, elementName);
			if (string.IsNullOrEmpty(elementVal)) return null;

			var trimChars = new[] { '\'' };
			var split = elementVal.Split(new[] { "','", "', '" }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim(trimChars)).Distinct();
			var newSplit = new List<char>();
			foreach (var s in split)
			{
				//handle special cases to ease xml encoding
				if (s.Equals("CR-LF"))
				{
					newSplit.Add('\r');
					newSplit.Add('\n');
				}
				else if (s.Equals("CR")) newSplit.Add('\r');
				else if (s.Equals("LF")) newSplit.Add('\n');
				else newSplit.Add(s[0]);
			}
			return newSplit.Distinct().ToArray();
		}

		public static string SetCsvStringList(string[] data)
		{
			if (data == null || data.Length < 1) return "";
			string result = "";
			bool first = true;
			foreach (var d in data)
			{
				if (string.IsNullOrEmpty(d)) continue;
				if (first) first = false;
				else result += ",";
				//handle special cases to ease xml encoding
				if (d.Equals("\r\n")) result += "\"CR-LF\"";
				else if (d.Equals("\r")) result += "\"CR\"";
				else if (d.Equals("\n")) result += "\"LF\"";
				else result += "\"" + d + "\"";
			}
			return result;
		}

		public static string SetCsvCharList(char[] data)
		{
			if (data == null || data.Length < 1) return "";
			string result = "";
			bool first = true;
			foreach (var d in data)
			{
				if (first) first = false;
				else result += ",";
				//handle special cases to ease xml encoding
				if (d.Equals('\r')) result += "'CR'";
				else if (d.Equals('\n')) result += "'LF'";
				else result += "'" + d.ToString(CultureInfo.InvariantCulture) + "'";
			}
			return result;
		}

		/// <summary>
		/// Safe attribute parsing from an XElement container
		/// Default response is an empty string
		/// </summary>
		/// <param name="element"></param>
		/// <param name="attributeName"></param>
		/// <returns>Never throws an exception</returns>
		public static string GetAttribute(XElement element, string attributeName)
		{
			if (string.IsNullOrEmpty(attributeName)) return "";
			//return element.Attributes().Where(x => x.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase)).Select(x => x.Value).DefaultIfEmpty("").Single();
			try
			{
				foreach (XAttribute xa in element.Attributes())
					if (xa.Name.LocalName.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
						return xa.Value;
			}
			catch { }
			return "";
		}

		public static string RemoveTablePrefix(string fieldName)
		{
			if (string.IsNullOrEmpty(fieldName)) return "";
			var index = fieldName.IndexOf('.') + 1;
			return (index > 0) ? fieldName.Substring(index) : fieldName;
		}

		/// <summary>
		/// Parse an exception to get the message and inner meesage is any and format for logging
		/// Provides HTTPStatusCode as well
		/// </summary>
		/// <param name="ex"></param>
		/// <param name="code"></param>
		/// <returns></returns>
		public static string GetExMessage(Exception ex, out HttpStatusCode code)
		{
			var result = RemoveLinefeeds(ex.Message);

			//TODO: identify different errors and pass different status codes
			code = GetCode(ex);
			return result;
		}

		public static HttpStatusCode GetCode(Exception ex)
		{
			//TODO: identify different errors and pass different status codes
			var code = HttpStatusCode.BadRequest;
			return code;
		}

		public static HttpStatusCode GetCode(WebException wex)
		{
			//TODO: identify different errors and pass different status codes
			var code = HttpStatusCode.BadRequest;
			return code;
		}

		/// <summary>
		/// Parse an exception to get the message and inner meesage if any and format for logging
		/// </summary>
		/// <param name="ex"></param>
		/// <returns></returns>
		public static string GetExMessage(Exception ex)
		{
			var result = RemoveLinefeeds(ex.Message);
			if (ex.InnerException != null)
				result += "---" + RemoveLinefeeds(ex.InnerException.Message);

			return result;
		}
		

		public static WebFaultException<string> FormatWex(Exception ex, bool log = false, string alias = null)
		{
			if (log && BoostLog.Instance != null)
			{
				var details = "Type = Exception";
				if (ex.InnerException != null)
					details += "\nInner Exception = " + ex.InnerException.Message;
				details += "\nStackTrace = " + ex.StackTrace.ToString();

				BoostLog.Instance.WriteEntry(EventLogEntryType.Error, ex.Message, details, alias);
			}
			if (WebOperationContext.Current != null) WebOperationContext.Current.OutgoingResponse.Format = WebMessageFormat.Xml;
			return new WebFaultException<string>(RemoveLinefeeds(ex.Message), GetCode(ex));
		}

		public static WebFaultException<string> FormatWex(WebException wex, bool log = false, string alias = null)
		{
			if (log && BoostLog.Instance != null)
			{
				var details = "Type = WebFaultException";
				details += "\nStatus = " + wex.Status.ToString();
				if (wex.InnerException != null)
					details += "\nInner Exception = " + wex.InnerException.Message;
				// Get the response stream  
				var wexResponse = (HttpWebResponse)wex.Response;
				if (wexResponse != null)
				{
					details += "\nRequest = " + wexResponse.ResponseUri;
					wexResponse.Close();
				}

				BoostLog.Instance.WriteEntry(EventLogEntryType.Error, wex.Message, details, alias);
			}
			if (WebOperationContext.Current != null) WebOperationContext.Current.OutgoingResponse.Format = WebMessageFormat.Xml;
			return new WebFaultException<string>(RemoveLinefeeds(wex.Message), GetCode(wex));
		}

		/// <summary>
		/// Convert line feeds to "---" for safe service response on error messages
		/// </summary>
		/// <param name="message"></param>
		/// <returns></returns>
		public static string RemoveLinefeeds(string message)
		{
			return message.Replace("\n", "---");
		}

		public static void RemoveIllegalEscapeCodes(ref string data)
		{
			var index = 0;
			do
			{				
				var start = data.IndexOf("&#x", index, StringComparison.Ordinal);
				if (start < 0) break;

				index = start + 3;
				var end = data.IndexOf(';', index, 7);
				if (end < start) continue;


				//TODO: check actual values in case some are allowed
				//currently all escape-coded values starting with a # are removed

				var illegal = data.Substring(start, end - start + 1);
				//assume that it will probably show up more than once
				data = data.Replace(illegal, ""); 
				index = start; //item was removed so we need to back up

			} while (true);
		}

#if !CART_EXTRACTOR_TEST_SITE
        public static string GetToutParamDetails(string toutParamString, BoostDataContracts.ToutParams[] toutParams)
		{
			var details = "input params = " + (toutParamString == null ? "null" :
				toutParamString.Length > 500 ? toutParamString.Substring(0, 500) : toutParamString);
			details += "\nToutParams = " + (toutParams == null ? "null" : 
				!toutParams.Any() ? "none" :
				toutParams.Aggregate("[", (w, j) => string.Format("{0},[{1}]", w, j.ToJson())) + "]");
			return details;
		}
#endif

		/// <summary>
		/// Take any string and convert to a safe XName by ignoring all illegal characters
		/// </summary>
		/// <param name="name"></param>
		/// <returns>Throws ArgumentException if no valid characters are found</returns>
		public static XName CreateSafeXName(string name)
		{
			if (string.IsNullOrEmpty(name)) throw new ArgumentException("Name is empty", "name");

			//var validChars = Encoding.ASCII.GetBytes("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ");
			const string alpha = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
			const string alphaNumeric = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
			var validName = "";
			int index;

			//special handling ---first char must be alpha (not numeric)
			for (index = 0; index < name.Length; index++)
			{
				var c = name.Substring(index, 1);
				if (!alpha.Contains(c)) continue; //keep looking until we get a valid alpha char to start

				validName += c;
				index++;
				break;
			} 
 
			//rest of characters can be any alphaNumeric
			for (; index < name.Length; index++)
			{
				var c = name.Substring(index, 1);
				if (!alphaNumeric.Contains(c)) continue;
				validName += c;
			} 
			if (validName.Length < 1) throw new ArgumentException("No valid characters found", "name");
#if DEBUG
			var breakTest = validName.Equals("Accessories");
			if (breakTest)
				breakTest = false;
#endif
			return validName;
		}

		/// <summary>
		/// Convert a string to an integer. Return 0 if there is a conversion error.
		/// </summary>
		/// <param name="value"></param>
		/// <param name="defaultVal"></param>
		/// <returns></returns>
		public static int SafeIntConvert(string value, int defaultVal = 0)
		{
			if (string.IsNullOrEmpty(value)) return defaultVal;
			value = value.Trim(new[] { '%' }); //always ignore percent sign
			int output;
			if (!int.TryParse(value, out output))
				return defaultVal;
			return output;
		}

		/// <summary>
		/// Convert a string to an float. Return 0 if there is a conversion error.
		/// Automatically scale percentages to the float equivalent if desired
		/// </summary>
		/// <param name="value"></param>
		/// <param name="defaultVal"></param>
		/// <param name="ignorePercent"></param>
		/// <returns></returns>
		public static float SafeFloatConvert(string value, float defaultVal = 0F, bool ignorePercent = true)
		{
			if (string.IsNullOrEmpty(value)) return defaultVal;
			var isPercent = !ignorePercent && value.EndsWith("%");
			value = value.Trim(new[] { '%' });
			float output;
			if (!float.TryParse(value, out output))
				return defaultVal;
			return isPercent ? output/100F : output;
		}

		public static bool TryConvert(char input, out string output)
		{
			try
			{
				if (input < '(' || input > '~')
					output = string.Format("0x{0:x2}", (int) input);
				else
					output = input.ToString();
				return true;
			}
			catch (Exception)
			{
				output = "";
				return false;
			}
		}

		/// <summary>
		/// Convert a string to a character.
		/// This can be used to parse input values to look for special character codes.
		/// </summary>
		/// <param name="input"></param>
		/// <param name="output"></param>
		/// <returns></returns>
		public static bool TryConvert(string input, out char output)
		{
			var specialchars = new Dictionary<string, char>
				{
					{"\\t", '\t'},
					{"\\r", '\r'},
					{"\\n", '\n'},
					{"\\\"", '"'}
				};

			if (!char.TryParse(input, out output))
			{
				if (input.Length > 2)
				{
					var start = -1;
					if (input.StartsWith("0x")) start = 2;
					else if (input.StartsWith("\\u")) start = 3;
					if (start > 0)
					{
						try
						{
							var val = int.Parse(input.Substring(start), NumberStyles.AllowHexSpecifier);
							output = (char) val;
							return true;
						}
						catch {}
					}
				}
				return specialchars.TryGetValue(input, out output);
			}
			return true;
		}

		/// <summary>
		/// Convert a string to a formatted date. 
		/// Default format is yyyy-MM-dd 
		/// Return empty string if there is a conversion error.
		/// </summary>
		/// <param name="dateIn"></param>
		/// <param name="format"></param>
		/// <returns></returns>
		public static string SafeDateConvert(string dateIn, string format = "yyyy-MM-dd")
		{
			DateTime dateOut;
			if (!DateTime.TryParse(dateIn, out dateOut))
				return "";
			return dateOut.ToString(format);
		}

		public static string ReverseDate(string dateIn)
		{
			if (dateIn == null || dateIn.Length < 7) return dateIn;
				
			//reversed is day/month/year insetead of month/day/year
			return dateIn.Substring(3, 2) + "/"
						+ dateIn.Substring(0, 2) + "/"
						+ dateIn.Substring(6, 4);
		}

		/// <summary>
		/// Convert a string to a DateTime. 
		/// Default input date is used in case of an error parsing the date 
		/// </summary>
		/// <param name="dateIn"></param>
		/// <param name="defaultDate"></param>
		/// <returns></returns>
		public static DateTime SafeDateConvert(string dateIn, DateTime defaultDate, bool dateReversed = false)
		{
			if (dateReversed) dateIn = ReverseDate(dateIn);

			DateTime dateOut;
			if (!DateTime.TryParse(dateIn, out dateOut)) return defaultDate;
			return dateOut;
		}

		public static string GetDateString(XElement source, string elementName, bool dateReversed = false)
		{
			var date = GetValue(source, elementName);
			if (dateReversed) date = ReverseDate(date);

			return date;
		}

		public static bool TryGetDate(out DateTime result, string dateIn, bool dateReversed = false)
		{
			if (dateReversed) dateIn = ReverseDate(dateIn);
			result = DateTime.MinValue;
			return DateTime.TryParse(dateIn, out result);
		}

		public static bool TryGetDate(out DateTime result, XElement source, string elementName, bool dateReversed = false)
		{
			var date = GetDateString(source, elementName, dateReversed);
			result = DateTime.MinValue;
			return DateTime.TryParse(date, out result);
		}

		public static DateTime DateTimeConvert(DateTime dateTime, TimeZoneInfo timeZone)
		{
			//all incoming times are in US Pacific Standard Time
			if (timeZone.Id.Equals("Pacific Standard Time")) return dateTime;
			return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(dateTime, "Pacific Standard Time", timeZone.Id);
		}

		public static string EncodeTimeSpan(TimeSpan span, ValueNodeBase.ValueUnits units)
		{
			var test = span.ToString();
			switch (units)
			{
				case ValueNodeBase.ValueUnits.Days:
					return string.Format("{0} Days", span.Days);
				case ValueNodeBase.ValueUnits.Hours:
					return string.Format("{0} Days", span.Hours);
				case ValueNodeBase.ValueUnits.Minutes:
				case ValueNodeBase.ValueUnits.General:
					return string.Format("{0} Minutes", span.Days);
				default:
					throw new ArgumentOutOfRangeException("units");
			}
		}

		public static bool TryGetTimeSpan(string data, out TimeSpan span, out ValueNodeBase.ValueUnits units)
		{
			span = TimeSpan.MinValue;
			units = ValueNodeBase.ValueUnits.General;
			if (string.IsNullOrWhiteSpace(data)) return false;

			var split = data.Split(' ');
			if (split.Length != 2) return false;
			int val;
			if (!int.TryParse(split[0], out val)) return false;

			switch (split[1][0]) //just check first character
			{
				case 'D':
				case 'd':
					span = new TimeSpan(val, 0, 0, 0);
					units = ValueNodeBase.ValueUnits.Days;
					return true;
				case 'H':
				case 'h':
					span = new TimeSpan(val, 0, 0);
					units = ValueNodeBase.ValueUnits.Hours;
					return true;
				case 'M':
				case 'm':
					span = new TimeSpan(0, val, 0);
					units = ValueNodeBase.ValueUnits.Days;
					return true;
				default:
					return false;
			}
		}
		/// <summary>
		/// Encode a string using base64 encodeding
		/// </summary>
		/// <param name="text"></param>
		/// <returns></returns>
		public static string Base64Encode(string text)
		{
			if (string.IsNullOrEmpty(text)) return "";
			var bytes = Encoding.ASCII.GetBytes(text);
			return Convert.ToBase64String(bytes);
		}

		/// <summary>
		/// Dencode a string from base64 encodeding
		/// </summary>
		/// <param name="text"></param>
		/// <returns></returns>
		public static string Base64Decode(string text)
		{
			if (string.IsNullOrEmpty(text)) return "";
			var encodedAsBytes = Convert.FromBase64String(text);
			return Encoding.ASCII.GetString(encodedAsBytes);
		}

		public static List<List<string>> ParseJsonArrayOfArrays(string data)
		{

			using (var dataStream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
			{
				var rows = GetRows(dataStream); //GetRows closes stream
				return rows.Count < 1 ? new List<List<string>>() : rows.Select(SplitJsonRow).ToList();
			}
		}

		public static List<string> GetRows(MemoryStream dataStream, string[] rowEnd = null, char[] trimChars = null)
		{
			var rows = new List<string>();
			if (dataStream == null) return rows;

			string data = null;
			using (var reader = new StreamReader(dataStream, Encoding.UTF8))
			{
				data = reader.ReadToEnd();
			}
			dataStream.Close();
			if (string.IsNullOrEmpty(data)) return rows;

			if (rowEnd == null || rowEnd.Length < 1) 
				rowEnd = new[] {"],["}; //default to Json row endings
			var isJson = rowEnd.Contains("],[");

			if (trimChars == null || trimChars.Length < 1)
				trimChars = new[] { ' ', '\r', '\n' }; //quotes, tabs and commas and brackets must be left for the SplitRow logic
			//trimChars = new[] { ' ', '[', ']', '\r', '\n' }; //quotes, tabs and commas must be left for the SplitRow logic

			//look for beginning [[ for json
			if (isJson)
			{
				var begin = data.IndexOf("[[", StringComparison.Ordinal);
				if (begin > -1) data = data.Substring(begin + 2);
			}

			//split into rows
			rows.AddRange(data.Trim().Trim('\r', '\n')
											.Split(rowEnd, StringSplitOptions.RemoveEmptyEntries)
											.Select(x => x.Trim(trimChars)).ToList());
			rows.RemoveAll(string.IsNullOrWhiteSpace);

			//remove final ]] for json
			if (isJson)
			{
				var end = rows[rows.Count - 1].LastIndexOf("]]", StringComparison.Ordinal);
				if (end > -1) rows[rows.Count - 1] = rows[rows.Count - 1].Substring(0, end);
			}
			return rows;
		}

		public static List<string> SplitRow(string row, string columnEnd = null, string columnStart = null) //"[")
		{
			if (string.IsNullOrEmpty(row)) return new List<string>();
			if (string.IsNullOrEmpty(columnEnd))
				columnEnd = ","; //default to comma-separated (works with Json or CSV)

			//Logic Considerations:
			//can't just split on columnEnd as these characters could exist inside a field
			//can't assume field ends with a quote because numerical fields may not have quotes
			//	and some fields could have internal \"'s
			//so if it starts with a quote, then it must end with a quote-columnEnd
			//if no quote at the start then end at next columnEnd
			//same consideration for start/end [...], for lists

			char[] trimChars;
			char[] trimStart;
			if (columnEnd.Equals("\t"))
			{
				//trimChars = new[] { ' ', '[', ']', ',' }; //don't trim tabs or we lose empty entries
				//trimStart = new[] { ' ', '[', ']', ',', '\r', '\n' }; //quotes trimmed separately
				trimChars = new[] { ' ', ',' }; //don't trim tabs or we lose empty entries
				trimStart = new[] { ' ', ',', '\r', '\n' }; //quotes and []'s trimmed separately
			}
			else
			{
				//trimChars = new[] { ' ', '[', ']', '\t' }; //don't trim commas or we lose empty entries
				//trimStart = new[] { ' ', '[', ']', '\t', '\r', '\n' }; //commas and quotes trimmed separately
				trimChars = new[] { ' ', '\t' }; //don't trim commas or we lose empty entries
				trimStart = new[] { ' ', '\t', '\r', '\n' }; //commas and quotes and []'s trimmed separately
			}
			var separator1 = "\"" + columnEnd; //if startsWithQuote
			var separator2 = "]" + columnEnd; //if startsWithBracket
			var separator3 = columnEnd;

			var cols = new List<string>();
			//trim off any extra characters before the first '[' (or other columnStart characters)
			var start = 0;
			if (!string.IsNullOrEmpty(columnStart))
			{
				start = row.IndexOf(columnStart, StringComparison.Ordinal);
				if (start > 0) row = row.Substring(start);
			}
#if DEBUG
			var breakHere = false;
			foreach (var id in TableAccess.Instance.DebugIds)
				if (row.Contains(id))
				{
					breakHere = true;
					break;
				}
#endif
			var lastItem = false;
			while (true)
			{
				row = row.TrimStart(trimStart); //don't trim quotes or commas yet
				var startsWithQuote = row.StartsWith("\"");
				var startsWithBracket = row.StartsWith("[");
				var separator = startsWithQuote ? separator1 : startsWithBracket? separator2 : separator3;
				start = startsWithQuote || startsWithBracket ? 1 : 0; //look past the first quote/bracket
				var end = row.IndexOf(separator, start, StringComparison.Ordinal);
				while (startsWithQuote && end > 0) //must handle case of "\"," within item name (internal bracket not allowed when startsWithBracket)
				{
					if (!row[end - 1].Equals('\\')) break; //stop looking if the previous character was NOT a backslash
					end = row.IndexOf(separator, end + 1, StringComparison.Ordinal); //look for the next occurance
					if (end < 0) break; //end of row
				}
				if (end < start)
				{
					lastItem = true;
					end = startsWithQuote || startsWithBracket ? row.Length - 1 : row.Length;
					if (end < start) end = start;
				}
				var item = row.Substring(start, end - start);
				item = item.Trim(trimChars);


				//if bracketed then need to parse the contents and aggregate to remove quotes (without removing internal quotes)
				if (startsWithBracket) 
				{
					var contents = SplitRow(item);
					item = contents.Any() ? contents.Aggregate((w, j) => string.Format("{0},{1}", w, j)) : "";
				}
				cols.Add(item);
				if (lastItem) break;
				row = row.Substring(end + separator.Length);
			}

			return cols;
		}

		/// <summary>
		/// Only trim one quote from each end of the string
		/// This is necessary since Trim( new[] { '\"' } ) would also trim a second quote that could be the last char in the string
		/// </summary>
		/// <param name="source"></param>
		/// <returns></returns>
		public static string TrimQuotes(string source)
		{
			if (string.IsNullOrEmpty(source)) return "";
			if (source[0].Equals('"')) 
				source = source.Substring(1);
			var len = source.Length;
			if (len > 0 && source[len - 1].Equals('"'))
				source = source.Substring(0, len - 1);
			return source;
		}

		//public static List<string> GetJsonRows(string data)
		//{
		//  return GetRows(data);

		//  //var rows = new List<string>();
		//  //if (string.IsNullOrEmpty(data)) return rows;

		//  //var trimChars = new[] { ' ', '[', ']', ',' }; //quotes trimmed separately
		//  //rows.AddRange(data.Trim().Trim('\r', '\n')
		//  //                .Split(new[] {"],["}, StringSplitOptions.RemoveEmptyEntries)
		//  //                .Select(x => x.Trim(trimChars)).ToList());
		//  //rows.RemoveAll(string.IsNullOrWhiteSpace);
		//  //return rows;
		//}

		public static List<string> SplitJsonRow(string row)
		{
			if (string.IsNullOrEmpty(row)) return new List<string>();

			return SplitRow(row);

			//Logic Considerations:
			//can't just split on commas as there can be commas inside a field
			//can't just split on quote because numerical fields may not have quotes
			//so if it starts with a quote, then it must end with a quote-comma
			//if no quote at the start then end at next comma

			//bool startsWithQuote = false;
			//var trimStart = new[] { ' ', '[', ']', '\t', '\r', '\n' }; //commas and quotes trimmed separately
			//var trimChars = new[] { ' ', '[', ']', ',', '\t' }; //quotes trimmed separately
			//var trimQuotes = new[] { '\"' };
			//const string separator1 = "\","; //if startswithquote
			//const string separator2 = ",";

			//var cols = new List<string>();
			////trim off any extra characters before the first '['
			//var start = row.IndexOf('[');
			//if (start > -1) row = row.Substring(start);
			//while (true)
			//{
			//  row = row.TrimStart(trimStart); //don't trim quotes or commas yet
			//  startsWithQuote = row.StartsWith("\"");
			//  var separator = startsWithQuote ? separator1 : separator2;
			//  start = startsWithQuote ? 1 : 0; //look past the first quote
			//  var end = row.IndexOf(separator, start, StringComparison.Ordinal);
			//  var item = end < 0 ? row : row.Substring(0, end);
			//  if (startsWithQuote) item = item.Trim(trimQuotes); //only trim qoutes here
			//  item = item.Trim(trimChars);
			//  cols.Add(item);
			//  if (end < 0) break;
			//  row = startsWithQuote ? row.Substring(end + 2) : row.Substring(end + 1);
			//}

			//return cols;
		}

		public static string GetColVal(List<string> cols, int i)
		{
			return (i < 0 || i >= cols.Count) ? "" : cols[i];
		}

		public static int GetHeaderPosition(string header, string column)
		{
			if (string.IsNullOrEmpty(column) || string.IsNullOrEmpty(header)) return -1;

			var index = header.IndexOf(column, StringComparison.OrdinalIgnoreCase);
			return index > 0 ? header.Substring(0, index).Count(x => x.Equals(',')) : index;
		}

		public static int GetHeaderPosition(List<string> header, string column)
		{
			if (string.IsNullOrEmpty(column)) return -1;

			return header.FindIndex(x => x.Equals(column, StringComparison.OrdinalIgnoreCase));
		}

		public static string GetSafeValue(int index, ref List<string> cols)
		{
			if (index < 0 || index >= cols.Count) return "";
			return cols[index];
		}

	}
}