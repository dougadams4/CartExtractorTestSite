using System;
using System.Collections;
using System.Configuration;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Web.UI.WebControls;
using System.IO;
using System.Net;
using System.Text;
using System.Collections.Specialized;
using System.Xml;
using System.Xml.Serialization;
using System.Xml.Linq;
using _4_Tell.IO;
using _4_Tell.CartExtractors;
using _4_Tell.CommonTools;
using _4_Tell.Logs;

public partial class _Default : System.Web.UI.Page
{
    protected void Page_Load(object sender, EventArgs e)
    {
        if (!CartExtractorTestSite.Globals._dataLoaded)
        {
            CartExtractorTestSite.Globals._dataLoaded = true;

            GetAppSettings(); // read initial boost version and service address from web.config

            ProgressTimer.Interval = 2000; //2 sec
            ProgressTimer.Enabled = !CheckBoxExtractPause.Checked;

            this.MaintainScrollPositionOnPostBack = true;
        }
    }

	// read app settings from web.config
    public XElement ReadSiteRules(string alias, string subFolder = null)
    {
        XElement settings;
        try
        {
            //first look for individual settings in the client folder
            var loadPath = subFolder == null ? DataPath.Instance.ClientDataPath(alias, true) : DataPath.Instance.ClientDataPath(alias, false) + subFolder + "\\";
            settings = XElement.Load(loadPath + "SiteRules.xml");
        }
        catch (Exception ex)
        {
            if (BoostLog.Instance != null)
                BoostLog.Instance.WriteEntry(EventLogEntryType.Error, "Error reading SiteRules", ex, alias);
            settings = null;
        }
        return settings;
    }

    private void GetAppSettings()
    {
        try
        {
            lock (CartExtractorTestSite.Globals._updateLock) //only allow one pass through here at a time;
            {
                string _clientAlias = "";
                NameValueCollection appSettings = ConfigurationManager.AppSettings;
                for (int i = 0; i < appSettings.Count; i++)
                    if (appSettings.GetKey(i).Equals("DefaultClient"))
                        _clientAlias = appSettings[i];
                TextBoxClientAlias.Text = _clientAlias;
            }
        }
        catch (Exception ex)
        {
            TextBoxResults.Text = "Error Reading App Settings\n" + ex.Message;
            UpdatePanelResults.Update();
        }
    }

    protected void ProgressTimer_Tick(object sender, EventArgs e)
    {
        if (CartExtractorTestSite.Globals._cart == null)
            return;
       
        if (CheckBoxExtractPause.Checked)
        {
            ProgressTimer.Enabled = false;
            return;
        }
        if (CartExtractorTestSite.Globals._statusRequestPending)
            return;
        CartExtractorTestSite.Globals._statusRequestPending = true;
        ProgressTimer.Enabled = false;
        var statusDesc = "";
        try
        {
            statusDesc = CartExtractorTestSite.Globals._cart.Progress.Text;
        }
        catch (Exception ex)
        {
            statusDesc += "\n" + ex.Message;
            if (ex.InnerException != null) 
                statusDesc += "\n" + ex.InnerException.Message;
        }
        finally
        {
            if (TextBoxExtractProgress.Text != statusDesc)
            {
                TextBoxExtractProgress.Text = statusDesc;
                UpdatePanelProgress.Update();
            }
            CartExtractorTestSite.Globals._statusRequestPending = false;
            ProgressTimer.Enabled = !CheckBoxExtractPause.Checked;
        }
        
        // disable timer if cart is not processing
        if (CartExtractorTestSite.Globals._cart.Progress.State != ExtractorProgress.ProgressState.InProgress)
            CheckBoxExtractPause.Checked = true;
        
        // Call out end of execution
        TextBoxResults.Text += CartExtractorTestSite.Globals._cart.Progress.State.ToString() + "\n";
        UpdatePanelResults.Update();
    }

    protected void CheckBoxExtractPause_CheckedChanged(object sender, EventArgs e)
    {
        ProgressTimer.Enabled = !CheckBoxExtractPause.Checked;
    }

    private CartExtractor GetCart()
    {
        string alias = TextBoxClientAlias.Text;
        XElement settings = ReadSiteRules(alias, "upload");
        if (settings == null)
        {
            TextBoxResults.Text = "Error Reading Site Rules\n";
            UpdatePanelResults.Update();
            return null;
        }
        SiteRules rules = new SiteRules(alias, 1, settings);
        return CartExtractor.GetCart(rules);   
    }

    protected void CancelExtraction_Click(object sender, EventArgs e)
    {
        if (CartExtractorTestSite.Globals.textBoxClientAliasChanged == true)
        {
            CartExtractorTestSite.Globals.textBoxClientAliasChanged = false;
            return;
        }
        if (CartExtractorTestSite.Globals._cart != null && CartExtractorTestSite.Globals._cart.IsExtracting == true)
        {
            CartExtractorTestSite.Globals._cart.Progress.Abort();
            TextBoxResults.Text += "Extraction Cancelation Initiated - Please Wait\n";
        }
        else
            TextBoxResults.Text += "Cart is not currently extracting.\n"; 
        UpdatePanelResults.Update();
    }
    
    protected void BeginExtraction_Click(object sender, EventArgs e)
    {
        if (CartExtractorTestSite.Globals.textBoxClientAliasChanged == true)
        {
            CartExtractorTestSite.Globals.textBoxClientAliasChanged = false;
            return;
        } 
        if (CartExtractorTestSite.Globals._cart == null)
        {
            // Do an init here just in case initialization has not been done
            CartExtractorTestSite.Globals._cart = this.GetCart();
            if (CartExtractorTestSite.Globals._cart == null)
                return;
        }
        if (CartExtractorTestSite.Globals._cart.IsExtracting == true)
            TextBoxResults.Text += "Cart is currently extracting, cannot start new extraction.\n";
        else
        {
            TextBoxResults.Text = "Extraction Initiated - Please Wait";
            TextBoxExtractProgress.Text = "";
            CheckBoxExtractPause.Checked = false;
            ProgressTimer.Enabled = !CheckBoxExtractPause.Checked;
            UpdatePanelProgress.Update();

            if (DropDownListExtractType.SelectedIndex < 0)
                DropDownListExtractType.SelectedIndex = 0;
            try
            {
                CartExtractor cart = CartExtractorTestSite.Globals._cart;
                System.Threading.Tasks.Task.Factory.StartNew(() => cart.GetData((CartExtractor.ExtractType)(DropDownListExtractType.SelectedIndex + 1)));
            }
            catch (Exception ex)
            {
                TextBoxResults.Text += "Exception = " + ex.Message + "\n";
                if (ex.InnerException != null)
                    TextBoxResults.Text += "\n\nInner Exception = " + ex.InnerException.Message + "\n";
            }
        }
        UpdatePanelResults.Update();
    }
    
    protected void TextBoxClientAlias_TextChanged(object sender, EventArgs e)
    {
        CartExtractorTestSite.Globals.textBoxClientAliasChanged = true;
        if (CartExtractorTestSite.Globals._cart != null)
        {
            if (CartExtractorTestSite.Globals._cart.IsExtracting == true ||
                CartExtractorTestSite.Globals._cart.Progress.State == ExtractorProgress.ProgressState.InProgress)
            {
                TextBoxResults.Text = "Cart is currently extracting, cancel extraction before changing alias.";
                UpdatePanelResults.Update();
                TextBoxClientAlias.Text = CartExtractorTestSite.Globals._cart.Alias;
                return;
            }
        }
        CartExtractorTestSite.Globals._cart = null;
        CheckBoxExtractPause.Checked = true;
        ProgressTimer.Enabled = !CheckBoxExtractPause.Checked;
        TextBoxExtractProgress.Text = "";
        UpdatePanelProgress.Update();
    }

    protected void DropDownListExtractType_SelectedIndexChanged(object sender, EventArgs e)
    {
        CartExtractorTestSite.Globals.textBoxClientAliasChanged = false;
    }
}
