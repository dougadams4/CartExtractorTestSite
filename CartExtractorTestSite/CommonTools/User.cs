using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

//XElement

namespace _4_Tell.CommonTools
{
	/// <summary>
	/// Main User class defines contact information and has a list of SiteRoles
	/// </summary>
	public class User
	{
		public string Email { get; set; } //this is the PK
		public string Name { get; set; }
		public string Title { get; set; }
		public string Phone { get; set; }
		public string Password { get; set; }
		public UserContactRole ContactRole { get; set; }
		public readonly List<SiteRole> SiteRoles;

		public User()
		{
			Email = "";
			Name = "";
			Title = "";
			Phone = "";
			Password = "";
			ContactRole = UserContactRole.Other;
			SiteRoles = new List<SiteRole>();
		}

		public User(XElement u)
		{
			UserContactRole contactRole;
			if (!Enum.TryParse(Input.GetValue(u, "contactRole"), true, out contactRole))
				contactRole = UserContactRole.Other;

			Name = Input.GetValue(u, "name");
			Email = Input.GetValue(u, "email");
			Title = Input.GetValue(u, "title");
			Phone = Input.GetValue(u, "phone");
			Password = Input.GetValue(u, "password");
			ContactRole = contactRole;

			var siteRoleElement = u.Element("siteRoles");
			if (siteRoleElement != null)
			{
				var siteRoles = siteRoleElement.Descendants("siteRole");
				if (siteRoles.Any())
				{
					foreach (var sr in siteRoles)
					{
						UserAccessRole accessRole;
						if (!Enum.TryParse(Input.GetValue(sr, "accessRole"), true, out accessRole))
							accessRole = UserAccessRole.User;
						var role = new SiteRole
						{
							Alias = Input.GetValue(sr, "alias"),
							AccessRole = accessRole
						};
						var reportElement = sr.Element("reports");
						if (reportElement != null)
						{
							var reports = reportElement.Descendants("reportType");
							if (reports.Any())
							{
								foreach (var r in reports)
								{
									ReportType rType;
									if (Enum.TryParse(Input.GetValue(r, "reportType"), true, out rType))
										role.Reports.Add(rType);
								}
							}
						}
						SiteRoles.Add(role);
					}
				}
			}
		}

		public XElement ToXml(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				if (!string.IsNullOrEmpty(Name)) name = Name;
				else name = "Unnamed";
			}
			var userXml = new XElement(name);

			userXml.Add(new XElement("email", Email));
			userXml.Add(new XElement("name", Name));
			userXml.Add(new XElement("title", Title));
			userXml.Add(new XElement("phone", Phone));
			userXml.Add(new XElement("password", Password));
			userXml.Add(new XElement("contactRole", ContactRole));
			if (SiteRoles != null && SiteRoles.Any())
			{
				var siteRoles = new XElement("siteRoles");
				foreach (var s in SiteRoles)
				{
					var role = new XElement("siteRole");
					role.Add("alias", s.Alias);
					role.Add("accessRole", s.AccessRole);
					if (s.Reports != null && s.Reports.Any())
					{
						var reports = new XElement("reports");
						foreach (var r in s.Reports)
						{
							reports.Add("reportType", r);
						}
						role.Add(reports);
					}
					siteRoles.Add(role);
				}
				userXml.Add(siteRoles);
			}
			return userXml;
		}

		public UserContact ToUserContact()
		{
			return new UserContact {Email = Email, Name = Name};
		}
	}

	#region Support Classes & Enums

	/// <summary>
	/// Abbreviated User contact information for use in subscriptions
	/// </summary>
	public class UserContact
	{
		public string Name { get; set; }
		public string Email { get; set; }

		/// <summary>
		/// Users are deemed equal if they have the same email address
		/// since email addresses are required to be unique
		/// </summary>
		public bool Equals(UserContact user2)
		{
			if (Email == null) return false;
			return Email.Equals(user2.Email);
		}

		/// <summary>
		/// Users are deemed equal if they have the same email address
		/// since email addresses are required to be unique
		/// </summary>
		public class Comparer : IEqualityComparer<UserContact>
		{
			public bool Equals(UserContact user1, UserContact user2)
			{
				if (user1 == null || user2 == null) return false;
				return user1.Email.Equals(user2.Email);
			}

			public int GetHashCode(UserContact user)
			{
				if (user == null || user.Email == null) return 0;
				return user.Email.GetHashCode();
			}
		}

	}

	/// <summary>
	/// Defines the UserRole and Report subscriptions for a specified site
	/// Each user has a list of these and can have different roles and reports for each site
	/// </summary>
	public class SiteRole
	{
		public string Alias { get; set; }
		public UserAccessRole AccessRole { get; set; }
		public readonly List<ReportType> Reports;

		public SiteRole()
		{
			Alias = "";
			AccessRole = UserAccessRole.User;
			Reports = new List<ReportType>();
		}
	}

	/// <summary>
	/// Defines the access level of the User
	/// Many Dashboard features are restricted to specific user roles
	/// </summary>
	public enum UserAccessRole
	{
		SuperAdmin,
		AccountManager,
		SalesDemo,
		Admin,
		SuperUser,
		User
	}

	/// <summary>
	/// Defines the primary role of the user in their organization (self-identified) 
	/// </summary>
	public enum UserContactRole
	{
		Technical,
		Accounting,
		Marketing,
		Merchandizing,
		Management,
		Other
	}

	/// <summary>
	/// Defines the types of reports
	/// These are used for report subscriptions and service logs
	/// </summary>
	public enum ReportType
	{
		All,
		News,
		TechUpdates,
		Analytics,
		ServiceInfo,
		ServiceWarning,
		ServiceError,
		None
	}

	/// <summary>
	/// Reverse index of report subscriptions
	/// Provides a means for users to subscribed to one or more specific reports
	/// and allow easy lookup of user lists for delivery
	/// </summary>
	public class ReportSubscriptions
	{
		//public ReportType Report { get; private set; }
		//public readonly List<UserContact> Users;

		/// <summary>
		/// Nested dictionary of report sucscriptions to provide reverse lookup
		/// First level dictionary is by alias
		/// Second level dictionary is by report type
		/// </summary>
		private readonly Dictionary<string, Dictionary<ReportType, List<UserContact>>> _subscriptions;

		public ReportSubscriptions()
		{
			//Report = report;
			//Users = new List<UserContact>();
			_subscriptions = new Dictionary<string, Dictionary<ReportType, List<UserContact>>>();
		}

		/// <summary>
		/// Clear all report subscriptions for a given user
		/// This is normally used in an update process before adding new subscriptions
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="user"></param>
		public void Clear(string alias, UserContact user)
		{
			if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(user.Email)) return;

			var siteSubscriptions = new Dictionary<ReportType, List<UserContact>>();
			if (_subscriptions.TryGetValue(alias, out siteSubscriptions))
			{
				var comparer = new UserContact.Comparer();
				foreach (var subscription in siteSubscriptions)
				{
					subscription.Value.RemoveAll(user.Equals);
					//var users = subscription.Value;
					//if (!users.Contains(user, comparer)) continue;

					//users.Remove(user);
				}
			}
		}

		/// <summary>
		/// Add a single report subscritption for a user
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="user"></param>
		/// <param name="report"></param>
		public void Add(string alias, UserContact user, ReportType report)
		{
			if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(user.Email)) return;

			if (report == ReportType.None) //special case: adding ReportType.none will clear all existing subscriptions
			{
				Clear(alias, user);
				return;
			}

			Dictionary<ReportType, List<UserContact>> siteSubscriptions;
			List<UserContact> users;

			if (_subscriptions.TryGetValue(alias, out siteSubscriptions))
			{
				if (siteSubscriptions.TryGetValue(report, out users))
				{
					if (users.Contains(user, new UserContact.Comparer())) return; //subscription already exists
					siteSubscriptions.Remove(report); //clear the entry so it can be updated below
				}
				else
					users = new List<UserContact>();

				_subscriptions.Remove(alias); //clear the entry so it can be updated below
			}
			else
			{
				siteSubscriptions = new Dictionary<ReportType, List<UserContact>>();
				users = new List<UserContact>();
			}
			//add new subscriptions for the user
			users.Add(user);
			siteSubscriptions.Add(report, users);
			_subscriptions.Add(alias, siteSubscriptions);
		}

		/// <summary>
		/// Add subscriptions for a user to a number of reports for a given site alias
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="user"></param>
		/// <param name="reports"></param>
		public void Add(string alias, UserContact user, List<ReportType> reports)
		{
			if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(user.Email)) return;

			if (reports.Contains(ReportType.None)) //special case: adding ReportType.none will clear all existing subscriptions
			{
				Clear(alias, user);
				return;
			}

			var siteSubscriptions = new Dictionary<ReportType, List<UserContact>>();
			var comparer = new UserContact.Comparer();

			if (_subscriptions.TryGetValue(alias, out siteSubscriptions))
				_subscriptions.Remove(alias);
			else
				siteSubscriptions = new Dictionary<ReportType, List<UserContact>>();

			foreach (var report in reports)
			{
				List<UserContact> users;
				if (siteSubscriptions.TryGetValue(report, out users))
				{
					if (users.Contains(user, new UserContact.Comparer())) continue; //subscription already exists

					siteSubscriptions.Remove(report); //clear the entry so it can be updated below
				}
				else
					users = new List<UserContact>();

				users.Add(user);
				siteSubscriptions.Add(report, users);
			}

			//add new subscriptions for the user
			_subscriptions.Add(alias, siteSubscriptions);
		}

		/// <summary>
		/// Remove all existing subscriptions for a user and then add new ones given a list of desired reports
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="user"></param>
		/// <param name="reports"></param>
		public void Update(string alias, UserContact user, List<ReportType> reports)
		{
			if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(user.Email)) return;
			Clear(alias, user);
			Add(alias, user, reports);
		}

		/// <summary>
		/// Get all users subscribed to a specific report
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="report"></param>
		/// <returns></returns>
		public List<UserContact> GetUsers(string alias, ReportType report)
		{
			var users = new List<UserContact>();
			if (string.IsNullOrEmpty(alias)) return users;

			Dictionary<ReportType, List<UserContact>> siteSubscriptions;
			if (!_subscriptions.TryGetValue(alias, out siteSubscriptions)) return users; //not found

			siteSubscriptions.TryGetValue(report, out users); //doesn't matter whether or not they're found
			return users;
		}

		/// <summary>
		/// Get all report subscriptions for a specific user
		/// </summary>
		/// <param name="alias"></param>
		/// <param name="user"></param>
		/// <returns></returns>
		public List<ReportType> GetReports(string alias, UserContact user)
		{
			var reports = new List<ReportType>();
			if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(user.Email)) return reports;

			Dictionary<ReportType, List<UserContact>> siteSubscriptions;
			if (!_subscriptions.TryGetValue(alias, out siteSubscriptions)) return reports; //not found

			var comparer = new UserContact.Comparer();
			foreach (var subscription in siteSubscriptions)
			{
				if (subscription.Value.Contains(user, comparer))
					reports.Add(subscription.Key);
			}
			return reports;
		}
	}
	#endregion

	//class
}

//namespace