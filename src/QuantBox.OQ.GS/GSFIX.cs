using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SmartQuant.FIXApplication;
using System.ComponentModel;
using SmartQuant.Providers;
using SmartQuant;

namespace QuantBox.OQ.GS
{
    public class GSFIX:QuickFIX42CommonProvider
    {
        // Class members
		private string password;
		private string account;

        public GSFIX()
        {
            // defaults
            base.DataDictionary = string.Format(@"{0}\FIX42_GS.xml", Framework.Installation.FIXDir.FullName);
            base.FileStorePath = string.Format(@"{0}", Framework.Installation.LogDir.FullName);
            base.FileLogPath = string.Format(@"{0}", Framework.Installation.LogDir.FullName);

            // price
            //base.priceSenderCompID = "tester";
            //base.priceTargetCompID = "TT_PRICES";
            //base.priceSocketConnectHost = "localhost";
            //base.priceSocketConnectPort = 10502;
            base.PriceFileStorePath = string.Format(@"{0}", Framework.Installation.LogDir.FullName);
            base.PriceSessionEnabled = false;

            // order
            //base.orderSenderCompID = "tester";
            //base.orderTargetCompID = "TT_ORDER";
            //base.orderSocketConnectHost = "localhost";
            //base.orderSocketConnectPort = 10501;
            base.OrderFileStorePath = string.Format(@"{0}", Framework.Installation.LogDir.FullName);


            ProviderManager.Add(this);
        }

        public override byte Id
        {
            get { return 59; }
        }

        public override string Name
        {
            get { return "GS FIX"; }
        }

        public override string Title
        {
            get { return "QuantBox GS FIX Adapter"; }
        }

        public override string URL
        {
            get { return "http://www.quantbox.cn"; }
        }

        [Category("Login")]
        [Description("Password")]
        [PasswordPropertyText(true)]
        public string Password
        {
            get { return password; }
            set { password = value; }
        }

        [Category("Login")]
        [Description("Account")]
        public string Account
        {
            get { return account; }
            set { account = value; }
        }

        protected override QuickFIX42CommonApplication CreateApplicationInstance()
        {
            return new GSFIXApplication(this);
        }
    }
}
