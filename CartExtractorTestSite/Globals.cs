using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using _4_Tell.CartExtractors;

namespace CartExtractorTestSite
{
    public static class Globals
    {
        public static bool _dataLoaded = false;
        public static object _updateLock = new object();
        public static bool _statusRequestPending = false;
        public static CartExtractor _cart = null;
        public static bool textBoxClientAliasChanged = false;
    }
}