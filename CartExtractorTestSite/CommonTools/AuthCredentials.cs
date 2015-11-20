using System;
using System.Xml.Linq;

namespace _4_Tell.CommonTools
{
	public class AuthCredentials
	{
		public enum AuthType
		{
			BasicAuth,
			AuthParams,
			HttpAuth,
			LoginPage,
			//add future auth types here
			None
		}

		public AuthType Type { get; set; }
		public string UserName { get; set; }
		public string Password { get; set; }
		public string UserNameParam { get; set; }
		public string PasswordParam { get; set; }
		public bool RequireSsl { get; set; }

		public AuthCredentials()
		{
			Type = AuthType.None;
		}

		public AuthCredentials(AuthCredentials source)
		{
			Type = source.Type;
			UserName = source.UserName;
			Password = source.Password;
			UserNameParam = source.UserNameParam;
			PasswordParam = source.PasswordParam;
			RequireSsl = source.RequireSsl;
		}

		public AuthCredentials(XElement source)
		{
			var username = Input.GetValue(source, "userName");
			var password = Input.GetValue(source, "password");
			AuthType authType;
			if (!Enum.TryParse(Input.GetValue(source, "type"), true, out authType))
			{
				if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(password))
					authType = AuthType.None;
				else
					authType = AuthType.AuthParams;
			}
			Type = authType;
			UserName = username;
			Password = password;
			UserNameParam = Input.GetValue(source, "userNameParam");
			PasswordParam = Input.GetValue(source, "passwordParam");
			RequireSsl = Input.GetValue(source, "requireSsl").Equals("true");
		}

		public XElement ToXml(string name)
		{
			var credentials = new XElement(name);
			credentials.Add(new XElement("type", Type));
			credentials.Add(new XElement("userName", UserName));
			credentials.Add(new XElement("password", Password));
			if (!string.IsNullOrEmpty(UserNameParam))
				credentials.Add(new XElement("userNameParam", UserNameParam));
			if (!string.IsNullOrEmpty(PasswordParam))
				credentials.Add(new XElement("passwordParam", PasswordParam));
			if (RequireSsl) 
				credentials.Add(new XElement("requireSsl", true));
			return credentials;
		}
	}
}
