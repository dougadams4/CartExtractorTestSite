using System.Collections.Generic;
using System.Linq;

namespace _4_Tell.Logs
{
	public class WebContextProxy
	{
		public string IP = "";
		public string Operation = "";
		public string Parameters = "";
		public string Verb = "";
		public string ContentType = "";
		public Dictionary<string, string> Headers = null;
		public bool IsInternal = false; //true for internal calls between servers

		public override string ToString()
		{
			return
				string.Format("\n{0}\nIP = {1}\nOperation = {2}\nParameters = {3}\nVerb = {4}\nContentType = {5}\nHeaders = {6}\n",
											IsInternal ? "internal" : "external", IP, Operation, Parameters, Verb, ContentType,
											Headers == null ? "" : Headers.Aggregate("", (a, b) => string.Format("{0}\n\t{1}: {2}", a, b.Key, b.Value)));
		}
	}
}