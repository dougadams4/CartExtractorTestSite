using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Configuration;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using _4_Tell.Logs;
using _4_Tell.CommonTools;

namespace _4_Tell.IO
{
    internal class ClientData
    {
        private const string SiteRulesFileName = "SiteRules.xml"; 
        private static readonly ClientData _instance = new ClientData(); //Singleton

        public static ClientData Instance
        {
            get { return _instance; }
        }

        private ClientData()
        {
        }

        public string GetServiceKey(string alias)
        {
            return "dummy key";
        }
        
        public XElement ReadSiteRules(string alias, IEnumerable<XElement> allSettings = null, string subFolder = null)
        {
            XElement settings;
            try
            {
                //first look for individual settings in the client folder
                var loadPath = subFolder == null ? DataPath.Instance.ClientDataPath(alias, true) :
                                                                                     DataPath.Instance.ClientDataPath(alias, false) + subFolder + "\\";
                settings = XElement.Load(loadPath + SiteRulesFileName);
            }
            catch (Exception ex)
            {
                if (BoostLog.Instance != null)
                    BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error reading SiteRules", ex, alias);
                settings = null;
            }
            //check for defaults
            //if (settings == null)
            //{
            //  //if no individual settigns file found, read from the full client settings xml
            //  if (allSettings == null) allSettings = GetAllClientSettings();
            //  if (allSettings == null) return null;
            //  settings = allSettings.FirstOrDefault(x => GetValue(x, "alias").Equals(alias));
            //  if (settings != null)
            //    SaveSiteRules(alias, settings);
            //}
            return settings;
        }

        public void SaveSiteRules(string alias, XElement settings, SiteRules rules = null, WebContextProxy wc = null)
        {
            try
            {
#if DEBUG
                if (BoostLog.Instance != null && wc != null)
                    BoostLog.Instance.WriteEntry(EventLogEntryType.Information,
                        string.Format("Saving Rules for {0}", alias), "Context:\n" + wc.ToString(), alias);
#endif
                var path = DataPath.Instance.ClientDataPath(alias, true);
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                settings.Save(path + SiteRulesFileName);

                //check to see if we should update BoostConfigOverride (used when customers upload data directly to us)
                if (rules == null || rules.CartExtractorExists || TableAccess.Instance == null) return;

                var data = rules.FormatGeneratorConfig();
                TableAccess.Instance.WriteTable(alias, "ConfigBoostOverride.txt", data);
            }
            catch { }
        }

        public int SetExclusions(string alias, List<string> newExclusions)
        {
            return 0;
        }
    }
}